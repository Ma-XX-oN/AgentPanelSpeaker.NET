from pathlib import Path

path = Path("AgentPanelSpeaker/Issue46IndependentRegressionOracleTestRunner.cs")
text = path.read_text(encoding="utf-8")

start = text.index(
  "  /// <summary>\n"
  "  /// Starts a long Core-projected User Context fragment, then disables the\n")
end = text.index(
  "  private static Form CreateOffscreenHost(int width, int height)\n",
  start)

replacement = r'''  /// <summary>
  /// Starts a deliberately long Core-projected User Context fragment, then
  /// disables the actual Speak User/IDE context checkbox while SpeechService
  /// owns that active history fragment. The checkbox event must synchronously
  /// queue the exact next eligible User fragment and the normal inter-fragment
  /// pause before provider cancellation completes. This is deterministic in CI
  /// even though hosted WinMM does not provide a reliable advancing audio clock;
  /// audible mid-utterance cut-off remains a real-machine acceptance check.
  /// </summary>
  private static void TestIssue26ActiveToggleCancelsPlayback()
  {
    string root = CreateTempRoot("issue46-issue26-active");
    try
    {
      string path = Path.Combine(root, "fixture.jsonl");
      File.WriteAllText(path, BuildLongUserContextJsonl());

      using var formLease = CreateOffscreenMainForm();
      MainForm form = formLease.Form;
      ConfigureMainFormSession(form, path);
      TranscriptSettingsPopup popup =
        ReadField<TranscriptSettingsPopup>(form, "_transcriptSettingsPopup");
      SpeechService speech = ReadField<SpeechService>(form, "_speech");

      TranscriptSettings enabled = popup.Settings with
      {
        SpeakUserContext = true
      };
      popup.SetSettings(enabled, dark: false);
      SetField(form, "_speakUserContext", true);

      using var previewMonitor = new JsonlSessionMonitor();
      SpeechHistorySnapshot history = previewMonitor.LoadHistoryPreview(
        SessionLocator.FromPath(path, AgentSource.Codex),
        speakExistingLatestTurn: true,
        includeRolledBackTurns: false,
        includeUserContext: true);
      speech.LoadHistory(
        history.Fragments,
        Array.Empty<TurnCompletion>(),
        Array.Empty<BackgroundWorkEvent>(),
        PlaybackStartMode.Beginning);

      SpeechFragment context = history.Fragments
        .Where(fragment => fragment.Category == ContentCategory.UserContext)
        .OrderByDescending(fragment => fragment.Text.Length)
        .FirstOrDefault() ?? throw new InvalidOperationException(
          "Long User Context fixture produced no User Context fragment.");
      SpeechFragment user = history.Fragments.First(fragment =>
        fragment.Category == ContentCategory.User);
      long contextWordId = context.WordIds?.FirstOrDefault() ??
        throw new InvalidOperationException(
          "Long User Context fragment has no Core word identity.");
      SpeechFragment[] retainedBefore = ReadSpeechHistory(speech);
      int contextIndex = Array.FindIndex(retainedBefore, fragment =>
        fragment.Category == ContentCategory.UserContext &&
        string.Equals(fragment.Text, context.Text, StringComparison.Ordinal));
      int userIndex = Array.FindIndex(retainedBefore, fragment =>
        fragment.Category == ContentCategory.User &&
        string.Equals(fragment.Text, user.Text, StringComparison.Ordinal));
      Require(contextIndex >= 0 && userIndex > contextIndex,
        "Long User Context fixture did not preserve context before its User prompt.");
      int monitorSession = ReadField<int>(form, "_monitorSession");
      CheckBox control = ReadField<CheckBox>(popup, "_speakUserContextCheckBox");
      Require(control.Checked,
        "Active issue #26 checkbox was not ON before playback.");
      Require(
        speech.TrySeekToTranscriptWord(contextWordId, out _),
        "Could not seek to the long eligible User Context fragment.");
      Require(speech.TogglePause() == PauseToggleResult.Resumed,
        "Could not start long User Context playback.");
      Require(
        SpinWait.SpinUntil(() => speech.IsSpeaking, TimeSpan.FromSeconds(3)),
        "Long User Context never entered active SpeechService playback.");
      Require(ReadField<int>(speech, "_activeHistoryIndex") == contextIndex,
        "Long User Context was not the active history fragment before toggle.");
      Require(ReadNullableIntField(speech, "_pendingHistoryIndex") is null,
        "Active User Context unexpectedly had a pending replacement before toggle.");
      Require(!ReadField<bool>(speech, "_pauseBeforeNextHistory"),
        "Active User Context unexpectedly had a transition pause before toggle.");

      // Negative mutation: changing only the backing policy field bypasses the
      // real checkbox event and therefore must not satisfy the same transition
      // oracle. This proves the acceptance depends on the production control
      // event, not merely on reading the new policy value.
      SetField(form, "_speakUserContext", false);
      ExpectOracleRejects(
        () => RequireActiveEligibilityTransitionQueued(speech, userIndex),
        "The active issue #26 oracle accepted a policy-field mutation that " +
        "bypassed the real checkbox event.");
      SetField(form, "_speakUserContext", true);

      control.Checked = false;
      Application.DoEvents();
      RequireActiveEligibilityTransitionQueued(speech, userIndex);
      RequireUnchangedSessionState(
        form,
        monitorSession,
        retainedBefore,
        speech,
        "disabling Speak User/IDE context during active playback");
    }
    finally
    {
      DeleteTempRoot(root);
    }
  }

  private static void RequireActiveEligibilityTransitionQueued(
    SpeechService speech,
    int expectedUserIndex)
  {
    int? pending = ReadNullableIntField(speech, "_pendingHistoryIndex");
    Require(pending == expectedUserIndex,
      "Disabling the actual Speak User/IDE context checkbox did not queue " +
      "the exact next eligible User fragment for active cancellation.");
    Require(ReadField<int>(speech, "_nextHistoryIndex") == expectedUserIndex,
      "Active User Context cancellation did not preserve the queued User " +
      "fragment as the next playback index.");
    Require(ReadField<bool>(speech, "_pauseBeforeNextHistory"),
      "Active User Context cancellation did not request the normal " +
      "inter-fragment pause before the queued User prompt.");
  }

  private static int? ReadNullableIntField(object target, string name)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Field {name} was not found.");
    object? value = field.GetValue(target);
    return value is null ? null : Convert.ToInt32(value);
  }

'''
text = text[:start] + replacement + text[end:]

# Keep synthesis busy long enough that the synchronous pending-transition state
# can be observed before the worker consumes the queued cancellation.
old_fixture = '''      Enumerable.Repeat("active user context source", 80));'''
if old_fixture not in text:
  raise RuntimeError("Expected long User Context fixture repeat count was not found.")
text = text.replace(
  old_fixture,
  '''      Enumerable.Repeat("active user context source", 800));''',
  1)

path.write_text(text, encoding="utf-8")
print("Replaced Issue46 active User Context oracle with pending-transition RED.")
