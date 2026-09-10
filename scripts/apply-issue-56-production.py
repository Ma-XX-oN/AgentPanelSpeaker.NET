from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8-sig')
replacements = [
  (
    """let virtualShiftFrame = 0;\nlet lastManualScrollY = window.scrollY;\nlet userScrollIntentUntil = 0;\n\nfunction markUserScrollIntent() {\n  userScrollIntentUntil = performance.now() + VW_USER_SCROLL_INTENT_MS;\n}\n""",
    """let virtualShiftFrame = 0;\nlet lastManualScrollY = window.scrollY;\nlet userScrollIntentUntil = 0;\nlet userScrollIntentDirection = 0;\nlet lastTouchY = Number.NaN;\n\nfunction markUserScrollIntent(direction = 0) {\n  userScrollIntentUntil = performance.now() + VW_USER_SCROLL_INTENT_MS;\n  userScrollIntentDirection = Math.sign(Number(direction) || 0);\n}\n""",
    'user-intent state'),
  (
    """window.addEventListener('wheel', markUserScrollIntent, {\n  passive:true,\n  capture:true\n});\nwindow.addEventListener('touchstart', markUserScrollIntent, {\n  passive:true,\n  capture:true\n});\nwindow.addEventListener('touchmove', markUserScrollIntent, {\n  passive:true,\n  capture:true\n});\nwindow.addEventListener('keydown', event => {\n  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey ||\n      !VW_SCROLL_KEYS.has(event.key) || isEditableScrollTarget(event.target)) {\n    return;\n  }\n  markUserScrollIntent();\n}, {capture:true});\n""",
    """window.addEventListener('wheel', event => {\n  markUserScrollIntent(event.deltaY);\n}, {\n  passive:true,\n  capture:true\n});\nwindow.addEventListener('touchstart', event => {\n  lastTouchY = event.touches.length > 0\n    ? event.touches[0].clientY\n    : Number.NaN;\n  markUserScrollIntent();\n}, {\n  passive:true,\n  capture:true\n});\nwindow.addEventListener('touchmove', event => {\n  const currentTouchY = event.touches.length > 0\n    ? event.touches[0].clientY\n    : Number.NaN;\n  const direction = Number.isFinite(lastTouchY) &&\n    Number.isFinite(currentTouchY)\n      ? lastTouchY - currentTouchY\n      : 0;\n  lastTouchY = currentTouchY;\n  markUserScrollIntent(direction);\n}, {\n  passive:true,\n  capture:true\n});\nwindow.addEventListener('keydown', event => {\n  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey ||\n      !VW_SCROLL_KEYS.has(event.key) || isEditableScrollTarget(event.target)) {\n    return;\n  }\n  let direction = 0;\n  if (event.key === 'ArrowUp' || event.key === 'PageUp' ||\n      event.key === 'Home') {\n    direction = -1;\n  } else if (event.key === 'ArrowDown' || event.key === 'PageDown' ||\n             event.key === 'End') {\n    direction = 1;\n  } else if (event.key === ' ') {\n    direction = event.shiftKey ? -1 : 1;\n  }\n  markUserScrollIntent(direction);\n}, {capture:true});\n""",
    'physical-input direction'),
  (
    """  const explicitUserIntent = now <= userScrollIntentUntil;\n  if (explicitUserIntent) {\n    // User navigation always wins, even if it interrupts a smooth playback\n    // scroll or a vwindow replacement whose guard is still active.\n    programmaticScrollUntil = 0;\n  } else if (now <= programmaticScrollUntil) {\n    return;\n  }\n\n  if (followSpeech) setFollowSpeech(false, true);\n\n  const direction = delta > 0 ? 1 : -1;\n""",
    """  const direction = delta > 0 ? 1 : -1;\n  const explicitUserIntent = now <= userScrollIntentUntil;\n  if (now <= programmaticScrollUntil) {\n    // A physical input can override a programmatic scroll only when the\n    // resulting movement agrees with that input. Window replacement and\n    // anchor restoration can move scrollY in the opposite direction while the\n    // earlier user-intent timer is still alive; that movement is not a second\n    // user gesture and must not start a competing virtual-window shift.\n    if (!explicitUserIntent ||\n        (userScrollIntentDirection !== 0 &&\n         direction !== userScrollIntentDirection)) {\n      return;\n    }\n    programmaticScrollUntil = 0;\n  }\n\n  if (followSpeech) setFollowSpeech(false, true);\n""",
    'scroll guard')
]

for old, new, label in replacements:
  count = text.count(old)
  if count != 1:
    raise SystemExit(f'Issue #56 patch anchor {label!r} matched {count} times')
  text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
