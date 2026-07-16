namespace AgentPanelSpeaker;

/// <summary>
/// Serializes speech, retains navigation history, and resolves playback policy
/// immediately before every fragment begins.
/// </summary>
internal sealed class SpeechService : IDisposable
{
  private const int MaximumHistoryEntries = 5000;

  private readonly object _sync = new();
  private readonly SapiSpeechEngine _engine = new();
  private readonly List<SpeechFragment> _history = new();
  private Func<ContentCategory, SpeechProfileSettings> _profileProvider =
    _ => new SpeechProfileSettings(
      SpeechProfileSettings.NotSpoken,
      0,
      0);
  private Func<string, bool> _fenceTypeProvider = _ => false;
  private Func<IReadOnlyList<string>> _spelledWordsProvider =
    () => Array.Empty<string>();
  private Func<PronunciationRuleSet> _pronunciationProvider =
    () => PronunciationRuleSet.Parse(string.Empty);
  private Func<AudioWakeSettings> _wakeSettingsProvider =
    () => AudioWakeSettings.Default;
  private ActiveSpeechKind _activeKind;
  private int _activeHistoryIndex = -1;
  private int _nextHistoryIndex;
  private int? _pendingHistoryIndex;
  private UntrackedSpeech? _pendingUntracked;
  private FenceActivityKey? _lastFenceActivity;
  private bool _reportedSpeaking;
  private bool _isPaused;
  private bool _disposed;

  /// <summary>
  /// Initializes output and completion handling.
  /// </summary>
  public SpeechService()
  {
    _engine.Completed += EngineCompleted;
    _engine.Faulted += EngineFaulted;
  }

  /// <summary>
  /// Raised when a fenced block is spoken or skipped.
  /// </summary>
  public event Action<string>? Activity;

  /// <summary>
  /// Raised when speech starts or fully stops.
  /// </summary>
  public event Action<bool>? SpeakingStateChanged;

  /// <summary>
  /// Gets whether the synthesizer is currently speaking.
  /// </summary>
  public bool IsSpeaking
  {
    get
    {
      lock (_sync)
      {
        return _activeKind != ActiveSpeechKind.None;
      }
    }
  }

  /// <summary>
  /// Gets whether the active utterance is paused.
  /// </summary>
  public bool IsPaused
  {
    get
    {
      lock (_sync)
      {
        return _isPaused;
      }
    }
  }

  /// <summary>
  /// Gets whether any indexed history exists.
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
  /// Gets enabled installed voices and their descriptive labels.
  /// </summary>
  public IReadOnlyList<InstalledSpeechVoice> GetInstalledVoices()
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      return _engine.Voices.ToArray();
    }
  }

  /// <summary>
  /// Installs thread-safe providers used before each fragment starts.
  /// </summary>
  public void SetPolicyProviders(
    Func<ContentCategory, SpeechProfileSettings> profileProvider,
    Func<string, bool> fenceTypeProvider,
    Func<IReadOnlyList<string>> spelledWordsProvider,
    Func<PronunciationRuleSet> pronunciationProvider,
    Func<AudioWakeSettings> wakeSettingsProvider)
  {
    ArgumentNullException.ThrowIfNull(profileProvider);
    ArgumentNullException.ThrowIfNull(fenceTypeProvider);
    ArgumentNullException.ThrowIfNull(spelledWordsProvider);
    ArgumentNullException.ThrowIfNull(pronunciationProvider);
    ArgumentNullException.ThrowIfNull(wakeSettingsProvider);
    lock (_sync)
    {
      _profileProvider = profileProvider;
      _fenceTypeProvider = fenceTypeProvider;
      _spelledWordsProvider = spelledWordsProvider;
      _pronunciationProvider = pronunciationProvider;
      _wakeSettingsProvider = wakeSettingsProvider;
    }
  }

  /// <summary>
  /// Starts a new monitored transcript session with empty history.
  /// </summary>
  public void BeginLiveSession()
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      _history.Clear();
      _pendingHistoryIndex = null;
      _pendingUntracked = null;
      _activeHistoryIndex = -1;
      _nextHistoryIndex = 0;
      _lastFenceActivity = null;
      if (_activeKind != ActiveSpeechKind.None)
      {
        CancelEngineLocked();
      }
    }
  }

  /// <summary>
  /// Replaces navigation history and applies the requested start mode.
  /// </summary>
  public void LoadHistory(
    IReadOnlyList<SpeechFragment> fragments,
    PlaybackStartMode startMode)
  {
    ArgumentNullException.ThrowIfNull(fragments);
    lock (_sync)
    {
      ThrowIfDisposed();
      _pendingHistoryIndex = null;
      _pendingUntracked = null;
      _activeHistoryIndex = -1;
      _lastFenceActivity = null;
      _history.Clear();

      int firstRetained = Math.Max(0, fragments.Count - MaximumHistoryEntries);
      foreach (SpeechFragment fragment in fragments.Skip(firstRetained))
      {
        string text = fragment.Text.Trim();
        if (text.Length != 0)
        {
          _history.Add(fragment with { Text = text });
        }
      }

      _nextHistoryIndex = startMode switch
      {
        PlaybackStartMode.Beginning => FindNextEligibleLocked(0),
        PlaybackStartMode.LastEnabledNode => FindLastEnabledNodeStartLocked(),
        _ => _history.Count
      };
      if (_nextHistoryIndex < 0)
      {
        _nextHistoryIndex = _history.Count;
      }

      if (_activeKind != ActiveSpeechKind.None)
      {
        CancelEngineLocked();
      }
      else
      {
        StartPendingOrNextLocked();
      }
    }
  }

  /// <summary>
  /// Appends one live fragment and starts it when currently eligible.
  /// </summary>
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
      bool wasAtLiveEnd = _nextHistoryIndex >= _history.Count;
      _history.Add(fragment with { Text = text });
      if (wasAtLiveEnd)
      {
        _nextHistoryIndex = _history.Count - 1;
      }
      TrimHistoryLocked();
      StartPendingOrNextLocked();
    }
  }

  /// <summary>
  /// Speaks a test phrase using one explicit profile.
  /// </summary>
  public void SpeakUntracked(string text, SpeechProfileSettings profile)
  {
    if (string.IsNullOrWhiteSpace(text) || !profile.IsSpoken)
    {
      return;
    }

    lock (_sync)
    {
      ThrowIfDisposed();
      _pendingHistoryIndex = null;
      _pendingUntracked = new UntrackedSpeech(text.Trim(), profile.Normalize());
      _nextHistoryIndex = _history.Count;
      RestartPendingLocked();
    }
  }

  /// <summary>
  /// Previews one IPA phone and its example using the central audio path.
  /// </summary>
  public void PreviewIpa(
    string? isolatedIpa,
    string exampleWord,
    string exampleIpa,
    SpeechProfileSettings profile,
    AudioWakeSettings wakeSettings)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(exampleWord);
    ArgumentException.ThrowIfNullOrWhiteSpace(exampleIpa);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(wakeSettings);

    lock (_sync)
    {
      ThrowIfDisposed();
      if (_activeKind != ActiveSpeechKind.None || !profile.IsSpoken)
      {
        return;
      }

      SpeechProfileSettings normalized = profile.Normalize();
      SpeechMarkup? isolatedMarkup = string.IsNullOrWhiteSpace(isolatedIpa)
        ? null
        : SpeechSapiXmlBuilder.BuildIpaPreview(
          isolatedIpa,
          isolatedIpa,
          normalized.Pitch);
      SpeechMarkup exampleMarkup = SpeechSapiXmlBuilder.BuildIpaPreview(
        exampleWord,
        exampleIpa,
        normalized.Pitch);
      SetActiveKindLocked(ActiveSpeechKind.Untracked);
      try
      {
        _engine.PreviewIpa(
          isolatedMarkup,
          exampleMarkup,
          normalized,
          wakeSettings.Normalize());
      }
      catch
      {
        SetActiveKindLocked(ActiveSpeechKind.None);
        throw;
      }
    }
  }

  /// <summary>
  /// Plays a wake-tone-only test through the central audio worker.
  /// </summary>
  public void TestWakeTone(AudioWakeSettings wakeSettings)
  {
    ArgumentNullException.ThrowIfNull(wakeSettings);
    lock (_sync)
    {
      ThrowIfDisposed();
      if (_activeKind != ActiveSpeechKind.None)
      {
        return;
      }

      SetActiveKindLocked(ActiveSpeechKind.Untracked);
      try
      {
        _engine.TestWakeTone(wakeSettings.Normalize());
      }
      catch
      {
        SetActiveKindLocked(ActiveSpeechKind.None);
        throw;
      }
    }
  }

  /// <summary>
  /// Plays a forced wake prefix and test phrase as one contiguous stream.
  /// </summary>
  public void TestWakePhrase(
    string text,
    SpeechProfileSettings profile,
    AudioWakeSettings wakeSettings)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(text);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(wakeSettings);
    lock (_sync)
    {
      ThrowIfDisposed();
      if (_activeKind != ActiveSpeechKind.None || !profile.IsSpoken)
      {
        return;
      }

      SpeechProfileSettings normalized = profile.Normalize();
      SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
        text.Trim(),
        normalized.Pitch,
        _spelledWordsProvider(),
        _pronunciationProvider());
      SetActiveKindLocked(ActiveSpeechKind.Untracked);
      try
      {
        _engine.TestWakePhrase(
          markup,
          normalized,
          wakeSettings.Normalize());
      }
      catch
      {
        SetActiveKindLocked(ActiveSpeechKind.None);
        throw;
      }
    }
  }

  /// <summary>
  /// Moves one eligible fragment backward and continues playback.
  /// </summary>
  public bool TryRewindSentence(out string text)
  {
    lock (_sync)
    {
      int anchor = GetNavigationAnchorLocked();
      int candidate = FindPreviousEligibleLocked(
        anchor >= _history.Count ? _history.Count - 1 : anchor - 1);
      return RestartCandidateLocked(candidate, out text);
    }
  }

  /// <summary>
  /// Moves one eligible fragment forward and continues playback.
  /// </summary>
  public bool TryForwardSentence(out string text)
  {
    lock (_sync)
    {
      int anchor = GetNavigationAnchorLocked();
      int candidate = FindNextEligibleLocked(anchor + 1);
      if (candidate < 0)
      {
        MoveToLiveEndLocked();
        text = string.Empty;
        return false;
      }
      return RestartCandidateLocked(candidate, out text);
    }
  }

  /// <summary>
  /// Moves to the previous node containing an eligible fragment.
  /// </summary>
  public bool TryRewindNode(out string text)
  {
    lock (_sync)
    {
      int anchor = GetNavigationAnchorLocked();
      int candidate = anchor >= _history.Count
        ? _history.Count - 1
        : FindNodeStartLocked(Math.Max(0, anchor)) - 1;
      while (candidate >= 0)
      {
        int start = FindNodeStartLocked(candidate);
        int eligible = FindNextEligibleLocked(start, FindNodeEndLocked(start));
        if (eligible >= 0)
        {
          text = JoinEligibleNodeLocked(start);
          RestartHistoryLocked(eligible);
          return true;
        }
        candidate = start - 1;
      }

      text = string.Empty;
      return false;
    }
  }

  /// <summary>
  /// Moves to the next node containing an eligible fragment.
  /// </summary>
  public bool TryForwardNode(out string text)
  {
    lock (_sync)
    {
      int anchor = GetNavigationAnchorLocked();
      if (anchor >= _history.Count)
      {
        MoveToLiveEndLocked();
        text = string.Empty;
        return false;
      }

      int candidate = FindNodeEndLocked(anchor) + 1;
      while (candidate < _history.Count)
      {
        int end = FindNodeEndLocked(candidate);
        int eligible = FindNextEligibleLocked(candidate, end);
        if (eligible >= 0)
        {
          text = JoinEligibleNodeLocked(candidate);
          RestartHistoryLocked(eligible);
          return true;
        }
        candidate = end + 1;
      }

      MoveToLiveEndLocked();
      text = string.Empty;
      return false;
    }
  }

  /// <summary>
  /// Cancels speech and returns playback to the live end.
  /// </summary>
  public void CancelAll()
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      MoveToLiveEndLocked();
    }
  }

  /// <summary>
  /// Pauses or resumes the active utterance.
  /// </summary>
  public PauseToggleResult TogglePause()
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      if (_activeKind == ActiveSpeechKind.None)
      {
        return PauseToggleResult.Unavailable;
      }

      if (_isPaused)
      {
        _engine.Resume();
        SetPausedLocked(false);
        return PauseToggleResult.Resumed;
      }

      _engine.Pause();
      SetPausedLocked(true);
      return PauseToggleResult.Paused;
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
      _engine.Completed -= EngineCompleted;
      _engine.Faulted -= EngineFaulted;
      _engine.Cancel();
      _engine.Dispose();
    }
  }

  /// <summary>
  /// Advances serialized playback after one prompt completes or is cancelled.
  /// </summary>
  private void EngineCompleted()
  {
    lock (_sync)
    {
      if (_disposed)
      {
        return;
      }
      SetPausedLocked(false);
      _activeKind = ActiveSpeechKind.None;
      _activeHistoryIndex = -1;
      TrimHistoryLocked();
      StartPendingOrNextLocked();
      if (_activeKind == ActiveSpeechKind.None)
      {
        SetActiveKindLocked(ActiveSpeechKind.None);
      }
    }
  }

  /// <summary>
  /// Reports a SAPI worker failure without leaving playback stuck active.
  /// </summary>
  private void EngineFaulted(Exception exception)
  {
    DiagnosticLog.Write("speech.engine_fault", new
    {
      exception = exception.ToString()
    });
    Activity?.Invoke($"Speech synthesis failed: {exception.Message}");
  }

  /// <summary>
  /// Starts pending text or advances to the next currently eligible fragment.
  /// </summary>
  private void StartPendingOrNextLocked()
  {
    if (_activeKind != ActiveSpeechKind.None)
    {
      return;
    }

    if (_pendingUntracked is not null)
    {
      UntrackedSpeech pending = _pendingUntracked;
      _pendingUntracked = null;
      StartSpeechLocked(
        ActiveSpeechKind.Untracked,
        pending.Text,
        pending.Profile);
      return;
    }

    if (_pendingHistoryIndex is int pendingIndex)
    {
      _nextHistoryIndex = pendingIndex;
      _pendingHistoryIndex = null;
    }

    while (_nextHistoryIndex < _history.Count)
    {
      int index = _nextHistoryIndex++;
      SpeechFragment fragment = _history[index];
      if (!TryGetEligibleProfileLocked(
            fragment,
            out SpeechProfileSettings profile,
            out string reason))
      {
        ReportFenceActivityLocked(fragment, spoken: false, reason);
        continue;
      }

      _activeHistoryIndex = index;
      StartSpeechLocked(
        ActiveSpeechKind.History,
        fragment.Text,
        profile);
      ReportFenceActivityLocked(fragment, spoken: true, string.Empty);
      return;
    }
  }

  /// <summary>
  /// Starts one prompt and restores idle state when configuration fails.
  /// </summary>
  private void StartSpeechLocked(
    ActiveSpeechKind kind,
    string text,
    SpeechProfileSettings profile)
  {
    SetActiveKindLocked(kind);
    try
    {
      SpeakConfiguredLocked(text, profile);
    }
    catch
    {
      _activeHistoryIndex = -1;
      SetActiveKindLocked(ActiveSpeechKind.None);
      throw;
    }
  }

  /// <summary>
  /// Applies voice, rate, pitch, and volume before speaking.
  /// </summary>
  private void SpeakConfiguredLocked(
    string text,
    SpeechProfileSettings profile)
  {
    SpeechProfileSettings normalized = profile.Normalize();
    IReadOnlyList<string> spelledWords = _spelledWordsProvider();
    PronunciationRuleSet pronunciations = _pronunciationProvider();
    AudioWakeSettings wakeSettings = _wakeSettingsProvider().Normalize();
    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
      text,
      normalized.Pitch,
      spelledWords,
      pronunciations);
    DiagnosticLog.Write("speech.configure", new
    {
      normalized.VoiceName,
      normalized.Rate,
      normalized.Pitch,
      normalized.Volume,
      spelledWordCount = spelledWords.Count,
      pronunciationCount = pronunciations.Rules.Count,
      wakeEnabled = wakeSettings.Enabled
    });
    _engine.Speak(markup, normalized, wakeSettings);
  }

  /// <summary>
  /// Updates pause state and refreshes dependent controls.
  /// </summary>
  private void SetPausedLocked(bool paused)
  {
    if (_isPaused == paused)
    {
      return;
    }
    _isPaused = paused;
    SpeakingStateChanged?.Invoke(_activeKind != ActiveSpeechKind.None);
  }

  /// <summary>
  /// Cancels the active speech provider and clears pause state.
  /// </summary>
  private void CancelEngineLocked()
  {
    SetPausedLocked(false);
    _engine.Cancel();
  }

  /// <summary>
  /// Sets active speech state and reports transitions.
  /// </summary>
  private void SetActiveKindLocked(ActiveSpeechKind kind)
  {
    _activeKind = kind;
    bool isSpeaking = kind != ActiveSpeechKind.None;
    if (_reportedSpeaking != isSpeaking)
    {
      _reportedSpeaking = isSpeaking;
      SpeakingStateChanged?.Invoke(isSpeaking);
    }
  }

  /// <summary>
  /// Resolves current playback eligibility for one fragment.
  /// </summary>
  private bool TryGetEligibleProfileLocked(
    SpeechFragment fragment,
    out SpeechProfileSettings profile,
    out string reason)
  {
    profile = _profileProvider(fragment.Category).Normalize();
    if (!profile.IsSpoken)
    {
      reason = $"{fragment.Category.ToString().ToLowerInvariant()} text is not spoken";
      return false;
    }

    if (fragment.Kind == SpeechFragmentKind.FencedCodeLine &&
        !_fenceTypeProvider(fragment.FenceType))
    {
      reason = "type is not enabled";
      return false;
    }

    reason = string.Empty;
    return true;
  }

  /// <summary>
  /// Reports one outcome per fenced block encountered during playback.
  /// </summary>
  private void ReportFenceActivityLocked(
    SpeechFragment fragment,
    bool spoken,
    string reason)
  {
    if (fragment.Kind != SpeechFragmentKind.FencedCodeLine)
    {
      _lastFenceActivity = null;
      return;
    }

    var key = new FenceActivityKey(
      fragment.NodeId,
      fragment.FenceBlockId,
      spoken,
      reason);
    if (_lastFenceActivity == key)
    {
      return;
    }

    _lastFenceActivity = key;
    string message = spoken
      ? $"Spoken fenced block: type={fragment.FenceType}; " +
        $"non-empty lines={fragment.FenceLineCount}."
      : $"Skipped fenced block: type={fragment.FenceType}; reason={reason}.";
    DiagnosticLog.Write("speech.fenced_block", new
    {
      spoken,
      fragment.FenceType,
      fragment.FenceLineCount,
      reason
    });
    Activity?.Invoke(message);
  }

  /// <summary>
  /// Cancels active playback and places navigation after the final fragment.
  /// </summary>
  private void MoveToLiveEndLocked()
  {
    _pendingHistoryIndex = null;
    _pendingUntracked = null;
    _nextHistoryIndex = _history.Count;
    _lastFenceActivity = null;
    if (_activeKind != ActiveSpeechKind.None)
    {
      CancelEngineLocked();
    }
  }

  /// <summary>
  /// Restarts playback from one history index.
  /// </summary>
  private void RestartHistoryLocked(int index)
  {
    _pendingUntracked = null;
    _pendingHistoryIndex = index;
    _nextHistoryIndex = index;
    _lastFenceActivity = null;
    RestartPendingLocked();
  }

  /// <summary>
  /// Cancels the active prompt or starts a pending request immediately.
  /// </summary>
  private void RestartPendingLocked()
  {
    if (_activeKind != ActiveSpeechKind.None)
    {
      CancelEngineLocked();
    }
    else
    {
      StartPendingOrNextLocked();
    }
  }

  /// <summary>
  /// Restarts one valid candidate and returns its text.
  /// </summary>
  private bool RestartCandidateLocked(int candidate, out string text)
  {
    if (candidate < 0 || candidate >= _history.Count)
    {
      text = string.Empty;
      return false;
    }
    text = _history[candidate].Text;
    RestartHistoryLocked(candidate);
    return true;
  }

  /// <summary>
  /// Gets the active, pending, next, or end navigation anchor.
  /// </summary>
  private int GetNavigationAnchorLocked()
  {
    if (_pendingHistoryIndex is int pending)
    {
      return pending;
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
  /// Finds the next eligible fragment from a starting index.
  /// </summary>
  private int FindNextEligibleLocked(int start, int? inclusiveEnd = null)
  {
    int end = inclusiveEnd ?? (_history.Count - 1);
    for (int index = Math.Max(0, start);
         index < _history.Count && index <= end;
         ++index)
    {
      if (TryGetEligibleProfileLocked(_history[index], out _, out _))
      {
        return index;
      }
    }
    return -1;
  }

  /// <summary>
  /// Finds the previous eligible fragment from a starting index.
  /// </summary>
  private int FindPreviousEligibleLocked(int start)
  {
    for (int index = Math.Min(start, _history.Count - 1); index >= 0; --index)
    {
      if (TryGetEligibleProfileLocked(_history[index], out _, out _))
      {
        return index;
      }
    }
    return -1;
  }

  /// <summary>
  /// Finds the first eligible fragment of the final enabled node.
  /// </summary>
  private int FindLastEnabledNodeStartLocked()
  {
    int eligible = FindPreviousEligibleLocked(_history.Count - 1);
    if (eligible < 0)
    {
      return _history.Count;
    }
    int start = FindNodeStartLocked(eligible);
    int firstEligible = FindNextEligibleLocked(start, FindNodeEndLocked(start));
    return firstEligible < 0 ? _history.Count : firstEligible;
  }

  /// <summary>
  /// Finds the first entry belonging to one JSONL node.
  /// </summary>
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
  /// Finds the final entry belonging to one JSONL node.
  /// </summary>
  private int FindNodeEndLocked(int index)
  {
    long nodeId = _history[index].NodeId;
    int last = index;
    while (last + 1 < _history.Count && _history[last + 1].NodeId == nodeId)
    {
      ++last;
    }
    return last;
  }

  /// <summary>
  /// Joins currently eligible entries in one node for activity text.
  /// </summary>
  private string JoinEligibleNodeLocked(int start)
  {
    int end = FindNodeEndLocked(start);
    return string.Join(
      " ",
      _history
        .Skip(start)
        .Take(end - start + 1)
        .Where(fragment => TryGetEligibleProfileLocked(
          fragment,
          out _,
          out _))
        .Select(fragment => fragment.Text));
  }

  /// <summary>
  /// Bounds completed history without removing active or pending entries.
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
    if (_pendingHistoryIndex is int pending)
    {
      protectedIndex = Math.Min(protectedIndex, pending);
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
    if (_pendingHistoryIndex is int adjusted)
    {
      _pendingHistoryIndex = adjusted - removable;
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

  private enum ActiveSpeechKind
  {
    None,
    History,
    Untracked
  }

  private sealed record UntrackedSpeech(
    string Text,
    SpeechProfileSettings Profile);

  private sealed record FenceActivityKey(
    long NodeId,
    int FenceBlockId,
    bool Spoken,
    string Reason);
}

/// <summary>
/// Describes the result of a pause/resume toggle.
/// </summary>
internal enum PauseToggleResult
{
  Unavailable,
  Paused,
  Resumed
}
