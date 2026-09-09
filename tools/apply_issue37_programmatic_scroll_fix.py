from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptView.cs")
text = path.read_text(encoding="utf-8")
old = '''  virtualShiftTimer = setTimeout(() => {
    virtualShiftTimer = 0;
    const visibleRecord = firstVisibleVirtualRecord();
'''
new = '''  virtualShiftTimer = setTimeout(() => {
    virtualShiftTimer = 0;
    // Window replacement, playback reveal, and anchor restoration all scroll
    // programmatically.  Those scroll events must not be reinterpreted as
    // manual edge navigation or they can bounce the virtual window away from
    // the playback target and back indefinitely.
    if (performance.now() <= programmaticScrollUntil) return;
    const visibleRecord = firstVisibleVirtualRecord();
'''
if old not in text:
  raise SystemExit("programmatic virtual-shift insertion point was not found")
if text.count(old) != 1:
  raise SystemExit(f"expected one insertion point, found {text.count(old)}")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
