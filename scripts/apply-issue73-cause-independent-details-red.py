from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')
old = '''      ("virtual-window/manual-scroll-rematerializes-paused-marker-and-required-disclosure",\n        TestManualScrollRematerializesPausedMarkerAndRequiredDisclosure)\n'''
new = '''      ("virtual-window/manual-scroll-rematerializes-paused-marker-and-required-disclosure",\n        TestManualScrollRematerializesPausedMarkerAndRequiredDisclosure),\n      ("virtual-window/programmatic-disclosure-state-survives-eviction",\n        TestProgrammaticDisclosureStateSurvivesEviction)\n'''
if old not in text:
  raise SystemExit('test-list insertion point not found')
text = text.replace(old, new, 1)
marker = '''  private static Form CreateOffscreenHost()\n'''
test = r'''  /// <summary>
  /// Disclosure state belongs to the logical disclosure, not to the cause of
  /// its last transition.  A programmatic open must survive full eviction and
  /// rematerialization; a later programmatic close returns it to the default
  /// closed state and removes the remembered-open state.
  /// </summary>
  private static void TestProgrammaticDisclosureStateSurvivesEviction()
  {
    using var host = CreateOffscreenHost();
    using var view = new TranscriptView { Dock = DockStyle.Fill };
    host.Controls.Add(view);
    host.Show();
    _ = host.Handle;
    _ = view.Handle;
    WaitForViewInitialization(view);

    WebView2 webView = ReadField<WebView2>(view, "_webView");
    JsonElement result = ExecuteJsonProbe(
      webView,
      """
(() => {
  const withDetails =
    '<section class="virtual-record" data-virtual-index="10">' +
    '<details data-presentation-id="context:programmatic"><summary>Context</summary>' +
    '<p>payload</p></details></section>';
  const away =
    '<section class="virtual-record" data-virtual-index="30"><p>far away</p></section>';

  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  let details = document.querySelector('details[data-presentation-id="context:programmatic"]');
  setDisclosureOpenProgrammatically(details, true);
  replaceTranscriptWindow(away, false, [], 30, 30, 500, 50);
  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  details = document.querySelector('details[data-presentation-id="context:programmatic"]');
  const openAfterProgrammaticReturn = Boolean(details?.open);

  setDisclosureOpenProgrammatically(details, false);
  replaceTranscriptWindow(away, false, [], 30, 30, 500, 50);
  replaceTranscriptWindow(withDetails, false, [], 10, 10, 100, 100);
  details = document.querySelector('details[data-presentation-id="context:programmatic"]');
  return JSON.stringify({
    openAfterProgrammaticReturn,
    closedAfterProgrammaticCloseAndReturn: details ? !details.open : false
  });
})()
""");

    Require(result.GetProperty("openAfterProgrammaticReturn").GetBoolean(),
      "Programmatically opened disclosure lost state after eviction/rematerialization.");
    Require(result.GetProperty("closedAfterProgrammaticCloseAndReturn").GetBoolean(),
      "Programmatic close did not clear the remembered open disclosure state.");
  }

'''
if marker not in text:
  raise SystemExit('function insertion point not found')
text = text.replace(marker, test + marker, 1)
path.write_text(text, encoding='utf-8')
