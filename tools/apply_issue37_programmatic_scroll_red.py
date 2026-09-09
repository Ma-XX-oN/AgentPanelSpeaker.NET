from pathlib import Path

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

old_tests = '''      ("virtual-window/browser-is-bounded-convergent-and-diagnostics-are-bounded",
        TestBrowserWindowBehaviour)
'''
new_tests = '''      ("virtual-window/browser-is-bounded-convergent-and-diagnostics-are-bounded",
        TestBrowserWindowBehaviour),
      ("virtual-window/programmatic-playback-scroll-does-not-request-shift",
        TestProgrammaticPlaybackScrollDoesNotRequestShift)
'''
if old_tests not in text:
  raise SystemExit("test-list insertion point was not found")
text = text.replace(old_tests, new_tests, 1)

marker = '''  private static void WriteFixture(string path)
'''
method = r'''  /// <summary>
  /// Reproduces the real-machine #37 playback/window ping-pong.  Moving playback
  /// from an old virtual window to a distant node replaces the browser window
  /// and performs a programmatic focus scroll.  That scroll must not be
  /// reinterpreted as user navigation and emit a competing virtual-window shift.
  /// Once the programmatic-scroll guard expires, an edge scroll must still be
  /// able to request normal virtualization movement.
  /// </summary>
  private static void TestProgrammaticPlaybackScrollDoesNotRequestShift()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-programmatic-scroll-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-programmatic-scroll.jsonl");
    WriteFixture(path);

    try
    {
      using var form = new Form
      {
        Width = 900,
        Height = 700,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      using var view = new TranscriptView
      {
        Dock = DockStyle.Fill
      };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization for programmatic-scroll regression");

      int windowShiftCount = 0;
      var windowShiftReasons = new List<string>();
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (!rootElement.TryGetProperty("type", out JsonElement typeElement) ||
              typeElement.ValueKind != JsonValueKind.String ||
              typeElement.GetString() != "window-shift")
          {
            return;
          }
          ++windowShiftCount;
          windowShiftReasons.Add(
            rootElement.TryGetProperty("reason", out JsonElement reasonElement) &&
            reasonElement.ValueKind == JsonValueKind.String
              ? reasonElement.GetString() ?? string.Empty
              : string.Empty);
        }
        catch (JsonException)
        {
          // Production owns malformed-message handling.  This independent
          // observer records only well-formed virtual-window shift requests.
        }
      };

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 programmatic-scroll fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial virtual window for programmatic-scroll regression");

      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      TranscriptNodeIdentity playbackIdentity = identities
        .Last(identity => identity.Segments.Count > 0);
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      Require(
        document.TryGetIndex(
          playbackIdentity.RecordNumber,
          playbackIdentity.SourceId,
          out int playbackVirtualIndex),
        "Playback identity was not present in the virtual document.");

      Task moveAway = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        0,
        "test-precondition",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(
        moveAway,
        "precondition window to move away from playback target");
      Require(
        playbackVirtualIndex < ReadField<int>(view, "_windowStartIndex") ||
        playbackVirtualIndex > ReadField<int>(view, "_windowEndIndex"),
        "Precondition window still contains the playback target.");
      PumpMessages(250);

      int shiftsBeforePlayback = windowShiftCount;
      string playbackFragment = playbackIdentity.Segments[0];
      string playbackWord = SpeechTokenization.First(playbackFragment);
      view.ShowPlaybackPosition(new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Speaking,
        playbackFragment,
        0,
        playbackWord,
        playbackIdentity.NodeId,
        0,
        playbackWord.Length,
        Stopwatch.GetTimestamp()));

      PumpUntil(
        () =>
          playbackVirtualIndex >= ReadField<int>(view, "_windowStartIndex") &&
          playbackVirtualIndex <= ReadField<int>(view, "_windowEndIndex"),
        "playback target window to materialize");
      PumpMessages(1200);

      Require(
        windowShiftCount == shiftsBeforePlayback,
        "Playback-driven programmatic scroll emitted competing virtual-window " +
        $"shift request(s): {string.Join(", ", windowShiftReasons.Skip(shiftsBeforePlayback))}.");
      Require(
        playbackVirtualIndex >= ReadField<int>(view, "_windowStartIndex") &&
        playbackVirtualIndex <= ReadField<int>(view, "_windowEndIndex"),
        "Playback window was displaced after its programmatic focus scroll.");

      // The production focus-scroll guard currently lasts at most two seconds.
      // After it expires, a normal edge scroll must still drive virtualization.
      PumpMessages(2100);
      int shiftsBeforeEdgeScroll = windowShiftCount;
      ExecuteVoidScript(webView, "window.scrollTo(0, 0);");
      PumpUntil(
        () => windowShiftCount > shiftsBeforeEdgeScroll,
        "unguarded edge scroll to request a virtual-window shift",
        timeoutMilliseconds: 5000);
      Require(
        windowShiftReasons.Skip(shiftsBeforeEdgeScroll).Any(
          reason => reason == "scroll-up"),
        "Unguarded edge scroll did not preserve normal upward virtualization.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

'''
if marker not in text:
  raise SystemExit("method insertion point was not found")
text = text.replace(marker, method + marker, 1)
path.write_text(text, encoding="utf-8")
