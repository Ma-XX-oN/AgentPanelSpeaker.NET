from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''      ("virtual-window/disclosure-open-state-survives-eviction-and-close-clears-override",\n        TestDisclosureOpenStateSurvivesEvictionAndCloseClearsOverride)\n'''
new = '''      ("virtual-window/disclosure-open-state-survives-eviction-and-close-clears-override",\n        TestDisclosureOpenStateSurvivesEvictionAndCloseClearsOverride),\n      ("virtual-window/manual-scroll-rematerializes-paused-marker-and-required-disclosure",\n        TestManualScrollRematerializesPausedMarkerAndRequiredDisclosure)\n'''
if old not in text:
  raise SystemExit('test-list insertion point not found')
text = text.replace(old, new, 1)

marker = '''  private static Form CreateOffscreenHost()\n'''
test = r'''  /// <summary>
  /// Real-machine regression: a paused marker inside a programmatically opened
  /// disclosure may be evicted by manual virtual scrolling.  Returning to that
  /// virtual record must redraw the retained voice marker and reopen only the
  /// disclosure required to expose it.  Reapplying marker state must not turn
  /// manual scrolling back into playback-controlled window navigation.
  /// </summary>
  private static void TestManualScrollRematerializesPausedMarkerAndRequiredDisclosure()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-marker-return-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "fixture.jsonl");
    WriteFollowOffFixture(path);

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
      view.SelectSession(path, AgentSource.Codex, "marker rematerialization");
      WaitForTranscriptRender(view);

      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      TranscriptNodeIdentity target = identities.FirstOrDefault(identity =>
        identity.Segments.Any(segment =>
          segment.Contains("Open tabs:", StringComparison.Ordinal))) ??
        throw new InvalidOperationException(
          "Marker-rematerialization fixture has no User Context Open tabs identity.");
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      Require(document.TryGetIndex(target.RecordNumber, out int targetIndex),
        "User Context marker target is absent from the virtual document.");

      Task materializeTarget = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        targetIndex,
        "test-precondition",
        null,
        null);
      PumpUntilCompleted(materializeTarget, "User Context target materialization");

      string fragment = target.Segments.First(segment =>
        segment.Contains("Open tabs:", StringComparison.Ordinal));
      string word = SpeechTokenization.First(fragment);
      view.ShowPlaybackPosition(new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Paused,
        fragment,
        0,
        word,
        target.NodeId,
        0,
        word.Length,
        Stopwatch.GetTimestamp()));

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => ExecuteJsonProbe(
          webView,
          "JSON.stringify({paused:!!document.querySelector('.word.paused')," +
          "open:!!document.querySelector('.word.paused')?.closest('details')?.open})")
          .GetProperty("paused").GetBoolean(),
        "paused User Context marker to appear");
      JsonElement initial = ExecuteJsonProbe(
        webView,
        "JSON.stringify({paused:!!document.querySelector('.word.paused')," +
        "open:!!document.querySelector('.word.paused')?.closest('details')?.open})");
      Require(initial.GetProperty("open").GetBoolean(),
        "Paused User Context marker did not programmatically open its disclosure.");

      int awayIndex = targetIndex == 0 ? document.Count - 1 : 0;
      Task moveAway = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        awayIndex,
        targetIndex == 0 ? "scroll-down" : "scroll-up",
        null,
        null);
      PumpUntilCompleted(moveAway, "manual scroll to evict paused marker");
      Require(
        targetIndex < ReadField<int>(view, "_windowStartIndex") ||
        targetIndex > ReadField<int>(view, "_windowEndIndex"),
        "Manual-scroll precondition did not evict the paused marker record.");

      Task returnToTarget = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        targetIndex,
        targetIndex < awayIndex ? "scroll-up" : "scroll-down",
        null,
        null);
      PumpUntilCompleted(returnToTarget, "manual scroll back to paused marker record");
      PumpMessages(200);

      Require(
        targetIndex >= ReadField<int>(view, "_windowStartIndex") &&
        targetIndex <= ReadField<int>(view, "_windowEndIndex"),
        "Manual scroll did not return to the paused marker record.");
      JsonElement restored = ExecuteJsonProbe(
        webView,
        "JSON.stringify({paused:!!document.querySelector('.word.paused')," +
        "open:!!document.querySelector('.word.paused')?.closest('details')?.open})");
      Require(restored.GetProperty("paused").GetBoolean(),
        "Paused voice cursor disappeared after manual-scroll eviction and rematerialization.");
      Require(restored.GetProperty("open").GetBoolean(),
        "Programmatically required disclosure stayed closed after the paused marker rematerialized.");
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
