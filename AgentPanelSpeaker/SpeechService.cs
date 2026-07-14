using System.Speech.Synthesis;

namespace AgentPanelSpeaker;

/// <summary>
/// Queues Windows SAPI speech and retains sentence and node replay history.
/// </summary>
internal sealed class SpeechService : IDisposable
{
  private const int MaximumHistoryEntries = 1000;

  private readonly SpeechSynthesizer _synthesizer = new();
  private readonly List<SpeechFragment> _history = new();
  private int _rewindIndex;
  private bool _disposed;

  /// <summary>
  /// Initializes speech output to the default audio device.
  /// </summary>
  public SpeechService()
  {
    _synthesizer.SetOutputToDefaultAudioDevice();
  }

  /// <summary>
  /// Gets whether replay history contains any live transcript speech.
  /// </summary>
  public bool HasHistory => _history.Count != 0;

  /// <summary>
  /// Gets installed enabled voice names.
  /// </summary>
  /// <returns>Voice names in display order.</returns>
  public IReadOnlyList<string> GetInstalledVoiceNames()
  {
    ThrowIfDisposed();

    return _synthesizer
      .GetInstalledVoices()
      .Where(voice => voice.Enabled)
      .Select(voice => voice.VoiceInfo.Name)
      .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
      .ToArray();
  }

  /// <summary>
  /// Selects the voice and speaking rate.
  /// </summary>
  /// <param name="voiceName">Installed voice name.</param>
  /// <param name="rate">Speech rate from -10 through 10.</param>
  public void Configure(string voiceName, int rate)
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

  /// <summary>
  /// Queues a live transcript fragment and records it for replay.
  /// </summary>
  /// <param name="fragment">Live fragment.</param>
  public void SpeakLive(SpeechFragment fragment)
  {
    ArgumentNullException.ThrowIfNull(fragment);
    ThrowIfDisposed();

    string text = fragment.Text.Trim();
    if (text.Length == 0)
    {
      return;
    }

    _history.Add(fragment with { Text = text });
    TrimHistory();
    _rewindIndex = _history.Count;
    _synthesizer.SpeakAsync(text);
  }

  /// <summary>
  /// Queues untracked text, such as the voice test phrase.
  /// </summary>
  /// <param name="text">Text to speak.</param>
  public void SpeakUntracked(string text)
  {
    ThrowIfDisposed();

    if (!string.IsNullOrWhiteSpace(text))
    {
      _synthesizer.SpeakAsync(text.Trim());
    }
  }

  /// <summary>
  /// Cancels queued speech and replays the previous sentence.
  /// </summary>
  /// <param name="text">Replayed sentence.</param>
  /// <returns>True when sentence history was available.</returns>
  public bool TryRewindSentence(out string text)
  {
    ThrowIfDisposed();
    text = string.Empty;

    int candidate = _rewindIndex >= _history.Count
      ? _history.Count - 1
      : _rewindIndex - 1;
    if (candidate < 0)
    {
      return false;
    }

    _rewindIndex = candidate;
    text = _history[candidate].Text;
    Replay(text);
    return true;
  }

  /// <summary>
  /// Cancels queued speech and replays the previous accessibility node.
  /// </summary>
  /// <param name="text">Replayed node text.</param>
  /// <returns>True when node history was available.</returns>
  public bool TryRewindNode(out string text)
  {
    ThrowIfDisposed();
    text = string.Empty;

    int candidate = _rewindIndex >= _history.Count
      ? _history.Count - 1
      : _rewindIndex - 1;
    if (candidate < 0)
    {
      return false;
    }

    long nodeId = _history[candidate].NodeId;
    int first = candidate;
    while (first > 0 && _history[first - 1].NodeId == nodeId)
    {
      --first;
    }

    int last = candidate;
    while (last + 1 < _history.Count &&
           _history[last + 1].NodeId == nodeId)
    {
      ++last;
    }

    _rewindIndex = first;
    text = string.Join(
      " ",
      _history
        .Skip(first)
        .Take(last - first + 1)
        .Select(fragment => fragment.Text));
    Replay(text);
    return true;
  }

  /// <summary>
  /// Cancels current and queued speech.
  /// </summary>
  public void CancelAll()
  {
    ThrowIfDisposed();
    _synthesizer.SpeakAsyncCancelAll();
  }

  /// <summary>
  /// Cancels speech and releases the synthesizer.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _synthesizer.SpeakAsyncCancelAll();
    _synthesizer.Dispose();
    _disposed = true;
  }

  /// <summary>
  /// Cancels pending speech and speaks one historical selection.
  /// </summary>
  /// <param name="text">Historical text to replay.</param>
  private void Replay(string text)
  {
    _synthesizer.SpeakAsyncCancelAll();
    _synthesizer.SpeakAsync(text);
  }

  /// <summary>
  /// Bounds replay history while preserving the rewind cursor.
  /// </summary>
  private void TrimHistory()
  {
    int excess = _history.Count - MaximumHistoryEntries;
    if (excess <= 0)
    {
      return;
    }

    _history.RemoveRange(0, excess);
    _rewindIndex = Math.Max(0, _rewindIndex - excess);
  }

  /// <summary>
  /// Throws after disposal.
  /// </summary>
  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
