from pathlib import Path

source = Path(".tmp/apply-acceptance-fixes-v5.py").read_text(encoding="utf-8")
exec(compile(source, ".tmp/apply-acceptance-fixes-v5.py", "exec"))


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one anchor, found {count}.")
  return text.replace(old, new, 1)


test_path = Path(
  "AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs")
test = test_path.read_text(encoding="utf-8")

old_assertion = '''      PumpMessages(1000);

      Require(
        reasons.Skip(beforeReplacement).Any(reason => reason == "scroll-up"),
        "A completed manual-scroll replacement remained at the unloaded upper " +
        "edge after the physical-intent timer expired and did not request the " +
        "next adjacent canonical window.");
'''
new_assertion = '''      PumpMessages(1000);

      string convergenceState = ExecuteStringScript(
        webView,
        """
(() => {
  const bounds = materializedWindowBounds();
  const reference = firstVisibleVirtualRecord(-1);
  return JSON.stringify({
    now:performance.now(),
    programmaticScrollUntil,
    remainingProgrammaticGuard:
      Math.max(0, programmaticScrollUntil - performance.now()),
    userScrollIntentUntil,
    remainingUserIntent:
      Math.max(0, userScrollIntentUntil - performance.now()),
    userScrollIntentDirection,
    virtualShiftPending,
    windowStartIndex,
    windowEndIndex,
    firstTop:bounds?.first?.top ?? null,
    firstBottom:bounds?.first?.bottom ?? null,
    lastTop:bounds?.last?.top ?? null,
    lastBottom:bounds?.last?.bottom ?? null,
    triggerDistance:window.innerHeight * VW_EDGE_TRIGGER_VIEWPORTS,
    viewportHeight:window.innerHeight,
    scrollY:window.scrollY,
    referenceIndex:reference
      ? Number(reference.dataset.virtualIndex || -1)
      : -1,
    referenceRecord:reference?.querySelector('.record-anchor')
      ? Number(reference.querySelector('.record-anchor').dataset.jsonlRecord || 0)
      : 0
  });
})()
""");
      Require(
        reasons.Skip(beforeReplacement).Any(reason => reason == "scroll-up"),
        "A completed manual-scroll replacement remained at the unloaded upper " +
        "edge after the physical-intent timer expired and did not request the " +
        $"next adjacent canonical window. Browser state: {convergenceState}");
'''
test = replace_once(
  test,
  old_assertion,
  new_assertion,
  "scroll convergence diagnostic assertion",
)

helper_anchor = '''  private static int ExecuteIntScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser issue #71 probe");
    return JsonSerializer.Deserialize<int>(task.Result);
  }

'''
helper_replacement = helper_anchor + '''  private static string ExecuteStringScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser issue #54 string probe");
    return JsonSerializer.Deserialize<string>(task.Result) ?? string.Empty;
  }

'''
test = replace_once(
  test,
  helper_anchor,
  helper_replacement,
  "browser string probe helper",
)

test_path.write_text(test, encoding="utf-8")
print("Applied acceptance production repairs v6 diagnostics.")
