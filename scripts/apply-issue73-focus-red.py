from pathlib import Path

path = Path('AgentPanelSpeaker/Issue73CtrlClickVoicePointerRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''      ("ctrl-click-voice-pointer/browser-affordance-and-click-contract",
        TestBrowserAffordanceAndClickContract)
'''
new = '''      ("ctrl-click-voice-pointer/browser-affordance-and-click-contract",
        TestBrowserAffordanceAndClickContract),
      ("ctrl-click-voice-pointer/host-ctrl-routing-does-not-require-webview-focus",
        TestHostCtrlRoutingDoesNotRequireWebViewFocus)
'''
if text.count(old) != 1:
  raise SystemExit('test-list anchor not found exactly once')
text = text.replace(old, new)

anchor = '''  private static TranscriptRangeProbe[] ReadRanges(object? value)
'''
method = '''  /// <summary>
  /// Ctrl selection mode must be driven by the containing WinForms window as
  /// well as browser key events so focus on another app control still exposes
  /// the currently seekable transcript words.  Key release is likewise routed
  /// by the host without consuming the original key message.
  /// </summary>
  private static void TestHostCtrlRoutingDoesNotRequireWebViewFocus()
  {
    MethodInfo setMode = typeof(TranscriptView).GetMethod(
      "SetVoicePointerSelectMode",
      BindingFlags.Instance | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "TranscriptView.SetVoicePointerSelectMode(bool) is missing.");
    Require(setMode.GetParameters() is [{ ParameterType: var parameterType }] &&
        parameterType == typeof(bool),
      "TranscriptView.SetVoicePointerSelectMode has the wrong contract.");

    MethodInfo resolver = typeof(MainForm).GetMethod(
      "TryGetVoicePointerSelectModeMessage",
      BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "MainForm host Ctrl-message resolver is missing.");

    object?[] down = { 0x0100, (IntPtr)(int)Keys.ControlKey, false };
    Require((bool)(resolver.Invoke(null, down) ?? false) &&
        down[2] is true,
      "Host WM_KEYDOWN for Ctrl did not enable voice-pointer selection mode.");

    object?[] up = { 0x0101, (IntPtr)(int)Keys.ControlKey, true };
    Require((bool)(resolver.Invoke(null, up) ?? false) &&
        up[2] is false,
      "Host WM_KEYUP for Ctrl did not disable voice-pointer selection mode.");

    object?[] ordinary = { 0x0100, (IntPtr)(int)Keys.A, false };
    Require(!(bool)(resolver.Invoke(null, ordinary) ?? true),
      "An ordinary host key was incorrectly treated as Ctrl selection state.");
  }

'''
if text.count(anchor) != 1:
  raise SystemExit('method insertion anchor not found exactly once')
text = text.replace(anchor, method + anchor)
path.write_text(text, encoding='utf-8')
