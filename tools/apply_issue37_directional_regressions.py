from pathlib import Path

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

old_tests = '''      ("virtual-window/programmatic-playback-scroll-does-not-request-shift",
        TestProgrammaticPlaybackScrollDoesNotRequestShift)
'''
new_tests = '''      ("virtual-window/programmatic-playback-scroll-does-not-request-shift",
        TestProgrammaticPlaybackScrollDoesNotRequestShift),
      ("virtual-window/spacer-shift-restores-materialized-content",
        TestSpacerShiftRestoresMaterializedContent),
      ("virtual-window/directional-prefetch-follows-scroll-direction",
        TestDirectionalPrefetchFollowsScrollDirection)
'''
if text.count(old_tests) != 1:
  raise SystemExit(
    f"expected one test-list insertion point, found {text.count(old_tests)}")
text = text.replace(old_tests, new_tests, 1)

marker = '''  private static void WriteFixture(string path)
  {
'''
if text.count(marker) != 1:
  raise SystemExit(
    f"expected one method insertion point, found {text.count(marker)}")

methods = r'''  /// <summary>
  /// Reproduces the real-machine blank-viewport failure.  A fast manual scroll
  /// can enter the synthetic spacer before the next virtual window is ready.
  /// Once that shift completes, the browser viewport must intersect actual
  /// materialized transcript records rather than remain wholly in the spacer.
  /// </summary>
  private static void TestSpacerShiftRestoresMaterializedContent()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-spacer-recovery-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-spacer-recovery.jsonl");
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
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization for spacer recovery regression");

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 spacer recovery fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") > 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial trailing virtual window for spacer recovery regression");

      int initialStart = ReadField<int>(view, "_windowStartIndex");
      int initialEnd = ReadField<int>(view, "_windowEndIndex");
      PumpMessages(600);

      ExecuteVoidScript(
        webView,
        "programmaticScrollUntil = 0; window.scrollTo(0, 0);");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") != initialStart ||
          ReadField<int>(view, "_windowEndIndex") != initialEnd,
        "spacer scroll to install a predecessor virtual window",
        timeoutMilliseconds: 5000);
      PumpMessages(250);

      JsonElement probe = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const intersecting = records.filter(record => {
    const rect = record.getBoundingClientRect();
    return rect.bottom > 0 && rect.top < window.innerHeight;
  });
  const topSpacer = document.querySelector(
    '.virtual-spacer[data-virtual-spacer="top"]');
  return JSON.stringify({
    intersectingRecordCount: intersecting.length,
    scrollY: window.scrollY,
    innerHeight: window.innerHeight,
    topSpacerHeight: topSpacer?.getBoundingClientRect().height ?? 0,
    firstRecordTop: records.length
      ? records[0].getBoundingClientRect().top
      : null,
    lastRecordBottom: records.length
      ? records[records.length - 1].getBoundingClientRect().bottom
      : null
  });
})()
""");

      int intersectingRecordCount =
        probe.GetProperty("intersectingRecordCount").GetInt32();
      Require(
        intersectingRecordCount > 0,
        "Completed virtual-window shift left the viewport wholly in synthetic " +
        "spacer instead of restoring materialized transcript content. " +
        $"scrollY={probe.GetProperty("scrollY").GetDouble():F1}, " +
        $"topSpacerHeight={probe.GetProperty("topSpacerHeight").GetDouble():F1}.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  /// <summary>
  /// Manual virtualization must follow the user's actual scroll direction and
  /// prefetch before the viewport crosses into synthetic spacer.  The current
  /// window can be smaller than the historical 20-record threshold, so record
  /// index bands are not a valid direction detector.
  /// </summary>
  private static void TestDirectionalPrefetchFollowsScrollDirection()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-directional-prefetch-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-directional-prefetch.jsonl");
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
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 core initialization for directional prefetch regression");

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
          windowShiftReasons.Add(
            rootElement.TryGetProperty("reason", out JsonElement reasonElement) &&
            reasonElement.ValueKind == JsonValueKind.String
              ? reasonElement.GetString() ?? string.Empty
              : string.Empty);
        }
        catch (JsonException)
        {
          // Production owns malformed-message handling.  This observer records
          // only well-formed window-shift direction requests.
        }
      };

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 directional prefetch fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial virtual window for directional prefetch regression");

      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        30,
        "test-precondition",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(
        middleWindow,
        "middle virtual-window precondition for directional prefetch");
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0,
        "Directional prefetch precondition did not leave unloaded content above.");

      JsonElement positioned = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const last = records[records.length - 1];
  const bottomSpacer = document.querySelector(
    '.virtual-spacer[data-virtual-spacer="bottom"]');
  programmaticScrollUntil = performance.now() + 500;
  const desiredBottom = window.innerHeight * 1.25;
  const delta = last.getBoundingClientRect().bottom - desiredBottom;
  window.scrollBy(0, delta);
  return JSON.stringify({
    recordCount: records.length,
    bottomSpacerHeight: bottomSpacer?.getBoundingClientRect().height ?? 0,
    innerHeight: window.innerHeight
  });
})()
""");
      Require(
        positioned.GetProperty("recordCount").GetInt32() > 0,
        "Directional prefetch fixture has no materialized records.");
      Require(
        positioned.GetProperty("bottomSpacerHeight").GetDouble() > 0,
        "Directional prefetch precondition has no unloaded content below.");
      PumpMessages(650);

      JsonElement beforeDown = ExecuteJsonProbe(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const last = records[records.length - 1];
  const intersecting = records.filter(record => {
    const rect = record.getBoundingClientRect();
    return rect.bottom > 0 && rect.top < window.innerHeight;
  }).length;
  return JSON.stringify({
    intersecting,
    lastBottom: last.getBoundingClientRect().bottom,
    innerHeight: window.innerHeight
  });
})()
""");
      Require(
        beforeDown.GetProperty("intersecting").GetInt32() > 0,
        "Downward prefetch test started after materialized content was already lost.");
      Require(
        beforeDown.GetProperty("lastBottom").GetDouble() >
          beforeDown.GetProperty("innerHeight").GetDouble() + 80,
        "Downward prefetch test did not retain visible materialized margin.");

      int shiftsBeforeDown = windowShiftReasons.Count;
      ExecuteVoidScript(
        webView,
        "programmaticScrollUntil = 0; window.scrollBy(0, 80);");
      PumpUntil(
        () => windowShiftReasons.Count > shiftsBeforeDown,
        "downward scroll to request predictive virtual-window movement",
        timeoutMilliseconds: 5000);
      string downwardReason = windowShiftReasons[shiftsBeforeDown];
      Require(
        string.Equals(downwardReason, "scroll-down", StringComparison.Ordinal),
        "Downward scrolling requested the wrong virtual-window direction: " +
        downwardReason + ".");

      PumpUntil(
        () => !ReadBrowserBoolean(webView, "virtualShiftPending"),
        "downward virtual-window request to settle",
        timeoutMilliseconds: 5000);

      Task resetMiddleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        30,
        "test-precondition",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(
        resetMiddleWindow,
        "middle virtual-window reset before upward prefetch");

      ExecuteVoidScript(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const first = records[0];
  programmaticScrollUntil = performance.now() + 500;
  const desiredTop = -window.innerHeight * 0.25;
  const delta = first.getBoundingClientRect().top - desiredTop;
  window.scrollBy(0, delta);
})()
""");
      PumpMessages(650);

      int shiftsBeforeUp = windowShiftReasons.Count;
      ExecuteVoidScript(
        webView,
        "programmaticScrollUntil = 0; window.scrollBy(0, -80);");
      PumpUntil(
        () => windowShiftReasons.Count > shiftsBeforeUp,
        "upward scroll to request predictive virtual-window movement",
        timeoutMilliseconds: 5000);
      string upwardReason = windowShiftReasons[shiftsBeforeUp];
      Require(
        string.Equals(upwardReason, "scroll-up", StringComparison.Ordinal),
        "Upward scrolling requested the wrong virtual-window direction: " +
        upwardReason + ".");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private static bool ReadBrowserBoolean(WebView2 webView, string expression)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(
      "Boolean(" + expression + ")");
    PumpUntilCompleted(task, "browser boolean probe");
    return string.Equals(task.Result, "true", StringComparison.OrdinalIgnoreCase);
  }

'''

text = text.replace(marker, methods + marker, 1)
path.write_text(text, encoding="utf-8")
