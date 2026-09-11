from pathlib import Path

path = Path(__file__).resolve().parents[1] / "AgentPanelSpeaker" / "Issue73CtrlClickVoicePointerRegressionTestRunner.cs"
text = path.read_text(encoding="utf-8")
old = '''      ("ctrl-click-voice-pointer/owned-popup-focus-preserves-held-ctrl",\n        TestOwnedPopupFocusPreservesHeldCtrl),\n'''
new = '''      ("ctrl-click-voice-pointer/owned-popup-focus-preserves-held-ctrl",\n        TestOwnedPopupFocusPreservesHeldCtrl),\n      ("ctrl-click-voice-pointer/owned-popup-root-routes-ctrl-messages",\n        TestOwnedPopupRootRoutesCtrlMessages),\n'''
if text.count(old) != 1:
  raise RuntimeError(f"test-list anchor count={text.count(old)}")
text = text.replace(old, new)
anchor = '''  private static TranscriptRangeProbe[] ReadRanges(object? value)\n'''
method = '''  /// <summary>\n  /// A top-level owned popup is not the MainForm root, but it is still part of\n  /// this process and its Ctrl key messages must reach the transcript modifier\n  /// router. External-process roots must remain excluded.\n  /// </summary>\n  private static void TestOwnedPopupRootRoutesCtrlMessages()\n  {\n    MethodInfo resolver = typeof(MainForm).GetMethod(\n      "ShouldRouteVoicePointerMessageSource",\n      BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) ??\n      throw new InvalidOperationException(\n        "MainForm popup-root voice-pointer routing resolver is missing.");\n\n    bool Resolve(bool isMainFormRoot, bool rootIsCurrentProcess)\n    {\n      object? result = resolver.Invoke(\n        null,\n        new object?[] { isMainFormRoot, rootIsCurrentProcess });\n      return result is bool value\n        ? value\n        : throw new InvalidOperationException(\n          "MainForm popup-root routing resolver returned no bool.");\n    }\n\n    Require(Resolve(isMainFormRoot: true, rootIsCurrentProcess: true),\n      "MainForm-root Ctrl messages were rejected.");\n    Require(Resolve(isMainFormRoot: false, rootIsCurrentProcess: true),\n      "Owned same-process popup Ctrl messages were rejected.");\n    Require(!Resolve(isMainFormRoot: false, rootIsCurrentProcess: false),\n      "External-process messages were accepted as transcript Ctrl input.");\n  }\n\n'''
if text.count(anchor) != 1:
  raise RuntimeError(f"method anchor count={text.count(anchor)}")
text = text.replace(anchor, method + anchor)
path.write_text(text, encoding="utf-8")
print("Staged owned-popup root Ctrl-routing RED.")
