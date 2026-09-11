from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# -----------------------------------------------------------------------------
# TranscriptView.cs: keep clickable words, but put their nodeWordIndex in the
# exact SpeechTokenization ordinal space (which includes punctuation/symbols).
# -----------------------------------------------------------------------------
path = ROOT / "AgentPanelSpeaker" / "TranscriptView.cs"
text = path.read_text(encoding="utf-8")
old = r'''function markVoiceSelectableWords(
  collection,
  start,
  end,
  nodeId,
  startNodeWordIndex) {
  let nodeWordIndex = startNodeWordIndex;
  for (let index = start; index <= end; ++index) {
    const word = collection[index];
    if (!word) continue;
    word.classList.add('voice-selectable');
    word.dataset.nodeId = String(nodeId);
    word.dataset.nodeWordIndex = String(nodeWordIndex++);
  }
}

function markVoiceSelectableWordsByGlobalRange(
  collection,
  globalStart,
  globalEnd,
  nodeId,
  startNodeWordIndex) {
  const selected = collection.filter(word => {
    const index = Number(word.dataset.index ?? -1);
    return index >= globalStart && index <= globalEnd;
  });
  if (!selected.length) return;
  let nodeWordIndex = startNodeWordIndex;
  for (const word of selected) {
    word.classList.add('voice-selectable');
    word.dataset.nodeId = String(nodeId);
    word.dataset.nodeWordIndex = String(nodeWordIndex++);
  }
}
'''
new = r'''function markVoiceSelectableWords(
  collection,
  start,
  end,
  nodeId,
  startNodeWordIndex,
  speechTokenOffsets) {
  let relativeLexicalIndex = 0;
  for (let index = start; index <= end; ++index) {
    const word = collection[index];
    if (!word) continue;
    const speechTokenOffset = speechTokenOffsets[relativeLexicalIndex++];
    if (speechTokenOffset === undefined) continue;
    word.classList.add('voice-selectable');
    word.dataset.nodeId = String(nodeId);
    word.dataset.nodeWordIndex = String(
      startNodeWordIndex + speechTokenOffset);
  }
}

function markVoiceSelectableWordsByGlobalRange(
  collection,
  globalStart,
  globalEnd,
  nodeId,
  startNodeWordIndex,
  speechTokenOffsets) {
  const selected = collection.filter(word => {
    const index = Number(word.dataset.index ?? -1);
    return index >= globalStart && index <= globalEnd;
  });
  if (!selected.length) return;
  for (let index = 0; index < selected.length; ++index) {
    const speechTokenOffset = speechTokenOffsets[index];
    if (speechTokenOffset === undefined) continue;
    const word = selected[index];
    word.classList.add('voice-selectable');
    word.dataset.nodeId = String(nodeId);
    word.dataset.nodeWordIndex = String(
      startNodeWordIndex + speechTokenOffset);
  }
}
'''
if old not in text:
  raise SystemExit("TranscriptView voice-selectable helper block not found")
text = text.replace(old, new, 1)
old = r'''      const displayTarget = tokenizeDisplay(segment);
      const lexicalTarget = tokenize(segment);
      const segmentNodeWordStart = nodeWordIndex;
      nodeWordIndex += lexicalTarget.length;
      nodeWordCursors.set(nodeId, nodeWordIndex);
'''
new = r'''      const displayTarget = tokenizeDisplay(segment);
      const lexicalTarget = tokenize(segment);
      const speechTokenOffsets = displayTarget
        .map((token, index) => isLexical(token) ? index : -1)
        .filter(index => index >= 0);
      const segmentNodeWordStart = nodeWordIndex;
      // SpeechService indexes every SpeechTokenization token, including
      // punctuation/symbols.  Clickable lexical words therefore keep gaps for
      // punctuation instead of being renumbered into a lexical-only space.
      nodeWordIndex += displayTarget.length;
      nodeWordCursors.set(nodeId, nodeWordIndex);
'''
if old not in text:
  raise SystemExit("TranscriptView node cursor block not found")
text = text.replace(old, new, 1)
old = r'''        markVoiceSelectableWordsByGlobalRange(
          recordLexicalWords,
          globalStart,
          globalEnd,
          nodeId,
          segmentNodeWordStart);
'''
new = r'''        markVoiceSelectableWordsByGlobalRange(
          recordLexicalWords,
          globalStart,
          globalEnd,
          nodeId,
          segmentNodeWordStart,
          speechTokenOffsets);
'''
if old not in text:
  raise SystemExit("TranscriptView display-range voice mapping call not found")
text = text.replace(old, new, 1)
old = r'''        markVoiceSelectableWords(
          recordLexicalWords,
          lexicalStart,
          lexicalEnd,
          nodeId,
          segmentNodeWordStart);
'''
new = r'''        markVoiceSelectableWords(
          recordLexicalWords,
          lexicalStart,
          lexicalEnd,
          nodeId,
          segmentNodeWordStart,
          speechTokenOffsets);
'''
if old not in text:
  raise SystemExit("TranscriptView lexical-fallback voice mapping call not found")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")

# -----------------------------------------------------------------------------
# SpeechService.cs: when a policy change makes the paused target ineligible,
# reuse the established forward-to-next-eligible / otherwise-live-end policy.
# -----------------------------------------------------------------------------
path = ROOT / "AgentPanelSpeaker" / "SpeechService.cs"
text = path.read_text(encoding="utf-8")
marker = '''  /// <summary>\n  /// Gets enabled installed voices and their descriptive labels.\n'''
method = '''  /// <summary>\n  /// Revalidates a paused history cursor after a speech-eligibility policy\n  /// change. If the current fragment became ineligible, navigation advances\n  /// to the next eligible fragment, or to the paused live end when none\n  /// remains. Canonical history and node identities are unchanged.\n  /// </summary>\n  public void RevalidatePausedNavigationEligibility(string reason)\n  {\n    ArgumentException.ThrowIfNullOrWhiteSpace(reason);\n    lock (_sync)\n    {\n      ThrowIfDisposed();\n      if (!_isPaused || _history.Count == 0)\n      {\n        return;\n      }\n\n      int anchor = _activeHistoryIndex >= 0\n        ? _activeHistoryIndex\n        : _pendingHistoryIndex ?? _nextHistoryIndex;\n      if (anchor < 0 || anchor >= _history.Count ||\n          TryGetEligibleProfileLocked(_history[anchor], out _, out _))\n      {\n        return;\n      }\n\n      int candidate = FindNextEligibleLocked(anchor + 1);\n      DiagnosticLog.Write("speech.paused_eligibility_revalidated", new\n      {\n        reason,\n        anchor,\n        anchorNodeId = GetHistoryNodeIdLocked(anchor),\n        candidate,\n        candidateNodeId = GetHistoryNodeIdLocked(candidate),\n        activeKind = _activeKind.ToString(),\n        activeHistoryIndex = _activeHistoryIndex,\n        pendingHistoryIndex = _pendingHistoryIndex,\n        pendingHistoryWordIndex = _pendingHistoryWordIndex,\n        nextHistoryIndex = _nextHistoryIndex,\n        historyCount = _history.Count\n      });\n\n      if (_activeKind == ActiveSpeechKind.History)\n      {\n        if (candidate >= 0)\n        {\n          RestartHistoryLocked(candidate);\n        }\n        else\n        {\n          MoveToPausedLiveEndLocked();\n        }\n        return;\n      }\n\n      if (candidate >= 0)\n      {\n        _pendingHistoryIndex = candidate;\n        _pendingHistoryWordIndex = 0;\n        _nextHistoryIndex = candidate;\n        _lastFenceActivity = null;\n        SetPausedNavigationPositionLocked(candidate);\n      }\n      else\n      {\n        MoveToPausedLiveEndLocked();\n      }\n    }\n  }\n\n'''
if marker not in text:
  raise SystemExit("SpeechService insertion marker not found")
text = text.replace(marker, method + marker, 1)
path.write_text(text, encoding="utf-8")

# -----------------------------------------------------------------------------
# MainForm.cs: invoke the authoritative speech revalidation at every settings
# path that can change final playback eligibility, and log requested seek coords.
# -----------------------------------------------------------------------------
path = ROOT / "AgentPanelSpeaker" / "MainForm.cs"
text = path.read_text(encoding="utf-8")
old = '''      _speech.SetShowRolledBackHistory(\n        settings.Transcript.ShowRolledBackHistory);\n      _transcriptView.ApplySettings(settings.Transcript, transcriptDark);\n'''
new = '''      _speech.SetShowRolledBackHistory(\n        settings.Transcript.ShowRolledBackHistory);\n      _speech.RevalidatePausedNavigationEligibility(\n        "settings-loaded");\n      _transcriptView.ApplySettings(settings.Transcript, transcriptDark);\n'''
if old not in text:
  raise SystemExit("MainForm settings-load policy block not found")
text = text.replace(old, new, 1)
old = '''    SaveControlsToSettings();\n    RefreshTranscriptVoiceSelectability();\n    AppendLog(\n      "Spoken fenced-code types updated: " +\n'''
new = '''    SaveControlsToSettings();\n    _speech.RevalidatePausedNavigationEligibility(\n      "fenced-code-types-changed");\n    RefreshTranscriptVoiceSelectability();\n    AppendLog(\n      "Spoken fenced-code types updated: " +\n'''
if old not in text:
  raise SystemExit("MainForm fence policy block not found")
text = text.replace(old, new, 1)
old = '''    row.Voice.Invalidate();\n    SaveControlsToSettings();\n    RefreshTranscriptVoiceSelectability();\n    ScheduleVoiceSettingsPreview(role, context);\n'''
new = '''    row.Voice.Invalidate();\n    SaveControlsToSettings();\n    _speech.RevalidatePausedNavigationEligibility(\n      "voice-profile-changed");\n    RefreshTranscriptVoiceSelectability();\n    ScheduleVoiceSettingsPreview(role, context);\n'''
if old not in text:
  raise SystemExit("MainForm voice policy block not found")
text = text.replace(old, new, 1)
old = '''    _speakUserContext = settings.SpeakUserContext;\n    _speech.SetShowRolledBackHistory(settings.ShowRolledBackHistory);\n    _transcriptView.ApplySettings(settings, dark);\n'''
new = '''    _speakUserContext = settings.SpeakUserContext;\n    _speech.SetShowRolledBackHistory(settings.ShowRolledBackHistory);\n    _speech.RevalidatePausedNavigationEligibility(\n      "transcript-settings-changed");\n    _transcriptView.ApplySettings(settings, dark);\n'''
if old not in text:
  raise SystemExit("MainForm transcript-settings policy block not found")
text = text.replace(old, new, 1)
old = '''  private void TranscriptFindSeekRequested(\n    object? sender,\n    FindSeekRequestedEventArgs eventArgs)\n  {\n    if (_speech.TrySeekToTranscriptWord(\n'''
new = '''  private void TranscriptFindSeekRequested(\n    object? sender,\n    FindSeekRequestedEventArgs eventArgs)\n  {\n    DiagnosticLog.Write("transcript.seek_requested", new\n    {\n      eventArgs.Source,\n      eventArgs.NodeId,\n      eventArgs.NodeWordIndex\n    });\n    if (_speech.TrySeekToTranscriptWord(\n'''
if old not in text:
  raise SystemExit("MainForm seek diagnostic insertion point not found")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
