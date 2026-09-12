from pathlib import Path

path = Path('AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8-sig')

old = '''        "Follow ON to reattach the retained canonical cursor",
        timeoutMilliseconds: 8000);

      int appliedAfterEnable = exactPlaybackApplied;
'''
new = '''        "Follow ON to reattach the retained canonical cursor",
        timeoutMilliseconds: 8000);

      // The OFF->ON transition can still be completing its asynchronous Core
      // materialization when the first applied marker arrives. Let that one
      // transition settle before taking the inverse-contract baseline. Any
      // later marker can then be attributed to the already-ON settings apply.
      PumpMessages(500);
      int appliedAfterEnable = exactPlaybackApplied;
'''
if old in text:
  text = text.replace(old, new, 1)
elif 'inverse-contract baseline' not in text:
  raise SystemExit('Issue #57 settled-baseline anchor was not found.')

old = '''  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: 160,
    bubbles: true,
    cancelable: true
  }));
  const next = window.scrollY + 220;
  window.scrollTo(0, next);
  window.dispatchEvent(new Event('scroll'));
'''
new = '''  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: 160,
    bubbles: true,
    cancelable: true
  }));
  // The initial virtual window is normally at the end of the transcript, so a
  // real downward scroll can clamp at the document boundary on CI. Drive the
  // scroll handler with a deterministic positive delta after the physical wheel
  // event; this tests intent classification without depending on page geometry.
  lastManualScrollY = window.scrollY - 220;
  window.dispatchEvent(new Event('scroll'));
'''
if old in text:
  text = text.replace(old, new, 1)
elif 'tests intent classification without depending on page geometry' not in text:
  raise SystemExit('Issue #78 physical-scroll anchor was not found.')

path.write_text(text, encoding='utf-8')
