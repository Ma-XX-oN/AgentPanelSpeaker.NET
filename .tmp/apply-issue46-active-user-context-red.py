from pathlib import Path
import subprocess

BASE = "d00e6903a5fdb80c7633883ebbd5ed9ed2ba580a"


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one anchor, found {count}.")
  return text.replace(old, new, 1)


# Remove the exploratory Issue26 active-audio test from the permanent RED set.
# Its history remains in Git; the documented Issue26 runner is component-level,
# while real checkbox acceptance belongs in Issue46.
subprocess.run(
  ["git", "checkout", BASE, "--",
   "AgentPanelSpeaker/Issue26UserContextSpeechRegressionTestRunner.cs"],
  check=True)

path = Path("AgentPanelSpeaker/Issue46IndependentRegressionOracleTestRunner.cs")
text = path.read_text(encoding="utf-8")

old = '''      ("independent-oracle/issue26-real-toggle-playback-policy",
        TestIssue26RealTogglePlaybackPolicy)
'''
new = '''      ("independent-oracle/issue26-real-toggle-playback-policy",
        TestIssue26RealTogglePlaybackPolicy),
      ("independent-oracle/issue26-active-toggle-cancels-playback",
        TestIssue26ActiveToggleCancelsPlayback)
'''
text = replace_once(text, old, new, "Issue46 test registration")

anchor = '''  private static Form CreateOffscreenHost(int width, int height)
'''
method = r'''  /// <summary>
  /// Starts a long Core-projected User Context fragment, then disables the
  /// actual Speak User/IDE context checkbox while SpeechService owns that
  /// active history fragment. The real control event must cancel the now-
  /// ineligible source and advance playback to the actual User prompt without
  /// rebuilding retained history. Physical audible interruption remains a
  /// real-machine acceptance requirement because hosted CI has no reliable
  /// advancing WinMM output clock.
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
      int monitorSession = ReadField<int>(form, "_monitorSession");
      CheckBox control = ReadField<CheckBox>(popup, "_speakUserContextCheckBox");
      Require(control.Checked,
        "Active issue #26 checkbox was not ON before playback.");
      Require(
        speech.TrySeekToTranscriptWord(contextWordId, out _),
        "Could not seek to the long eligible User Context fragment.");

      var positions = new List<TranscriptPlaybackPosition>();
      speech.PlaybackPositionChanged += position =>
      {
        lock (positions)
        {
          positions.Add(position);
        }
      };
      Require(speech.TogglePause() == PauseToggleResult.Resumed,
        "Could not start long User Context playback.");
      Require(
        SpinWait.SpinUntil(() => speech.IsSpeaking, TimeSpan.FromSeconds(3)),
        "Long User Context never entered active SpeechService playback.");

      // Negative mutation: changing only the policy field reproduces broken
      // checkbox wiring. The same destination-transition oracle must reject it.
      int mutationStart;
      lock (positions)
      {
        mutationStart = positions.Count;
      }
      SetField(form, "_speakUserContext", false);
      ExpectOracleRejects(
        () => RequireActiveUserDestinationTransition(
          positions,
          mutationStart,
          user.NodeId,
          timeoutMilliseconds: 750),
        "The active issue #26 oracle accepted a policy-field mutation that " +
        "bypassed the real checkbox event.");
      SetField(form, "_speakUserContext", true);

      int transitionStart;
      lock (positions)
      {
        transitionStart = positions.Count;
      }
      control.Checked = false;
      Application.DoEvents();
      RequireActiveUserDestinationTransition(
        positions,
        transitionStart,
        user.NodeId,
        timeoutMilliseconds: 10000);

      TranscriptPlaybackPosition[] transition;
      lock (positions)
      {
        transition = positions.Skip(transitionStart).ToArray();
      }
      Require(!transition.Any(position =>
          position.State == TranscriptPlaybackState.Speaking &&
          position.NodeId == context.NodeId),
        "Disabling the real Speak User/IDE context checkbox restarted the " +
        "now-ineligible User Context fragment.");
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

  private static void RequireActiveUserDestinationTransition(
    List<TranscriptPlaybackPosition> positions,
    int start,
    long userNodeId,
    int timeoutMilliseconds)
  {
    var timer = Stopwatch.StartNew();
    while (timer.ElapsedMilliseconds < timeoutMilliseconds)
    {
      lock (positions)
      {
        if (positions.Skip(start).Any(position =>
            position.State == TranscriptPlaybackState.Speaking &&
            position.NodeId == userNodeId))
        {
          return;
        }
      }
      Application.DoEvents();
      Thread.Sleep(10);
    }
    throw new InvalidOperationException(
      "Disabling the actual Speak User/IDE context checkbox did not cancel " +
      "the active ineligible User Context and advance playback to the User prompt.");
  }

'''
text = replace_once(text, anchor, method + anchor, "Issue46 active test method")

anchor = '''  private static string BuildUserContextJsonl()
'''
fixture = r'''  private static string BuildLongUserContextJsonl()
  {
    string activeContext = string.Join(
      ' ',
      Enumerable.Repeat("active user context source", 80));
    string message =
      "# Context from my IDE setup:\n\n" +
      "## Active file: " + activeContext + "\n\n" +
      "## Open tabs:\n" +
      "- sessions/example.jsonl\n\n" +
      "## My request for Codex:\n" +
      "Visible user destination.";
    object[] records =
    {
      new
      {
        type = "event_msg",
        timestamp = "2026-09-07T00:00:00Z",
        payload = new { type = "user_message", message }
      }
    };
    return JoinJsonl(records);
  }

'''
text = replace_once(text, anchor, fixture + anchor, "Issue46 long fixture")
path.write_text(text, encoding="utf-8")
print("Applied Issue46 active User Context RED and restored Issue26 component suite.")
