from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptView.cs")
text = path.read_text(encoding="utf-8")

replacements = [
  (
    "let findMatches = [];\nlet currentFindMatch = -1;\nlet findGeneration = 0;",
    "let findMatches = [];\nlet currentFindMatch = -1;\nlet findEditOrigin = null;\nlet findGeneration = 0;"
  ),
  (
    """function runFind() {\n  const origin = getFindOrigin();\n  if (followSpeech) setFollowSpeech(false, true);""",
    """function runFind() {\n  if (findEditOrigin === null) {\n    findEditOrigin = getFindOrigin();\n  }\n  const origin = findEditOrigin;\n  if (followSpeech) setFollowSpeech(false, true);"""
  ),
  (
    """findInput.addEventListener('input', () => {\n  if (findInputTimer) clearTimeout(findInputTimer);\n  cancelFindSearch(false);""",
    """findInput.addEventListener('beforeinput', () => {\n  const replacesEntireQuery = findInput.value.length > 0 &&\n    findInput.selectionStart === 0 &&\n    findInput.selectionEnd === findInput.value.length;\n  if (replacesEntireQuery || findEditOrigin === null) {\n    findEditOrigin = getFindOrigin();\n  }\n});\nfindInput.addEventListener('input', () => {\n  if (findEditOrigin === null) {\n    findEditOrigin = getFindOrigin();\n  }\n  if (findInputTimer) clearTimeout(findInputTimer);\n  cancelFindSearch(false);"""
  ),
  (
    """function closeFind() {\n  if (findInputTimer) {""",
    """function closeFind() {\n  findEditOrigin = null;\n  if (findInputTimer) {"""
  ),
  (
    """function toggleFindOption(button, setter) {\n  setter();\n  button.classList.toggle('enabled');\n  runFind();\n}""",
    """function toggleFindOption(button, setter) {\n  findEditOrigin = getFindOrigin();\n  setter();\n  button.classList.toggle('enabled');\n  runFind();\n}"""
  ),
  (
    """  currentFindMatch = (index + findMatches.length) % findMatches.length;\n  const match = findMatches[currentFindMatch];\n  if (followSpeech) setFollowSpeech(false, true);""",
    """  currentFindMatch = (index + findMatches.length) % findMatches.length;\n  const match = findMatches[currentFindMatch];\n  if (trigger === 'enter' ||\n      trigger === 'shift-enter' ||\n      trigger === 'button-previous' ||\n      trigger === 'button-next' ||\n      trigger === 'reopened') {\n    findEditOrigin = null;\n  }\n  if (followSpeech) setFollowSpeech(false, true);"""
  )
]

for old, new in replacements:
  count = text.count(old)
  if count != 1:
    raise SystemExit(f"Expected exactly one match, found {count}: {old[:80]!r}")
  text = text.replace(old, new, 1)

path.write_text(text, encoding="utf-8")
