from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

old = '''  const visibleStartIndex = visibleRecords.length
    ? Number(visibleRecords[0].dataset.virtualIndex || -1)
    : null;
  const visibleEndIndex = visibleRecords.length
    ? Number(visibleRecords[visibleRecords.length - 1].dataset.virtualIndex || -1)
    : null;
  virtualShiftPending = true;
  chrome.webview.postMessage({
    type:'window-shift',
    reason:reason || (direction < 0 ? 'scroll-up' : 'scroll-down'),
    focalIndex:visibleIndex,
    visibleStartIndex,
    visibleEndIndex,
    viewportHeight:window.innerHeight,
'''
new = '''  const visibleRange = visibleRecords.length
    ? {
        visibleStartIndex:Number(
          visibleRecords[0].dataset.virtualIndex || -1),
        visibleEndIndex:Number(
          visibleRecords[visibleRecords.length - 1].dataset.virtualIndex || -1)
      }
    : {};
  virtualShiftPending = true;
  chrome.webview.postMessage({
    type:'window-shift',
    reason:reason || (direction < 0 ? 'scroll-up' : 'scroll-down'),
    focalIndex:visibleIndex,
    ...visibleRange,
    viewportHeight:window.innerHeight,
'''
if text.count(old) != 1:
  raise SystemExit('visible-range message omission anchor mismatch')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
