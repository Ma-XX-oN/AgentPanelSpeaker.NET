from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

list_anchor = '''      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",\n        TestPlaybackVoiceCursorPrefetchesInsideTallTurn)\n'''
list_replacement = '''      ("virtual-window/playback-voice-cursor-prefetches-inside-tall-turn",\n        TestPlaybackVoiceCursorPrefetchesInsideTallTurn),\n      ("virtual-window/visible-turn-survives-shift-until-outside-viewport",\n        TestVisibleTurnSurvivesShiftUntilOutsideViewport)\n'''
if list_anchor not in text:
  raise SystemExit('test-list anchor not found')
text = text.replace(list_anchor, list_replacement, 1)

method_anchor = '''  private static bool ReadBrowserBoolean(WebView2 webView, string expression)\n'''
method = r'''  /// <summary>
  /// A virtual-window shift must not evict any Core atomic unit that still
  /// intersects the physical browser viewport.  The old total-height-only
  /// contract could keep five viewport heights materialized while removing a
  /// turn the user was still looking at, briefly leaving blank/spacer content.
  /// </summary>
  private static void TestVisibleTurnSurvivesShiftUntilOutsideViewport()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-visible-retention-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-visible-retention.jsonl");
    WriteDirectionalFixture(path);

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
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization for visible-turn retention regression");

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 visible-turn retention fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial virtual window for visible-turn retention regression");

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int middleIndex = document.Count / 2;
      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "test-precondition",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(
        middleWindow,
        "middle virtual-window precondition for visible-turn retention");
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
        ReadField<int>(view, "_windowEndIndex") < document.Count - 1,
        "Visible-retention fixture did not leave unloaded content on both sides.");

      // Position the viewport near the lower physical trigger boundary while
      // still showing several materialized records.  Keep this positioning
      // programmatic so it cannot itself request the shift under test.
      ExecuteVoidScript(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const last = records[records.length - 1];
  programmaticScrollUntil = performance.now() + 500;
  const desiredBottom = window.innerHeight * 1.35;
  const delta = last.getBoundingClientRect().bottom - desiredBottom;
  window.scrollBy(0, delta);
})()
""");
      PumpMessages(650);

      JsonElement before = ExecuteJsonProbe(
        webView,
        """
(() => {
  const visible = [...document.querySelectorAll('.virtual-record')]
    .filter(record => {
      const rect = record.getBoundingClientRect();
      return rect.bottom > 0 && rect.top < window.innerHeight;
    })
    .map(record => Number(record.dataset.virtualIndex));
  return JSON.stringify({
    visible,
    start:windowStartIndex,
    end:windowEndIndex,
    scrollY:window.scrollY,
    innerHeight:window.innerHeight
  });
})()
""");
      int[] visibleBefore = before.GetProperty("visible")
        .EnumerateArray()
        .Select(item => item.GetInt32())
        .ToArray();
      Require(
        visibleBefore.Length >= 2,
        "Visible-retention precondition did not span at least two atomic turns.");

      int beforeStart = ReadField<int>(view, "_windowStartIndex");
      int beforeEnd = ReadField<int>(view, "_windowEndIndex");
      ExecuteVoidScript(
        webView,
        "programmaticScrollUntil = 0; window.scrollBy(0, 120);");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") != beforeStart ||
          ReadField<int>(view, "_windowEndIndex") != beforeEnd,
        "downward shift to replace the visible-retention window",
        timeoutMilliseconds: 5000);
      PumpMessages(250);

      JsonElement after = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const materialized = records.map(
    record => Number(record.dataset.virtualIndex));
  const intersecting = records.filter(record => {
    const rect = record.getBoundingClientRect();
    return rect.bottom > 0 && rect.top < window.innerHeight;
  }).map(record => Number(record.dataset.virtualIndex));
  return JSON.stringify({
    materialized,
    intersecting,
    scrollY:window.scrollY,
    innerHeight:window.innerHeight
  });
})()
""");
      var materializedAfter = after.GetProperty("materialized")
        .EnumerateArray()
        .Select(item => item.GetInt32())
        .ToHashSet();
      int[] evictedVisible = visibleBefore
        .Where(index => !materializedAfter.Contains(index))
        .ToArray();
      Require(
        evictedVisible.Length == 0,
        "Virtual-window replacement evicted turn(s) that still intersected " +
        "the physical viewport before the shift: " +
        string.Join(", ", evictedVisible) + ".");
      Require(
        after.GetProperty("intersecting").GetArrayLength() > 0,
        "Virtual-window replacement left no materialized turn intersecting " +
        "the physical viewport.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

'''
if method_anchor not in text:
  raise SystemExit('method insertion anchor not found')
text = text.replace(method_anchor, method + method_anchor, 1)
path.write_text(text, encoding='utf-8')
