from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{path}: expected one exact match, found {count}: {old[:120]!r}")
  path.write_text(text.replace(old, new), encoding="utf-8")


def insert_once(path: Path, anchor: str, insertion: str) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(anchor)
  if count != 1:
    raise RuntimeError(f"{path}: expected one insertion anchor, found {count}: {anchor[:120]!r}")
  path.write_text(text.replace(anchor, insertion + anchor), encoding="utf-8")


main = ROOT / "AgentPanelSpeaker" / "MainForm.cs"
replace_once(
  main,
  "  private const int WmSetRedraw = 0x000B;\n",
  "  private const int WmSetRedraw = 0x000B;\n  private const int WmActivateApp = 0x001C;\n")

insert_once(
  main,
  "  /// <summary>\n  /// Handles transport hotkeys before focused child windows consume them.\n  /// </summary>\n  public bool PreFilterMessage(ref Message message)\n",
  '''  /// <summary>\n  /// Resolves whether the Ctrl voice-pointer affordance remains active while\n  /// focus moves between top-level windows owned by this process.\n  /// </summary>\n  internal static bool ResolveVoicePointerSelectModeForActivation(\n    bool controlHeld,\n    bool foregroundIsCurrentProcess) =>\n    controlHeld && foregroundIsCurrentProcess;\n\n  /// <summary>\n  /// Returns whether one top-level message source belongs to the MainForm or\n  /// another top-level window owned by this process.\n  /// </summary>\n  internal static bool ShouldRouteVoicePointerMessageSource(\n    bool isMainFormRoot,\n    bool rootIsCurrentProcess) =>\n    isMainFormRoot || rootIsCurrentProcess;\n\n  private static bool IsWindowFromCurrentProcess(IntPtr window)\n  {\n    if (window == IntPtr.Zero)\n    {\n      return false;\n    }\n    _ = GetWindowThreadProcessIdForTabDiagnostics(window, out uint processId);\n    return processId == (uint)Environment.ProcessId;\n  }\n\n''')

replace_once(
  main,
  '''    Activated += (_, _) =>\n      _transcriptView.SetVoicePointerSelectMode(\n        (Control.ModifierKeys & Keys.Control) != 0);\n    Deactivate += (_, _) =>\n    {\n      _transcriptView.SetVoicePointerSelectMode(false);\n      PopupFormBase.WriteActivationDiagnostics(\n        "mainform-deactivate-event",\n        this);\n      HoverPopupController.HandleOwnerDeactivated(this);\n    };\n''',
  '''    Activated += (_, _) =>\n    {\n      bool foregroundIsCurrentProcess = IsWindowFromCurrentProcess(\n        GetForegroundWindowForTabDiagnostics());\n      _transcriptView.SetVoicePointerSelectMode(\n        ResolveVoicePointerSelectModeForActivation(\n          (Control.ModifierKeys & Keys.Control) != 0,\n          foregroundIsCurrentProcess));\n    };\n    Deactivate += (_, _) =>\n    {\n      bool foregroundIsCurrentProcess = IsWindowFromCurrentProcess(\n        GetForegroundWindowForTabDiagnostics());\n      _transcriptView.SetVoicePointerSelectMode(\n        ResolveVoicePointerSelectModeForActivation(\n          (Control.ModifierKeys & Keys.Control) != 0,\n          foregroundIsCurrentProcess));\n      PopupFormBase.WriteActivationDiagnostics(\n        "mainform-deactivate-event",\n        this);\n      if (!foregroundIsCurrentProcess)\n      {\n        HoverPopupController.HandleOwnerDeactivated(this);\n      }\n    };\n''')

replace_once(
  main,
  '''  public bool PreFilterMessage(ref Message message)\n  {\n    if (message.Msg is WmLButtonDown or WmRButtonDown or\n''',
  '''  public bool PreFilterMessage(ref Message message)\n  {\n    if (message.Msg == WmActivateApp)\n    {\n      bool foregroundIsCurrentProcess = IsWindowFromCurrentProcess(\n        GetForegroundWindowForTabDiagnostics());\n      _transcriptView.SetVoicePointerSelectMode(\n        ResolveVoicePointerSelectModeForActivation(\n          (Control.ModifierKeys & Keys.Control) != 0,\n          foregroundIsCurrentProcess));\n      return false;\n    }\n\n    if (message.Msg is WmLButtonDown or WmRButtonDown or\n''')

replace_once(
  main,
  '''    if (GetAncestor(message.HWnd, GaRoot) != Handle)\n    {\n      return false;\n    }\n\n    if (TryGetVoicePointerSelectModeMessage(\n''',
  '''    IntPtr messageRoot = GetAncestor(message.HWnd, GaRoot);\n    bool isMainFormRoot = messageRoot == Handle;\n    bool rootIsCurrentProcess = IsWindowFromCurrentProcess(messageRoot);\n    if (!ShouldRouteVoicePointerMessageSource(\n          isMainFormRoot,\n          rootIsCurrentProcess))\n    {\n      return false;\n    }\n\n    if (TryGetVoicePointerSelectModeMessage(\n''')

view = ROOT / "AgentPanelSpeaker" / "TranscriptView.cs"
replace_once(
  view,
  "let seekableVoiceRanges = [];\n",
  '''let seekableVoiceRanges = [];\nconst openDisclosureOverrides = new Set();\nconst programmaticDisclosureStates = new WeakMap();\nlet discardDisclosureStateOnNextReplacement = false;\n''')

replace_once(
  view,
  '''function structureDetailsKey(details) {\n  const presentation = details.getAttribute('data-presentation-id');\n  if (presentation) return 'presentation:' + presentation;\n  const marker = details.querySelector('[data-aicore-unit-id]');\n  if (marker) return 'core-unit:' + marker.getAttribute('data-aicore-unit-id');\n  const summary = Array.from(details.children).find(\n    child => child.tagName === 'SUMMARY');\n  const summaryText = summary\n    ? summary.textContent.trim().replace(/\\s+/g, ' ')\n    : '';\n  return 'summary:' + summaryText;\n}\n''',
  '''function structureDetailsKey(details) {\n  const presentation = details.getAttribute('data-presentation-id');\n  if (presentation) return 'presentation:' + presentation;\n  const marker = details.querySelector('[data-aicore-unit-id]');\n  if (marker) return 'core-unit:' + marker.getAttribute('data-aicore-unit-id');\n  const summary = Array.from(details.children).find(\n    child => child.tagName === 'SUMMARY');\n  const summaryText = summary\n    ? summary.textContent.trim().replace(/\\s+/g, ' ')\n    : '';\n  const turn = details.closest('section.transcript-turn');\n  const turnId = turn?.getAttribute('data-presentation-id') || '';\n  const anchor = details.querySelector('.record-anchor') ||\n    turn?.querySelector('.record-anchor');\n  const recordNumber = anchor?.getAttribute('data-jsonl-record') || '';\n  const sourceId = anchor?.getAttribute('data-source-id') || '';\n  return 'fallback:' + turnId + ':' + recordNumber + ':' + sourceId + ':' +\n    summaryText;\n}\n\nfunction resetDisclosureOpenOverrides() {\n  openDisclosureOverrides.clear();\n  discardDisclosureStateOnNextReplacement = true;\n}\n\nfunction setDisclosureOpenProgrammatically(details, open) {\n  const requested = !!open;\n  if (!details || details.open === requested) return;\n  programmaticDisclosureStates.set(details, requested);\n  details.open = requested;\n}\n\ntranscript.addEventListener('toggle', event => {\n  const details = event.target;\n  if (!(details instanceof HTMLDetailsElement)) return;\n  const expected = programmaticDisclosureStates.get(details);\n  if (expected !== undefined && expected === details.open) {\n    programmaticDisclosureStates.delete(details);\n    return;\n  }\n  const key = structureDetailsKey(details);\n  if (!key) return;\n  if (details.open) openDisclosureOverrides.add(key);\n  else openDisclosureOverrides.delete(key);\n}, true);\n''')

replace_once(
  view,
  '''    if (parent.tagName === 'DETAILS' && !parent.open) {\n      parent.open = true;\n''',
  '''    if (parent.tagName === 'DETAILS' && !parent.open) {\n      setDisclosureOpenProgrammatically(parent, true);\n''')

insert_once(
  view,
  "function assignNodeScopes(nodeMap) {\n",
  r'''function lexicalWordsCanJoin(left, right) {
  if (!left || !right) return false;
  try {
    const range = document.createRange();
    range.setStartAfter(left);
    range.setEndBefore(right);
    return !/\s/u.test(range.toString());
  } catch {
    return false;
  }
}

function findSpeechLexicalAlignment(collection, target, startAt) {
  if (!collection.length || !target.length) return null;
  const first = Math.max(0, Number(startAt) || 0);
  for (let candidate = first; candidate < collection.length; ++candidate) {
    let cursor = candidate;
    const groups = [];
    let matched = true;
    for (const targetToken of target) {
      const groupStart = cursor;
      let combined = '';
      let complete = false;
      while (cursor < collection.length) {
        if (cursor > groupStart &&
            !lexicalWordsCanJoin(collection[cursor - 1], collection[cursor])) {
          break;
        }
        const piece = collection[cursor].dataset.normalized || '';
        if (!piece) break;
        combined += piece;
        if (!targetToken.startsWith(combined)) break;
        const groupEnd = cursor;
        ++cursor;
        if (combined === targetToken) {
          groups.push({start:groupStart, end:groupEnd});
          complete = true;
          break;
        }
      }
      if (!complete) {
        matched = false;
        break;
      }
    }
    if (matched) return groups;
  }
  return null;
}

function markAlignedVoiceSelectableWords(
  collection,
  groups,
  nodeId,
  startNodeWordIndex,
  speechTokenOffsets) {
  for (let targetIndex = 0; targetIndex < groups.length; ++targetIndex) {
    const speechTokenOffset = speechTokenOffsets[targetIndex];
    if (speechTokenOffset === undefined) continue;
    const group = groups[targetIndex];
    for (let index = group.start; index <= group.end; ++index) {
      const word = collection[index];
      if (!word) continue;
      word.classList.add('voice-selectable');
      word.dataset.nodeId = String(nodeId);
      word.dataset.nodeWordIndex = String(
        startNodeWordIndex + speechTokenOffset);
    }
  }
}

''')

replace_once(
  view,
  '''        displayCursors.set(key, displayCursor);\n        continue;\n      }\n\n      const failureKey = nodeId + ':' + recordNumber + ':' + segment;\n''',
  '''        displayCursors.set(key, displayCursor);\n        continue;\n      }\n\n      let lexicalAlignment = findSpeechLexicalAlignment(\n        recordLexicalWords,\n        lexicalTarget,\n        lexicalCursor);\n      if (!lexicalAlignment && lexicalCursor > 0) {\n        lexicalAlignment = findSpeechLexicalAlignment(\n          recordLexicalWords,\n          lexicalTarget,\n          0);\n      }\n      if (lexicalAlignment && lexicalAlignment.length) {\n        const firstGroup = lexicalAlignment[0];\n        const lastGroup = lexicalAlignment[lexicalAlignment.length - 1];\n        const firstWord = recordLexicalWords[firstGroup.start];\n        const lastWord = recordLexicalWords[lastGroup.end];\n        const tokenStart = Number(firstWord.dataset.index);\n        const tokenEnd = Number(lastWord.dataset.index);\n        markNodeRange(tokenStart, tokenEnd, nodeId);\n        markAlignedVoiceSelectableWords(\n          recordLexicalWords,\n          lexicalAlignment,\n          nodeId,\n          segmentNodeWordStart,\n          speechTokenOffsets);\n        rememberSegmentRange(\n          nodeId,\n          tokenStart,\n          tokenEnd,\n          displayTarget,\n          lexicalTarget);\n        lexicalCursor = lastGroup.end + 1;\n        displayCursor = Number(lastWord.dataset.recordIndex) + 1;\n        lexicalCursors.set(key, lexicalCursor);\n        displayCursors.set(key, displayCursor);\n        continue;\n      }\n\n      const failureKey = nodeId + ':' + recordNumber + ':' + segment;\n''')

replace_once(
  view,
  '''  const openDetails = preserve\n    ? [...transcript.querySelectorAll('details')].map(x => x.open)\n    : [];\n''',
  '''  const localDetailsState = new Map();\n  if (!discardDisclosureStateOnNextReplacement) {\n    for (const details of transcript.querySelectorAll('details')) {\n      const key = structureDetailsKey(details);\n      localDetailsState.set(key, details.open);\n      if (!details.open) openDisclosureOverrides.delete(key);\n    }\n  }\n''')

replace_once(
  view,
  '''  [...transcript.querySelectorAll('details')].forEach((item, index) => {\n    if (index < openDetails.length) item.open = openDetails[index];\n  });\n''',
  '''  for (const details of transcript.querySelectorAll('details')) {\n    const key = structureDetailsKey(details);\n    if (localDetailsState.has(key)) {\n      setDisclosureOpenProgrammatically(details, localDetailsState.get(key));\n    } else if (openDisclosureOverrides.has(key)) {\n      setDisclosureOpenProgrammatically(details, true);\n    }\n  }\n  discardDisclosureStateOnNextReplacement = false;\n''')

replace_once(
  view,
  '''  for (const details of transcript.querySelectorAll('details')) {\n    const key = structureDetailsKey(details);\n    if (openDetails.has(key)) details.open = openDetails.get(key);\n  }\n''',
  '''  for (const details of transcript.querySelectorAll('details')) {\n    const key = structureDetailsKey(details);\n    if (openDetails.has(key)) {\n      setDisclosureOpenProgrammatically(details, openDetails.get(key));\n    } else if (openDisclosureOverrides.has(key)) {\n      setDisclosureOpenProgrammatically(details, true);\n    }\n  }\n  discardDisclosureStateOnNextReplacement = false;\n''')

replace_once(
  view,
  '''    if (sameSession)\n    {\n      QueueRefresh(force: false);\n      return;\n    }\n\n    _pendingPosition = null;\n''',
  '''    if (sameSession)\n    {\n      QueueRefresh(force: false);\n      return;\n    }\n\n    if (_initialized)\n    {\n      _ = ExecuteAsync("resetDisclosureOpenOverrides();");\n    }\n    _pendingPosition = null;\n''')

replace_once(
  view,
  '''      _ = ExecuteAsync("replaceTranscript('', false, []);");\n''',
  '''      _ = ExecuteAsync(\n        "resetDisclosureOpenOverrides(); replaceTranscript('', false, []);");\n''')

print("Applied popup routing, markup-to-speech alignment, and disclosure-state repairs.")
