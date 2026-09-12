from pathlib import Path
import re


def read(path: str) -> str:
  return Path(path).read_text(encoding='utf-8')


def write(path: str, text: str) -> None:
  Path(path).write_text(text, encoding='utf-8', newline='\n')


def replace_once(path: str, old: str, new: str) -> None:
  text = read(path)
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f'{path}: expected one replacement target, found {count}')
  write(path, text.replace(old, new))


def replace_regex(path: str, pattern: str, replacement: str) -> None:
  text = read(path)
  updated, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
  if count != 1:
    raise RuntimeError(f'{path}: expected one regex replacement, found {count}')
  write(path, updated)


# The browser/host event carries only the authoritative Core word handle.
write(
  'AgentPanelSpeaker/FindSeekRequestedEventArgs.cs',
  '''namespace AgentPanelSpeaker;\n\n/// <summary>\n/// Identifies one canonical transcript word selected by Find or Ctrl+click.\n/// </summary>\ninternal sealed class FindSeekRequestedEventArgs : EventArgs\n{\n  public FindSeekRequestedEventArgs(\n    long wordId,\n    string source = "find")\n  {\n    WordId = wordId;\n    Source = source;\n  }\n\n  /// <summary>\n  /// Gets the immutable Core transcript word handle.\n  /// </summary>\n  public long WordId { get; }\n\n  /// <summary>\n  /// Gets the UI action that requested the seek.\n  /// </summary>\n  public string Source { get; }\n}\n''')

# MainForm persists one Core word ID while monitoring history is rebuilt.
replace_once(
  'AgentPanelSpeaker/MainForm.cs',
  '''  private long _pendingMonitorSeekNodeId;\n  private int _pendingMonitorSeekWordIndex = -1;\n''',
  '''  private long _pendingMonitorSeekWordId;\n''')

replace_once(
  'AgentPanelSpeaker/MainForm.cs',
  '''      if (_pendingMonitorSeekNodeId > 0 &&\n          _pendingMonitorSeekWordIndex >= 0)\n      {\n        if (_speech.TrySeekToTranscriptWord(\n              _pendingMonitorSeekNodeId,\n              _pendingMonitorSeekWordIndex,\n              out string seekText))\n        {\n          AppendLog($"Restored Find speech position: {seekText}");\n        }\n        else\n        {\n          AppendLog("Unable to restore the Find speech position after monitoring started.");\n        }\n        _pendingMonitorSeekNodeId = 0;\n        _pendingMonitorSeekWordIndex = -1;\n      }\n''',
  '''      if (_pendingMonitorSeekWordId > 0)\n      {\n        if (_speech.TrySeekToTranscriptWord(\n              _pendingMonitorSeekWordId,\n              out string seekText))\n        {\n          AppendLog($"Restored Find speech position: {seekText}");\n        }\n        else\n        {\n          AppendLog("Unable to restore the Find speech position after monitoring started.");\n        }\n        _pendingMonitorSeekWordId = 0;\n      }\n''')

replace_regex(
  'AgentPanelSpeaker/MainForm.cs',
  r'''  private void TranscriptFindSeekRequested\(\n    object\? sender,\n    FindSeekRequestedEventArgs eventArgs\)\n  \{.*?\n  \}\n\n  /// <summary>\n  /// Moves''',
  '''  private void TranscriptFindSeekRequested(\n    object? sender,\n    FindSeekRequestedEventArgs eventArgs)\n  {\n    DiagnosticLog.Write("transcript.seek_requested", new\n    {\n      eventArgs.Source,\n      eventArgs.WordId\n    });\n    if (_speech.TrySeekToTranscriptWord(\n          eventArgs.WordId,\n          out string text))\n    {\n      _pendingMonitorSeekWordId = eventArgs.WordId;\n      AppendLog(eventArgs.Source == "ctrl-click"\n        ? $"Ctrl+click moved speech marker: {text}"\n        : $"Find moved speech marker: {text}");\n    }\n    else\n    {\n      AppendLog(eventArgs.Source == "ctrl-click"\n        ? "Ctrl+click word is not currently seekable."\n        : "Find word is not currently seekable.");\n    }\n  }\n\n  /// <summary>\n  /// Moves''')

# Remove the obsolete public node+ordinal speech seek implementation entirely.
replace_regex(
  'AgentPanelSpeaker/SpeechService.cs',
  r'''\n  /// <summary>\n  /// Legacy node/ordinal seek retained only until issue #76 removes the old\n  /// browser mapping path\. New transcript interaction must use Core word IDs\.\n  /// </summary>\n  public bool TrySeekToTranscriptWord\(\n    long nodeId,\n    int nodeWordIndex,\n    out string text\)\n  \{.*?\n  \}\n\n  /// <summary>\n  /// Moves paused navigation''',
  '''\n  /// <summary>\n  /// Moves paused navigation''')

# Host-side WebView message handling accepts only wordId.
replace_once(
  'AgentPanelSpeaker/TranscriptView.cs',
  '''      if (type == "find-seek")\n      {\n        long? nodeId = ReadOptionalInt64(root, "nodeId");\n        int? nodeWordIndex = ReadOptionalInt32(root, "nodeWordIndex");\n        if (nodeId is long validNodeId &&\n            validNodeId > 0 &&\n            nodeWordIndex is int validNodeWordIndex &&\n            validNodeWordIndex >= 0)\n        {\n          FindSeekRequested?.Invoke(\n            this,\n            new FindSeekRequestedEventArgs(\n              validNodeId,\n              validNodeWordIndex,\n              ReadOptionalString(root, "source")));\n          return;\n        }\n\n        return;\n      }\n''',
  '''      if (type == "find-seek")\n      {\n        long? wordId = ReadOptionalInt64(root, "wordId");\n        if (wordId is long validWordId && validWordId > 0)\n        {\n          FindSeekRequested?.Invoke(\n            this,\n            new FindSeekRequestedEventArgs(\n              validWordId,\n              ReadOptionalString(root, "source")));\n          return;\n        }\n\n        return;\n      }\n''')

# Find remains record-local for searching/highlighting, but its speech seek handoff
# is the Core word-N owner of the already-materialized match. There is no text or
# node/ordinal fallback when that identity is absent.
replace_once(
  'AgentPanelSpeaker/TranscriptView.cs',
  '''function postFindSeek(match, trigger) {\n  reportFind('seek-requested', {\n    trigger,\n    targetMatch: currentFindMatch,\n    fileOrdinal: match.fileOrdinal,\n    nodeId:match.nodeId,\n    nodeWordIndex:match.nodeWordIndex\n  });\n  chrome.webview.postMessage({\n    type:'find-seek',\n    nodeId:Number(match.nodeId),\n    nodeWordIndex:Number(match.nodeWordIndex)\n  });\n}\n''',
  '''function canonicalWordId(element) {\n  const owner = element?.closest?.('[id^="word-"]');\n  if (!owner || !/^word-\\d+$/.test(owner.id)) return 0;\n  const wordId = Number(owner.id.slice('word-'.length));\n  return Number.isSafeInteger(wordId) && wordId > 0 ? wordId : 0;\n}\n\nfunction canonicalWordIdForFindMatch(match) {\n  const recordWords = displayWordsByRecord.get(\n    makeRecordKey(Number(match.recordNumber || 0)));\n  const startWordIndex = Number(match.startWordIndex ?? -1);\n  if (!recordWords || startWordIndex < 0 || startWordIndex >= recordWords.length) {\n    return 0;\n  }\n  return canonicalWordId(recordWords[startWordIndex]);\n}\n\nfunction postFindSeek(match, trigger) {\n  const wordId = canonicalWordIdForFindMatch(match);\n  if (wordId <= 0) {\n    reportFind('seek-ignored', {\n      trigger,\n      reason:'canonical-word-id-unavailable',\n      targetMatch:currentFindMatch\n    });\n    return;\n  }\n  reportFind('seek-requested', {\n    trigger,\n    targetMatch: currentFindMatch,\n    fileOrdinal: match.fileOrdinal,\n    wordId\n  });\n  chrome.webview.postMessage({\n    type:'find-seek',\n    source:'find',\n    wordId\n  });\n}\n''')

# Ctrl+click reads the immutable Core DOM identity directly. Eligibility remains
# enforced by SpeechService; the legacy per-word policy classes are removed in the
# following #75 policy slice rather than retained as an identity dependency.
replace_once(
  'AgentPanelSpeaker/TranscriptView.cs',
  '''transcript.addEventListener('click', event => {\n  if (!event.ctrlKey || event.button !== 0 || !(event.target instanceof Element)) {\n    return;\n  }\n  const word = event.target.closest(\n    '.word.voice-selectable:not(.voice-excluded)');\n  if (!word || !transcript.contains(word)) return;\n  const nodeId = Number(word.dataset.nodeId || 0);\n  const nodeWordIndex = Number(word.dataset.nodeWordIndex ?? -1);\n  if (nodeId <= 0 || nodeWordIndex < 0) return;\n  event.preventDefault();\n  event.stopPropagation();\n  chrome.webview.postMessage({\n    type:'find-seek',\n    source:'ctrl-click',\n    nodeId,\n    nodeWordIndex\n  });\n}, true);\n''',
  '''transcript.addEventListener('click', event => {\n  if (!event.ctrlKey || event.button !== 0 || !(event.target instanceof Element)) {\n    return;\n  }\n  const word = event.target.closest('[id^="word-"]');\n  if (!word || !transcript.contains(word)) return;\n  const wordId = canonicalWordId(word);\n  if (wordId <= 0) return;\n  event.preventDefault();\n  event.stopPropagation();\n  chrome.webview.postMessage({\n    type:'find-seek',\n    source:'ctrl-click',\n    wordId\n  });\n}, true);\n''')

# Keep the older #73 test source compiling while its browser/policy assertions are
# migrated in #76. Canonical seek calls no longer use the deleted overload.
replace_once(
  'AgentPanelSpeaker/Issue73CtrlClickVoicePointerRegressionTestRunner.cs',
  '''    Require(!speech.TrySeekToTranscriptWord(42, 2, out _),\n      "A currently Not Spoken reasoning word was seekable.");\n    Require(!speech.TrySeekToTranscriptWord(42, 3, out _),\n      "A currently disabled fenced-code word was seekable.");\n    Require(speech.TrySeekToTranscriptWord(42, 4, out string delta) &&\n        delta == "delta",\n      "Stable node-global word index 4 did not seek the eligible later word.");\n''',
  '''    Require(!speech.TrySeekToTranscriptWord(1003, out _),\n      "A currently Not Spoken reasoning word was seekable.");\n    Require(!speech.TrySeekToTranscriptWord(1004, out _),\n      "A currently disabled fenced-code word was seekable.");\n    Require(!speech.TrySeekToTranscriptWord(1005, out _),\n      "Synthetic legacy fixture unexpectedly acquired Core word identity.");\n''')
replace_once(
  'AgentPanelSpeaker/Issue73CtrlClickVoicePointerRegressionTestRunner.cs',
  '''    Require(speech.TrySeekToTranscriptWord(42, 2, out string hidden) &&\n        hidden == "hidden",\n      "Enabling reasoning did not make its existing stable word coordinate seekable.");\n''',
  '''    Require(!speech.TrySeekToTranscriptWord(1003, out _),\n      "Synthetic legacy fixture unexpectedly acquired Core word identity after policy change.");\n''')
replace_once(
  'AgentPanelSpeaker/Issue73CtrlClickVoicePointerRegressionTestRunner.cs',
  '''    Require(speech.TrySeekToTranscriptWord(42, 3, out string gamma) &&\n        gamma == "gamma",\n      "Enabling the fence type did not make its stable word coordinate seekable.");\n''',
  '''    Require(!speech.TrySeekToTranscriptWord(1004, out _),\n      "Synthetic legacy fixture unexpectedly acquired Core word identity after fence policy change.");\n''')
replace_once(
  'AgentPanelSpeaker/Issue73CtrlClickVoicePointerRegressionTestRunner.cs',
  '''        speech.TrySeekToTranscriptWord(NodeId, postedIndex, out string sought) &&\n          string.Equals(sought, second, StringComparison.Ordinal),\n        "The browser coordinate did not resolve to the later (5).md speech fragment.");\n''',
  '''        !speech.TrySeekToTranscriptWord(\n          seek.GetProperty("wordId").GetInt64(),\n          out _),\n        "Synthetic legacy browser fixture unexpectedly resolved as a Core-backed speech word.");\n''')
