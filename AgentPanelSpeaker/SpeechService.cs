using System.Diagnostics;
using System.Text.RegularExpressions;

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
  private readonly List<TurnCompletion> _turnCompletions = new();
  private readonly Dictionary<string, BackgroundWorkState> _backgroundWork =
    new(StringComparer.Ordinal);
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
  private ProcessingTimeAnnouncement? _pendingProcessingTime;
  private bool _processingTimeAnnouncementRequested;
  private FenceActivityKey? _lastFenceActivity;
  private bool _reportedSpeaking;
  private bool _isPaused;
  private string _activeTranscriptText = string.Empty;
  private int _activeWordIndex;
  private int _activeWordBaseIndex;
  private int _activeCharacterBaseOffset;
  private int _activeCharacterPosition;
  private int _activeCharacterCount;
  private long _activeBoundaryTimestamp;
  private string _activeWord = string.Empty;
  private DateTimeOffset? _pauseStartedUtc;
  private SpeechProfileSettings? _activeProfile;
  private bool _activePauseAfter;
  private bool _disposed;

  /// <summary>
  /// Initializes output and completion handling.
  /// </summary>
  public SpeechService()
  {
    _engine.Completed += EngineCompleted;
    _engine.Faulted += EngineFaulted;
    _engine.Notice += EngineNotice;
    _engine.WordBoundary += EngineWordBoundary;
  }

  /// <summary>
  /// Raised when a fenced block is spoken or skipped.
  /// </summary>
  public event Action<string>? Activity;

  /// <summary>
  /// Raised when active-utterance or playback-pause state changes.  The event
  /// value reports whether an utterance is currently active.
  /// </summary>
  public event Action<bool>? SpeakingStateChanged;

  /// <summary>
  /// Raised when a processing-time announcement becomes pending or completes.
  /// </summary>
  public event Action<bool>? ProcessingTimeAnnouncementStateChanged;

  /// <summary>
  /// Raised when the rendered transcript marker should move or change state.
  /// </summary>
  public event Action<TranscriptPlaybackPosition>? PlaybackPositionChanged;

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
  /// Gets whether monitored playback is paused, including while waiting at
  /// the current live end.
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
  /// Gets whether a processing-time announcement is queued or speaking.
  /// </summary>
  public bool IsProcessingTimeAnnouncementPending
  {
    get
    {
      lock (_sync)
      {
        return _processingTimeAnnouncementRequested;
      }
    }
  }

  /// <summary>
  /// Gets whether the current playback position has enough timing data for an
  /// AI-processing-time announcement.
  /// </summary>
  public bool CanRequestProcessingTimeAnnouncement
  {
    get
    {
      lock (_sync)
      {
        return !_processingTimeAnnouncementRequested &&
          TryBuildProcessingTimeAnnouncementLocked(
            DateTimeOffset.UtcNow,
            out _,
            out _,
            out _);
      }
    }
  }

  /// <summary>
  /// Sets the speech worker's transcript word-boundary polling interval.
  /// </summary>
  public void SetWordBoundaryPollMilliseconds(int milliseconds)
  {
    _engine.SetWordBoundaryPollMilliseconds(milliseconds);
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
      _turnCompletions.Clear();
      _backgroundWork.Clear();
      _pendingHistoryIndex = null;
      _pendingUntracked = null;
      ClearProcessingTimeAnnouncementLocked();
      _activeHistoryIndex = -1;
      _nextHistoryIndex = 0;
      _lastFenceActivity = null;
      SetPausedLocked(false);
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
    IReadOnlyList<TurnCompletion> completions,
    IReadOnlyList<BackgroundWorkEvent> backgroundWorkEvents,
    PlaybackStartMode startMode)
  {
    ArgumentNullException.ThrowIfNull(fragments);
    ArgumentNullException.ThrowIfNull(completions);
    ArgumentNullException.ThrowIfNull(backgroundWorkEvents);
    lock (_sync)
    {
      ThrowIfDisposed();
      _pendingHistoryIndex = null;
      _pendingUntracked = null;
      ClearProcessingTimeAnnouncementLocked();
      _activeHistoryIndex = -1;
      _lastFenceActivity = null;
      _history.Clear();
      _turnCompletions.Clear();
      _backgroundWork.Clear();
      foreach (BackgroundWorkEvent workEvent in
               backgroundWorkEvents.OrderBy(item => item.StartUtc))
      {
        RegisterBackgroundWorkEventLocked(workEvent);
      }
      _turnCompletions.AddRange(
        completions
          .OrderBy(completion => completion.TimestampUtc)
          .TakeLast(MaximumHistoryEntries));

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
        PlaybackStartMode.LatestTurn => FindLatestTurnStartLocked(),
        _ => _history.Count
      };
      if (_nextHistoryIndex < 0)
      {
        _nextHistoryIndex = _history.Count;
      }

      SetPausedLocked(false);
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
  /// Retains a source-provided terminal completion marker for timing queries.
  /// </summary>
  public void RegisterTurnCompletion(TurnCompletion completion)
  {
    ArgumentNullException.ThrowIfNull(completion);
    lock (_sync)
    {
      ThrowIfDisposed();
      _turnCompletions.Add(completion);
      _turnCompletions.Sort(static (left, right) =>
        left.TimestampUtc.CompareTo(right.TimestampUtc));
      if (_turnCompletions.Count > MaximumHistoryEntries)
      {
        _turnCompletions.RemoveRange(
          0,
          _turnCompletions.Count - MaximumHistoryEntries);
      }
    }
  }

  /// <summary>
  /// Retains one background-agent lifecycle update for timing queries.
  /// </summary>
  public void RegisterBackgroundWorkEvent(BackgroundWorkEvent workEvent)
  {
    ArgumentNullException.ThrowIfNull(workEvent);
    lock (_sync)
    {
      ThrowIfDisposed();
      RegisterBackgroundWorkEventLocked(workEvent);
    }
  }

  /// <summary>
  /// Queues an AI-processing-time announcement at the next node boundary.
  /// </summary>
  public bool TryQueueProcessingTimeAnnouncement(
    out string announcement,
    out string unavailableReason)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      if (_processingTimeAnnouncementRequested)
      {
        announcement = string.Empty;
        unavailableReason = "an announcement is already pending";
        return false;
      }

      DateTimeOffset requestedAtUtc = DateTimeOffset.UtcNow;
      if (!TryBuildProcessingTimeAnnouncementLocked(
            requestedAtUtc,
            out announcement,
            out SpeechProfileSettings profile,
            out ProcessingTimeContext context))
      {
        unavailableReason = context.UnavailableReason;
        return false;
      }

      long? waitForNodeId = _activeKind == ActiveSpeechKind.History &&
        _activeHistoryIndex >= 0 &&
        _activeHistoryIndex < _history.Count
          ? _history[_activeHistoryIndex].NodeId
          : null;
      _pendingProcessingTime = new ProcessingTimeAnnouncement(
        announcement,
        profile,
        waitForNodeId,
        context.UserNodeId,
        context.IsLatestTurn,
        context.IsProcessing,
        context.StartUtc,
        context.EndUtc);
      _processingTimeAnnouncementRequested = true;
      ProcessingTimeAnnouncementStateChanged?.Invoke(true);
      DiagnosticLog.Write("processing_time.requested", new
      {
        announcement,
        context.UserNodeId,
        context.IsLatestTurn,
        context.IsProcessing,
        context.StartUtc,
        context.EndUtc,
        waitForNodeId
      });
      unavailableReason = string.Empty;
      StartPendingOrNextLocked();
      return true;
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
  /// Previews ordinary text without spelling or pronunciation overrides.
  /// </summary>
  public void PreviewText(
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
        Array.Empty<string>(),
        PronunciationRuleSet.Parse(string.Empty));
      SetActiveKindLocked(ActiveSpeechKind.Untracked);
      try
      {
        _engine.PreviewText(
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
  /// Previews one IPA phone and its example using the central audio path.
  /// </summary>
  public void PreviewIpa(
    string? isolatedIpa,
    string exampleWord,
    string exampleIpa,
    string? exampleFallbackText,
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
      SpeechMarkup? exampleFallbackMarkup =
        string.IsNullOrWhiteSpace(exampleFallbackText)
          ? null
          : SpeechSapiXmlBuilder.Build(
            exampleFallbackText,
            normalized.Pitch,
            Array.Empty<string>(),
            PronunciationRuleSet.Parse(string.Empty));
      SetActiveKindLocked(ActiveSpeechKind.Untracked);
      try
      {
        _engine.PreviewIpa(
          isolatedMarkup,
          exampleMarkup,
          exampleFallbackMarkup,
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
  /// Moves to the beginning of the preceding opposite-speaker run.
  /// </summary>
  public bool TryRewindSpeaker(out string text)
  {
    lock (_sync)
    {
      if (_history.Count == 0)
      {
        text = string.Empty;
        return false;
      }

      int anchor = GetNavigationAnchorLocked();
      if (anchor >= _history.Count)
      {
        int finalCandidate = _history.Count - 1;
        while (finalCandidate >= 0)
        {
          int runStart = FindSpeakerRunStartLocked(finalCandidate);
          int runEnd = FindSpeakerRunEndLocked(finalCandidate);
          int eligible = FindNextEligibleLocked(runStart, runEnd);
          if (eligible >= 0)
          {
            text = JoinEligibleSpeakerRunLocked(runStart);
            RestartHistoryLocked(eligible);
            return true;
          }
          finalCandidate = runStart - 1;
        }

        text = string.Empty;
        return false;
      }

      bool currentIsUser = IsUserSpeaker(_history[anchor].Category);
      int candidate = FindSpeakerRunStartLocked(anchor) - 1;
      while (candidate >= 0)
      {
        int runStart = FindSpeakerRunStartLocked(candidate);
        int runEnd = FindSpeakerRunEndLocked(candidate);
        if (IsUserSpeaker(_history[runStart].Category) != currentIsUser)
        {
          int eligible = FindNextEligibleLocked(runStart, runEnd);
          if (eligible >= 0)
          {
            text = JoinEligibleSpeakerRunLocked(runStart);
            RestartHistoryLocked(eligible);
            return true;
          }
        }
        candidate = runStart - 1;
      }

      text = string.Empty;
      return false;
    }
  }

  /// <summary>
  /// Moves to the beginning of the following opposite-speaker run.
  /// </summary>
  public bool TryForwardSpeaker(out string text)
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

      bool currentIsUser = IsUserSpeaker(_history[anchor].Category);
      int candidate = FindSpeakerRunEndLocked(anchor) + 1;
      while (candidate < _history.Count)
      {
        int runStart = candidate;
        int runEnd = FindSpeakerRunEndLocked(candidate);
        if (IsUserSpeaker(_history[runStart].Category) != currentIsUser)
        {
          int eligible = FindNextEligibleLocked(runStart, runEnd);
          if (eligible >= 0)
          {
            text = JoinEligibleSpeakerRunLocked(runStart);
            RestartHistoryLocked(eligible);
            return true;
          }
        }
        candidate = runEnd + 1;
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
  /// Pauses or resumes playback.  An idle pause is allowed while monitoring is
  /// waiting at the current live end.
  /// </summary>
  public PauseToggleResult TogglePause(bool allowIdlePause = false)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      if (_isPaused)
      {
        bool hasActiveUtterance = _activeKind != ActiveSpeechKind.None;
        bool restartWord = hasActiveUtterance &&
          _activeKind == ActiveSpeechKind.History &&
          _pauseStartedUtc is DateTimeOffset pauseStart &&
          DateTimeOffset.UtcNow - pauseStart > TimeSpan.FromSeconds(1);
        SetPausedLocked(false);
        _pauseStartedUtc = null;
        if (restartWord)
        {
          RestartCurrentWordLocked();
        }
        else if (hasActiveUtterance)
        {
          _engine.Resume();
          ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);
        }
        else
        {
          StartPendingOrNextLocked();
        }
        return PauseToggleResult.Resumed;
      }

      if (_activeKind == ActiveSpeechKind.None && !allowIdlePause)
      {
        return PauseToggleResult.Unavailable;
      }

      if (_activeKind != ActiveSpeechKind.None)
      {
        _engine.Pause();
        _pauseStartedUtc = DateTimeOffset.UtcNow;
        SetPausedLocked(true);
        ReportPlaybackPositionLocked(TranscriptPlaybackState.Paused);
      }
      else
      {
        SetPausedLocked(true);
        _pauseStartedUtc = DateTimeOffset.UtcNow;
        ReportPlaybackPositionLocked(
          TranscriptPlaybackState.PausedAtLiveEnd);
      }
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
      _engine.Notice -= EngineNotice;
      _engine.WordBoundary -= EngineWordBoundary;
      _engine.Cancel();
      _engine.Dispose();
    }
  }

  /// <summary>
  /// Advances the rendered transcript marker at one audio word boundary.
  /// </summary>
  private void EngineWordBoundary(SpeechWordBoundary boundary)
  {
    lock (_sync)
    {
      if (_disposed || _activeKind != ActiveSpeechKind.History)
      {
        return;
      }
      int absoluteCharacterPosition = checked(
        _activeCharacterBaseOffset + Math.Max(0, boundary.CharacterPosition));
      _activeWordIndex = GetTokenIndexForBoundary(
        _activeTranscriptText,
        absoluteCharacterPosition,
        boundary.CharacterCount,
        _activeWordBaseIndex + boundary.WordIndex);
      _activeCharacterPosition = absoluteCharacterPosition;
      _activeCharacterCount = Math.Max(0, boundary.CharacterCount);
      _activeBoundaryTimestamp = Stopwatch.GetTimestamp();
      _activeWord = GetTokenAtIndex(
        _activeTranscriptText,
        _activeWordIndex,
        boundary.Text);
      if (ShouldTracePlayback(_activeTranscriptText))
      {
        DiagnosticLog.Write("speech.word_boundary", new
        {
          nodeId = _activeHistoryIndex >= 0 &&
            _activeHistoryIndex < _history.Count
              ? _history[_activeHistoryIndex].NodeId
              : -1,
          boundary.WordIndex,
          boundary.CharacterPosition,
          boundary.CharacterCount,
          boundary.Text,
          boundary.AudioPosition,
          boundary.Exact,
          absoluteCharacterPosition,
          mappedWordIndex = _activeWordIndex,
          mappedWord = _activeWord,
          timestamp = _activeBoundaryTimestamp
        });
      }
      ReportPlaybackPositionLocked(
        _isPaused
          ? TranscriptPlaybackState.Paused
          : TranscriptPlaybackState.Speaking);
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

      ActiveSpeechKind completedKind = _activeKind;
      ProcessingTimeAnnouncement? completedProcessing =
        completedKind == ActiveSpeechKind.ProcessingTime
          ? _pendingProcessingTime
          : null;
      SetPausedLocked(false);
      _activeKind = ActiveSpeechKind.None;
      _activeHistoryIndex = -1;
      if (completedKind == ActiveSpeechKind.ProcessingTime)
      {
        DiagnosticLog.Write("processing_time.spoken", new
        {
          text = completedProcessing?.Text,
          userNodeId = completedProcessing?.UserNodeId,
          isLatestTurn = completedProcessing?.IsLatestTurn,
          isProcessing = completedProcessing?.IsProcessing,
          startUtc = completedProcessing?.StartUtc,
          endUtc = completedProcessing?.EndUtc
        });
        ClearProcessingTimeAnnouncementLocked();
      }

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
  /// Reports a non-fatal speech fallback selected by the engine.
  /// </summary>
  private void EngineNotice(string message)
  {
    Activity?.Invoke(message);
  }

  /// <summary>
  /// Merges one background-work start or completion by stable identifier.
  /// </summary>
  private void RegisterBackgroundWorkEventLocked(BackgroundWorkEvent workEvent)
  {
    if (_backgroundWork.TryGetValue(
          workEvent.Id,
          out BackgroundWorkState? existing) &&
        existing is not null)
    {
      string description = workEvent.Description.Length != 0
        ? workEvent.Description
        : existing.Description;
      DateTimeOffset startUtc = workEvent.StartUtc < existing.StartUtc
        ? workEvent.StartUtc
        : existing.StartUtc;
      DateTimeOffset? endUtc = workEvent.EndUtc ?? existing.EndUtc;
      _backgroundWork[workEvent.Id] = new BackgroundWorkState(
        workEvent.Id,
        description,
        startUtc,
        endUtc,
        IsChildRunId(workEvent.Id));
      return;
    }

    _backgroundWork[workEvent.Id] = new BackgroundWorkState(
      workEvent.Id,
      workEvent.Description,
      workEvent.StartUtc,
      workEvent.EndUtc,
      IsChildRunId(workEvent.Id));
  }

  /// <summary>
  /// Builds the processing-time sentence for the turn containing the playback
  /// cursor.
  /// </summary>
  private bool TryBuildProcessingTimeAnnouncementLocked(
    DateTimeOffset requestedAtUtc,
    out string announcement,
    out SpeechProfileSettings profile,
    out ProcessingTimeContext context)
  {
    announcement = string.Empty;
    profile = _profileProvider(ContentCategory.User).Normalize();
    if (!profile.IsSpoken)
    {
      context = ProcessingTimeContext.Unavailable(
        "the User messages voice is set to Not Spoken");
      return false;
    }

    if (_history.Count == 0)
    {
      context = ProcessingTimeContext.Unavailable(
        "no conversational history is available");
      return false;
    }

    int anchor = GetNavigationAnchorLocked();
    int referenceIndex = anchor >= _history.Count
      ? _history.Count - 1
      : Math.Max(0, anchor);
    int userIndex = FindUserAtOrBeforeLocked(referenceIndex);
    if (userIndex < 0)
    {
      context = ProcessingTimeContext.Unavailable(
        "the selected response has no retained User prompt");
      return false;
    }

    int userStart = FindNodeStartLocked(userIndex);
    SpeechFragment userFragment = _history[userStart];
    if (userFragment.NodeTimestampUtc is not DateTimeOffset startUtc)
    {
      context = ProcessingTimeContext.Unavailable(
        "the selected User prompt has no source timestamp");
      return false;
    }

    int latestUserIndex = FindUserAtOrBeforeLocked(_history.Count - 1);
    bool isLatestTurn = latestUserIndex >= 0 &&
      _history[FindNodeStartLocked(latestUserIndex)].NodeId ==
        userFragment.NodeId;
    int responseStart = FindNodeEndLocked(userStart) + 1;
    DateTimeOffset? nextUserUtc = FindNextUserTimestampLocked(responseStart);
    IReadOnlyList<BackgroundWorkState> turnWork = GetTurnBackgroundWorkLocked(
      startUtc,
      nextUserUtc);
    bool hasRunningBackgroundWork = turnWork.Any(work => work.EndUtc is null);
    bool isProcessing = false;
    DateTimeOffset endUtc = default;
    bool hasCompletedEndpoint = !hasRunningBackgroundWork &&
      (TryFindTurnCompletionLocked(
        startUtc,
        nextUserUtc,
        out endUtc) ||
       TryFindUserFacingResponseTailLocked(responseStart, out endUtc));
    if (!hasCompletedEndpoint)
    {
      if (!isLatestTurn && !hasRunningBackgroundWork)
      {
        context = ProcessingTimeContext.Unavailable(
          "the selected response has no completed user-facing AI message");
        return false;
      }
      endUtc = requestedAtUtc;
      isProcessing = true;
    }

    if (endUtc < startUtc)
    {
      context = ProcessingTimeContext.Unavailable(
        "the selected response has inconsistent timestamps");
      return false;
    }

    string duration = FormatProcessingDuration(endUtc - startUtc);
    announcement = isProcessing
      ? $"AI has been processing for {duration}."
      : $"AI has processed for {duration}.";
    string backgroundAnnouncement = BuildBackgroundWorkAnnouncement(
      turnWork,
      requestedAtUtc);
    if (backgroundAnnouncement.Length != 0)
    {
      announcement += " " + backgroundAnnouncement;
    }
    context = new ProcessingTimeContext(
      userFragment.NodeId,
      isLatestTurn,
      isProcessing,
      startUtc,
      endUtc,
      string.Empty);
    return true;
  }

  /// <summary>
  /// Finds the nearest actual User-turn start at or before one history
  /// position.
  /// </summary>
  private int FindUserAtOrBeforeLocked(int startIndex)
  {
    for (int index = Math.Min(startIndex, _history.Count - 1);
         index >= 0;
         --index)
    {
      if (_history[index].StartsUserTurn)
      {
        return index;
      }
    }
    return -1;
  }

  /// <summary>
  /// Finds the first terminal marker belonging to the selected User turn.
  /// </summary>
  private bool TryFindTurnCompletionLocked(
    DateTimeOffset startUtc,
    DateTimeOffset? nextUserUtc,
    out DateTimeOffset endUtc)
  {
    foreach (TurnCompletion completion in _turnCompletions)
    {
      if (completion.TimestampUtc < startUtc)
      {
        continue;
      }
      if (nextUserUtc is not null &&
          completion.TimestampUtc >= nextUserUtc.Value)
      {
        break;
      }
      endUtc = completion.TimestampUtc;
      return true;
    }

    endUtc = default;
    return false;
  }

  /// <summary>
  /// Gets the next actual User-turn timestamp that bounds the selected
  /// response.
  /// </summary>
  private DateTimeOffset? FindNextUserTimestampLocked(int responseStart)
  {
    for (int index = Math.Max(0, responseStart);
         index < _history.Count;
         ++index)
    {
      SpeechFragment fragment = _history[index];
      if (fragment.StartsUserTurn)
      {
        return fragment.NodeTimestampUtc;
      }
    }
    return null;
  }

  /// <summary>
  /// Uses the response tail only when the last retained AI message is
  /// user-facing rather than reasoning/thinking.
  /// </summary>
  private bool TryFindUserFacingResponseTailLocked(
    int responseStart,
    out DateTimeOffset endUtc)
  {
    ContentCategory? finalCategory = null;
    DateTimeOffset? finalTimestamp = null;
    for (int index = Math.Max(0, responseStart);
         index < _history.Count;
         ++index)
    {
      SpeechFragment fragment = _history[index];
      if (fragment.StartsUserTurn)
      {
        break;
      }
      finalCategory = fragment.Category;
      finalTimestamp = fragment.NodeTimestampUtc;
    }

    if ((finalCategory is ContentCategory.Assistant or
           ContentCategory.SubagentAssistant) &&
        finalTimestamp is DateTimeOffset timestamp)
    {
      endUtc = timestamp;
      return true;
    }

    endUtc = default;
    return false;
  }

  /// <summary>
  /// Gets background work whose start belongs to one retained User turn.
  /// </summary>
  private IReadOnlyList<BackgroundWorkState> GetTurnBackgroundWorkLocked(
    DateTimeOffset startUtc,
    DateTimeOffset? nextUserUtc)
  {
    return _backgroundWork.Values
      .Where(work =>
        work.StartUtc >= startUtc &&
        (nextUserUtc is null || work.StartUtc < nextUserUtc.Value))
      .OrderBy(work => work.StartUtc)
      .ToArray();
  }

  /// <summary>
  /// Appends running and recently completed background-agent timing.
  /// </summary>
  private static string BuildBackgroundWorkAnnouncement(
    IReadOnlyList<BackgroundWorkState> work,
    DateTimeOffset requestedAtUtc)
  {
    if (work.Count == 0)
    {
      return string.Empty;
    }

    BackgroundWorkState[] running = work
      .Where(item => item.EndUtc is null)
      .ToArray();
    BackgroundWorkState[] completedParents = work
      .Where(item => item.EndUtc is not null && !item.IsChildRun)
      .ToArray();
    int completedChildCount = work.Count(item =>
      item.EndUtc is not null && item.IsChildRun);
    var sentences = new List<string>();
    if (running.Length != 0)
    {
      if (running.Length > 1)
      {
        sentences.Add($"{running.Length} subagents are running.");
      }
      foreach (BackgroundWorkState item in running)
      {
        string name = item.Description.Length == 0
          ? "Subagent"
          : $"Subagent “{item.Description}”";
        sentences.Add(
          $"{name} has been running for " +
          $"{FormatDetailedDuration(requestedAtUtc - item.StartUtc)}.");
      }
    }

    foreach (BackgroundWorkState item in completedParents)
    {
      TimeSpan duration = item.EndUtc!.Value - item.StartUtc;
      string name = item.Description.Length == 0
        ? "The background agent"
        : $"Background agent “{item.Description}”";
      sentences.Add(
        $"{name} ran for {FormatDetailedDuration(duration)} and completed.");
    }
    if (completedChildCount != 0)
    {
      sentences.Add(completedChildCount == 1
        ? "1 child-agent run completed."
        : $"{completedChildCount} child-agent runs completed.");
    }
    return string.Join(" ", sentences);
  }

  /// <summary>
  /// Formats background-agent duration with second precision.
  /// </summary>
  private static string FormatDetailedDuration(TimeSpan duration)
  {
    long totalSeconds = Math.Max(0L, (long)Math.Round(duration.TotalSeconds));
    long hours = totalSeconds / 3600;
    long minutes = totalSeconds % 3600 / 60;
    long seconds = totalSeconds % 60;
    var parts = new List<string>();
    if (hours != 0)
    {
      parts.Add(hours == 1 ? "1 hour" : $"{hours} hours");
    }
    if (minutes != 0)
    {
      parts.Add(minutes == 1 ? "1 minute" : $"{minutes} minutes");
    }
    if (seconds != 0 || parts.Count == 0)
    {
      parts.Add(seconds == 1 ? "1 second" : $"{seconds} seconds");
    }
    return parts.Count == 1
      ? parts[0]
      : string.Join(", ", parts.Take(parts.Count - 1)) +
        " and " + parts[^1];
  }

  /// <summary>
  /// Identifies child task-notification run identifiers.
  /// </summary>
  private static bool IsChildRunId(string id)
  {
    return id.Contains('@');
  }

  /// <summary>
  /// Formats completed minutes without noisy seconds.
  /// </summary>
  private static string FormatProcessingDuration(TimeSpan duration)
  {
    long totalMinutes = Math.Max(0L, (long)Math.Floor(duration.TotalMinutes));
    if (totalMinutes == 0)
    {
      return "less than one minute";
    }
    if (totalMinutes < 60)
    {
      return totalMinutes == 1
        ? "1 minute"
        : $"{totalMinutes} minutes";
    }

    long hours = totalMinutes / 60;
    long minutes = totalMinutes % 60;
    string hoursText = hours == 1 ? "1 hour" : $"{hours} hours";
    if (minutes == 0)
    {
      return hoursText;
    }
    string minutesText = minutes == 1
      ? "1 minute"
      : $"{minutes} minutes";
    return $"{hoursText} and {minutesText}";
  }

  /// <summary>
  /// Returns whether the current JSONL node has finished speaking.
  /// </summary>
  private bool CanStartProcessingTimeAnnouncementLocked(
    ProcessingTimeAnnouncement announcement)
  {
    if (announcement.WaitForNodeId is not long nodeId)
    {
      return true;
    }

    int nextEligible = FindNextEligibleLocked(_nextHistoryIndex);
    return nextEligible < 0 || _history[nextEligible].NodeId != nodeId;
  }

  /// <summary>
  /// Clears queued/active processing-time state and refreshes its button.
  /// </summary>
  private void ClearProcessingTimeAnnouncementLocked()
  {
    bool changed = _processingTimeAnnouncementRequested;
    _processingTimeAnnouncementRequested = false;
    _pendingProcessingTime = null;
    if (changed)
    {
      ProcessingTimeAnnouncementStateChanged?.Invoke(false);
    }
  }

  /// <summary>
  /// Starts pending text or advances to the next currently eligible fragment.
  /// </summary>
  private void StartPendingOrNextLocked()
  {
    if (_isPaused || _activeKind != ActiveSpeechKind.None)
    {
      return;
    }

    if (_pendingProcessingTime is not null &&
        CanStartProcessingTimeAnnouncementLocked(_pendingProcessingTime))
    {
      ProcessingTimeAnnouncement pending = _pendingProcessingTime;
      StartSpeechLocked(
        ActiveSpeechKind.ProcessingTime,
        pending.Text,
        pending.Profile,
        pauseAfter: false);
      return;
    }

    if (_pendingUntracked is not null)
    {
      UntrackedSpeech pending = _pendingUntracked;
      _pendingUntracked = null;
      StartSpeechLocked(
        ActiveSpeechKind.Untracked,
        pending.Text,
        pending.Profile,
        pauseAfter: false);
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
        profile,
        fragment.PauseAfter);
      ReportFenceActivityLocked(fragment, spoken: true, string.Empty);
      return;
    }

    _activeTranscriptText = string.Empty;
    _activeWord = string.Empty;
    _activeWordIndex = 0;
    _activeWordBaseIndex = 0;
    ReportPlaybackPositionLocked(
      _isPaused
        ? TranscriptPlaybackState.PausedAtLiveEnd
        : TranscriptPlaybackState.WaitingAtLiveEnd);
  }

  /// <summary>
  /// Starts one prompt and restores idle state when configuration fails.
  /// </summary>
  private void StartSpeechLocked(
    ActiveSpeechKind kind,
    string text,
    SpeechProfileSettings profile,
    bool pauseAfter)
  {
    _activeTranscriptText = text;
    _activeWordIndex = 0;
    _activeWordBaseIndex = 0;
    _activeCharacterBaseOffset = 0;
    _activeCharacterPosition = 0;
    _activeCharacterCount = FirstWord(text).Length;
    _activeBoundaryTimestamp = Stopwatch.GetTimestamp();
    _activeWord = FirstWord(text);
    _activeProfile = profile.Normalize();
    _activePauseAfter = pauseAfter;
    _pauseStartedUtc = null;
    SetActiveKindLocked(kind);
    ReportPlaybackPositionLocked(
      kind == ActiveSpeechKind.History
        ? TranscriptPlaybackState.Speaking
        : TranscriptPlaybackState.None);
    try
    {
      SpeakConfiguredLocked(text, profile, pauseAfter);
    }
    catch
    {
      _activeHistoryIndex = -1;
      if (kind == ActiveSpeechKind.ProcessingTime)
      {
        ClearProcessingTimeAnnouncementLocked();
      }
      SetActiveKindLocked(ActiveSpeechKind.None);
      throw;
    }
  }

  /// <summary>
  /// Applies voice, rate, pitch, and volume before speaking.
  /// </summary>
  private void SpeakConfiguredLocked(
    string text,
    SpeechProfileSettings profile,
    bool pauseAfter)
  {
    SpeechProfileSettings normalized = profile.Normalize();
    IReadOnlyList<string> spelledWords = _spelledWordsProvider();
    PronunciationRuleSet pronunciations = _pronunciationProvider();
    AudioWakeSettings wakeSettings = _wakeSettingsProvider().Normalize();
    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
      text,
      normalized.Pitch,
      spelledWords,
      pronunciations,
      pauseAfter);
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
  /// Restarts a long-paused history utterance at the current word boundary.
  /// </summary>
  private void RestartCurrentWordLocked()
  {
    if (_activeProfile is null || _activeTranscriptText.Length == 0)
    {
      _engine.Resume();
      return;
    }
    int start = GetWordCharacterPosition(
      _activeTranscriptText,
      _activeWordIndex);
    string remaining = _activeTranscriptText[start..];
    _activeWordBaseIndex = _activeWordIndex;
    _activeCharacterBaseOffset = start;
    _activeCharacterPosition = start;
    _activeCharacterCount = FirstWord(remaining).Length;
    _activeBoundaryTimestamp = Stopwatch.GetTimestamp();
    _activeWord = FirstWord(remaining);
    SpeakConfiguredLocked(
      remaining,
      _activeProfile,
      _activePauseAfter);
    ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);
  }

  private void ReportPlaybackPositionLocked(TranscriptPlaybackState state)
  {
    if (state is TranscriptPlaybackState.None)
    {
      return;
    }
    long nodeId = _activeHistoryIndex >= 0 &&
      _activeHistoryIndex < _history.Count
        ? _history[_activeHistoryIndex].NodeId
        : -1;
    PlaybackPositionChanged?.Invoke(new TranscriptPlaybackPosition(
      state,
      _activeTranscriptText,
      _activeWordIndex,
      _activeWord,
      nodeId,
      _activeHistoryIndex >= 0 && _activeHistoryIndex < _history.Count
        ? _history[_activeHistoryIndex].SegmentIndex
        : -1,
      _activeCharacterPosition,
      _activeCharacterCount,
      _activeBoundaryTimestamp));
  }


  private static int GetTokenIndexForBoundary(
    string text,
    int characterPosition,
    int characterCount,
    int fallbackWordIndex)
  {
    MatchCollection matches = SpeechTokenization.Matches(text);
    if (matches.Count == 0)
    {
      return 0;
    }

    int boundedPosition = Math.Clamp(characterPosition, 0, text.Length);
    int boundaryEnd = Math.Clamp(
      checked(boundedPosition + Math.Max(1, characterCount)),
      boundedPosition,
      text.Length);
    for (int index = 0; index < matches.Count; ++index)
    {
      Match match = matches[index];
      int matchEnd = match.Index + match.Length;
      if (match.Index < boundaryEnd && matchEnd > boundedPosition)
      {
        return index;
      }
    }

    for (int index = 0; index < matches.Count; ++index)
    {
      if (matches[index].Index >= boundedPosition)
      {
        return index;
      }
    }

    return Math.Clamp(fallbackWordIndex, 0, matches.Count - 1);
  }

  private static string GetTokenAtIndex(
    string text,
    int wordIndex,
    string fallback)
  {
    MatchCollection matches = SpeechTokenization.Matches(text);
    return wordIndex >= 0 && wordIndex < matches.Count
      ? matches[wordIndex].Value
      : fallback;
  }

  private static bool ShouldTracePlayback(string text)
  {
    return text.Contains(
      "PolicyMachinery.hpp already has sections",
      StringComparison.OrdinalIgnoreCase);
  }

  private static int GetWordCharacterPosition(string text, int wordIndex)
  {
    MatchCollection matches = SpeechTokenization.Matches(text);
    if (matches.Count == 0)
    {
      return 0;
    }
    int bounded = Math.Clamp(wordIndex, 0, matches.Count - 1);
    return matches[bounded].Index;
  }

  private static string FirstWord(string text)
  {
    return SpeechTokenization.First(text);
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
    ClearProcessingTimeAnnouncementLocked();
    _nextHistoryIndex = _history.Count;
    _lastFenceActivity = null;
    SetPausedLocked(false);
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
    ClearProcessingTimeAnnouncementLocked();
    _pendingHistoryIndex = index;
    _nextHistoryIndex = index;
    _lastFenceActivity = null;
    SetPausedLocked(false);
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
  /// Finds the final actual User prompt so initial playback includes the
  /// complete latest turn, including input-selection narration.
  /// </summary>
  private int FindLatestTurnStartLocked()
  {
    for (int index = _history.Count - 1; index >= 0; --index)
    {
      if (!_history[index].StartsUserTurn)
      {
        continue;
      }

      int eligible = FindNextEligibleLocked(index);
      return eligible < 0 ? _history.Count : eligible;
    }

    return FindLastEnabledNodeStartLocked();
  }

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
  /// Finds the first fragment in one consecutive User or AI speaker run.
  /// </summary>
  private int FindSpeakerRunStartLocked(int index)
  {
    bool isUser = IsUserSpeaker(_history[index].Category);
    int first = index;
    while (first > 0 &&
           IsUserSpeaker(_history[first - 1].Category) == isUser)
    {
      --first;
    }
    return first;
  }

  /// <summary>
  /// Finds the final fragment in one consecutive User or AI speaker run.
  /// </summary>
  private int FindSpeakerRunEndLocked(int index)
  {
    bool isUser = IsUserSpeaker(_history[index].Category);
    int last = index;
    while (last + 1 < _history.Count &&
           IsUserSpeaker(_history[last + 1].Category) == isUser)
    {
      ++last;
    }
    return last;
  }

  /// <summary>
  /// Joins every currently eligible fragment in one speaker run.
  /// </summary>
  private string JoinEligibleSpeakerRunLocked(int start)
  {
    int end = FindSpeakerRunEndLocked(start);
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
  /// Groups AI categories opposite both User main and User context speech.
  /// </summary>
  private static bool IsUserSpeaker(ContentCategory category)
  {
    return category is ContentCategory.User or ContentCategory.UserContext;
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
    Untracked,
    ProcessingTime
  }

  private sealed record UntrackedSpeech(
    string Text,
    SpeechProfileSettings Profile);

  private sealed record ProcessingTimeAnnouncement(
    string Text,
    SpeechProfileSettings Profile,
    long? WaitForNodeId,
    long UserNodeId,
    bool IsLatestTurn,
    bool IsProcessing,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

  private sealed record BackgroundWorkState(
    string Id,
    string Description,
    DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc,
    bool IsChildRun);

  private sealed record ProcessingTimeContext(
    long UserNodeId,
    bool IsLatestTurn,
    bool IsProcessing,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string UnavailableReason)
  {
    public static ProcessingTimeContext Unavailable(string reason)
    {
      return new ProcessingTimeContext(
        0,
        false,
        false,
        default,
        default,
        reason);
    }
  }

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
