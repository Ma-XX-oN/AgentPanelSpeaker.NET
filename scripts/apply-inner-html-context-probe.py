from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

# Add raw-input and parsed-DOM context helpers immediately before
# postStructureStage().  These capture only records that actually diverge.
marker = '''function postStructureStage(\n'''
helper = r'''function structureAnchorSelector(recordNumber, sourceId) {
  return '.record-anchor[data-jsonl-record="' +
    CSS.escape(String(recordNumber)) + '"][data-source-id="' +
    CSS.escape(String(sourceId || '')) + '"]';
}

function inputContextForRecord(html, recordNumber, sourceId) {
  const recordNeedle = 'data-jsonl-record="' + String(recordNumber) + '"';
  const sourceNeedle = 'data-source-id="' + String(sourceId || '') + '"';
  let index = html.indexOf(sourceNeedle);
  if (index < 0) index = html.indexOf(recordNeedle);
  if (index < 0) return '';
  const start = Math.max(0, index - 1800);
  const end = Math.min(html.length, index + 2200);
  return html.slice(start, end);
}

function domContextForRecord(recordNumber, sourceId) {
  const anchor = transcript.querySelector(
    structureAnchorSelector(recordNumber, sourceId));
  if (!anchor) return '';
  let element = anchor.parentElement;
  let selected = anchor;
  for (let depth = 0; element && depth < 4; depth++) {
    selected = element;
    if (element.tagName === 'DETAILS') break;
    element = element.parentElement;
  }
  const html = selected.outerHTML || '';
  return html.length <= 5000 ? html : html.slice(0, 5000);
}

function buildStructureDivergenceContexts(html, differences) {
  return (differences || []).slice(0, 20).map(diff => ({
    recordNumber: diff.recordNumber,
    sourceId: diff.sourceId,
    beforeDetails: diff.beforeDetails,
    afterDetails: diff.afterDetails,
    inputContext: inputContextForRecord(
      html,
      diff.recordNumber,
      diff.sourceId),
    domContext: domContextForRecord(diff.recordNumber, diff.sourceId)
  }));
}

'''
if marker not in text:
  raise SystemExit('postStructureStage marker not found')
text = text.replace(marker, helper + marker, 1)

old = '''function postStructureStage(\n  probeId,\n  stage,\n  expectedMap,\n  previousMap,\n  previousStage) {'''
new = '''function postStructureStage(\n  probeId,\n  stage,\n  expectedMap,\n  previousMap,\n  previousStage,\n  rawInputHtml = '') {'''
if old not in text:
  raise SystemExit('postStructureStage signature not found')
text = text.replace(old, new, 1)

old = '''    differencesFromExpected: expectedDifferences,\n    differencesFromPrevious: previousDifferences\n  });'''
new = '''    differencesFromExpected: expectedDifferences,\n    differencesFromPrevious: previousDifferences,\n    divergenceContexts:\n      previousDifferences.length === 0 || !rawInputHtml\n        ? []\n        : buildStructureDivergenceContexts(rawInputHtml, previousDifferences)\n  });'''
if old not in text:
  raise SystemExit('structure postMessage body not found')
text = text.replace(old, new, 1)

old = '''  previousStructureMap = postStructureStage(\n    structureProbeId,\n    'after-inner-html',\n    expectedStructureMap,\n    previousStructureMap,\n    previousStructureStage);'''
new = '''  previousStructureMap = postStructureStage(\n    structureProbeId,\n    'after-inner-html',\n    expectedStructureMap,\n    previousStructureMap,\n    previousStructureStage,\n    html);'''
if old not in text:
  raise SystemExit('after-inner-html probe call not found')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
