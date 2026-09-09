from pathlib import Path

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

old = '''      int beforeStart = ReadField<int>(view, "_windowStartIndex");
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
'''
new = '''      int beforeStart = ReadField<int>(view, "_windowStartIndex");
      int beforeEnd = ReadField<int>(view, "_windowEndIndex");
      ExecuteVoidScript(
        webView,
        """
(() => {
  programmaticScrollUntil = performance.now() + 2000;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: 160,
    bubbles: true,
    cancelable: true
  }));
  window.scrollBy(0, 160);
})()
""");

      PumpUntil(
        () => notifiedFollowState == false,
        "manual wheel input to override the active programmatic-scroll guard",
        timeoutMilliseconds: 1000);
      Require(
        !ReadBrowserBoolean(webView, "followSpeech"),
        "Manual user scrolling during an active programmatic-scroll guard " +
        "did not disable Follow mode.");

      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") != beforeStart ||
          ReadField<int>(view, "_windowEndIndex") != beforeEnd,
        "manual scroll to move the virtual window after disabling Follow",
        timeoutMilliseconds: 5000);
      PumpMessages(300);

      Require(
        notifiedFollowState == false,
        "Manual user scrolling did not emit FollowSpeechChanged(false) before " +
        "the vwindow replacement completed.");
'''

if text.count(old) != 1:
  raise SystemExit("manual-follow regression block did not match exactly once")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
