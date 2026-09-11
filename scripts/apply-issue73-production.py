from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise SystemExit(f"{path}: expected exactly one match, found {count}")
  path.write_text(text.replace(old, new, 1), encoding="utf-8")


speech = ROOT / "AgentPanelSpeaker" / "SpeechService.cs"
replace_once(
  speech,
  "namespace AgentPanelSpeaker;\n\n/// <summary>\n/// Serializes speech, retains navigation history, and resolves playback policy",
  "namespace AgentPanelSpeaker;\n\n/// <summary>\n/// Identifies one contiguous node-global word range that is currently eligible\n/// for speech and direct transcript seeking.\n/// </summary>\ninternal sealed record SeekableTranscriptWordRange(\n  long NodeId,\n  int StartNodeWordIndex,\n  int WordCount);\n\n/// <summary>\n/// Serializes speech, retains navigation history, and resolves playback policy")

old_seek = '''  /// <summary>
  /// Moves the paused playback marker to one lexical word within a JSONL node.
  /// </summary>
  public bool TrySeekToTranscriptWord(
    long nodeId,
    int nodeWordIndex,
    out string text)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      if (nodeWordIndex < 0)
      {
        text = string.Empty;
        return false;
      }

      int remaining = nodeWordIndex;
      for (int index = 0; index < _history.Count; ++index)
      {
        SpeechFragment fragment = _history[index];
        if (fragment.NodeId != nodeId ||
            !TryGetEligibleProfileLocked(
              fragment,
              out _,
              out _))
        {
          continue;
        }

        MatchCollection matches = SpeechTokenization.Matches(fragment.Text);
        if (remaining >= matches.Count)
        {
          remaining -= matches.Count;
          continue;
        }

        bool hadActiveSpeech = _activeKind != ActiveSpeechKind.None;
        _pendingUntracked = null;
        ClearProcessingTimeAnnouncementLocked();
        _pendingHistoryIndex = index;
        _pendingHistoryWordIndex = remaining;
        _nextHistoryIndex = index;
        _lastFenceActivity = null;
        SetPausedLocked(true);
        SetPausedNavigationPositionLocked(index, remaining);
        if (hadActiveSpeech)
        {
          RequestPauseRestoreAfterCancellationLocked(
            "seek-transcript-word");
          _engine.Cancel();
        }
        text = fragment.Text;
        return true;
      }

      text = string.Empty;
      return false;
    }
  }
'''
new_seek = '''  /// <summary>
  /// Returns the node-global word ranges that are eligible for speech under
  /// the current profile, fence, and rolled-back-history policies.
  /// </summary>
  public IReadOnlyList<SeekableTranscriptWordRange>
    GetSeekableTranscriptWordRanges()
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      var ranges = new List<SeekableTranscriptWordRange>();
      var nodeWordOffsets = new Dictionary<long, int>();
      foreach (SpeechFragment fragment in _history)
      {
        int wordCount = SpeechTokenization.Matches(fragment.Text).Count;
        int start = nodeWordOffsets.TryGetValue(fragment.NodeId, out int offset)
          ? offset
          : 0;
        nodeWordOffsets[fragment.NodeId] = checked(start + wordCount);
        if (wordCount == 0 ||
            !TryGetEligibleProfileLocked(fragment, out _, out _))
        {
          continue;
        }

        if (ranges.Count != 0)
        {
          SeekableTranscriptWordRange previous = ranges[^1];
          if (previous.NodeId == fragment.NodeId &&
              previous.StartNodeWordIndex + previous.WordCount == start)
          {
            ranges[^1] = previous with
            {
              WordCount = checked(previous.WordCount + wordCount)
            };
            continue;
          }
        }
        ranges.Add(new SeekableTranscriptWordRange(
          fragment.NodeId,
          start,
          wordCount));
      }
      return ranges.ToArray();
    }
  }

  /// <summary>
  /// Moves the paused playback marker to one node-global lexical word when the
  /// fragment containing that word is currently eligible for speech.
  /// </summary>
  public bool TrySeekToTranscriptWord(
    long nodeId,
    int nodeWordIndex,
    out string text)
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      if (nodeWordIndex < 0)
      {
        text = string.Empty;
        return false;
      }

      int remaining = nodeWordIndex;
      for (int index = 0; index < _history.Count; ++index)
      {
        SpeechFragment fragment = _history[index];
        if (fragment.NodeId != nodeId)
        {
          continue;
        }

        MatchCollection matches = SpeechTokenization.Matches(fragment.Text);
        if (remaining >= matches.Count)
        {
          remaining -= matches.Count;
          continue;
        }
        if (!TryGetEligibleProfileLocked(fragment, out _, out _))
        {
          text = string.Empty;
          return false;
        }

        bool hadActiveSpeech = _activeKind != ActiveSpeechKind.None;
        _pendingUntracked = null;
        ClearProcessingTimeAnnouncementLocked();
        _pendingHistoryIndex = index;
        _pendingHistoryWordIndex = remaining;
        _nextHistoryIndex = index;
        _lastFenceActivity = null;
        SetPausedLocked(true);
        SetPausedNavigationPositionLocked(index, remaining);
        if (hadActiveSpeech)
        {
          RequestPauseRestoreAfterCancellationLocked(
            "seek-transcript-word");
          _engine.Cancel();
        }
        text = fragment.Text;
        return true;
      }

      text = string.Empty;
      return false;
    }
  }
'''
replace_once(speech, old_seek, new_seek)

view = ROOT / "AgentPanelSpeaker" / "TranscriptView.cs"
replace_once(
  view,
  "  private readonly SemaphoreSlim _windowRenderGate = new(1, 1);\n",
  "  private readonly SemaphoreSlim _windowRenderGate = new(1, 1);\n  private IReadOnlyList<SeekableTranscriptWordRange> _seekableVoiceRanges =\n    Array.Empty<SeekableTranscriptWordRange>();\n")

open_find = '''  public void OpenFind()
  {
    if (!_initialized)
    {
      return;
    }

    _webView.Focus();
    BeginInvoke(new Action(() =>
    {
      if (!_webView.IsDisposed)
      {
        _webView.Focus();
        _ = ExecuteAsync("openFind();");
      }
    }));
  }
'''
open_find_new = open_find + '''
  /// <summary>
  /// Publishes the authoritative currently seekable node-global speech ranges
  /// used by the Ctrl+click transcript affordance.
  /// </summary>
  public void SetSeekableVoiceRanges(
    IReadOnlyList<SeekableTranscriptWordRange> ranges)
  {
    ArgumentNullException.ThrowIfNull(ranges);
    _seekableVoiceRanges = ranges.ToArray();
    PostSeekableVoiceRanges();
  }

  private void PostSeekableVoiceRanges()
  {
    PostMessage(new
    {
      type = "seekable-voice-ranges",
      ranges = _seekableVoiceRanges
    });
  }
'''
replace_once(view, open_find, open_find_new)

replace_once(
  view,
  "    ApplySettings(_settings, _dark);\n    QueueSettingsApply(immediate: true);\n",
  "    ApplySettings(_settings, _dark);\n    QueueSettingsApply(immediate: true);\n    PostSeekableVoiceRanges();\n")

replace_once(
  view,
  ".word.paused {\n  outline: 2px solid var(--highlight);\n  outline-offset: 1px;\n  animation: marker-blink 1s steps(1, end) infinite;\n}\n",
  ".word.paused {\n  outline: 2px solid var(--highlight);\n  outline-offset: 1px;\n  animation: marker-blink 1s steps(1, end) infinite;\n}\nbody.voice-pointer-select-mode\n  .word.voice-selectable:not(.voice-excluded) {\n  outline: 1px solid var(--link);\n  outline-offset: 1px;\n  cursor: pointer;\n}\n")

replace_once(
  view,
  "let findCurrentWords = [];\nlet findInputTimer = 0;\n",
  "let findCurrentWords = [];\nlet findInputTimer = 0;\nlet seekableVoiceRanges = [];\n")

assign_prefix = '''function assignNodeScopes(nodeMap) {
  ++mappingGeneration;
  knownNodeIds = new Set();
  segmentRangesByNode = new Map();
  const displayCursors = new Map();
  const lexicalCursors = new Map();
  for (const item of nodeMap || []) {
    const nodeId = String(item.NodeId ?? item.nodeId ?? '');
    knownNodeIds.add(nodeId);
    const recordNumber = String(
      item.RecordNumber ?? item.recordNumber ?? '');
    const segments = item.Segments ?? item.segments ?? [];
    const key = makeRecordKey(recordNumber);
    const recordWords = displayWordsByRecord.get(key) || [];
    const recordLexicalWords = lexicalWordsByRecord.get(key) || [];
    let displayCursor = displayCursors.get(key) || 0;
    let lexicalCursor = lexicalCursors.get(key) || 0;
    for (const segment of segments) {
      const displayTarget = tokenizeDisplay(segment);
      const lexicalTarget = tokenize(segment);
      if (!displayTarget.length && !lexicalTarget.length) continue;
'''
assign_prefix_new = '''function markVoiceSelectableWords(
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

function isSeekableVoiceWord(word) {
  const nodeId = Number(word.dataset.nodeId || 0);
  const nodeWordIndex = Number(word.dataset.nodeWordIndex ?? -1);
  if (nodeId <= 0 || nodeWordIndex < 0) return false;
  return seekableVoiceRanges.some(range =>
    range.nodeId === nodeId &&
    nodeWordIndex >= range.startNodeWordIndex &&
    nodeWordIndex < range.startNodeWordIndex + range.wordCount);
}

function applyVoiceEligibilityClasses() {
  for (const word of transcript.querySelectorAll('.word.voice-selectable')) {
    word.classList.toggle('voice-excluded', !isSeekableVoiceWord(word));
  }
}

function setSeekableVoiceRanges(ranges) {
  seekableVoiceRanges = (ranges || []).map(range => ({
    nodeId:Number(range.NodeId ?? range.nodeId ?? 0),
    startNodeWordIndex:Number(
      range.StartNodeWordIndex ?? range.startNodeWordIndex ?? -1),
    wordCount:Number(range.WordCount ?? range.wordCount ?? 0)
  })).filter(range =>
    range.nodeId > 0 && range.startNodeWordIndex >= 0 && range.wordCount > 0);
  applyVoiceEligibilityClasses();
}

function assignNodeScopes(nodeMap) {
  ++mappingGeneration;
  knownNodeIds = new Set();
  segmentRangesByNode = new Map();
  const displayCursors = new Map();
  const lexicalCursors = new Map();
  const nodeWordCursors = new Map();
  for (const item of nodeMap || []) {
    const nodeId = String(item.NodeId ?? item.nodeId ?? '');
    knownNodeIds.add(nodeId);
    const recordNumber = String(
      item.RecordNumber ?? item.recordNumber ?? '');
    const segments = item.Segments ?? item.segments ?? [];
    const key = makeRecordKey(recordNumber);
    const recordWords = displayWordsByRecord.get(key) || [];
    const recordLexicalWords = lexicalWordsByRecord.get(key) || [];
    let displayCursor = displayCursors.get(key) || 0;
    let lexicalCursor = lexicalCursors.get(key) || 0;
    let nodeWordIndex = nodeWordCursors.get(nodeId) || 0;
    for (const segment of segments) {
      const displayTarget = tokenizeDisplay(segment);
      const lexicalTarget = tokenize(segment);
      const segmentNodeWordStart = nodeWordIndex;
      nodeWordIndex += lexicalTarget.length;
      nodeWordCursors.set(nodeId, nodeWordIndex);
      if (!displayTarget.length && !lexicalTarget.length) continue;
'''
replace_once(view, assign_prefix, assign_prefix_new)

replace_once(
  view,
  "        const globalStart = Number(recordWords[start].dataset.index);\n        const globalEnd = Number(recordWords[end].dataset.index);\n        rememberSegmentRange(\n",
  "        const globalStart = Number(recordWords[start].dataset.index);\n        const globalEnd = Number(recordWords[end].dataset.index);\n        markVoiceSelectableWordsByGlobalRange(\n          recordLexicalWords,\n          globalStart,\n          globalEnd,\n          nodeId,\n          segmentNodeWordStart);\n        rememberSegmentRange(\n")

replace_once(
  view,
  "        const tokenEnd = Number(\n          recordLexicalWords[lexicalEnd].dataset.index);\n        markNodeRange(tokenStart, tokenEnd, nodeId);\n",
  "        const tokenEnd = Number(\n          recordLexicalWords[lexicalEnd].dataset.index);\n        markNodeRange(tokenStart, tokenEnd, nodeId);\n        markVoiceSelectableWords(\n          recordLexicalWords,\n          lexicalStart,\n          lexicalEnd,\n          nodeId,\n          segmentNodeWordStart);\n")

replace_once(
  view,
  "    }\n  }\n}\n\nfunction chooseNearestRange(matches, nodeId) {\n",
  "    }\n  }\n  applyVoiceEligibilityClasses();\n}\n\nfunction chooseNearestRange(matches, nodeId) {\n")

replace_once(
  view,
  "  if (data.type === 'settings') {\n",
  "  if (data.type === 'seekable-voice-ranges') {\n    setSeekableVoiceRanges(data.ranges ?? data.Ranges ?? []);\n    return;\n  }\n  if (data.type === 'settings') {\n")

final_keydown = '''window.addEventListener('keydown', event => {
  if (!event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey &&
'''
ctrl_handlers = '''function setVoicePointerSelectMode(enabled) {
  document.body.classList.toggle('voice-pointer-select-mode', enabled);
}

window.addEventListener('keydown', event => {
  if (event.key === 'Control') setVoicePointerSelectMode(true);
}, true);
window.addEventListener('keyup', event => {
  if (event.key === 'Control') setVoicePointerSelectMode(false);
}, true);
window.addEventListener('blur', () => setVoicePointerSelectMode(false));

transcript.addEventListener('click', event => {
  if (!event.ctrlKey || event.button !== 0 || !(event.target instanceof Element)) {
    return;
  }
  const word = event.target.closest(
    '.word.voice-selectable:not(.voice-excluded)');
  if (!word || !transcript.contains(word)) return;
  const nodeId = Number(word.dataset.nodeId || 0);
  const nodeWordIndex = Number(word.dataset.nodeWordIndex ?? -1);
  if (nodeId <= 0 || nodeWordIndex < 0) return;
  event.preventDefault();
  event.stopPropagation();
  chrome.webview.postMessage({
    type:'find-seek',
    source:'ctrl-click',
    nodeId,
    nodeWordIndex
  });
}, true);

window.addEventListener('keydown', event => {
  if (!event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey &&
'''
replace_once(view, final_keydown, ctrl_handlers)

args = ROOT / "AgentPanelSpeaker" / "FindSeekRequestedEventArgs.cs"
replace_once(
  args,
  "  public FindSeekRequestedEventArgs(long nodeId, int nodeWordIndex)\n  {\n    NodeId = nodeId;\n    NodeWordIndex = nodeWordIndex;\n  }\n\n  public long NodeId { get; }\n  public int NodeWordIndex { get; }\n",
  "  public FindSeekRequestedEventArgs(\n    long nodeId,\n    int nodeWordIndex,\n    string source = \"find\")\n  {\n    NodeId = nodeId;\n    NodeWordIndex = nodeWordIndex;\n    Source = source;\n  }\n\n  public long NodeId { get; }\n  public int NodeWordIndex { get; }\n  public string Source { get; }\n")

replace_once(
  view,
  "          FindSeekRequested?.Invoke(\n            this,\n            new FindSeekRequestedEventArgs(validNodeId, validNodeWordIndex));\n",
  "          FindSeekRequested?.Invoke(\n            this,\n            new FindSeekRequestedEventArgs(\n              validNodeId,\n              validNodeWordIndex,\n              ReadOptionalString(root, \"source\")));\n")

main = ROOT / "AgentPanelSpeaker" / "MainForm.cs"
replace_once(
  main,
  "    _masterSpeechProfile.ProfileChanged += (_, _) =>\n      SaveControlsToSettings();\n",
  "    _masterSpeechProfile.ProfileChanged += (_, _) =>\n    {\n      SaveControlsToSettings();\n      RefreshTranscriptVoiceSelectability();\n    };\n")
replace_once(
  main,
  "    SaveControlsToSettings();\n    AppendLog(\n      \"Spoken fenced-code types updated: \" +\n",
  "    SaveControlsToSettings();\n    RefreshTranscriptVoiceSelectability();\n    AppendLog(\n      \"Spoken fenced-code types updated: \" +\n")
replace_once(
  main,
  "    SaveControlsToSettings();\n    ScheduleVoiceSettingsPreview(role, context);\n",
  "    SaveControlsToSettings();\n    RefreshTranscriptVoiceSelectability();\n    ScheduleVoiceSettingsPreview(role, context);\n")
replace_once(
  main,
  "    _speech.SetShowRolledBackHistory(settings.ShowRolledBackHistory);\n    _transcriptView.ApplySettings(settings, dark);\n",
  "    _speech.SetShowRolledBackHistory(settings.ShowRolledBackHistory);\n    _transcriptView.ApplySettings(settings, dark);\n    RefreshTranscriptVoiceSelectability();\n")
replace_once(
  main,
  "      _speech.LoadHistory(\n        snapshot.Fragments,\n        snapshot.Completions,\n        snapshot.BackgroundWorkEvents,\n        snapshot.StartMode);\n",
  "      _speech.LoadHistory(\n        snapshot.Fragments,\n        snapshot.Completions,\n        snapshot.BackgroundWorkEvents,\n        snapshot.StartMode);\n      RefreshTranscriptVoiceSelectability();\n")
replace_once(
  main,
  "      _speech.SpeakLive(fragment);\n      AppendLog($\"Queued {fragment.Category}: {fragment.Text}\");\n",
  "      _speech.SpeakLive(fragment);\n      RefreshTranscriptVoiceSelectability();\n      AppendLog($\"Queued {fragment.Category}: {fragment.Text}\");\n")

# Both live-session reset paths should clear stale browser eligibility immediately.
text = main.read_text(encoding="utf-8")
needle = "        _speech.BeginLiveSession();\n"
if text.count(needle) != 2:
  raise SystemExit(f"{main}: expected two BeginLiveSession() reset sites, found {text.count(needle)}")
text = text.replace(
  needle,
  "        _speech.BeginLiveSession();\n        RefreshTranscriptVoiceSelectability();\n")
main.write_text(text, encoding="utf-8")

seek_handler = '''  /// <summary>
  /// Moves the paused speech marker to the voiced word selected by Find.
  /// </summary>
  private void TranscriptFindSeekRequested(
'''
seek_handler_new = '''  /// <summary>
  /// Publishes current speech eligibility to the transcript. Ctrl itself only
  /// toggles one page-level CSS class; individual word classes change only
  /// when mapping or speech eligibility changes.
  /// </summary>
  private void RefreshTranscriptVoiceSelectability()
  {
    _transcriptView.SetSeekableVoiceRanges(
      _speech.GetSeekableTranscriptWordRanges());
  }

  /// <summary>
  /// Moves the paused speech marker to a voiced word selected by Find or by
  /// direct Ctrl+click transcript navigation.
  /// </summary>
  private void TranscriptFindSeekRequested(
'''
replace_once(main, seek_handler, seek_handler_new)
replace_once(
  main,
  "      AppendLog($\"Find moved speech marker: {text}\");\n",
  "      AppendLog(eventArgs.Source == \"ctrl-click\"\n        ? $\"Ctrl+click moved speech marker: {text}\"\n        : $\"Find moved speech marker: {text}\");\n")

print("Applied issue #73 production implementation.")
