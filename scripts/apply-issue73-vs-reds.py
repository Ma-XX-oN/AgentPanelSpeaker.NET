from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{path}: expected one match, found {count}")
  path.write_text(text.replace(old, new), encoding="utf-8")


issue73 = ROOT / "AgentPanelSpeaker" / "Issue73CtrlClickVoicePointerRegressionTestRunner.cs"
replace_once(
  issue73,
  '''      ("ctrl-click-voice-pointer/host-ctrl-routing-does-not-require-webview-focus",\n        TestHostCtrlRoutingDoesNotRequireWebViewFocus)\n''',
  '''      ("ctrl-click-voice-pointer/host-ctrl-routing-does-not-require-webview-focus",\n        TestHostCtrlRoutingDoesNotRequireWebViewFocus),\n      ("ctrl-click-voice-pointer/owned-popup-focus-preserves-held-ctrl",\n        TestOwnedPopupFocusPreservesHeldCtrl)\n''')
replace_once(
  issue73,
  '''  private static TranscriptRangeProbe[] ReadRanges(object? value)\n''',
  '''  /// <summary>\n  /// Moving focus from MainForm to an owned popup keeps the application in the\n  /// foreground.  That same-process deactivation must therefore preserve a\n  /// physically held Ctrl affordance; only external application deactivation\n  /// may clear it.\n  /// </summary>\n  private static void TestOwnedPopupFocusPreservesHeldCtrl()\n  {\n    MethodInfo resolver = typeof(MainForm).GetMethod(\n      "ResolveVoicePointerSelectModeForActivation",\n      BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) ??\n      throw new InvalidOperationException(\n        "MainForm activation-aware Ctrl-mode resolver is missing.");\n\n    bool Resolve(bool controlHeld, bool foregroundIsCurrentProcess)\n    {\n      object? result = resolver.Invoke(\n        null,\n        new object?[] { controlHeld, foregroundIsCurrentProcess });\n      return result is bool value\n        ? value\n        : throw new InvalidOperationException(\n          "MainForm activation-aware Ctrl-mode resolver returned no bool.");\n    }\n\n    Require(Resolve(controlHeld: true, foregroundIsCurrentProcess: true),\n      "Held Ctrl was cleared when focus moved to an owned same-process popup.");\n    Require(!Resolve(controlHeld: true, foregroundIsCurrentProcess: false),\n      "Held Ctrl remained exposed after the foreground moved outside the app.");\n    Require(!Resolve(controlHeld: false, foregroundIsCurrentProcess: true),\n      "Ctrl selection mode remained enabled after physical Ctrl release.");\n  }\n\n  private static TranscriptRangeProbe[] ReadRanges(object? value)\n''')

issue37 = ROOT / "AgentPanelSpeaker" / "Issue37VirtualWindowRegressionTestRunner.cs"
replace_once(
  issue37,
  '''      ("virtual-window/follow-off-initial-render-ignores-pending-playback",\n        TestFollowOffInitialRenderIgnoresPendingPlayback)\n''',
  '''      ("virtual-window/follow-off-initial-render-ignores-pending-playback",\n        TestFollowOffInitialRenderIgnoresPendingPlayback),\n      ("virtual-window/manual-scroll-preserves-disclosure-state",\n        TestManualScrollPreservesDisclosureState)\n''')
replace_once(
  issue37,
  '''  private static bool ReadBrowserBoolean(WebView2 webView, string expression)\n''',
  '''  /// <summary>\n  /// A disclosure opened by the user must stay open when virtualization\n  /// replaces an overlapping materialized window during ordinary scrolling.\n  /// The replacement itself may use preserve=false for scroll-position policy;\n  /// disclosure state is a separate UI invariant and must survive that swap.\n  /// </summary>\n  private static void TestManualScrollPreservesDisclosureState()\n  {\n    using var host = CreateOffscreenHost();\n    using var view = new TranscriptView { Dock = DockStyle.Fill };\n    host.Controls.Add(view);\n    host.Show();\n    _ = host.Handle;\n    _ = view.Handle;\n    WaitForViewInitialization(view);\n\n    WebView2 webView = ReadField<WebView2>(view, "_webView");\n    JsonElement result = ExecuteJsonProbe(\n      webView,\n      """\n(() => {\n  const first =\n    '<section class="virtual-record" data-virtual-index="10">' +\n    '<details data-presentation-id="context:stable"><summary>Context</summary>' +\n    '<p>first window</p></details></section>';\n  const second =\n    '<section class="virtual-record" data-virtual-index="10">' +\n    '<details data-presentation-id="context:stable"><summary>Context</summary>' +\n    '<p>shifted window</p></details></section>' +\n    '<section class="virtual-record" data-virtual-index="11"><p>next</p></section>';\n  replaceTranscriptWindow(first, false, [], 10, 10, 100, 100);\n  const before = document.querySelector('details[data-presentation-id="context:stable"]');\n  if (!before) throw new Error('Initial disclosure was not materialized.');\n  before.open = true;\n  replaceTranscriptWindow(second, false, [], 10, 11, 100, 50);\n  const after = document.querySelector('details[data-presentation-id="context:stable"]');\n  return JSON.stringify({exists:Boolean(after), open:Boolean(after?.open)});\n})()\n""");\n\n    Require(result.GetProperty("exists").GetBoolean(),\n      "Overlapping disclosure disappeared during virtual-window replacement.");\n    Require(result.GetProperty("open").GetBoolean(),\n      "Virtual-window replacement collapsed a disclosure that the user opened.");\n  }\n\n  private static bool ReadBrowserBoolean(WebView2 webView, string expression)\n''')

print("Staged issue #73 owned-popup RED and issue #37 disclosure-state RED.")
