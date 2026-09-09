from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptView.cs")
text = path.read_text(encoding="utf-8")

old = '''let scrollFollowTimer = 0;
let virtualShiftFrame = 0;
let lastManualScrollY = window.scrollY;
'''
new = '''const VW_USER_SCROLL_INTENT_MS = 1200;
const VW_WINDOW_REPLACEMENT_SCROLL_GUARD_MS = 300;
const VW_SCROLL_KEYS = new Set([
  'ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' '
]);
let virtualShiftFrame = 0;
let lastManualScrollY = window.scrollY;
let userScrollIntentUntil = 0;

function markUserScrollIntent() {
  userScrollIntentUntil = performance.now() + VW_USER_SCROLL_INTENT_MS;
}

function isEditableScrollTarget(target) {
  return target instanceof Element &&
    (target.matches('input,textarea,select') || target.isContentEditable);
}

window.addEventListener('wheel', markUserScrollIntent, {
  passive:true,
  capture:true
});
window.addEventListener('touchstart', markUserScrollIntent, {
  passive:true,
  capture:true
});
window.addEventListener('touchmove', markUserScrollIntent, {
  passive:true,
  capture:true
});
window.addEventListener('keydown', event => {
  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey ||
      !VW_SCROLL_KEYS.has(event.key) || isEditableScrollTarget(event.target)) {
    return;
  }
  markUserScrollIntent();
}, {capture:true});
window.addEventListener('pointerdown', event => {
  if (event.button !== 0) return;
  const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth;
  if (scrollbarWidth > 0 && event.clientX >= document.documentElement.clientWidth) {
    markUserScrollIntent();
  }
}, {capture:true});
'''
if text.count(old) != 1:
  raise SystemExit("physical scroll-controller declarations did not match exactly once")
text = text.replace(old, new, 1)

old = '''  const exactAssignedHtml =
    '<div class="virtual-spacer" data-virtual-spacer="top" style="height:' +
'''
new = '''  // Replacing spacer heights and materialized records can itself change
  // scrollY.  Mark that layout-induced movement as programmatic.  Genuine
  // wheel/touch/scroll-key/scrollbar input can explicitly override this guard.
  programmaticScrollUntil = Math.max(
    programmaticScrollUntil,
    performance.now() + VW_WINDOW_REPLACEMENT_SCROLL_GUARD_MS);
  const exactAssignedHtml =
    '<div class="virtual-spacer" data-virtual-spacer="top" style="height:' +
'''
if text.count(old) != 1:
  raise SystemExit("replaceTranscriptWindow assigned-html anchor did not match exactly once")
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
  const delta = currentY - lastManualScrollY;
  lastManualScrollY = currentY;
  if (Math.abs(delta) <= VW_SCROLL_DIRECTION_EPSILON_PX) return;

  const explicitUserIntent = now <= userScrollIntentUntil;
  if (explicitUserIntent) {
    // User navigation always wins, even if it interrupts a smooth playback
    // scroll or a vwindow replacement whose guard is still active.
    programmaticScrollUntil = 0;
  } else if (now <= programmaticScrollUntil) {
    return;
  }

  if (followSpeech) setFollowSpeech(false, true);

  const direction = delta > 0 ? 1 : -1;
'''
if text.count(old) != 1:
  raise SystemExit("physical scroll-controller follow block did not match exactly once")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
