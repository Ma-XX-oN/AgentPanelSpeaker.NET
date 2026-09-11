from pathlib import Path

path = Path('AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old_entry = '''      ("real-session/stale-find-materialization-is-cancelled-before-install",
        TestStaleFindMaterializationIsCancelledBeforeInstall)
'''
new_entry = '''      ("real-session/stale-find-materialization-is-cancelled-before-install",
        TestStaleFindMaterializationIsCancelledBeforeInstall),
      ("real-session/stale-find-invalidated-after-install-stays-visible",
        TestStaleFindInvalidatedAfterInstallStaysVisible)
'''
if old_entry not in text:
  raise SystemExit('issue 71 test-list insertion point not found')
text = text.replace(old_entry, new_entry, 1)

marker = '''  private static CanonicalHtmlUnitProjection CreateUnit(
'''
method = r'''  /// <summary>
  /// Reproduces the remaining issue #71 race observed on the real machine.
  /// The browser can invalidate Find after the replacement has started but
  /// before C# posts window-ready. A stale ready must never leave the viewport
  /// over an unloaded spacer with zero visible materialized records.
  /// </summary>
  private static void TestStaleFindInvalidatedAfterInstallStaysVisible()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue71-postinstall-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue71-postinstall.jsonl");
    WriteFixture(path);

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
      view.SelectSession(path, AgentSource.Codex, "Issue 71 post-install fixture");
      WaitForTranscriptRender(view);

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int initialStart = ReadField<int>(view, "_windowStartIndex");
      int initialEnd = ReadField<int>(view, "_windowEndIndex");
      TranscriptWindow targetWindow = document.CreateWindow(
        0,
        TranscriptVirtualDocument.DefaultViewportHeight);
      int targetRecordNumber = targetWindow.Records[0].RecordNumber;
      Require(
        !document.TryGetIndex(targetRecordNumber, out int targetIndex) ||
        targetIndex < initialStart || targetIndex > initialEnd,
        "Issue #71 post-install fixture did not choose an off-window target.");

      ExecuteVoidScript(
        webView,
        $$"""
(() => {
  findInput.value = 'Issue';
  findSearchPending = false;
  findMatches = [{
    fileOrdinal:1,
    recordNumber:{{targetRecordNumber}},
    startWordIndex:0,
    endWordIndex:0,
    nodeId:0,
    nodeWordIndex:-1
  }];
  currentFindMatch = 0;
  findNavigationGeneration = 205;
  window.__issue71PostInstallInvalidated = false;

  const observer = new MutationObserver(() => {
    observer.disconnect();
    findInput.value = 'Changed';
    cancelFindSearch(false);
    window.__issue71PostInstallInvalidated = true;
  });
  observer.observe(transcript, {childList:true});

  chrome.webview.postMessage({
    type:'window-request',
    recordNumber:{{targetRecordNumber}},
    reason:'search',
    matchIndex:0,
    navigationGeneration:205
  });
})()
""");

      PumpUntil(
        () => ReadField<long>(
          view,
          "_latestFindWindowNavigationGeneration") > 205,
        "issue #71 post-install invalidation to reach C#");
      PumpMessages(500);

      int invalidated = ExecuteIntScript(
        webView,
        "window.__issue71PostInstallInvalidated ? 1 : 0");
      int visibleRecords = ExecuteIntScript(
        webView,
        """
[...document.querySelectorAll('.virtual-record')].filter(record => {
  const rect = record.getBoundingClientRect();
  return rect.bottom > 0 && rect.top < window.innerHeight;
}).length
""");

      Require(
        invalidated == 1,
        "Issue #71 post-install invalidation hook did not execute.");
      Require(
        visibleRecords > 0,
        "Find invalidation after replacement began left zero visible " +
        "materialized records.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

'''
if marker not in text:
  raise SystemExit('CreateUnit insertion point not found')
text = text.replace(marker, method + marker, 1)
path.write_text(text, encoding='utf-8')
