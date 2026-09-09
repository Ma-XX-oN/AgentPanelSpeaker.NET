from pathlib import Path

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

old_after_render = '''      PumpUntilCompleted(middleWindow, "middle physical virtual window");
      Require(
'''
new_after_render = '''      PumpUntilCompleted(middleWindow, "middle physical virtual window");
      // Let the browser's natural-height measurements reach the virtual
      // document, then ask for the same focal window again.  A physical-window
      // implementation must use those measurements to avoid retaining an
      // unnecessarily large record-count window.
      PumpMessages(150);
      Task measuredMiddleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        document.Count / 2,
        "test-measured-refinement",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(
        measuredMiddleWindow,
        "measured middle physical virtual window");
      Require(
'''
if text.count(old_after_render) != 1:
  raise SystemExit(f"unexpected measured-window anchor count: {text.count(old_after_render)}")
text = text.replace(old_after_render, new_after_render, 1)

old_probe = '''(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const first = records[0]?.getBoundingClientRect();
  const last = records[records.length - 1]?.getBoundingClientRect();
  return JSON.stringify({
    recordCount: records.length,
    innerHeight: window.innerHeight,
    materializedHeight: first && last ? last.bottom - first.top : 0
  });
})()
'''
new_probe = '''(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const firstRecord = records[0];
  const lastRecord = records[records.length - 1];
  const first = firstRecord?.getBoundingClientRect();
  const last = lastRecord?.getBoundingClientRect();
  return JSON.stringify({
    recordCount: records.length,
    innerHeight: window.innerHeight,
    materializedHeight: first && last ? last.bottom - first.top : 0,
    firstHeight: first?.height ?? 0,
    lastHeight: last?.height ?? 0,
    firstIndex: Number(firstRecord?.dataset.virtualIndex ?? -1),
    lastIndex: Number(lastRecord?.dataset.virtualIndex ?? -1)
  });
})()
'''
if text.count(old_probe) != 1:
  raise SystemExit(f"unexpected physical probe count: {text.count(old_probe)}")
text = text.replace(old_probe, new_probe, 1)

old_minimum = '''      Require(
        materializedHeight >= innerHeight * MinimumWindowViewportHeights,
        "Materialized vwindow is shorter than the required physical minimum: " +
        $"height={materializedHeight:F1}, viewport={innerHeight:F1}, " +
        $"minimumViewports={MinimumWindowViewportHeights:F1}.");
'''
new_minimum = '''      double targetHeight = innerHeight * MinimumWindowViewportHeights;
      Require(
        materializedHeight >= targetHeight,
        "Materialized vwindow is shorter than the required physical minimum: " +
        $"height={materializedHeight:F1}, viewport={innerHeight:F1}, " +
        $"minimumViewports={MinimumWindowViewportHeights:F1}.");

      int focalIndex = document.Count / 2;
      int firstIndex = probe.GetProperty("firstIndex").GetInt32();
      int lastIndex = probe.GetProperty("lastIndex").GetInt32();
      double firstHeight = probe.GetProperty("firstHeight").GetDouble();
      double lastHeight = probe.GetProperty("lastHeight").GetDouble();
      if (firstIndex < focalIndex)
      {
        Require(
          materializedHeight - firstHeight < targetHeight,
          "Physical vwindow retains a removable leading unit after measured " +
          "heights are known; this unnecessarily increases render work. " +
          $"height={materializedHeight:F1}, firstHeight={firstHeight:F1}, " +
          $"target={targetHeight:F1}.");
      }
      if (lastIndex > focalIndex)
      {
        Require(
          materializedHeight - lastHeight < targetHeight,
          "Physical vwindow retains a removable trailing unit after measured " +
          "heights are known; this unnecessarily increases render work. " +
          $"height={materializedHeight:F1}, lastHeight={lastHeight:F1}, " +
          $"target={targetHeight:F1}.");
      }
'''
if text.count(old_minimum) != 1:
  raise SystemExit(f"unexpected minimum assertion count: {text.count(old_minimum)}")
text = text.replace(old_minimum, new_minimum, 1)

path.write_text(text, encoding="utf-8")
