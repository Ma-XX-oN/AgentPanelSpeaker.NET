from pathlib import Path

path = Path('AgentPanelSpeaker/Issue73CtrlClickVoicePointerRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''  window.dispatchEvent(new KeyboardEvent('keydown', {
    key:'Control', code:'ControlLeft', ctrlKey:true, bubbles:true
  }));
  window.dispatchEvent(new Event('blur'));
  const afterBlur = document.body.classList.contains('voice-pointer-select-mode');
  return JSON.stringify({
    heldThroughReplacement:selectable.join(' ') === 'delta epsilon',
    afterUp,
    afterBlur
  });
})()
'''
new = '''  window.dispatchEvent(new KeyboardEvent('keydown', {
    key:'Control', code:'ControlLeft', ctrlKey:true, bubbles:true
  }));
  window.dispatchEvent(new Event('blur'));
  const afterBrowserBlur = document.body.classList.contains(
    'voice-pointer-select-mode');
  setVoicePointerSelectMode(false);
  const afterHostDeactivate = document.body.classList.contains(
    'voice-pointer-select-mode');
  return JSON.stringify({
    heldThroughReplacement:selectable.join(' ') === 'delta epsilon',
    afterUp,
    afterBrowserBlur,
    afterHostDeactivate
  });
})()
'''
if text.count(old) != 1:
  raise SystemExit('browser blur probe anchor not found exactly once')
text = text.replace(old, new)

old_assert = '''      Require(!replacement.GetProperty("afterBlur").GetBoolean(),
        "Window blur did not clear Ctrl selection mode.");
'''
new_assert = '''      Require(replacement.GetProperty("afterBrowserBlur").GetBoolean(),
        "WebView focus loss incorrectly impersonated Ctrl-up while the app remained active.");
      Require(!replacement.GetProperty("afterHostDeactivate").GetBoolean(),
        "Host deactivation did not clear Ctrl selection mode.");
'''
if text.count(old_assert) != 1:
  raise SystemExit('browser blur assertion anchor not found exactly once')
text = text.replace(old_assert, new_assert)
path.write_text(text, encoding='utf-8')
