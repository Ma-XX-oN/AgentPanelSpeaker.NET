from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptView.cs")
text = path.read_text(encoding="utf-8")

old = '''    bool VoicedEnabled,\n    bool HasSelectionOrigin,\n    int OriginRecordNumber,\n'''
new = '''    bool VoicedEnabled,\n    string OriginKind,\n    int OriginRecordNumber,\n'''
if old not in text:
  raise RuntimeError("PendingFindRequest origin field marker not found")
text = text.replace(old, new, 1)

old = '''      ReadOptionalBoolean(root, "voicedEnabled") != false,\n      string.Equals(\n        ReadOptionalString(root, "originKind"),\n        "selection",\n        StringComparison.Ordinal),\n      ReadOptionalInt32(root, "originRecordNumber") ?? 0,\n'''
new = '''      ReadOptionalBoolean(root, "voicedEnabled") != false,\n      ReadOptionalString(root, "originKind"),\n      ReadOptionalInt32(root, "originRecordNumber") ?? 0,\n'''
if old not in text:
  raise RuntimeError("HandleFindQuery origin marker not found")
text = text.replace(old, new, 1)

old = '''    int originRecordNumber = pending.OriginRecordNumber;\n    int originWordIndex = pending.OriginWordIndex;\n    if (!pending.HasSelectionOrigin &&\n        _pendingPosition is TranscriptPlaybackPosition voicePosition &&\n'''
new = '''    int originRecordNumber = pending.OriginRecordNumber;\n    int originWordIndex = pending.OriginWordIndex;\n    bool hasProvidedOrigin = IsProvidedFindOriginKind(pending.OriginKind);\n    if (!hasProvidedOrigin &&\n        _pendingPosition is TranscriptPlaybackPosition voicePosition &&\n'''
if old not in text:
  raise RuntimeError("ExecuteFindQueryAsync origin resolution marker not found")
text = text.replace(old, new, 1)

old = '''        originKind = pending.HasSelectionOrigin ? "selection" : "voice",\n        originRecordNumber,\n'''
new = '''        originKind = hasProvidedOrigin ? pending.OriginKind : "voice",\n        originRecordNumber,\n'''
if old not in text:
  raise RuntimeError("Find diagnostic origin marker not found")
text = text.replace(old, new, 1)

marker = '''  private static IReadOnlyList<TranscriptSearchMatch> RotateMatchesAfterOrigin(\n'''
helper = '''  private static bool IsProvidedFindOriginKind(string originKind)\n  {\n    return string.Equals(originKind, "selection", StringComparison.Ordinal) ||\n      string.Equals(originKind, "find", StringComparison.Ordinal);\n  }\n\n'''
if helper not in text:
  if marker not in text:
    raise RuntimeError("origin-kind helper insertion marker not found")
  text = text.replace(marker, helper + marker, 1)

old = '''function getFindOrigin() {\n  const selection = window.getSelection();\n  if (selection && selection.rangeCount > 0 && !selection.isCollapsed) {\n    const range = selection.getRangeAt(0);\n    const node = range.endContainer.nodeType === Node.ELEMENT_NODE\n      ? range.endContainer\n      : range.endContainer.parentElement;\n    const word = node?.closest?.('.word');\n    if (word) {\n      return {\n        kind:'selection',\n        recordNumber:Number(word.dataset.recordNumber || 0),\n        wordIndex:Number(word.dataset.recordIndex || -1)\n      };\n    }\n  }\n  return {kind:'voice', recordNumber:0, wordIndex:-1};\n}\n'''
new = '''function getFindOrigin() {\n  const selection = window.getSelection();\n  if (selection && selection.rangeCount > 0 && !selection.isCollapsed) {\n    const range = selection.getRangeAt(0);\n    const node = range.endContainer.nodeType === Node.ELEMENT_NODE\n      ? range.endContainer\n      : range.endContainer.parentElement;\n    const word = node?.closest?.('.word');\n    if (word) {\n      return {\n        kind:'selection',\n        recordNumber:Number(word.dataset.recordNumber || 0),\n        wordIndex:Number(word.dataset.recordIndex || -1)\n      };\n    }\n  }\n  if (currentFindMatch >= 0 && currentFindMatch < findMatches.length) {\n    const match = findMatches[currentFindMatch];\n    return {\n      kind:'find',\n      recordNumber:Number(match.recordNumber || 0),\n      wordIndex:Number(match.startWordIndex ?? -1)\n    };\n  }\n  return {kind:'voice', recordNumber:0, wordIndex:-1};\n}\n'''
if old not in text:
  raise RuntimeError("getFindOrigin marker not found")
text = text.replace(old, new, 1)

path.write_text(text, encoding="utf-8", newline="\n")
