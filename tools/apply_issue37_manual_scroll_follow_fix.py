from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptView.cs")
text = path.read_text(encoding="utf-8")

old = '''let scrollFollowTimer = 0;
let virtualShiftFrame = 0;
let lastManualScrollY = window.scrollY;
'''
new = '''let virtualShiftFrame = 0;
let lastManualScrollY = window.scrollY;
'''
if text.count(old) != 1:
  raise SystemExit("physical scroll-controller declarations did not match exactly once")
text = text.replace(old, new, 1)

old = '''window.addEventListener('scroll', () => {
  const now = performance.now();
  if (followSpeech && now > programmaticScrollUntil) {
    if (scrollFollowTimer) clearTimeout(scrollFollowTimer);
    scrollFollowTimer = setTimeout(() => {
      scrollFollowTimer = 0;
      if (followSpeech && performance.now() > programmaticScrollUntil) {
        setFollowSpeech(false, true);
      }
    }, 120);
  }

  const currentY = window.scrollY;
  if (now <= programmaticScrollUntil) {
    lastManualScrollY = currentY;
    return;
  }
  const delta = currentY - lastManualScrollY;
  lastManualScrollY = currentY;
  if (Math.abs(delta) <= VW_SCROLL_DIRECTION_EPSILON_PX) return;
  const direction = delta > 0 ? 1 : -1;
'''
new = '''window.addEventListener('scroll', () => {
  const now = performance.now();
  const currentY = window.scrollY;
  if (now <= programmaticScrollUntil) {
    lastManualScrollY = currentY;
    return;
  }
  const delta = currentY - lastManualScrollY;
  lastManualScrollY = currentY;
  if (Math.abs(delta) <= VW_SCROLL_DIRECTION_EPSILON_PX) return;

  // A genuine user scroll owns navigation immediately.  Do this before a
  // virtual-window request can replace the DOM and mark anchor restoration as
  // programmatic, otherwise Follow can remain logically enabled while the
  // vwindow follows the user's scrolling.
  if (followSpeech) setFollowSpeech(false, true);

  const direction = delta > 0 ? 1 : -1;
'''
if text.count(old) != 1:
  raise SystemExit("physical scroll-controller follow block did not match exactly once")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
