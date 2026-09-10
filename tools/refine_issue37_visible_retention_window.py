from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')
old = '''      PumpUntilCompleted(
        middleWindow,
        "mixed-height virtual-window precondition for visible-turn retention");
      PumpMessages(300);
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
'''
new = '''      PumpUntilCompleted(
        middleWindow,
        "mixed-height virtual-window precondition for visible-turn retention");
      // Let natural browser heights reach the virtual document, then rebuild
      // the same focal window so the test exercises the production five-
      // viewport geometry rather than the initial estimated-height window.
      PumpMessages(300);
      Task measuredWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        tallIndex + 1,
        "test-measured-refinement",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(
        measuredWindow,
        "measured mixed-height window for visible-turn retention");
      PumpMessages(150);
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0 &&
'''
if text.count(old) != 1:
  raise SystemExit('measured-refinement insertion anchor mismatch')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
