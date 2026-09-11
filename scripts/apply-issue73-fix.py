from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
  file = Path(path)
  text = file.read_text(encoding='utf-8')
  if new in text:
    return
  if old not in text:
    raise SystemExit(f'Patch anchor not found in {path}: {old[:120]!r}')
  file.write_text(text.replace(old, new, 1), encoding='utf-8')


# SpeechService: expose authoritative current eligibility and keep node-global
# lexical coordinates stable across disabled fragments.
speech = 'AgentPanelSpeaker/SpeechService.cs'
seek_anchor = '''  /// <summary>\n  /// Moves the paused playback marker to one lexical word within a JSONL node.\n  /// </summary>\n  public bool TrySeekToTranscriptWord(\n'''
seek_insert = '''  /// <summary>\n  /// Returns node-global lexical-word ranges that are currently eligible for\n  /// speech navigation under the same policy used by playback.\n  /// </summary>\n  public IReadOnlyList<TranscriptVoiceWordRange> GetSeekableTranscriptWordRanges()\n  {\n    lock (_sync)\n    {\n      ThrowIfDisposed();\n      var result = new List<TranscriptVoiceWordRange>();\n      var nextWordIndexByNode = new Dictionary<long, int>();\n      foreach (SpeechFragment fragment in _history)\n      {\n        int start = nextWordIndexByNode.GetValueOrDefault(fragment.NodeId);\n        int wordCount = SpeechTokenization.Matches(fragment.Text).Count;\n        nextWordIndexByNode[fragment.NodeId] = checked(start + wordCount);\n        if (wordCount == 0 ||\n            !TryGetEligibleProfileLocked(fragment, out _, out _))\n        {\n          continue;\n        }\n\n        if (result.Count != 0 &&\n            result[^1].NodeId == fragment.NodeId &&\n            result[^1].StartNodeWordIndex + result[^1].WordCount == start)\n        {\n          TranscriptVoiceWordRange previous = result[^1];\n          result[^1] = previous with\n          {\n            WordCount = checked(previous.WordCount + wordCount)\n          };\n        }\n        else\n        {\n          result.Add(new TranscriptVoiceWordRange(\n            fragment.NodeId,\n            start,\n            wordCount));\n        }\n      }\n      return result;\n    }\n  }\n\n  /// <summary>\n  /// Moves the paused playback marker to one lexical word within a JSONL node.\n  /// </summary>\n  public bool TrySeekToTranscriptWord(\n'''
replace_once(speech, seek_anchor, seek_insert)

old_loop = '''      int remaining = nodeWordIndex;\n      for (int index = 0; index < _history.Count; ++index)\n      {\n        SpeechFragment fragment = _history[index];\n        if (fragment.NodeId != nodeId ||\n            !TryGetEligibleProfileLocked(\n              fragment,\n              out _,\n              out _))\n        {\n          continue;\n        }\n\n        MatchCollection matches = SpeechTokenization.Matches(fragment.Text);\n        if (remaining >= matches.Count)\n        {\n          remaining -= matches.Count;\n          continue;\n        }\n\n        bool hadActiveSpeech = _activeKind != ActiveSpeechKind.None;\n        _pendingUntracked = null;\n        ClearProcessingTimeAnnouncementLocked();\n        _pendingHistoryIndex = index;\n        _pendingHistoryWordIndex = remaining;\n        _nextHistoryIndex = index;\n        _lastFenceActivity = null;\n        SetPausedLocked(true);\n        SetPausedNavigationPositionLocked(index, remaining);\n'''
new_loop = '''      int nodeWordStart = 0;\n      for (int index = 0; index < _history.Count; ++index)\n      {\n        SpeechFragment fragment = _history[index];\n        if (fragment.NodeId != nodeId)\n        {\n          continue;\n        }\n\n        MatchCollection matches = SpeechTokenization.Matches(fragment.Text);\n        int nodeWordEnd = checked(nodeWordStart + matches.Count);\n        if (nodeWordIndex >= nodeWordEnd)\n        {\n          nodeWordStart = nodeWordEnd;\n          continue;\n        }\n        if (!TryGetEligibleProfileLocked(fragment, out _, out _))\n        {\n          text = string.Empty;\n          return false;\n        }\n\n        int fragmentWordIndex = nodeWordIndex - nodeWordStart;\n        bool hadActiveSpeech = _activeKind != ActiveSpeechKind.None;\n        _pendingUntracked = null;\n        ClearProcessingTimeAnnouncementLocked();\n        _pendingHistoryIndex = index;\n        _pendingHistoryWordIndex = fragmentWordIndex;\n        _nextHistoryIndex = index;\n        _lastFenceActivity = null;\n        SetPausedLocked(true);\n        SetPausedNavigationPositionLocked(index, fragmentWordIndex);\n'''
replace_once(speech, old_loop, new_loop)

# TranscriptView: retain the authoritative ranges, classify words during normal
# mapping/eligibility updates, and make Ctrl itself a constant-time body-class
# toggle. No Ctrl key transition walks the word list.
view = 'AgentPanelSpeaker/TranscriptView.cs'
replace_once(
  view,
  '''  private IReadOnlyList<TranscriptNodeIdentity> _identities =\n    Array.Empty<TranscriptNodeIdentity>();\n''',
  '''  private IReadOnlyList<TranscriptNodeIdentity> _identities =\n    Array.Empty<TranscriptNodeIdentity>();\n  private IReadOnlyList<TranscriptVoiceWordRange> _seekableVoiceRanges =\n    Array.Empty<TranscriptVoiceWordRange>();\n''')

open_find_anchor = '''  /// <summary>\n  /// Opens and focuses the transcript find popup.\n  /// </summary>\n  public void OpenFind()\n'''
open_find_insert = '''  /// <summary>\n  /// Replaces the speech-authoritative set of currently seekable transcript\n  /// word ranges. The browser keeps these as exclusion classes; Ctrl itself\n  /// only toggles the page-level discovery class.\n  /// </summary>\n  public void SetSeekableVoiceRanges(\n    IReadOnlyList<TranscriptVoiceWordRange> ranges)\n  {\n    ArgumentNullException.ThrowIfNull(ranges);\n    _seekableVoiceRanges = ranges.ToArray();\n    PostSeekableVoiceRanges();\n  }\n\n  private void PostSeekableVoiceRanges()\n  {\n    PostMessage(new\n    {\n      type = "voice-selectability",\n      ranges = _seekableVoiceRanges\n    });\n  }\n\n  /// <summary>\n  /// Opens and focuses the transcript find popup.\n  /// </summary>\n  public void OpenFind()\n'''
replace_once(view, open_find_anchor, open_find_insert)

replace_once(
  view,
  '''    ApplySettings(_settings, _dark);\n    QueueSettingsApply(immediate: true);\n''',
  '''    ApplySettings(_settings, _dark);\n    PostSeekableVoiceRanges();\n    QueueSettingsApply(immediate: true);\n''')

replace_once(
  view,
  '''.word { border-radius: 2px; }\n.word.active { background: var(--highlight); }\n''',
  '''.word { border-radius: 2px; }\nbody.voice-pointer-select-mode\n  .word.voice-selectable:not(.voice-excluded) {\n  outline: 1px solid var(--link);\n  outline-offset: 1px;\n  border-radius: 0;\n  cursor: pointer;\n}\n.word.active { background: var(--highlight); }\n''')

replace_once(
  view,
  '''let findCurrentWords = [];\nlet findInputTimer = 0;\n''',
  '''let findCurrentWords = [];\nlet findInputTimer = 0;\nlet seekableVoiceRanges = [];\n''')

assign_anchor = '''function assignNodeScopes(nodeMap) {\n'''
helper = '''function normalizedVoiceRanges(ranges) {\n  const byNode = new Map();\n  for (const item of ranges || []) {\n    const nodeId = String(item.NodeId ?? item.nodeId ?? '');\n    const start = Number(\n      item.StartNodeWordIndex ?? item.startNodeWordIndex ?? -1);\n    const count = Number(item.WordCount ?? item.wordCount ?? 0);\n    if (!nodeId || start < 0 || count <= 0) continue;\n    let nodeRanges = byNode.get(nodeId);\n    if (!nodeRanges) {\n      nodeRanges = [];\n      byNode.set(nodeId, nodeRanges);\n    }\n    nodeRanges.push({start, end:start + count});\n  }\n  return byNode;\n}\n\nfunction applyVoiceExclusions() {\n  const byNode = normalizedVoiceRanges(seekableVoiceRanges);\n  for (const word of words) {\n    if (!word.classList.contains('voice-selectable')) continue;\n    const nodeRanges = byNode.get(word.dataset.nodeId || '') || [];\n    const wordIndex = Number(word.dataset.nodeWordIndex ?? -1);\n    const eligible = nodeRanges.some(range =>\n      wordIndex >= range.start && wordIndex < range.end);\n    word.classList.toggle('voice-excluded', !eligible);\n  }\n}\n\nfunction setSeekableVoiceRanges(ranges) {\n  seekableVoiceRanges = Array.isArray(ranges) ? ranges : [];\n  applyVoiceExclusions();\n}\n\nfunction assignVoiceWordClasses() {\n  const nextByNode = new Map();\n  for (const word of words) {\n    word.classList.remove('voice-selectable', 'voice-excluded');\n    delete word.dataset.nodeWordIndex;\n    if (word.dataset.lexical !== '1') continue;\n    const nodeId = word.dataset.nodeId || '';\n    if (!nodeId) continue;\n    const nodeWordIndex = nextByNode.get(nodeId) || 0;\n    nextByNode.set(nodeId, nodeWordIndex + 1);\n    word.dataset.nodeWordIndex = String(nodeWordIndex);\n    word.classList.add('voice-selectable');\n  }\n  applyVoiceExclusions();\n}\n\nfunction assignNodeScopes(nodeMap) {\n'''
replace_once(view, assign_anchor, helper)

replace_once(
  view,
  '''    }\n  }\n}\n\nfunction chooseNearestRange(matches, nodeId) {\n''',
  '''    }\n  }\n  assignVoiceWordClasses();\n}\n\nfunction chooseNearestRange(matches, nodeId) {\n''')

message_anchor = '''  if (data.type === 'find-waiting') {\n'''
message_insert = '''  if (data.type === 'voice-selectability') {\n    setSeekableVoiceRanges(data.ranges);\n    return;\n  }\n  if (data.type === 'find-waiting') {\n'''
replace_once(view, message_anchor, message_insert)

# Insert the Ctrl affordance before the scroll-intent keyboard listener. The
# only key-transition mutation is the body class.
ctrl_anchor = '''window.addEventListener('keydown', event => {\n  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey ||\n      !VW_SCROLL_KEYS.has(event.key) || isEditableScrollTarget(event.target)) {\n'''
ctrl_insert = '''function setVoicePointerSelectionMode(enabled) {\n  document.body.classList.toggle('voice-pointer-select-mode', !!enabled);\n}\n\nwindow.addEventListener('keydown', event => {\n  if (event.key === 'Control') {\n    setVoicePointerSelectionMode(true);\n  }\n}, {capture:true});\nwindow.addEventListener('keyup', event => {\n  if (event.key === 'Control') {\n    setVoicePointerSelectionMode(false);\n  }\n}, {capture:true});\nwindow.addEventListener('blur', () => {\n  setVoicePointerSelectionMode(false);\n});\ntranscript.addEventListener('click', event => {\n  if (!event.ctrlKey || event.button !== 0 ||\n      !(event.target instanceof Element)) {\n    return;\n  }\n  const word = event.target.closest(\n    '.word.voice-selectable:not(.voice-excluded)');\n  if (!word || !transcript.contains(word)) return;\n  const nodeId = Number(word.dataset.nodeId || 0);\n  const nodeWordIndex = Number(word.dataset.nodeWordIndex ?? -1);\n  if (nodeId <= 0 || nodeWordIndex < 0) return;\n  event.preventDefault();\n  event.stopPropagation();\n  chrome.webview.postMessage({\n    type:'find-seek',\n    source:'ctrl-click',\n    nodeId,\n    nodeWordIndex\n  });\n}, {capture:true});\n\nwindow.addEventListener('keydown', event => {\n  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey ||\n      !VW_SCROLL_KEYS.has(event.key) || isEditableScrollTarget(event.target)) {\n'''
replace_once(view, ctrl_anchor, ctrl_insert)

# MainForm: push current authoritative eligibility after every history or policy
# change that can alter what is voiceable now.
main = 'AgentPanelSpeaker/MainForm.cs'
replace_once(
  main,
  '''    _masterSpeechProfile.ProfileChanged += (_, _) =>\n      SaveControlsToSettings();\n''',
  '''    _masterSpeechProfile.ProfileChanged += (_, _) =>\n    {\n      SaveControlsToSettings();\n      UpdateTranscriptVoiceSelectability();\n    };\n''')

replace_once(
  main,
  '''    SaveControlsToSettings();\n    AppendLog(\n      "Spoken fenced-code types updated: " +\n''',
  '''    SaveControlsToSettings();\n    UpdateTranscriptVoiceSelectability();\n    AppendLog(\n      "Spoken fenced-code types updated: " +\n''')

replace_once(
  main,
  '''    SaveControlsToSettings();\n    ScheduleVoiceSettingsPreview(role, context);\n''',
  '''    SaveControlsToSettings();\n    UpdateTranscriptVoiceSelectability();\n    ScheduleVoiceSettingsPreview(role, context);\n''')

replace_once(
  main,
  '''    _transcriptView.ApplySettings(settings, dark);\n    _playbackMailbox.SetCapacity(settings.HighlightQueueCapacity);\n''',
  '''    _transcriptView.ApplySettings(settings, dark);\n    UpdateTranscriptVoiceSelectability();\n    _playbackMailbox.SetCapacity(settings.HighlightQueueCapacity);\n''')

replace_once(
  main,
  '''      _speech.LoadHistory(\n        snapshot.Fragments,\n        snapshot.Completions,\n        snapshot.BackgroundWorkEvents,\n        snapshot.StartMode);\n\n      if (_pendingMonitorSeekNodeId > 0 &&\n''',
  '''      _speech.LoadHistory(\n        snapshot.Fragments,\n        snapshot.Completions,\n        snapshot.BackgroundWorkEvents,\n        snapshot.StartMode);\n      UpdateTranscriptVoiceSelectability();\n\n      if (_pendingMonitorSeekNodeId > 0 &&\n''')

replace_once(
  main,
  '''      _speech.SpeakLive(fragment);\n      AppendLog($"Queued {fragment.Category}: {fragment.Text}");\n''',
  '''      _speech.SpeakLive(fragment);\n      UpdateTranscriptVoiceSelectability();\n      AppendLog($"Queued {fragment.Category}: {fragment.Text}");\n''')

# Every explicit history reset must also clear selectable ranges immediately.
replace_once(
  main,
  '''        _speech.BeginLiveSession();\n      }\n      Interlocked.Increment(ref _monitorSession);\n''',
  '''        _speech.BeginLiveSession();\n        UpdateTranscriptVoiceSelectability();\n      }\n      Interlocked.Increment(ref _monitorSession);\n''')

replace_once(
  main,
  '''        _speech.BeginLiveSession();\n      }\n      SetSessionDisplay(session);\n''',
  '''        _speech.BeginLiveSession();\n        UpdateTranscriptVoiceSelectability();\n      }\n      SetSessionDisplay(session);\n''')

handler_anchor = '''  /// <summary>\n  /// Moves the paused speech marker to the voiced word selected by Find.\n  /// </summary>\n  private void TranscriptFindSeekRequested(\n'''
handler_insert = '''  /// <summary>\n  /// Refreshes the transcript's permanent selectable/excluded word classes\n  /// from SpeechService's current playback eligibility.\n  /// </summary>\n  private void UpdateTranscriptVoiceSelectability()\n  {\n    _transcriptView.SetSeekableVoiceRanges(\n      _speech.GetSeekableTranscriptWordRanges());\n  }\n\n  /// <summary>\n  /// Moves the paused speech marker to the voiced word selected by Find or by\n  /// direct Ctrl+click transcript navigation.\n  /// </summary>\n  private void TranscriptFindSeekRequested(\n'''
replace_once(main, handler_anchor, handler_insert)

replace_once(
  main,
  '''      AppendLog($"Find moved speech marker: {text}");\n''',
  '''      AppendLog($"Moved speech marker: {text}");\n''')
replace_once(
  main,
  '''      AppendLog("Find match is not in voiced speech history.");\n''',
  '''      AppendLog("Selected transcript word is not currently available to be voiced.");\n''')
