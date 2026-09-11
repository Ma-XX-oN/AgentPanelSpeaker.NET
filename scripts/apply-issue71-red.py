from pathlib import Path

path = Path('AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''      ("real-session/live-end-window-retains-preceding-unit",\n        TestLiveEndWindowRetainsPrecedingUnit)\n'''
new = '''      ("real-session/live-end-window-retains-preceding-unit",\n        TestLiveEndWindowRetainsPrecedingUnit),\n      ("real-session/stale-find-materialization-is-cancelled-before-install",\n        TestStaleFindMaterializationIsCancelledBeforeInstall)\n'''
if old not in text:
  raise RuntimeError('test-list insertion marker not found')
text = text.replace(old, new, 1)

marker = '''  private static CanonicalHtmlUnitProjection CreateUnit(\n'''
method = r'''  /// <summary>
  /// Reproduces issue #71. An off-window Find materialization can already be
  /// queued behind the render gate when a newer query invalidates its browser
  /// navigation generation. The invalidated request must not install its stale
  /// virtual window or strand the viewport over a spacer.
  /// </summary>
  private static void TestStaleFindMaterializationIsCancelledBeforeInstall()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue71-stale-find-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue71-stale-find.jsonl");
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
      view.SelectSession(path, AgentSource.Codex, "Issue 71 stale Find fixture");
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
        "Issue #71 fixture did not choose an off-window Find target.");

      SemaphoreSlim gate = ReadField<SemaphoreSlim>(view, "_windowRenderGate");
      gate.Wait();
      try
      {
        ExecuteVoidScript(
          webView,
          $$"""
(() => {
  findNavigationGeneration = 105;
  chrome.webview.postMessage({
    type:'window-request',
    recordNumber:{{targetRecordNumber}},
    reason:'search',
    matchIndex:0,
    navigationGeneration:105
  });
})()
""");
        PumpUntil(
          () => ReadField<long>(
            view,
            "_latestFindWindowNavigationGeneration") == 105,
          "issue #71 stale Find request to reach C#");

        // Supersede the queued request exactly as query-as-you-type does.
        // Current production increments only the browser generation here;
        // issue #71 requires that invalidation to reach C# before installation.
        ExecuteVoidScript(
          webView,
          """
(() => {
  findSearchPending = true;
  cancelFindSearch(false);
})()
""");
        PumpMessages(250);
      }
      finally
      {
        gate.Release();
      }

      PumpMessages(1500);
      int finalStart = ReadField<int>(view, "_windowStartIndex");
      int finalEnd = ReadField<int>(view, "_windowEndIndex");
      int visibleRecords = ExecuteIntScript(
        webView,
        """
[...document.querySelectorAll('.virtual-record')].filter(record => {
  const rect = record.getBoundingClientRect();
  return rect.bottom > 0 && rect.top < window.innerHeight;
}).length
""");

      Require(
        finalStart == initialStart && finalEnd == initialEnd &&
        visibleRecords > 0,
        $"Superseded Find navigation changed window {initialStart}..{initialEnd} " +
        $"to {finalStart}..{finalEnd}; visible materialized records={visibleRecords}.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

'''
if marker not in text:
  raise RuntimeError('method insertion marker not found')
text = text.replace(marker, method + marker, 1)

helper_marker = '''  private static void ExecuteVoidScript(WebView2 webView, string script)\n'''
helper = r'''  private static int ExecuteIntScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser issue #71 probe");
    return JsonSerializer.Deserialize<int>(task.Result);
  }

'''
if helper_marker not in text:
  raise RuntimeError('helper insertion marker not found')
text = text.replace(helper_marker, helper + helper_marker, 1)

path.write_text(text, encoding='utf-8', newline='\n')
