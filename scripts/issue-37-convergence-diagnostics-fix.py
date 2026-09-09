from pathlib import Path

path = Path("AgentPanelSpeaker/TranscriptView.cs")
text = path.read_text(encoding="utf-8")

edge_old = '''    _windowStartIndex = window.StartIndex;
    _windowEndIndex = window.EndIndex;
    if (_pendingPosition is TranscriptPlaybackPosition pending)
    {
      PostPlaybackPosition(pending);
    }
    DiagnosticLog.Write("transcript.window_rendered", new
    {
      reason = "keyboard-" + edge,
'''
edge_new = '''    _windowStartIndex = window.StartIndex;
    _windowEndIndex = window.EndIndex;
    // Keyboard Home/End is explicit manual navigation.  The WebView has already
    // disabled follow mode, so replaying the pending speech marker here would
    // countermand the user's chosen window and can restart window ping-pong.
    DiagnosticLog.Write("transcript.window_rendered", new
    {
      reason = "keyboard-" + edge,
'''
if text.count(edge_old) != 1:
  raise SystemExit(
    f"expected one keyboard-edge pending-playback block, found {text.count(edge_old)}")
text = text.replace(edge_old, edge_new, 1)

index_old = '''    _windowStartIndex = window.StartIndex;
    _windowEndIndex = window.EndIndex;
    if (_pendingPosition is TranscriptPlaybackPosition pending)
    {
      PostPlaybackPosition(pending);
    }
    DiagnosticLog.Write("transcript.window_rendered", new
    {
      reason,
      focalIndex,
'''
index_new = '''    _windowStartIndex = window.StartIndex;
    _windowEndIndex = window.EndIndex;
    bool manualScroll =
      string.Equals(reason, "scroll-up", StringComparison.OrdinalIgnoreCase) ||
      string.Equals(reason, "scroll-down", StringComparison.OrdinalIgnoreCase);
    if (!manualScroll &&
        _pendingPosition is TranscriptPlaybackPosition pending)
    {
      PostPlaybackPosition(pending);
    }
    DiagnosticLog.Write("transcript.window_rendered", new
    {
      reason,
      focalIndex,
'''
if text.count(index_old) != 1:
  raise SystemExit(
    f"expected one indexed-window pending-playback block, found {text.count(index_old)}")
text = text.replace(index_old, index_new, 1)

node_summary = '''    const ranges = segmentRangesByNode.get(nodeId) || [];
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
'''
if text.count(node_summary) != 1:
  raise SystemExit(
    f"expected one mapping-node-summary emission, found {text.count(node_summary)}")
text = text.replace(node_summary, '', 1)

mapped_declaration = '    let mappedAny = false;\n'
if text.count(mapped_declaration) != 1:
  raise SystemExit(
    f"expected one mappedAny declaration, found {text.count(mapped_declaration)}")
text = text.replace(mapped_declaration, '', 1)

mapped_assignment = '        mappedAny = true;\n'
if text.count(mapped_assignment) != 2:
  raise SystemExit(
    f"expected two mappedAny assignments, found {text.count(mapped_assignment)}")
text = text.replace(mapped_assignment, '')

path.write_text(text, encoding="utf-8")
