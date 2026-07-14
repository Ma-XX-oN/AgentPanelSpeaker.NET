using System.Speech.Synthesis;

namespace AgentPanelSpeaker;

/// <summary>
/// Serializes Windows SAPI speech and retains sentence and node playback
/// history.
/// </summary>
internal sealed class SpeechService : IDisposable
{
  private const int MaximumHistoryEntries = 1000;

  private readonly object _sync = new();
  private readonly SpeechSynthesizer _synthesizer = new();
  private readonly List<SpeechFragment> _history = new();
  private ActiveSpeechKind _activeKind;
  private int _activeHistoryIndex = -1;
  private int _nextHistoryIndex;
  private int? _pendingHistoryIndex;
  private string? _pendingUntrackedText;
  private bool _disposed;

  /// <summary>
  /// Initializes speech output to the default audio device.
  /// </summary>
  public SpeechService()
  {
    _synthesizer.SetOutputToDefaultAudioDevice();
    _synthesizer.SpeakCompleted += SynthesizerSpeakCompleted;
  }

  /// <summary>
  /// Gets whether replay history contains any live transcript speech.
  /// </summary>
  public bool HasHistory
  {
    get
    {
      lock (_sync)
      {
        return _history.Count != 0;
      }
    }
  }

  /// <summary>
  /// Gets installed enabled voice names.
  /// </summary>
  /// <returns>Voice names in display order.</returns>
  public IReadOnlyList<string> GetInstalledVoiceNames()
  {
    lock (_sync)
    {
      ThrowIfDisposed();

      return _synthesizer
        .GetInstalledVoices()
        .Where(voice => voice.Enabled)
        .Select(voice => voice.VoiceInfo.Name)
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();
    }
  }

  /// <summary>
  /// Selects the voice and speaking rate.
  /// </summary>
  /// <param name="voiceName">Installed voice name.</param>
  /// <param name="rate">Speech rate from -10 through 10.</param>
  public void Configure(string voiceName, int rate)
  {
    lock (_sync)
    {
      ThrowIfDisposed();

      if (rate is < -10 or > 10)
      {
        throw new ArgumentOutOfRangeException(
          nameof(rate),
          rate,
          "Speech rate must be between -10 and 10.");
      }

      if (!string.IsNullOrWhiteSpace(voiceName))
      {
        _synthesizer.SelectVoice(voiceName);
      }

      _synthesizer.Rate = rate;
    }
  }

  /// <summary>
  /// Starts a new monitored transcript session with empty replay history.
  /// </summary>
  public void BeginLiveSession()
  {
    lock (_sync)
    {
      ThrowIfDisposed();

      _history.Clear();
      _pendingHistoryIndex = null;
      _pendingUntrackedText = null;
      _activeHistoryIndex = -1;
      _nextHistoryIndex = 0;
      if (_activeKind != ActiveSpeechKind.None)
      {
        _synthesizer.SpeakAsyncCancelAll();
      }
    }
  }

  /// <summary>
  /// Queues a live transcript fragment and records it for playback.
  /// </summary>
  /// <param name="fragment">Live fragment.</param>
  public void SpeakLive(SpeechFragment fragment)
  {
    ArgumentNullException.ThrowIfNull(fragment);

    string text = fragment.Text.Trim();
    if (text.Length == 0)
    {
      return;
    }

    lock (_sync)
    {
      ThrowIfDisposed();

      _history.Add(fragment with { Text = text });
      TrimHistoryLocked();
      StartPendingOrNextLocked();
    }
  }

  /// <summary>
  /// Cancels transcript playback and speaks untracked text such as the voice
  /// test phrase.
  /// </summary>
  /// <param name="text">Text to speak.</param>
  public void SpeakUntracked(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return;
    }

    lock (_sync)
    {
      ThrowIfDisposed();

      _pendingHistoryIndex = null;
      _pendingUntrackedText = text.Trim();
      _nextHistoryIndex = _history.Count;
      RestartPendingLocked();
    }
  }

  /// <summary>
  /// Moves one sentence backward and continues through all later history.
  /// </summary>
  /// <param name="text">Sentence at the new playback position.</param>
  /// <returns>True when an earlier playback position was available.</returns>
  public bool TryRewindSentence(out string text)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      text = string.Empty;

      int anchor = GetNavigationAnchorLocked();
      int candidate = anchor >= _history.Count
        ? _history.Count - 1
        : anchor - 1;
      if (candidate < 0)
      {
        return false;
      }

      text = _history[candidate].Text;
      RestartHistoryLocked(candidate);
      return true;
    }
  }

  /// <summary>
  /// Moves one sentence forward and continues through all later history.
  /// </summary>
  /// <param name="text">Sentence at the new playback position.</param>
  /// <returns>True when a later playback position was available.</returns>
  public bool TryForwardSentence(out string text)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      text = string.Empty;

      int anchor = GetNavigationAnchorLocked();
      int candidate = anchor + 1;
      if (anchor >= _history.Count || candidate >= _history.Count)
      {
        return false;
      }

      text = _history[candidate].Text;
      RestartHistoryLocked(candidate);
      return true;
    }
  }

  /// <summary>
  /// Moves to the previous accessibility node and continues through all later
  /// history.
  /// </summary>
  /// <param name="text">Node text at the new playback position.</param>
  /// <returns>True when a previous node was available.</returns>
  public bool TryRewindNode(out string text)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      text = string.Empty;

      int anchor = GetNavigationAnchorLocked();
      int candidate = anchor >= _history.Count
        ? _history.Count - 1
        : anchor - 1;
      if (candidate < 0)
      {
        return false;
      }

      int first = FindNodeStartLocked(candidate);
      int last = FindNodeEndLocked(first);
      text = JoinHistoryLocked(first, last);
      RestartHistoryLocked(first);
      return true;
    }
  }

  /// <summary>
  /// Moves to the next accessibility node and continues through all later
  /// history.
  /// </summary>
  /// <param name="text">Node text at the new playback position.</param>
  /// <returns>True when a later node was available.</returns>
  public bool TryForwardNode(out string text)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      text = string.Empty;

      int anchor = GetNavigationAnchorLocked();
      if (anchor >= _history.Count)
      {
        return false;
      }

      int candidate = FindNodeEndLocked(anchor) + 1;
      if (candidate >= _history.Count)
      {
        return false;
      }

      int last = FindNodeEndLocked(candidate);
      text = JoinHistoryLocked(candidate, last);
      RestartHistoryLocked(candidate);
      return true;
    }
  }

  /// <summary>
  /// Cancels current and queued speech and returns playback to the live end.
  /// </summary>
  public void CancelAll()
  {
    lock (_sync)
    {
      ThrowIfDisposed();

      _pendingHistoryIndex = null;
      _pendingUntrackedText = null;
      _nextHistoryIndex = _history.Count;
      if (_activeKind != ActiveSpeechKind.None)
      {
        _synthesizer.SpeakAsyncCancelAll();
      }
    }
  }

  /// <summary>
  /// Cancels speech and releases the synthesizer.
  /// </summary>
  public void Dispose()
  {
    lock (_sync)
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
      _pendingHistoryIndex = null;
      _pendingUntrackedText = null;
      _synthesizer.SpeakCompleted -= SynthesizerSpeakCompleted;
      _synthesizer.SpeakAsyncCancelAll();
      _synthesizer.Dispose();
    }
  }

  /// <summary>
  /// Advances the serialized playback queue after one prompt completes or is
  /// cancelled.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Speech completion information.</param>
  private void SynthesizerSpeakCompleted(
    object? sender,
    SpeakCompletedEventArgs eventArgs)
  {
    lock (_sync)
    {
      if (_disposed)
      {
        return;
      }

      _activeKind = ActiveSpeechKind.None;
      _activeHistoryIndex = -1;
      TrimHistoryLocked();
      StartPendingOrNextLocked();
    }
  }

  /// <summary>
  /// Restarts serialized playback from one history entry.
  /// </summary>
  /// <param name="index">History entry to speak first.</param>
  private void RestartHistoryLocked(int index)
  {
    _pendingUntrackedText = null;
    _pendingHistoryIndex = index;
    _nextHistoryIndex = index;
    RestartPendingLocked();
  }

  /// <summary>
  /// Cancels the active prompt or starts a pending request immediately.
  /// </summary>
  private void RestartPendingLocked()
  {
    if (_activeKind != ActiveSpeechKind.None)
    {
      _synthesizer.SpeakAsyncCancelAll();
      return;
    }

    StartPendingOrNextLocked();
  }

  /// <summary>
  /// Starts pending untracked text, a pending history restart, or the next
  /// sequential history entry.
  /// </summary>
  private void StartPendingOrNextLocked()
  {
    if (_activeKind != ActiveSpeechKind.None)
    {
      return;
    }

    if (_pendingUntrackedText is not null)
    {
      string text = _pendingUntrackedText;
      _pendingUntrackedText = null;
      _activeKind = ActiveSpeechKind.Untracked;
      _synthesizer.SpeakAsync(text);
      return;
    }

    if (_pendingHistoryIndex is int pendingIndex)
    {
      _nextHistoryIndex = pendingIndex;
      _pendingHistoryIndex = null;
    }

    if (_nextHistoryIndex >= _history.Count)
    {
      return;
    }

    _activeHistoryIndex = _nextHistoryIndex;
    ++_nextHistoryIndex;
    _activeKind = ActiveSpeechKind.History;
    _synthesizer.SpeakAsync(_history[_activeHistoryIndex].Text);
  }

  /// <summary>
  /// Finds the history entry that navigation should use as its current
  /// position.
  /// </summary>
  /// <returns>The active, pending, next, or end history index.</returns>
  private int GetNavigationAnchorLocked()
  {
    if (_pendingHistoryIndex is int pendingIndex)
    {
      return pendingIndex;
    }

    if (_activeKind == ActiveSpeechKind.History)
    {
      return _activeHistoryIndex;
    }

    return _nextHistoryIndex < _history.Count
      ? _nextHistoryIndex
      : _history.Count;
  }

  /// <summary>
  /// Finds the first history entry belonging to one accessibility node.
  /// </summary>
  /// <param name="index">An entry within the node.</param>
  /// <returns>The first entry index for that node.</returns>
  private int FindNodeStartLocked(int index)
  {
    long nodeId = _history[index].NodeId;
    int first = index;
    while (first > 0 && _history[first - 1].NodeId == nodeId)
    {
      --first;
    }

    return first;
  }

  /// <summary>
  /// Finds the final history entry belonging to one accessibility node.
  /// </summary>
  /// <param name="index">An entry within the node.</param>
  /// <returns>The final entry index for that node.</returns>
  private int FindNodeEndLocked(int index)
  {
    long nodeId = _history[index].NodeId;
    int last = index;
    while (last + 1 < _history.Count &&
           _history[last + 1].NodeId == nodeId)
    {
      ++last;
    }

    return last;
  }

  /// <summary>
  /// Joins an inclusive history range for activity logging.
  /// </summary>
  /// <param name="first">First history entry.</param>
  /// <param name="last">Last history entry.</param>
  /// <returns>Joined history text.</returns>
  private string JoinHistoryLocked(int first, int last)
  {
    return string.Join(
      " ",
      _history
        .Skip(first)
        .Take(last - first + 1)
        .Select(fragment => fragment.Text));
  }

  /// <summary>
  /// Bounds completed history without removing an active or pending entry.
  /// </summary>
  private void TrimHistoryLocked()
  {
    int excess = _history.Count - MaximumHistoryEntries;
    if (excess <= 0)
    {
      return;
    }

    int protectedIndex = _history.Count;
    if (_activeKind == ActiveSpeechKind.History)
    {
      protectedIndex = Math.Min(protectedIndex, _activeHistoryIndex);
    }

    if (_pendingHistoryIndex is int pendingIndex)
    {
      protectedIndex = Math.Min(protectedIndex, pendingIndex);
    }

    protectedIndex = Math.Min(protectedIndex, _nextHistoryIndex);
    int removable = Math.Min(excess, protectedIndex);
    if (removable <= 0)
    {
      return;
    }

    _history.RemoveRange(0, removable);
    if (_activeKind == ActiveSpeechKind.History)
    {
      _activeHistoryIndex -= removable;
    }

    if (_pendingHistoryIndex is int adjustedPending)
    {
      _pendingHistoryIndex = adjustedPending - removable;
    }

    _nextHistoryIndex = Math.Max(0, _nextHistoryIndex - removable);
  }

  /// <summary>
  /// Throws after disposal.
  /// </summary>
  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <summary>
  /// Identifies the one prompt currently owned by the serialized player.
  /// </summary>
  private enum ActiveSpeechKind
  {
    None,
    History,
    Untracked
  }
}
