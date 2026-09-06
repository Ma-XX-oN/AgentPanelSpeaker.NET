from pathlib import Path

# Make fallback disclosure keys independent of a window-local ordinal.  Core and
# direct-rendered reasoning groups normally have explicit stable ids; summary text
# is only the last-resort diagnostic key.
path = Path('AgentPanelSpeaker/TranscriptStructureProbe.cs')
text = path.read_text(encoding='utf-8')
text = text.replace(
  "const keyForDetails = (details, ordinal) => {",
  "const keyForDetails = (details) => {")
text = text.replace(
  "return 'details:' + ordinal + ':' + text;",
  "return 'summary:' + text;")
text = text.replace(
  "allDetails.map((details, index) => [details, keyForDetails(details, index)]));",
  "allDetails.map(details => [details, keyForDetails(details)]));")
text = text.replace(
  '    return $"details:{ordinal}:{summaryText}";',
  '    return "summary:" + summaryText;')
path.write_text(text, encoding='utf-8')

# For the direct renderer, compare the canonical presentation tree to the exact
# structural HTML before the HTML ever reaches virtualization or WebView2.
path = Path('AgentPanelSpeaker/TranscriptPresentationHtmlFormatter.cs')
text = path.read_text(encoding='utf-8')
old = '''    if (!string.IsNullOrWhiteSpace(structureProbeId))
    {
      TranscriptStructureProbe.CapturePresentationTree(structureProbeId, tree);
    }

    var output = new StringBuilder();'''
new = '''    TranscriptStructureSnapshot? presentationStructure = null;
    if (!string.IsNullOrWhiteSpace(structureProbeId))
    {
      presentationStructure =
        TranscriptStructureProbe.CapturePresentationTree(structureProbeId, tree);
    }

    var output = new StringBuilder();'''
if old not in text:
  raise SystemExit('presentation snapshot capture block not found')
text = text.replace(old, new, 1)
old = '''    foreach (JsonElement turn in turns.EnumerateArray())
    {
      cancellationToken.ThrowIfCancellationRequested();
      RenderTurn(
        output,
        turn,
        pipeline,
        emittedSourceIndexes,
        cancellationToken);
    }
    return output.ToString();'''
new = '''    foreach (JsonElement turn in turns.EnumerateArray())
    {
      cancellationToken.ThrowIfCancellationRequested();
      RenderTurn(
        output,
        turn,
        pipeline,
        emittedSourceIndexes,
        cancellationToken);
    }
    string html = output.ToString();
    if (presentationStructure is not null)
    {
      TranscriptStructureSnapshot directRendererStructure =
        TranscriptStructureProbe.CaptureHtml(
          structureProbeId!,
          "direct-renderer-html",
          html);
      TranscriptStructureProbe.Compare(
        presentationStructure,
        directRendererStructure);
    }
    return html;'''
if old not in text:
  raise SystemExit('direct renderer return block not found')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
