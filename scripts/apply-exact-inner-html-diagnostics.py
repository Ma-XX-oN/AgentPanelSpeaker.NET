from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

old = '''function postStructureStage(
  probeId,
  stage,
  expectedMap,
  previousMap,
  previousStage,
  rawInputHtml = '') {'''
new = '''function postStructureStage(
  probeId,
  stage,
  expectedMap,
  previousMap,
  previousStage,
  rawInputHtml = '',
  exactAssignedHtml = '',
  exactParsedHtml = '') {'''
if old not in text:
  raise SystemExit('postStructureStage signature not found')
text = text.replace(old, new, 1)

old = '''    divergenceContexts:
      previousDifferences.length === 0 || !rawInputHtml
        ? []
        : buildStructureDivergenceContexts(rawInputHtml, previousDifferences)
  });'''
new = '''    divergenceContexts:
      previousDifferences.length === 0 || !rawInputHtml
        ? []
        : buildStructureDivergenceContexts(rawInputHtml, previousDifferences),
    exactAssignedHtml:
      previousDifferences.length === 0 ? '' : exactAssignedHtml,
    exactParsedHtml:
      previousDifferences.length === 0 ? '' : exactParsedHtml,
    exactAssignedHtmlLength:
      previousDifferences.length === 0 ? 0 : exactAssignedHtml.length,
    exactParsedHtmlLength:
      previousDifferences.length === 0 ? 0 : exactParsedHtml.length
  });'''
if old not in text:
  raise SystemExit('postStructureStage message payload not found')
text = text.replace(old, new, 1)

old = '''  transcript.innerHTML =
    '<div class="virtual-spacer" data-virtual-spacer="top" style="height:' +
    Math.max(0, Number(topSpacerHeight) || 0) + 'px"></div>' + html +
    '<div class="virtual-spacer" data-virtual-spacer="bottom" style="height:' +
    Math.max(0, Number(bottomSpacerHeight) || 0) + 'px"></div>';
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-inner-html',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage,
    html);'''
new = '''  const exactAssignedHtml =
    '<div class="virtual-spacer" data-virtual-spacer="top" style="height:' +
    Math.max(0, Number(topSpacerHeight) || 0) + 'px"></div>' + html +
    '<div class="virtual-spacer" data-virtual-spacer="bottom" style="height:' +
    Math.max(0, Number(bottomSpacerHeight) || 0) + 'px"></div>';
  transcript.innerHTML = exactAssignedHtml;
  const exactParsedHtml = transcript.innerHTML;
  previousStructureMap = postStructureStage(
    structureProbeId,
    'after-inner-html',
    expectedStructureMap,
    previousStructureMap,
    previousStructureStage,
    html,
    exactAssignedHtml,
    exactParsedHtml);'''
if old not in text:
  raise SystemExit('innerHTML assignment block not found')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
