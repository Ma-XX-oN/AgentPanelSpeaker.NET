from pathlib import Path

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

list_old = '''      ("virtual-window/directional-prefetch-follows-scroll-direction",\n        TestDirectionalPrefetchFollowsScrollDirection),\n      ("virtual-window/physical-window-keeps-five-viewports-materialized",\n'''
list_new = '''      ("virtual-window/directional-prefetch-follows-scroll-direction",\n        TestDirectionalPrefetchFollowsScrollDirection),\n      ("virtual-window/manual-scroll-disables-follow-before-vwindow-shift",\n        TestManualScrollDisablesFollowBeforeVirtualShift),\n      ("virtual-window/physical-window-keeps-five-viewports-materialized",\n'''
if text.count(list_old) != 1:
  raise SystemExit("manual-follow test-list anchor did not match exactly once")
text = text.replace(list_old, list_new, 1)

method_anchor = '''  /// <summary>\n  /// The materialized browser window is sized in physical viewport heights,\n'''
method = r'''  /// <summary>
  /// A genuine user scroll has priority over speech following.  Follow mode
  /// must be disabled before a vwindow replacement can mark its own anchor
  /// restoration as programmatic; otherwise the delayed follow-off timer can
  /// be suppressed and leave the UI claiming that speech is still followed.
  /// </summary>
  private static void TestManualScrollDisablesFollowBeforeVirtualShift()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue37-manual-follow-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue37-manual-follow.jsonl");
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
        "WebView2 core initialization for manual-follow regression");

      bool? notifiedFollowState = null;
      view.FollowSpeechChanged += enabled => notifiedFollowState = enabled;
      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 37 manual-follow fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          ReadField<int>(view, "_windowEndIndex") >=
            ReadField<int>(view, "_windowStartIndex") &&
          webView.Visible,
        "initial manual-follow virtual window");

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
        "middle manual-follow virtual-window precondition");
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
        ReadField<int>(view, "_windowEndIndex") < document.Count - 1,
        "Manual-follow fixture did not retain unloaded content on both sides.");

      ExecuteVoidScript(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const last = records[records.length - 1];
  programmaticScrollUntil = performance.now() + 500;
  const desiredBottom = window.innerHeight * 1.25;
  const delta = last.getBoundingClientRect().bottom - desiredBottom;
  window.scrollBy(0, delta);
})()
""");
      PumpMessages(650);
      Require(
        ReadBrowserBoolean(webView, "followSpeech"),
        "Manual-follow precondition unexpectedly disabled Follow mode.");

      int beforeStart = ReadField<int>(view, "_windowStartIndex");
      int beforeEnd = ReadField<int>(view, "_windowEndIndex");
      ExecuteVoidScript(
        webView,
        "programmaticScrollUntil = 0; window.scrollBy(0, 160);");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") != beforeStart ||
          ReadField<int>(view, "_windowEndIndex") != beforeEnd,
        "manual scroll to move the virtual window",
        timeoutMilliseconds: 5000);
      PumpMessages(300);

      Require(
        !ReadBrowserBoolean(webView, "followSpeech"),
        "Manual user scrolling moved the vwindow but Follow mode remained enabled.");
      Require(
        notifiedFollowState == false,
        "Manual user scrolling did not emit FollowSpeechChanged(false) before " +
        "the vwindow replacement completed.");
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

'''
if text.count(method_anchor) != 1:
  raise SystemExit("manual-follow method insertion anchor did not match exactly once")
text = text.replace(method_anchor, method + method_anchor, 1)
path.write_text(text, encoding="utf-8")
