from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

replacements = [
  (
    ".word.active { background: var(--highlight); }\n.word.paused {\n  outline: 2px solid var(--highlight);\n  outline-offset: 1px;\n  animation: marker-blink 1s steps(1, end) infinite;\n}\n",
    ".word.active { background: var(--highlight); }\n.word.paused {\n  outline: 2px solid var(--highlight);\n  outline-offset: 1px;\n  animation: marker-blink 1s steps(1, end) infinite;\n}\nli.speech-list-item-active {\n  background: var(--highlight);\n  border-radius: 3px;\n}\nli.speech-list-item-paused {\n  outline: 2px solid var(--highlight);\n  outline-offset: 1px;\n  animation: marker-blink 1s steps(1, end) infinite;\n}\n"
  ),
  (
    "let currentBoundaryWordIndex = -1;\nlet fadeMs = 250;",
    "let currentBoundaryWordIndex = -1;\nlet currentSpeechListItem = null;\nlet fadeMs = 250;"
  ),
  (
    "function cancelFade(word) {\n  const animation = fadingAnimations.get(word);",
    "function ordinalListItemForWord(word) {\n  const ordinal = word?.closest('.speech-ordinal-map');\n  return ordinal ? ordinal.closest('li') : null;\n}\n\nfunction clearSpeechListItemHighlight() {\n  if (!currentSpeechListItem) return;\n  currentSpeechListItem.classList.remove(\n    'speech-list-item-active',\n    'speech-list-item-paused');\n  currentSpeechListItem = null;\n}\n\nfunction cancelFade(word) {\n  const animation = fadingAnimations.get(word);"
  ),
  (
    "function setPlayback(state, fragmentText, wordIndex, wordText, nodeId, follow) {\n  setFollowSpeech(follow, false);\n  clearMarkers();",
    "function setPlayback(state, fragmentText, wordIndex, wordText, nodeId, follow) {\n  setFollowSpeech(follow, false);\n  clearMarkers();\n  clearSpeechListItemHighlight();"
  ),
  (
    "  const target = words[range.start];\n  openAncestors(target);\n  if (state === 'paused') {\n    retireCurrentWord(false);\n    applyRangeClass(range, 'paused');\n  } else {\n    if (currentIndex >= 0 &&\n        (currentIndex !== range.start || currentEndIndex !== range.end)) {\n      retireCurrentWord(true);\n    }\n    applyRangeClass(range, 'active');\n  }\n  currentIndex = range.start;",
    "  const target = words[range.start];\n  const listItem = ordinalListItemForWord(target);\n  openAncestors(listItem || target);\n  if (state === 'paused') {\n    retireCurrentWord(false);\n    if (listItem) {\n      listItem.classList.add('speech-list-item-paused');\n      currentSpeechListItem = listItem;\n    } else {\n      applyRangeClass(range, 'paused');\n    }\n  } else {\n    if (currentIndex >= 0 &&\n        (currentIndex !== range.start || currentEndIndex !== range.end)) {\n      retireCurrentWord(true);\n    }\n    if (listItem) {\n      listItem.classList.add('speech-list-item-active');\n      currentSpeechListItem = listItem;\n    } else {\n      applyRangeClass(range, 'active');\n    }\n  }\n  currentIndex = range.start;"
  ),
  (
    "  reveal(target);\n}\n\n\nfunction clearFindHighlights()",
    "  reveal(listItem || target);\n}\n\n\nfunction clearFindHighlights()"
  ),
  (
    "  currentBoundaryWordIndex = -1;\n  liveEndMarker.style.display = 'none';\n  if (preserve) {\n    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);",
    "  currentBoundaryWordIndex = -1;\n  currentSpeechListItem = null;\n  liveEndMarker.style.display = 'none';\n  if (preserve) {\n    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);"
  ),
]

for old, new in replacements:
  count = text.count(old)
  if count == 0:
    raise SystemExit(f'Expected patch anchor not found:\n{old[:160]}')
  if old.startswith('  currentBoundaryWordIndex = -1;'):
    # This reset exists in both full-DOM and virtual-window replacement paths.
    text = text.replace(old, new)
  elif count != 1:
    raise SystemExit(f'Patch anchor occurred {count} times; expected exactly one:\n{old[:160]}')
  else:
    text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8', newline='')
