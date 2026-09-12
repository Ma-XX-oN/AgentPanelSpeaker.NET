from pathlib import Path
import re
import sys

ROOT = Path.cwd()


def replace_once(path, old, new):
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"{path}: expected one exact replacement, found {count}")
  path.write_text(text.replace(old, new), encoding="utf-8", newline="")


def regex_once(path, pattern, replacement):
  text = path.read_text(encoding="utf-8")
  updated, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
  if count != 1:
    raise RuntimeError(
      f"{path}: expected one regex replacement, found {count}")
  path.write_text(updated, encoding="utf-8", newline="")


issue75 = ROOT / "AgentPanelSpeaker" / "Issue75CoreWordIdMigrationRegressionTestRunner.cs"
issue73 = ROOT / "AgentPanelSpeaker" / "Issue73CtrlClickVoicePointerRegressionTestRunner.cs"
transcript = ROOT / "AgentPanelSpeaker" / "TranscriptView.cs"
speech = ROOT / "AgentPanelSpeaker" / "SpeechService.cs"
tracker = ROOT / "AgentPanelSpeaker" / "InputDiagnosticTracker.cs"
main = ROOT / "AgentPanelSpeaker" / "MainForm.cs"


if sys.argv[1] == "tests":
  replace_once(
    issue75,
    '''    Require(!shell.Contains("applyVoiceEligibilityClasses", StringComparison.Ordinal),
      "Browser still performs a transcript-wide per-word eligibility scan.");
''',
    '''    Require(!shell.Contains("applyVoiceEligibilityClasses", StringComparison.Ordinal),
      "Browser still performs a transcript-wide per-word eligibility scan.");
    Require(!shell.Contains("markAlignedVoiceSelectableWords(", StringComparison.Ordinal),
      "Browser still calls the removed legacy aligned-word eligibility stamper.");
''')

  replace_once(
    issue73,
    '''      ("ctrl-click-voice-pointer/current-speech-eligibility-is-authoritative",
        TestCurrentSpeechEligibilityIsAuthoritative),
''',
    '''      ("ctrl-click-voice-pointer/current-speech-eligibility-is-authoritative",
        TestCurrentSpeechEligibilityIsAuthoritative),
      ("ctrl-click-voice-pointer/active-seek-preserves-playing-state",
        TestActiveSeekPreservesPlayingState),
      ("ctrl-click-voice-pointer/physical-click-correlation-is-fresh",
        TestPhysicalClickCorrelationIsFresh),
''')

  marker = '''  /// <summary>
  /// Browser node-global coordinates must use the same speech-token ordinal
'''
  methods = '''  /// <summary>
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

  /// <summary>
  /// A trusted WebView Ctrl+click must enter the shared physical-input
  /// timeline before the seek command so command correlation cannot reuse a
  /// stale mouse identity from an unrelated native control.
  /// </summary>
  private static void TestPhysicalClickCorrelationIsFresh()
  {
    MethodInfo shellMethod = typeof(TranscriptView).GetMethod(
      "BuildShellHtml",
      BindingFlags.NonPublic | BindingFlags.Static) ??
      throw new InvalidOperationException(
        "TranscriptView.BuildShellHtml() was not found.");
    string shell = shellMethod.Invoke(null, null) as string ??
      throw new InvalidOperationException("Transcript shell was empty.");
    int seekIndex = shell.IndexOf("source:'ctrl-click'", StringComparison.Ordinal);
    Require(seekIndex >= 0, "Ctrl+click browser handler is missing.");
    int start = Math.Max(0, seekIndex - 1500);
    string block = shell.Substring(
      start,
      Math.Min(shell.Length - start, seekIndex - start + 300));
    Require(block.Contains("type:'physical-mouse-click'", StringComparison.Ordinal) &&
        block.Contains("event.isTrusted", StringComparison.Ordinal),
      "Ctrl+click does not emit a trusted WebView physical mouse event.");

    EventInfo? inputEvent = typeof(TranscriptView).GetEvent(
      "PhysicalMouseClickInput",
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Require(inputEvent is not null,
      "TranscriptView exposes no physical Ctrl+click input event.");

    MethodInfo? observe = typeof(InputDiagnosticTracker).GetMethod(
      "ObserveWebViewMouseClick",
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Require(observe is not null,
      "InputDiagnosticTracker cannot record a WebView mouse click.");
    var input = new InputDiagnosticTracker();
    object?[] arguments =
    {
      Keys.Control,
      false,
      true,
      false,
      "SPAN",
      "word-9002"
    };
    long first = Convert.ToInt64(observe!.Invoke(input, arguments));
    long second = Convert.ToInt64(observe.Invoke(input, arguments));
    Require(first > 0 && second > first,
      "WebView mouse clicks did not receive fresh physical input IDs.");
    Require(input.RecentMouseInputId == second,
      "Most recent mouse input did not advance to the trusted WebView click.");
  }

'''
  replace_once(issue73, marker, methods + marker)
  sys.exit(0)


if sys.argv[1] != "production":
  raise RuntimeError("Expected tests or production mode.")

# The Core word-ID migration removed all local eligibility stamping. This stale
# fallback call has no definition and aborts a virtual-window browser transaction
# as soon as lexical alignment reaches this branch.
replace_once(
  transcript,
  '''        markAlignedVoiceSelectableWords(
          recordLexicalWords,
          lexicalAlignment,
          nodeId,
          segmentNodeWordStart,
          speechTokenOffsets);
''',
  "")

# Preserve the transport state across an exact Core-word seek.
replace_once(
  speech,
  '''        bool hadActiveSpeech = _activeKind != ActiveSpeechKind.None;
        _pendingUntracked = null;
        ClearProcessingTimeAnnouncementLocked();
        _pendingHistoryIndex = index;
        _pendingHistoryWordIndex = localWordIndex;
        _nextHistoryIndex = index;
        _lastFenceActivity = null;
        SetPausedLocked(true);
        SetPausedNavigationPositionLocked(index, localWordIndex);
        if (hadActiveSpeech)
        {
          RequestPauseRestoreAfterCancellationLocked(
            "seek-canonical-transcript-word");
          _engine.Cancel();
        }
        text = fragment.Text;
        return true;
''',
  '''        bool preservePause = _isPaused;
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
''')

pattern = r'''  public long ObserveWebViewWheel\(.*?\n  \}\n\n  /// <summary>\n  /// Returns the most recent physical identity for one key\.'''
replacement = '''  public long ObserveWebViewWheel(
    int delta,
    Keys modifiers,
    bool followSpeech,
    bool speaking,
    bool paused,
    string targetTag,
    string targetId)
  {
    return ObserveWebViewMouse(
      "wheel",
      "vertical-wheel",
      delta,
      modifiers,
      followSpeech,
      speaking,
      paused,
      targetTag,
      targetId);
  }

  /// <summary>
  /// Records one trusted left-click consumed inside WebView2 and returns its
  /// identity in the shared physical-input timeline.
  /// </summary>
  public long ObserveWebViewMouseClick(
    Keys modifiers,
    bool followSpeech,
    bool speaking,
    bool paused,
    string targetTag,
    string targetId)
  {
    return ObserveWebViewMouse(
      "click",
      "left",
      0,
      modifiers,
      followSpeech,
      speaking,
      paused,
      targetTag,
      targetId);
  }

  private long ObserveWebViewMouse(
    string phase,
    string code,
    int delta,
    Keys modifiers,
    bool followSpeech,
    bool speaking,
    bool paused,
    string targetTag,
    string targetId)
  {
    long inputId = NextInputId();
    _recentMouseInputId = inputId;
    _recentInputId = inputId;
    DiagnosticLog.Write("input.physical", new
    {
      inputId,
      kind = "mouse",
      phase,
      code,
      modifiers = modifiers.ToString(),
      repeat = false,
      delta,
      route = "webview",
      nativeMessage = (string?)null,
      targetHwnd = (string?)null,
      targetType = string.IsNullOrWhiteSpace(targetTag)
        ? "WebView2.DOM"
        : $"WebView2.DOM.{targetTag}",
      targetName = string.IsNullOrWhiteSpace(targetId) ? null : targetId,
      targetPath = "MainForm/TranscriptView/WebView2",
      followSpeech,
      speaking,
      paused,
      timestamp = Stopwatch.GetTimestamp()
    });
    return inputId;
  }

  /// <summary>
  /// Returns the most recent physical identity for one key.'''
regex_once(tracker, pattern, replacement)

replace_once(
  transcript,
  '''  /// <summary>
  /// Raised for a vertical-wheel input received inside WebView2.
  /// </summary>
  public event Action<int, Keys, string, string>? PhysicalWheelInput;
''',
  '''  /// <summary>
  /// Raised for a vertical-wheel input received inside WebView2.
  /// </summary>
  public event Action<int, Keys, string, string>? PhysicalWheelInput;

  /// <summary>
  /// Raised for a trusted left-click received inside WebView2.
  /// </summary>
  public event Action<Keys, string, string>? PhysicalMouseClickInput;
''')

replace_once(
  transcript,
  '''      if (type == "physical-wheel")
      {
        int delta = (int)Math.Round(ReadOptionalDouble(root, "delta") ?? 0);
        Keys modifiers = Keys.None;
        if (ReadOptionalBoolean(root, "ctrlKey") == true)
        {
          modifiers |= Keys.Control;
        }
        if (ReadOptionalBoolean(root, "altKey") == true)
        {
          modifiers |= Keys.Alt;
        }
        if (ReadOptionalBoolean(root, "shiftKey") == true)
        {
          modifiers |= Keys.Shift;
        }
        if (ReadOptionalBoolean(root, "metaKey") == true)
        {
          modifiers |= Keys.LWin;
        }
        PhysicalWheelInput?.Invoke(
          delta,
          modifiers,
          ReadOptionalString(root, "targetTag"),
          ReadOptionalString(root, "targetId"));
        return;
      }
''',
  '''      if (type == "physical-mouse-click")
      {
        PhysicalMouseClickInput?.Invoke(
          ReadModifierKeys(root),
          ReadOptionalString(root, "targetTag"),
          ReadOptionalString(root, "targetId"));
        return;
      }
      if (type == "physical-wheel")
      {
        int delta = (int)Math.Round(ReadOptionalDouble(root, "delta") ?? 0);
        PhysicalWheelInput?.Invoke(
          delta,
          ReadModifierKeys(root),
          ReadOptionalString(root, "targetTag"),
          ReadOptionalString(root, "targetId"));
        return;
      }
''')

helper_marker = '''  private static string ReadOptionalString(
    JsonElement root,
    string propertyName)
'''
helper = '''  private static Keys ReadModifierKeys(JsonElement root)
  {
    Keys modifiers = Keys.None;
    if (ReadOptionalBoolean(root, "ctrlKey") == true)
    {
      modifiers |= Keys.Control;
    }
    if (ReadOptionalBoolean(root, "altKey") == true)
    {
      modifiers |= Keys.Alt;
    }
    if (ReadOptionalBoolean(root, "shiftKey") == true)
    {
      modifiers |= Keys.Shift;
    }
    if (ReadOptionalBoolean(root, "metaKey") == true)
    {
      modifiers |= Keys.LWin;
    }
    return modifiers;
  }

'''
replace_once(transcript, helper_marker, helper + helper_marker)

replace_once(
  transcript,
  '''  event.preventDefault();
  event.stopPropagation();
  chrome.webview.postMessage({
    type:'find-seek',
    source:'ctrl-click',
    wordId
  });
''',
  '''  event.preventDefault();
  event.stopPropagation();
  if (event.isTrusted) {
    chrome.webview.postMessage({
      type:'physical-mouse-click',
      ctrlKey:event.ctrlKey,
      altKey:event.altKey,
      shiftKey:event.shiftKey,
      metaKey:event.metaKey,
      targetTag:event.target.tagName,
      targetId:event.target.id || ''
    });
  }
  chrome.webview.postMessage({
    type:'find-seek',
    source:'ctrl-click',
    wordId
  });
''')

replace_once(
  main,
  '''    _transcriptView.PhysicalWheelInput += TranscriptPhysicalWheelInput;
''',
  '''    _transcriptView.PhysicalWheelInput += TranscriptPhysicalWheelInput;
    _transcriptView.PhysicalMouseClickInput += TranscriptPhysicalMouseClickInput;
''')

handler_marker = '''  /// <summary>
  /// Adds a WebView-consumed wheel event to the shared physical-input timeline
'''
handler = '''  /// <summary>
  /// Adds a trusted WebView click to the shared physical-input timeline before
  /// the corresponding Ctrl+click seek command is dispatched.
  /// </summary>
  private void TranscriptPhysicalMouseClickInput(
    Keys modifiers,
    string targetTag,
    string targetId)
  {
    _ = _inputDiagnostics.ObserveWebViewMouseClick(
      modifiers,
      _transcriptSettingsPopup.Settings.FollowSpeech,
      _speech.IsSpeaking,
      _speech.IsPaused,
      targetTag,
      targetId);
  }

'''
replace_once(main, handler_marker, handler + handler_marker)
