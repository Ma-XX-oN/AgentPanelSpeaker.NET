from pathlib import Path

CORE = '2255b6603ef5f2ccbd4111a891375c9c4c246d3e'


def replace_once(path, old, new, label):
  p = Path(path)
  text = p.read_text(encoding='utf-8')
  if old not in text:
    raise SystemExit(f'{label}: expected text not found in {path}')
  p.write_text(text.replace(old, new, 1), encoding='utf-8')


# Pin APS to the schema-2 provider-independent presentation tree.
replace_once(
  'AgentPanelSpeaker/AIConversationCoreClient.cs',
  '''  private const string ExpectedCoreCommit =\n    "c9c618ab1181109a2cf16f6d5596e886513799ba";\n  private const int ExpectedPresentationSchemaVersion = 1;\n  private const string ExpectedSplitPolicy =\n    "record-anchor-except-declared-atomic-unit";''',
  f'''  private const string ExpectedCoreCommit =\n    "{CORE}";\n  private const int ExpectedPresentationSchemaVersion = 2;\n  private const string ExpectedSplitPolicy =\n    "presentation-tree";''',
  'core contract')

replace_once(
  'tools/AIConversationCore-worker.mjs',
  "const CORE_COMMIT = 'c9c618ab1181109a2cf16f6d5596e886513799ba';",
  f"const CORE_COMMIT = '{CORE}';",
  'worker core pin')

# Expose a full, unsplit window. The DOM presentation path intentionally keeps
# complete structural objects together instead of reparsing/splitting HTML.
replace_once(
  'AgentPanelSpeaker/TranscriptVirtualDocument.cs',
  '''  public int Count => _records.Length;\n\n  public static TranscriptVirtualDocument Build(string html)''',
  '''  public int Count => _records.Length;\n\n  /// <summary>\n  /// Returns the complete rendered record inventory.\n  /// </summary>\n  public IReadOnlyList<TranscriptVirtualRecord> Records => _records;\n\n  /// <summary>\n  /// Returns the complete transcript as one unsplit window. This is used by the\n  /// canonical DOM renderer so structural elements are created as DOM objects\n  /// and are never divided across independently parsed HTML fragments.\n  /// </summary>\n  public TranscriptWindow CreateFullWindow()\n  {\n    string html = string.Concat(_records.Select(record => record.Html));\n    return new TranscriptWindow(\n      html,\n      0,\n      _records.Length - 1,\n      0,\n      0,\n      _records);\n  }\n\n  public static TranscriptVirtualDocument Build(string html)''',
  'full virtual window')

view = Path('AgentPanelSpeaker/TranscriptView.cs')
text = view.read_text(encoding='utf-8')

text = text.replace(
  '  private int _windowEndIndex = -1;\n  private CancellationTokenSource? _findCancellation;',
  '  private int _windowEndIndex = -1;\n  private bool _domPresentationMode;\n  private CancellationTokenSource? _findCancellation;',
  1)

# Reset the mode on session changes/clear.
text = text.replace(
  '    _windowStartIndex = -1;\n    _windowEndIndex = -1;\n    CancelFindSearch();',
  '    _windowStartIndex = -1;\n    _windowEndIndex = -1;\n    _domPresentationMode = false;\n    CancelFindSearch();',
  2)

old_prepare = '''      TranscriptRenderPayload payload = await Task.Run(() =>\n      {\n        string markdown = string.Empty;\n        IReadOnlyList<TranscriptNodeIdentity> identities =\n          Array.Empty<TranscriptNodeIdentity>();\n        var options = new ParallelOptions\n        {\n          CancellationToken = token\n        };\n        Parallel.Invoke(\n          options,\n          () => identities = TranscriptNodeIdentityMap.Build(\n            path,\n            source,\n            token),\n          () => markdown = TranscriptMarkdownFormatter.Format(\n            path,\n            source,\n            token));\n        token.ThrowIfCancellationRequested();\n        string html = TranscriptHtmlRenderer.ToHtml(markdown, _pipeline);\n        TranscriptStructureSnapshot rendererStructure =\n          TranscriptStructureProbe.CaptureHtml(\n            structureProbeId,\n            "renderer-html",\n            html);\n        TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(\n          html,\n          identities,\n          token);\n        TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);\n        return new TranscriptRenderPayload(\n          document,\n          identities,\n          searchIndex,\n          rendererStructure);\n      }, token);'''
new_prepare = '''      TranscriptRenderPayload payload = await Task.Run(() =>\n      {\n        TranscriptPresentationDomResult presentation = null!;\n        IReadOnlyList<TranscriptNodeIdentity> identities =\n          Array.Empty<TranscriptNodeIdentity>();\n        var options = new ParallelOptions\n        {\n          CancellationToken = token\n        };\n        Parallel.Invoke(\n          options,\n          () => identities = TranscriptNodeIdentityMap.Build(\n            path,\n            source,\n            token),\n          () => presentation = TranscriptPresentationDomFormatter.Format(\n            path,\n            source,\n            _pipeline,\n            token));\n        token.ThrowIfCancellationRequested();\n        string html = presentation.Html;\n        TranscriptStructureSnapshot rendererStructure =\n          TranscriptStructureProbe.CaptureHtml(\n            structureProbeId,\n            "dom-model-html",\n            html);\n        TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(\n          html,\n          identities,\n          token);\n        TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);\n        return new TranscriptRenderPayload(\n          document,\n          identities,\n          searchIndex,\n          rendererStructure,\n          presentation.Nodes);\n      }, token);'''
if old_prepare not in text:
  raise SystemExit('render preparation block not found')
text = text.replace(old_prepare, new_prepare, 1)

old_window = '''      _virtualDocument = payload.Document;\n      _identities = payload.Identities;\n      _searchIndex = payload.SearchIndex;\n      int focalIndex = ResolveInitialWindowIndex(payload.Document, payload.Identities);\n      TranscriptWindow window = payload.Document.CreateWindow(focalIndex);\n      TranscriptStructureSnapshot virtualStructure =\n        TranscriptStructureProbe.CaptureHtml(\n          structureProbeId,\n          "virtual-window-html",\n          window.Html);\n      TranscriptStructureProbe.Compare(\n        payload.RendererStructure,\n        virtualStructure);\n      string script = BuildReplaceWindowScript(\n        window,\n        preserve: !force,\n        focusVirtualIndex: focalIndex,\n        structureProbeId: structureProbeId,\n        expectedStructure: virtualStructure);\n      long domStartMilliseconds = renderTimer.ElapsedMilliseconds;\n      if (!await ExecuteAsync(script))\n      {\n        ShowLoading("Unable to load transcript view. See diagnostic log.");\n        return;\n      }\n      _windowStartIndex = window.StartIndex;\n      _windowEndIndex = window.EndIndex;'''
new_window = '''      _virtualDocument = payload.Document;\n      _identities = payload.Identities;\n      _searchIndex = payload.SearchIndex;\n      int focalIndex = ResolveInitialWindowIndex(payload.Document, payload.Identities);\n      TranscriptWindow window = payload.Document.CreateFullWindow();\n      TranscriptStructureSnapshot virtualStructure =\n        TranscriptStructureProbe.CaptureHtml(\n          structureProbeId,\n          "dom-model-serialized-html",\n          window.Html);\n      TranscriptStructureProbe.Compare(\n        payload.RendererStructure,\n        virtualStructure);\n      string script = BuildReplaceDomScript(\n        window,\n        payload.DomNodes,\n        preserve: !force,\n        structureProbeId: structureProbeId,\n        expectedStructure: virtualStructure);\n      long domStartMilliseconds = renderTimer.ElapsedMilliseconds;\n      if (!await ExecuteAsync(script))\n      {\n        ShowLoading("Unable to load transcript view. See diagnostic log.");\n        return;\n      }\n      _windowStartIndex = window.StartIndex;\n      _windowEndIndex = window.EndIndex;\n      _domPresentationMode = true;'''
if old_window not in text:
  raise SystemExit('initial window block not found')
text = text.replace(old_window, new_window, 1)

old_payload = '''  private sealed record TranscriptRenderPayload(\n    TranscriptVirtualDocument Document,\n    IReadOnlyList<TranscriptNodeIdentity> Identities,\n    TranscriptSearchIndex SearchIndex,\n    TranscriptStructureSnapshot RendererStructure);'''
new_payload = '''  private sealed record TranscriptRenderPayload(\n    TranscriptVirtualDocument Document,\n    IReadOnlyList<TranscriptNodeIdentity> Identities,\n    TranscriptSearchIndex SearchIndex,\n    TranscriptStructureSnapshot RendererStructure,\n    IReadOnlyList<TranscriptDomNode> DomNodes);'''
if old_payload not in text:
  raise SystemExit('render payload record not found')
text = text.replace(old_payload, new_payload, 1)

build_marker = '''  private string BuildReplaceWindowScript(\n    TranscriptWindow window,'''
if build_marker not in text:
  raise SystemExit('BuildReplaceWindowScript marker not found')
build_dom = '''  private string BuildReplaceDomScript(\n    TranscriptWindow window,\n    IReadOnlyList<TranscriptDomNode> domNodes,\n    bool preserve,\n    string? structureProbeId = null,\n    TranscriptStructureSnapshot? expectedStructure = null)\n  {\n    var keys = window.Records\n      .Select(record => record.SourceId + "\\0" + record.RecordNumber)\n      .ToHashSet(StringComparer.Ordinal);\n    IReadOnlyList<TranscriptNodeIdentity> identities = _identities\n      .Where(identity => keys.Contains(\n        identity.SourceId + "\\0" + identity.RecordNumber))\n      .ToArray();\n    IReadOnlyList<TranscriptRecordWordMap> wordMaps = _searchIndex?.GetWordMaps(\n      window.Records) ?? Array.Empty<TranscriptRecordWordMap>();\n    return "replaceTranscriptDom(" +\n      JsonSerializer.Serialize(domNodes) + "," +\n      JsonSerializer.Serialize(preserve) + "," +\n      JsonSerializer.Serialize(identities) + "," +\n      JsonSerializer.Serialize(wordMaps) + "," +\n      JsonSerializer.Serialize(expectedStructure?.Entries ??\n        Array.Empty<TranscriptStructureEntry>()) + "," +\n      JsonSerializer.Serialize(structureProbeId ?? string.Empty) + ");";\n  }\n\n'''
text = text.replace(build_marker, build_dom + build_marker, 1)

# Do not fall back to HTML-window replacement once the complete DOM model is
# resident. Home/End can scroll the existing object tree directly.
edge_marker = '''  private async Task RenderWindowForEdgeAsync(string edge)\n  {\n    TranscriptVirtualDocument? document = _virtualDocument;'''
edge_new = '''  private async Task RenderWindowForEdgeAsync(string edge)\n  {\n    if (_domPresentationMode)\n    {\n      await ExecuteAsync(edge == "start"\n        ? "window.scrollTo(0, 0);"\n        : "window.scrollTo(0, document.documentElement.scrollHeight);");\n      return;\n    }\n    TranscriptVirtualDocument? document = _virtualDocument;'''
if edge_marker not in text:
  raise SystemExit('edge render marker not found')
text = text.replace(edge_marker, edge_new, 1)

index_marker = '''  private async Task RenderWindowForIndexAsync(\n    int focalIndex,\n    string reason,\n    int? anchorRecordNumber,\n    string anchorSourceId,\n    double? anchorOffset)\n  {\n    TranscriptVirtualDocument? document = _virtualDocument;'''
index_new = '''  private async Task RenderWindowForIndexAsync(\n    int focalIndex,\n    string reason,\n    int? anchorRecordNumber,\n    string anchorSourceId,\n    double? anchorOffset)\n  {\n    if (_domPresentationMode)\n    {\n      return;\n    }\n    TranscriptVirtualDocument? document = _virtualDocument;'''
if index_marker not in text:
  raise SystemExit('index render marker not found')
text = text.replace(index_marker, index_new, 1)

js_marker = '''function replaceTranscriptWindow(\n  html,\n  preserve,'''
if js_marker not in text:
  raise SystemExit('replaceTranscriptWindow JS marker not found')
js_dom = r'''function buildTranscriptDomNode(spec) {
  if (!spec) return document.createDocumentFragment();
  const kind = String(spec.Kind ?? spec.kind ?? '');
  if (kind === 'text') {
    return document.createTextNode(String(spec.Text ?? spec.text ?? ''));
  }
  if (kind === 'html') {
    // Markdown parsing ends at the leaf boundary. Structural transcript nodes
    // never enter innerHTML/template parsing.
    const template = document.createElement('template');
    template.innerHTML = String(spec.Html ?? spec.html ?? '');
    return template.content.cloneNode(true);
  }
  if (kind !== 'element') return document.createDocumentFragment();

  const tag = String(spec.Tag ?? spec.tag ?? 'div');
  const element = document.createElement(tag);
  const attributes = spec.Attributes ?? spec.attributes ?? {};
  for (const [name, value] of Object.entries(attributes)) {
    element.setAttribute(name, String(value));
  }
  const children = spec.Children ?? spec.children ?? [];
  for (const child of children) {
    element.append(buildTranscriptDomNode(child));
  }
  return element;
}

function replaceTranscriptDom(
  domNodes,
  preserve,
  nodeMap,
  wordMap,
  expectedStructure = [],
  structureProbeId = '') {
  const expectedStructureMap = normalizeStructureEntries(expectedStructure);
  clearFindHighlights();
  findCurrentWords = [];
  const nearBottom = document.documentElement.scrollHeight -
    (window.scrollY + window.innerHeight) < 80;
  const previousY = window.scrollY;
  const openDetails = new Map();
  if (preserve) {
    for (const details of transcript.querySelectorAll('details')) {
      openDetails.set(structureDetailsKey(details), details.open);
    }
  }

  const fragment = document.createDocumentFragment();
  for (const spec of domNodes || []) {
    fragment.append(buildTranscriptDomNode(spec));
  }
  transcript.replaceChildren(fragment);

  let currentStructureMap = postStructureStage(
    structureProbeId,
    'after-dom-construction',
    expectedStructureMap,
    expectedStructureMap,
    'dom-model-serialized-html');
  for (const details of transcript.querySelectorAll('details')) {
    const key = structureDetailsKey(details);
    if (openDetails.has(key)) details.open = openDetails.get(key);
  }
  currentStructureMap = postStructureStage(
    structureProbeId,
    'after-details-restore',
    expectedStructureMap,
    currentStructureMap,
    'after-dom-construction');

  wrapWords();
  currentStructureMap = postStructureStage(
    structureProbeId,
    'after-wrap-words',
    expectedStructureMap,
    currentStructureMap,
    'after-details-restore');
  assignRecordScopes();
  currentStructureMap = postStructureStage(
    structureProbeId,
    'after-record-scopes',
    expectedStructureMap,
    currentStructureMap,
    'after-wrap-words');
  assignNodeScopes(nodeMap || []);
  currentStructureMap = postStructureStage(
    structureProbeId,
    'after-node-scopes',
    expectedStructureMap,
    currentStructureMap,
    'after-record-scopes');
  assignStableWordScopes(wordMap || []);
  postStructureStage(
    structureProbeId,
    'replace-dom-exit',
    expectedStructureMap,
    currentStructureMap,
    'after-node-scopes');

  windowStartIndex = 0;
  windowEndIndex = Number.MAX_SAFE_INTEGER;
  virtualShiftPending = false;
  currentIndex = -1;
  currentEndIndex = -1;
  voiceMarkerIndex = -1;
  currentNode = -1;
  currentFragmentText = null;
  currentFragmentStart = -1;
  currentFragmentEnd = -1;
  currentBoundaryWordIndex = -1;
  liveEndMarker.style.display = 'none';
  if (preserve) {
    if (nearBottom) window.scrollTo(0, document.documentElement.scrollHeight);
    else window.scrollTo(0, previousY);
  }
}

'''
text = text.replace(js_marker, js_dom + js_marker, 1)

view.write_text(text, encoding='utf-8')

# Exercise the new canonical DOM model in the interleaved reasoning/tool
# regression rather than validating only the legacy whole-transcript parser.
program = Path('tools/AgentPanelSpeaker.DisplayParity/Program.cs')
text = program.read_text(encoding='utf-8')
old = '''    string markdown = TranscriptMarkdownFormatter.Format(\n      tempPath,\n      AgentSource.Claude);\n    string html = TranscriptHtmlRenderer.ToHtml(\n      markdown,\n      new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());'''
new = '''    var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();\n    TranscriptPresentationDomResult dom = TranscriptPresentationDomFormatter.Format(\n      tempPath,\n      AgentSource.Claude,\n      pipeline);\n    string html = dom.Html;'''
if old not in text:
  raise SystemExit('interleaved DisplayParity render block not found')
text = text.replace(old, new, 1)
program.write_text(text, encoding='utf-8')
