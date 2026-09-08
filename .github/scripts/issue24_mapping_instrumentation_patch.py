from pathlib import Path


def replace_once(text, old, new, name):
  if old not in text:
    raise SystemExit(f'{name}: target not found')
  return text.replace(old, new, 1)


path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')

text = replace_once(
  text,
  "let segmentRangesByNode = new Map();\nconst reportedMappingFailures = new Set();",
  "let segmentRangesByNode = new Map();\nlet mappingGeneration = 0;\nconst reportedMappingFailures = new Set();",
  'mapping generation state')

old = '''      if (type == "mapping-failure" || type == "playback-unmatched")
      {
        DiagnosticLog.Write($"transcript.{type}", new
        {
          nodeId = ReadOptionalInt64(root, "nodeId"),
          recordNumber = ReadOptionalInt32(root, "recordNumber"),
          sourceId = ReadOptionalString(root, "sourceId"),
          text = ReadOptionalString(root, "text")
        });
        return;
      }'''
new = '''      if (type == "mapping-failure" || type == "playback-unmatched")
      {
        DiagnosticLog.Write($"transcript.{type}", root.Clone());
        return;
      }
      if (type is "mapping-node-summary" or
          "mapping-install-summary" or
          "fragment-range-miss")
      {
        DiagnosticLog.Write(
          $"transcript.{type.Replace('-', '_')}",
          root.Clone());
        return;
      }'''
text = replace_once(text, old, new, 'WebMessageReceived diagnostics')

anchor = '''function assignNodeScopes(nodeMap) {
  knownNodeIds = new Set();
  segmentRangesByNode = new Map();'''
replacement = '''function diagnosticRanges(ranges) {
  return (ranges || []).slice(0, 64).map(range => ({
    start: range.start,
    end: range.end,
    displayKey: String(range.displayKey || '').slice(0, 1000),
    lexicalKey: String(range.lexicalKey || '').slice(0, 1000)
  }));
}

function postMappingInstallSummary(nodeMap) {
  const nodes = [];
  for (const item of nodeMap || []) {
    const nodeId = String(item.NodeId ?? item.nodeId ?? '');
    const recordNumber = Number(
      item.RecordNumber ?? item.recordNumber ?? 0);
    const sourceId = String(item.SourceId ?? item.sourceId ?? '');
    const segments = item.Segments ?? item.segments ?? [];
    const ranges = segmentRangesByNode.get(nodeId) || [];
    const scopedWords = words.filter(word => word.dataset.nodeId === nodeId);
    nodes.push({
      nodeId: Number(nodeId),
      recordNumber,
      sourceId,
      segmentCount: segments.length,
      rangeCount: ranges.length,
      scopedWordCount: scopedWords.length,
      stableWordCount: scopedWords.filter(word => !!word.dataset.wordId).length
    });
  }
  chrome.webview.postMessage({
    type: 'mapping-install-summary',
    mappingGeneration,
    nodeCount: nodes.length,
    totalWordCount: words.length,
    nodes
  });
}

function postFragmentRangeMiss(
  text,
  nodeId,
  nodeKey,
  displayKey,
  lexicalKey,
  mapped,
  knownNode) {
  const nodeWords = words.filter(word => word.dataset.nodeId === nodeKey);
  chrome.webview.postMessage({
    type: 'fragment-range-miss',
    mappingGeneration,
    nodeId,
    knownNode,
    text: String(text || '').slice(0, 500),
    displayKey: String(displayKey || '').slice(0, 1000),
    lexicalKey: String(lexicalKey || '').slice(0, 1000),
    storedRangeCount: mapped.length,
    storedRanges: diagnosticRanges(mapped),
    currentNode,
    currentIndex,
    currentEndIndex,
    currentFragmentText: String(currentFragmentText || '').slice(0, 500),
    currentFragmentStart,
    currentFragmentEnd,
    currentBoundaryWordIndex,
    nodeWordCount: nodeWords.length,
    nodeWordSample: nodeWords.slice(0, 80).map(word => ({
      index: Number(word.dataset.index ?? -1),
      normalized: word.dataset.normalized || '',
      recordNumber: word.dataset.recordNumber || '',
      sourceId: word.dataset.sourceId || '',
      recordIndex: word.dataset.recordIndex || '',
      wordId: word.dataset.wordId || ''
    }))
  });
}

function assignNodeScopes(nodeMap) {
  ++mappingGeneration;
  knownNodeIds = new Set();
  segmentRangesByNode = new Map();'''
text = replace_once(text, anchor, replacement, 'mapping diagnostic helpers')

old = '''        chrome.webview.postMessage({
          type: 'mapping-failure',
          nodeId: Number(nodeId),
          recordNumber: Number(recordNumber),
          sourceId,
          text: segment.slice(0, 240)
        });'''
new = '''        chrome.webview.postMessage({
          type: 'mapping-failure',
          mappingGeneration,
          nodeId: Number(nodeId),
          recordNumber: Number(recordNumber),
          sourceId,
          text: segment.slice(0, 500),
          displayKey: displayTarget.join('\\u0000').slice(0, 1000),
          lexicalKey: lexicalTarget.join('\\u0000').slice(0, 1000),
          displayCursor,
          lexicalCursor,
          recordWordCount: recordWords.length,
          recordLexicalWordCount: recordLexicalWords.length,
          recordWordSample: recordWords.slice(0, 80)
            .map(word => word.dataset.normalized || ''),
          recordLexicalWordSample: recordLexicalWords.slice(0, 80)
            .map(word => word.dataset.normalized || '')
        });'''
text = replace_once(text, old, new, 'mapping failure details')

old = '''    if (!mappedAny) continue;
  }
}

function chooseNearestRange'''
new = '''    const ranges = segmentRangesByNode.get(nodeId) || [];
    chrome.webview.postMessage({
      type: 'mapping-node-summary',
      mappingGeneration,
      nodeId: Number(nodeId),
      recordNumber: Number(recordNumber),
      sourceId,
      segmentCount: segments.length,
      segments: Array.from(segments).slice(0, 64).map(segment => ({
        text: String(segment).slice(0, 500),
        displayKey: tokenizeDisplay(segment).join('\\u0000').slice(0, 1000),
        lexicalKey: tokenize(segment).join('\\u0000').slice(0, 1000)
      })),
      recordWordCount: recordWords.length,
      recordLexicalWordCount: recordLexicalWords.length,
      mappedAny,
      rangeCount: ranges.length,
      ranges: diagnosticRanges(ranges)
    });
    if (!mappedAny) continue;
  }
}

function chooseNearestRange'''
text = replace_once(text, old, new, 'node mapping summary')

old = '''  const mappedRange = chooseNearestRange(matches, nodeId);
  if (mappedRange) return mappedRange;
  if (knownNodeIds.has(nodeKey)) return null;

  const globalStart = findSequence(
    words,
    displayTarget,
    0,
    null,
    null,
    null);
  if (globalStart >= 0) {
    return {
      start: globalStart,
      end: globalStart + displayTarget.length - 1
    };
  }
  return null;'''
new = '''  const mappedRange = chooseNearestRange(matches, nodeId);
  if (mappedRange) return mappedRange;
  const knownNode = knownNodeIds.has(nodeKey);
  if (knownNode) {
    postFragmentRangeMiss(
      text, nodeId, nodeKey, displayKey, lexicalKey, mapped, true);
    return null;
  }

  const globalStart = findSequence(
    words,
    displayTarget,
    0,
    null,
    null,
    null);
  if (globalStart >= 0) {
    return {
      start: globalStart,
      end: globalStart + displayTarget.length - 1
    };
  }
  postFragmentRangeMiss(
    text, nodeId, nodeKey, displayKey, lexicalKey, mapped, false);
  return null;'''
text = replace_once(text, old, new, 'fragment range miss state')

old = '''  assignStableWordScopes(wordMap || []);
  postStructureStage('''
new = '''  assignStableWordScopes(wordMap || []);
  postMappingInstallSummary(nodeMap || []);
  postStructureStage('''
text = replace_once(text, old, new, 'DOM install summary')

old = '''  assignStableWordScopes(wordMap || []);
  previousStructureMap = postStructureStage('''
new = '''  assignStableWordScopes(wordMap || []);
  postMappingInstallSummary(nodeMap || []);
  previousStructureMap = postStructureStage('''
text = replace_once(text, old, new, 'window install summary')

path.write_text(text, encoding='utf-8')

path = Path('AgentPanelSpeaker/Issue24ProductionPathRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old = '''    Task ensure = webView.EnsureCoreWebView2Async();
    PumpUntilCompleted(ensure, $"{description} WebView2 initialization");

    MethodInfo? shellMethod'''
new = '''    Task ensure = webView.EnsureCoreWebView2Async();
    PumpUntilCompleted(ensure, $"{description} WebView2 initialization");

    var fragmentRangeMisses = new List<JsonElement>();
    webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
    {
      try
      {
        using JsonDocument message = JsonDocument.Parse(
          eventArgs.WebMessageAsJson);
        JsonElement root = message.RootElement;
        if (root.TryGetProperty("type", out JsonElement type) &&
            string.Equals(
              type.GetString(),
              "fragment-range-miss",
              StringComparison.Ordinal))
        {
          fragmentRangeMisses.Add(root.Clone());
        }
      }
      catch (JsonException)
      {
      }
    };

    MethodInfo? shellMethod'''
text = replace_once(text, old, new, 'diagnostic WebView listener')

old = '''    Require(table.GetProperty("listHighlights").GetInt32() == 0,
      $"{description}: ancestor list-item highlight remained during nested table speech.");
  }'''
new = '''    Require(table.GetProperty("listHighlights").GetInt32() == 0,
      $"{description}: ancestor list-item highlight remained during nested table speech.");

    string missingFragment = "__issue24_diagnostic_missing_fragment__";
    string missingJson = JsonSerializer.Serialize(missingFragment);
    string missingWordJson = JsonSerializer.Serialize("missing");
    Task<string> diagnosticProbe = webView.CoreWebView2.ExecuteScriptAsync(
      $"setPlayback('speaking', {missingJson}, 0, {missingWordJson}, " +
      $"{nodeId}, false);");
    PumpUntilCompleted(
      diagnosticProbe,
      $"{description} unmatched-fragment diagnostic probe");
    DateTime diagnosticDeadline = DateTime.UtcNow.AddSeconds(5);
    while (fragmentRangeMisses.Count == 0 &&
           DateTime.UtcNow < diagnosticDeadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(fragmentRangeMisses.Count != 0,
      $"{description}: unmatched playback emitted no fragment-range diagnostic.");
    JsonElement diagnostic = fragmentRangeMisses[^1];
    Require(diagnostic.GetProperty("knownNode").GetBoolean(),
      $"{description}: diagnostic did not report the known node.");
    Require(diagnostic.GetProperty("mappingGeneration").GetInt32() > 0,
      $"{description}: diagnostic omitted mapping generation.");
    Require(diagnostic.GetProperty("storedRangeCount").GetInt32() > 0,
      $"{description}: diagnostic omitted the node's stored ranges.");
    Require(diagnostic.GetProperty("nodeWordCount").GetInt32() > 0,
      $"{description}: diagnostic omitted node-scoped words.");
    Require(!string.IsNullOrEmpty(
        diagnostic.GetProperty("displayKey").GetString()),
      $"{description}: diagnostic omitted requested display tokens.");
    Require(!string.IsNullOrEmpty(
        diagnostic.GetProperty("lexicalKey").GetString()),
      $"{description}: diagnostic omitted requested lexical tokens.");
  }'''
text = replace_once(text, old, new, 'diagnostic acceptance assertions')
path.write_text(text, encoding='utf-8')
