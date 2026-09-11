from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''      ("virtual-window/programmatic-disclosure-state-survives-eviction",\n        TestProgrammaticDisclosureStateSurvivesEviction)\n'''
new = '''      ("virtual-window/programmatic-disclosure-state-survives-eviction",\n        TestProgrammaticDisclosureStateSurvivesEviction),\n      ("virtual-window/retained-playback-never-rebinds-to-duplicate-text-in-another-node",\n        TestRetainedPlaybackNeverRebindsToDuplicateTextInAnotherNode)\n'''
if old not in text:
  raise SystemExit('test-list insertion point not found')
text = text.replace(old, new, 1)

marker = '''  private static Form CreateOffscreenHost()\n'''
test = r'''  /// <summary>
  /// Real-machine regression: when the retained playback node is evicted, a
  /// duplicate fragment in another materialized node must never inherit the
  /// voice marker. Stable NodeId identity is authoritative across virtual DOM
  /// replacement; the marker may be absent while its node is absent, then must
  /// return only when that exact node rematerializes.
  /// </summary>
  private static void TestRetainedPlaybackNeverRebindsToDuplicateTextInAnotherNode()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-node-identity-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "fixture.jsonl");
    WriteFollowOffFixture(path);
    File.AppendAllLines(path, new[]
    {
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-07T01:59:59Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = "Open tabs:"
        }
      })
    });

    try
    {
      using var host = CreateOffscreenHost();
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;
      WaitForViewInitialization(view);
      view.ApplySettings(
        TranscriptSettings.Default with { FollowSpeech = false },
        dark: false);
      view.SelectSession(path, AgentSource.Codex, "stable-node playback identity");
      WaitForTranscriptRender(view);

      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      TranscriptNodeIdentity[] matches = identities
        .Where(identity => identity.Segments.Any(segment =>
          string.Equals(segment.Trim(), "Open tabs:", StringComparison.Ordinal)))
        .ToArray();
      Require(matches.Length >= 2,
        $"Duplicate-text fixture exposed only {matches.Length} Open tabs node(s).");
      TranscriptNodeIdentity target = matches[0];
      TranscriptNodeIdentity decoy = matches[^1];
      Require(target.NodeId != decoy.NodeId,
        "Duplicate-text fixture did not create distinct stable node identities.");

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      Require(document.TryGetIndex(target.RecordNumber, out int targetIndex),
        "Target playback node is absent from virtual document.");
      Require(document.TryGetIndex(decoy.RecordNumber, out int decoyIndex),
        "Duplicate-text decoy node is absent from virtual document.");
      Require(Math.Abs(decoyIndex - targetIndex) > 5,
        "Duplicate-text fixture did not separate target and decoy enough for eviction.");

      Task materializeTarget = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        targetIndex,
        "test-precondition",
        null,
        null);
      PumpUntilCompleted(materializeTarget, "target playback node materialization");

      view.ShowPlaybackPosition(new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Paused,
        "Open tabs:",
        0,
        "Open",
        target.NodeId,
        0,
        4,
        Stopwatch.GetTimestamp()));

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => ExecuteJsonProbe(
          webView,
          "JSON.stringify({node:Number(document.querySelector('.word.paused')?.dataset.nodeId||0)})")
          .GetProperty("node").GetInt64() == target.NodeId,
        "paused marker to bind to its target stable node");

      Task materializeDecoy = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        decoyIndex,
        decoyIndex > targetIndex ? "scroll-down" : "scroll-up",
        null,
        null);
      PumpUntilCompleted(materializeDecoy, "manual scroll to duplicate-text decoy");
      PumpMessages(200);
      Require(
        targetIndex < ReadField<int>(view, "_windowStartIndex") ||
        targetIndex > ReadField<int>(view, "_windowEndIndex"),
        "Manual-scroll precondition did not evict the authoritative playback node.");
      Require(
        decoyIndex >= ReadField<int>(view, "_windowStartIndex") &&
        decoyIndex <= ReadField<int>(view, "_windowEndIndex"),
        "Manual-scroll precondition did not materialize the duplicate-text decoy.");

      JsonElement absentTarget = ExecuteJsonProbe(
        webView,
        "JSON.stringify({" +
        "paused:!!document.querySelector('.word.paused')," +
        "node:Number(document.querySelector('.word.paused')?.dataset.nodeId||0)})");
      Require(!absentTarget.GetProperty("paused").GetBoolean(),
        "Retained voice cursor rebound to duplicate text in another node while " +
        $"authoritative node {target.NodeId} was evicted; rendered node was " +
        $"{absentTarget.GetProperty("node").GetInt64()}.");

      Task returnToTarget = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        targetIndex,
        targetIndex < decoyIndex ? "scroll-up" : "scroll-down",
        null,
        null);
      PumpUntilCompleted(returnToTarget, "manual scroll back to authoritative playback node");
      PumpUntil(
        () => ExecuteJsonProbe(
          webView,
          "JSON.stringify({node:Number(document.querySelector('.word.paused')?.dataset.nodeId||0)})")
          .GetProperty("node").GetInt64() == target.NodeId,
        "retained marker to return only to its authoritative stable node");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

'''
if marker not in text:
  raise SystemExit('function insertion point not found')
text = text.replace(marker, test + marker, 1)
path.write_text(text, encoding='utf-8')
