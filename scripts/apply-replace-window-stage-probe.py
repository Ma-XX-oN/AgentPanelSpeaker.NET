from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

# Pass the existing C# virtual-window snapshot into the WebView function so the
# very first JavaScript stage can compare the parsed DOM directly with the exact
# structure that entered replaceTranscriptWindow().
old = '''  private string BuildReplaceWindowScript(
    TranscriptWindow window,
    bool preserve,
    int? anchorRecordNumber = null,
    string? anchorSourceId = null,
    double? anchorOffset = null,
    int? focusVirtualIndex = null,
    string? focusEdge = null)
'''
new = '''  private string BuildReplaceWindowScript(
    TranscriptWindow window,
    bool preserve,
    int? anchorRecordNumber = null,
    string? anchorSourceId = null,
    double? anchorOffset = null,
    int? focusVirtualIndex = null,
    string? focusEdge = null,
    string? structureProbeId = null,
    TranscriptStructureSnapshot? expectedStructure = null)
'''
if old not in text:
  raise SystemExit('BuildReplaceWindowScript signature not found')
text = text.replace(old, new, 1)

old = '''      JsonSerializer.Serialize(focusVirtualIndex) + "," +
      JsonSerializer.Serialize(focusEdge) + ");";'''
new = '''      JsonSerializer.Serialize(focusVirtualIndex) + "," +
      JsonSerializer.Serialize(focusEdge) + "," +
      JsonSerializer.Serialize(expectedStructure?.Entries ??
        Array.Empty<TranscriptStructureEntry>()) + "," +
      JsonSerializer.Serialize(structureProbeId ?? string.Empty) + ");";'''
if old not in text:
  raise SystemExit('BuildReplaceWindowScript tail not found')
text = text.replace(old, new, 1)

old = '''      string script = BuildReplaceWindowScript(
        window,
        preserve: !force,
        focusVirtualIndex: focalIndex);'''
new = '''      string script = BuildReplaceWindowScript(
        window,
        preserve: !force,
        focusVirtualIndex: focalIndex,
        structureProbeId: structureProbeId,
        expectedStructure: virtualStructure);'''
if old not in text:
  raise SystemExit('initial BuildReplaceWindowScript call not found')
text = text.replace(old, new, 1)

old = '''        if (!await ExecuteAsync(BuildReplaceWindowScript(
              window,
              preserve: false,
              focusVirtualIndex: latestIndex)))'''
new = '''        if (!await ExecuteAsync(BuildReplaceWindowScript(
              window,
              preserve: false,
              focusVirtualIndex: latestIndex,
              structureProbeId: structureProbeId,
              expectedStructure: virtualStructure)))'''
if old not in text:
  raise SystemExit('positioned BuildReplaceWindowScript call not found')
text = text.replace(old, new, 1)

# Add a WebView-side structural snapshot/comparison helper immediately before
# replaceTranscriptWindow. It uses the same container keys as the C# probe.
marker = '''function replaceTranscriptWindow(
'''
helper = r'''function structureDetailsKey(details) {
  const presentation = details.getAttribute('data-presentation-id');
  if (presentation) return 'presentation:' + presentation;
  const marker = details.querySelector('[data-aicore-unit-id]');
  if (marker) return 'core-unit:' + marker.getAttribute('data-aicore-unit-id');
  const summary = Array.from(details.children).find(
    child => child.tagName === 'SUMMARY');
  const summaryText = summary
    ? summary.textContent.trim().replace(/\s+/g, ' ')
    : '';
  return 'summary:' + summaryText;
}

function normalizeStructureEntries(entries) {
  const result = new Map();
  for (const entry of entries || []) {
    const recordNumber = Number(entry.RecordNumber ?? entry.recordNumber ?? 0);
    const sourceId = String(entry.SourceId ?? entry.sourceId ?? '');
    const turnId = String(entry.TurnId ?? entry.turnId ?? '');
    const detailsChain = Array.from(
      entry.DetailsChain ?? entry.detailsChain ?? [],
      value => String(value));
    result.set(sourceId + '\u0000' + recordNumber, {
      recordNumber,
      sourceId,
      turnId,
      detailsChain
    });
  }
  return result;
}

function captureStructureDom() {
  const detailsKeys = new Map(
    Array.from(transcript.querySelectorAll('details'))
      .map(details => [details, structureDetailsKey(details)]));
  const entries = [];
  for (const anchor of transcript.querySelectorAll('.record-anchor')) {
    const chain = [];
    let element = anchor.parentElement;
    while (element) {
      if (element.tagName === 'DETAILS') {
        chain.unshift(detailsKeys.get(element) || 'details:?');
      }
      element = element.parentElement;
    }
    const turn = anchor.closest('section.transcript-turn');
    entries.push({
      recordNumber: Number(anchor.getAttribute('data-jsonl-record') || 0),
      sourceId: anchor.getAttribute('data-source-id') || '',
      turnId: turn
        ? 'presentation:' + (turn.getAttribute('data-presentation-id') || '')
        : '',
      detailsChain: chain
    });
  }
  return {
    entries,
    detailsCount: transcript.querySelectorAll('details').length,
    turnCount: transcript.querySelectorAll('section.transcript-turn').length
  };
}

function compareStructureMaps(before, after) {
  const differences = [];
  for (const [key, left] of before) {
    const right = after.get(key);
    if (!right) {
      differences.push({
        recordNumber: left.recordNumber,
        sourceId: left.sourceId,
        kind: 'missing-record',
        beforeTurn: left.turnId,
        afterTurn: '',
        beforeDetails: left.detailsChain,
        afterDetails: []
      });
      continue;
    }
    const detailsChanged =
      left.detailsChain.length !== right.detailsChain.length ||
      left.detailsChain.some((value, index) => value !== right.detailsChain[index]);
    const turnChanged = left.turnId && right.turnId && left.turnId !== right.turnId;
    if (detailsChanged || turnChanged) {
      differences.push({
        recordNumber: left.recordNumber,
        sourceId: left.sourceId,
        kind: 'containment-changed',
        beforeTurn: left.turnId,
        afterTurn: right.turnId,
        beforeDetails: left.detailsChain,
        afterDetails: right.detailsChain
      });
    }
  }
  for (const [key, right] of after) {
    if (!before.has(key)) {
      differences.push({
        recordNumber: right.recordNumber,
        sourceId: right.sourceId,
        kind: 'unexpected-record',
        beforeTurn: '',
        afterTurn: right.turnId,
        beforeDetails: [],
        afterDetails: right.detailsChain
      });
    }
  }
  return differences;
}

function postStructureStage(
  probeId,
  stage,
  expectedMap,
  previousMap,
  previousStage) {
  if (!probeId) return previousMap;
  const snapshot = captureStructureDom();
  const currentMap = normalizeStructureEntries(snapshot.entries);
  const expectedDifferences = compareStructureMaps(expectedMap, currentMap);
  const previousDifferences = compareStructureMaps(previousMap, currentMap);
  chrome.webview.postMessage({
    type: previousDifferences.length === 0
      ? 'structure-js-equivalent'
      : 'structure-js-divergence',
    probeId,
    stage,
    previousStage,
    anchorCount: snapshot.entries.length,
    detailsCount: snapshot.detailsCount,
    turnCount: snapshot.turnCount,
    expectedDifferenceCount: expectedDifferences.length,
    previousDifferenceCount: previousDifferences.length,
    differencesFromExpected: expectedDifferences,
    differencesFromPrevious: previousDifferences
  });
  return currentMap;
}

'''
if marker not in text:
  raise SystemExit('replaceTranscriptWindow marker not found')
text = text.replace(marker, helper + marker, 1)

old = '''  focusVirtualIndex = null,
  focusEdge = null) {
  clearFindHighlights();'''
new = '''  focusVirtualIndex = null,
  focusEdge = null,
  expectedStructure = [],
  structureProbeId = '') {
  const expectedStructureMap = normalizeStructureEntries(expectedStructure);
  let previousStructureMap = expectedStructureMap;
  let previousStructureStage = 'virtual-window-html';
  clearFindHighlights();'''
if old not in text:
  raise SystemExit('replaceTranscriptWindow signature tail not found')
text = text.replace(old, new, 1)

# Probe after every operation in replaceTranscriptWindow that can possibly affect
# the DOM structure. The first previousDifferenceCount > 0 is the exact mutation
# boundary for this hypothesis.
old = '''    '<div class="virtual-spacer" data-virtual-spacer="bottom" style="height:' +
    Math.max(0, Number(bottomSpacerHeight) || 0) + 'px"></div>';
  windowStartIndex = startIndex;'''
new = '''    '<div class="virtual-spacer" data-virtual-spacer="bottom" style="height:' +
    Math.max(0, Number(bottomSpacerHeight) || 0) + 'px"></div>';
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-inner-html',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-inner-html';
  windowStartIndex = startIndex;'''
if old not in text:
  raise SystemExit('after innerHTML insertion point not found')
text = text.replace(old, new, 1)

old = '''  [...transcript.querySelectorAll('details')].forEach((item, index) => {
    if (index < openDetails.length) item.open = openDetails[index];
  });
  wrapWords();
  assignRecordScopes();
  assignNodeScopes(nodeMap || []);
  assignStableWordScopes(wordMap || []);'''
new = '''  [...transcript.querySelectorAll('details')].forEach((item, index) => {
    if (index < openDetails.length) item.open = openDetails[index];
  });
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-details-restore',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-details-restore';
  wrapWords();
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-wrap-words',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-wrap-words';
  assignRecordScopes();
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-record-scopes',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-record-scopes';
  assignNodeScopes(nodeMap || []);
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-node-scopes',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-node-scopes';
  assignStableWordScopes(wordMap || []);
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-stable-word-scopes',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
  previousStructureStage = 'after-stable-word-scopes';'''
if old not in text:
  raise SystemExit('DOM mutation sequence not found')
text = text.replace(old, new, 1)

old = '''  if (preserve) {
    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);
    else window.scrollTo(0, previousY);
  }
}
'''
new = '''  if (preserve) {
    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);
    else window.scrollTo(0, previousY);
  }
  postStructureStage(
    structureProbeId,
    'replace-window-exit',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage);
}
'''
if old not in text:
  raise SystemExit('replaceTranscriptWindow exit insertion point not found')
text = text.replace(old, new, 1)

# Persist the WebView-side probes in the same JSONL diagnostics as the C# probes.
old = '''      if (type == "find-query")
      {
        HandleFindQuery(root);'''
new = '''      if (type is "structure-js-equivalent" or "structure-js-divergence")
      {
        DiagnosticLog.Write(
          type == "structure-js-equivalent"
            ? "transcript.structure_js_equivalent"
            : "transcript.structure_js_divergence",
          root.Clone());
        return;
      }
      if (type == "find-query")
      {
        HandleFindQuery(root);'''
if old not in text:
  raise SystemExit('WebMessageReceived structure insertion point not found')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
