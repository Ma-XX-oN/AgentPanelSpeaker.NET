from pathlib import Path
import sys

ROOT = Path.cwd()


def replace_once(path, old, new):
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"{path}: expected one exact replacement, found {count}")
  path.write_text(text.replace(old, new), encoding="utf-8", newline="")


issue73 = ROOT / "AgentPanelSpeaker" / "Issue73CtrlClickVoicePointerRegressionTestRunner.cs"
speech = ROOT / "AgentPanelSpeaker" / "SpeechService.cs"
main = ROOT / "AgentPanelSpeaker" / "MainForm.cs"


if sys.argv[1] == "tests":
  replace_once(
    issue73,
    '''      ("ctrl-click-voice-pointer/current-speech-eligibility-is-authoritative",
        TestCurrentSpeechEligibilityIsAuthoritative),
      ("ctrl-click-voice-pointer/active-seek-preserves-playing-state",
        TestActiveSeekPreservesPlayingState),
''',
    '''      ("ctrl-click-voice-pointer/current-speech-eligibility-is-authoritative",
        TestCurrentSpeechEligibilityIsAuthoritative),
      ("ctrl-click-voice-pointer/paused-seek-contract-remains-stable",
        TestPausedSeekContractRemainsStable),
      ("ctrl-click-voice-pointer/active-seek-preserves-playing-state",
        TestActiveSeekPreservesPlayingState),
''')

  old_method = '''  /// <summary>
  /// A Ctrl+click while playback is active changes the playback cursor but
  /// does not change transport state to paused.
  /// </summary>
  private static void TestActiveSeekPreservesPlayingState()
  {
    using var speech = new SpeechService();
    speech.SetPolicyProviders(
      _ => new SpeechProfileSettings("Test voice", 0, 0),
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.LoadHistory(
      new[]
      {
        new SpeechFragment(
          42,
          ContentCategory.Assistant,
          SpeechFragmentKind.Prose,
          "alpha beta",
          TranscriptWords: new[]
          {
            new SpeechFragmentWord(9001, "alpha", 0, 5),
            new SpeechFragmentWord(9002, "beta", 6, 4)
          })
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    FieldInfo pausedField = typeof(SpeechService).GetField(
      "_isPaused",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("SpeechService pause state is missing.");
    FieldInfo activeKindField = typeof(SpeechService).GetField(
      "_activeKind",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("SpeechService active kind is missing.");
    pausedField.SetValue(speech, false);
    activeKindField.SetValue(
      speech,
      Enum.Parse(activeKindField.FieldType, "History"));

    TranscriptPlaybackPosition? observed = null;
    speech.PlaybackPositionChanged += value => observed = value;
    Require(
      speech.TrySeekToTranscriptWord(9002, out string sought) &&
        string.Equals(sought, "alpha beta", StringComparison.Ordinal),
      "Active Core-word seek did not resolve the clicked word.");
    Require(!speech.IsPaused,
      "Active Core-word seek paused playback.");
    Require(observed is null ||
        observed.State != TranscriptPlaybackState.Paused,
      "Active Core-word seek emitted a paused playback position.");
  }
'''
  new_methods = '''  /// <summary>
  /// The authoritative general speech-history seek API remains a paused-marker
  /// operation even when the transport was active before the seek.
  /// </summary>
  private static void TestPausedSeekContractRemainsStable()
  {
    using var speech = new SpeechService();
    speech.SetPolicyProviders(
      _ => new SpeechProfileSettings("Test voice", 0, 0),
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.LoadHistory(
      new[]
      {
        new SpeechFragment(
          42,
          ContentCategory.Assistant,
          SpeechFragmentKind.Prose,
          "alpha beta",
          TranscriptWords: new[]
          {
            new SpeechFragmentWord(9001, "alpha", 0, 5),
            new SpeechFragmentWord(9002, "beta", 6, 4)
          })
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    FieldInfo pausedField = typeof(SpeechService).GetField(
      "_isPaused",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("SpeechService pause state is missing.");
    FieldInfo activeKindField = typeof(SpeechService).GetField(
      "_activeKind",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("SpeechService active kind is missing.");
    pausedField.SetValue(speech, false);
    activeKindField.SetValue(
      speech,
      Enum.Parse(activeKindField.FieldType, "History"));

    TranscriptPlaybackPosition? observed = null;
    speech.PlaybackPositionChanged += value => observed = value;
    Require(
      speech.TrySeekToTranscriptWord(9002, out string sought) &&
        string.Equals(sought, "alpha beta", StringComparison.Ordinal),
      "Paused Core-word seek did not resolve the selected word.");
    Require(speech.IsPaused,
      "Paused Core-word seek did not enter paused state.");
    Require(observed is not null &&
        observed.State == TranscriptPlaybackState.Paused &&
        string.Equals(observed.Word, "beta", StringComparison.Ordinal),
      "Paused Core-word seek did not publish the selected paused word.");
  }

  /// <summary>
  /// Ctrl+click is transport-sensitive: when playback is already active it
  /// redirects playback to the clicked word without changing transport to
  /// paused.
  /// </summary>
  private static void TestActiveSeekPreservesPlayingState()
  {
    using var form = new MainForm();
    _ = form.Handle;
    SpeechService speech =
      (SpeechService)(typeof(MainForm).GetField(
        "_speech",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ??
        throw new InvalidOperationException("MainForm speech service is missing."));
    speech.SetPolicyProviders(
      _ => new SpeechProfileSettings("Test voice", 0, 0),
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.LoadHistory(
      new[]
      {
        new SpeechFragment(
          42,
          ContentCategory.Assistant,
          SpeechFragmentKind.Prose,
          "alpha beta",
          TranscriptWords: new[]
          {
            new SpeechFragmentWord(9001, "alpha", 0, 5),
            new SpeechFragmentWord(9002, "beta", 6, 4)
          })
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    FieldInfo pausedField = typeof(SpeechService).GetField(
      "_isPaused",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("SpeechService pause state is missing.");
    FieldInfo activeKindField = typeof(SpeechService).GetField(
      "_activeKind",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("SpeechService active kind is missing.");
    pausedField.SetValue(speech, false);
    activeKindField.SetValue(
      speech,
      Enum.Parse(activeKindField.FieldType, "History"));

    MethodInfo handler = typeof(MainForm).GetMethod(
      "TranscriptFindSeekRequested",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "MainForm Ctrl+click seek handler is missing.");
    handler.Invoke(
      form,
      new object?[]
      {
        null,
        new FindSeekRequestedEventArgs(9002, "ctrl-click")
      });

    Require(!speech.IsPaused,
      "Active Ctrl+click changed transport state to paused.");
    FieldInfo pendingWordField = typeof(SpeechService).GetField(
      "_pendingHistoryWordIndex",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "SpeechService pending word index is missing.");
    Require((int)pendingWordField.GetValue(speech)! == 1,
      "Active Ctrl+click did not queue the exact clicked word.");
  }
'''
  replace_once(issue73, old_method, new_methods)
  sys.exit(0)


if sys.argv[1] != "production":
  raise RuntimeError("Expected tests or production mode.")

old_seek = '''  /// <summary>
  /// Moves the paused playback marker to one immutable Core transcript word ID
  /// when its containing fragment is currently eligible for speech.
  /// </summary>
  public bool TrySeekToTranscriptWord(long wordId, out string text)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      if (wordId < 1)
      {
        text = string.Empty;
        return false;
      }

      for (int index = 0; index < _history.Count; ++index)
      {
        SpeechFragment fragment = _history[index];
        IReadOnlyList<SpeechFragmentWord>? words = fragment.TranscriptWords;
        if (words is null)
        {
          continue;
        }
        int localWordIndex = -1;
        for (int candidate = 0; candidate < words.Count; ++candidate)
        {
          if (words[candidate].Id == wordId)
          {
            localWordIndex = candidate;
            break;
          }
        }
        if (localWordIndex < 0)
        {
          continue;
        }
        if (!TryGetEligibleProfileLocked(fragment, out _, out _))
        {
          text = string.Empty;
          return false;
        }

        bool preservePause = _isPaused;
        bool hadActiveSpeech = _activeKind != ActiveSpeechKind.None;
        _pendingUntracked = null;
        ClearProcessingTimeAnnouncementLocked();
        _pendingHistoryIndex = index;
        _pendingHistoryWordIndex = localWordIndex;
        _nextHistoryIndex = index;
        _lastFenceActivity = null;
        if (preservePause)
        {
          SetPausedNavigationPositionLocked(index, localWordIndex);
          if (hadActiveSpeech)
          {
            RequestPauseRestoreAfterCancellationLocked(
              "seek-canonical-transcript-word");
            _engine.Cancel();
          }
        }
        else
        {
          RestartPendingLocked();
        }
        text = fragment.Text;
        return true;
      }

      text = string.Empty;
      return false;
    }
  }
'''
new_seek = '''  /// <summary>
  /// Moves the paused playback marker to one immutable Core transcript word ID
  /// when its containing fragment is currently eligible for speech.
  /// </summary>
  public bool TrySeekToTranscriptWord(long wordId, out string text)
  {
    lock (_sync)
    {
      return TrySeekToTranscriptWordLocked(
        wordId,
        continueActivePlayback: false,
        out text);
    }
  }

  /// <summary>
  /// Seeks to one immutable Core transcript word while preserving active
  /// playback. If playback is not currently active, the ordinary paused-marker
  /// seek contract is retained.
  /// </summary>
  public bool TrySeekToTranscriptWordPreservingActivePlayback(
    long wordId,
    out string text)
  {
    lock (_sync)
    {
      bool continueActivePlayback =
        _activeKind != ActiveSpeechKind.None && !_isPaused;
      return TrySeekToTranscriptWordLocked(
        wordId,
        continueActivePlayback,
        out text);
    }
  }

  private bool TrySeekToTranscriptWordLocked(
    long wordId,
    bool continueActivePlayback,
    out string text)
  {
    ThrowIfDisposed();
    if (wordId < 1)
    {
      text = string.Empty;
      return false;
    }

    for (int index = 0; index < _history.Count; ++index)
    {
      SpeechFragment fragment = _history[index];
      IReadOnlyList<SpeechFragmentWord>? words = fragment.TranscriptWords;
      if (words is null)
      {
        continue;
      }
      int localWordIndex = -1;
      for (int candidate = 0; candidate < words.Count; ++candidate)
      {
        if (words[candidate].Id == wordId)
        {
          localWordIndex = candidate;
          break;
        }
      }
      if (localWordIndex < 0)
      {
        continue;
      }
      if (!TryGetEligibleProfileLocked(fragment, out _, out _))
      {
        text = string.Empty;
        return false;
      }

      bool hadActiveSpeech = _activeKind != ActiveSpeechKind.None;
      _pendingUntracked = null;
      ClearProcessingTimeAnnouncementLocked();
      _pendingHistoryIndex = index;
      _pendingHistoryWordIndex = localWordIndex;
      _nextHistoryIndex = index;
      _lastFenceActivity = null;
      if (continueActivePlayback)
      {
        RestartPendingLocked();
      }
      else
      {
        SetPausedLocked(true);
        SetPausedNavigationPositionLocked(index, localWordIndex);
        if (hadActiveSpeech)
        {
          RequestPauseRestoreAfterCancellationLocked(
            "seek-canonical-transcript-word");
          _engine.Cancel();
        }
      }
      text = fragment.Text;
      return true;
    }

    text = string.Empty;
    return false;
  }
'''
replace_once(speech, old_seek, new_seek)

replace_once(
  main,
  '''  /// <summary>
  /// Moves the paused speech marker to a voiced word selected by Find or by
  /// direct Ctrl+click transcript navigation.
  /// </summary>
  private void TranscriptFindSeekRequested(
    object? sender,
    FindSeekRequestedEventArgs eventArgs)
  {
    DiagnosticLog.Write("transcript.seek_requested", new
    {
      physicalInputId = eventArgs.Source == "ctrl-click"
        ? _inputDiagnostics.RecentMouseInputId
        : _inputDiagnostics.RecentInputId,
      eventArgs.Source,
      eventArgs.WordId
    });
    if (_speech.TrySeekToTranscriptWord(
          eventArgs.WordId,
          out string text))
    {
''',
  '''  /// <summary>
  /// Moves Find to a paused voiced word. Ctrl+click uses the same authoritative
  /// word seek while preserving playback only when playback is already active.
  /// </summary>
  private void TranscriptFindSeekRequested(
    object? sender,
    FindSeekRequestedEventArgs eventArgs)
  {
    DiagnosticLog.Write("transcript.seek_requested", new
    {
      physicalInputId = eventArgs.Source == "ctrl-click"
        ? _inputDiagnostics.RecentMouseInputId
        : _inputDiagnostics.RecentInputId,
      eventArgs.Source,
      eventArgs.WordId
    });
    string text;
    bool seeked = eventArgs.Source == "ctrl-click"
      ? _speech.TrySeekToTranscriptWordPreservingActivePlayback(
          eventArgs.WordId,
          out text)
      : _speech.TrySeekToTranscriptWord(eventArgs.WordId, out text);
    if (seeked)
    {
''')
