from pathlib import Path

final_core = '2255b6603ef5f2ccbd4111a891375c9c4c246d3e'

# Switch TranscriptView from whole-transcript Markdown reparsing to direct
# structural HTML from the presentation tree.
path = Path('AgentPanelSpeaker/TranscriptView.cs')
text = path.read_text(encoding='utf-8')
old = '''        string markdown = string.Empty;
        IReadOnlyList<TranscriptNodeIdentity> identities =
          Array.Empty<TranscriptNodeIdentity>();
        var options = new ParallelOptions
        {
          CancellationToken = token
        };
        Parallel.Invoke(
          options,
          () => identities = TranscriptNodeIdentityMap.Build(
            path,
            source,
            token),
          () => markdown = TranscriptMarkdownFormatter.Format(
            path,
            source,
            token));
        token.ThrowIfCancellationRequested();
        string html = TranscriptHtmlRenderer.ToHtml(markdown, _pipeline);'''
new = '''        string html = string.Empty;
        IReadOnlyList<TranscriptNodeIdentity> identities =
          Array.Empty<TranscriptNodeIdentity>();
        var options = new ParallelOptions
        {
          CancellationToken = token
        };
        Parallel.Invoke(
          options,
          () => identities = TranscriptNodeIdentityMap.Build(
            path,
            source,
            token),
          () => html = TranscriptPresentationHtmlFormatter.Format(
            path,
            source,
            _pipeline,
            token));
        token.ThrowIfCancellationRequested();'''
if old not in text:
  raise SystemExit('TranscriptView render pipeline snippet not found')
path.write_text(text.replace(old, new, 1), encoding='utf-8')

# Keep each complete direct-rendered turn atomic during virtualization.
path = Path('AgentPanelSpeaker/TranscriptVirtualDocument.cs')
text = path.read_text(encoding='utf-8')
marker = '''  private static readonly Regex StructuralUnitMarkerRegex = new('''
insert = '''  private static readonly Regex TranscriptTurnRegex = new(
    "<section\\s+class=\\\"transcript-turn\\\"\\b[^>]*>.*?</section>",
    RegexOptions.Compiled |
    RegexOptions.CultureInvariant |
    RegexOptions.IgnoreCase |
    RegexOptions.Singleline);
'''
if 'TranscriptTurnRegex' not in text:
  if marker not in text:
    raise SystemExit('Virtual document regex insertion point not found')
  text = text.replace(marker, insert + marker, 1)

old = '''    IReadOnlyList<HtmlRange> declaredRanges =
      FindDeclaredStructuralRanges(html);
    IReadOnlyList<HtmlRange> fallbackRanges =
      FindMultiRecordDetailsRanges(html, anchors, declaredRanges);
    HtmlRange[] structuralRanges = declaredRanges
      .Concat(fallbackRanges)
      .OrderBy(range => range.Start)
      .ToArray();

    DiagnosticLog.Write("transcript.virtual-structure", new
    {
      anchorCount = anchors.Count,
      declaredDetailsCount = declaredRanges.Count,
      fallbackDetailsCount = fallbackRanges.Count,
      structuralDetailsCount = structuralRanges.Length
    });'''
new = '''    IReadOnlyList<HtmlRange> turnRanges = FindTranscriptTurnRanges(html);
    IReadOnlyList<HtmlRange> declaredRanges = turnRanges.Count == 0
      ? FindDeclaredStructuralRanges(html)
      : Array.Empty<HtmlRange>();
    IReadOnlyList<HtmlRange> fallbackRanges = turnRanges.Count == 0
      ? FindMultiRecordDetailsRanges(html, anchors, declaredRanges)
      : Array.Empty<HtmlRange>();
    HtmlRange[] structuralRanges = turnRanges.Count != 0
      ? turnRanges.ToArray()
      : declaredRanges
        .Concat(fallbackRanges)
        .OrderBy(range => range.Start)
        .ToArray();

    DiagnosticLog.Write("transcript.virtual-structure", new
    {
      anchorCount = anchors.Count,
      directTurnCount = turnRanges.Count,
      declaredDetailsCount = declaredRanges.Count,
      fallbackDetailsCount = fallbackRanges.Count,
      structuralUnitCount = structuralRanges.Length
    });'''
if old not in text:
  raise SystemExit('Virtual document structural range block not found')
text = text.replace(old, new, 1)

method_marker = '''  /// <summary>
  /// Returns details ranges containing an explicit AIConversationCore'''
method = '''  /// <summary>
  /// Returns complete turns emitted by the direct canonical HTML renderer.
  /// </summary>
  private static IReadOnlyList<HtmlRange> FindTranscriptTurnRanges(string html)
  {
    return TranscriptTurnRegex.Matches(html)
      .Cast<Match>()
      .Select(match => new HtmlRange(match.Index, match.Index + match.Length))
      .ToArray();
  }

'''
if 'FindTranscriptTurnRanges(string html)' not in text:
  if method_marker not in text:
    raise SystemExit('Virtual document method insertion point not found')
  text = text.replace(method_marker, method + method_marker, 1)
path.write_text(text, encoding='utf-8')

# Update runtime pin constants to the clean core head.
path = Path('AgentPanelSpeaker/AIConversationCoreClient.cs')
text = path.read_text(encoding='utf-8').replace(
  '2c92bf3fe4b41a56051517ec47c5938243f5264a', final_core)
path.write_text(text, encoding='utf-8')

path = Path('tools/AIConversationCore-worker.mjs')
text = path.read_text(encoding='utf-8').replace(
  'c9c618ab1181109a2cf16f6d5596e886513799ba', final_core)
path.write_text(text, encoding='utf-8')
Path('tools/AIConversationCore-runtime/CORE_COMMIT').write_text(
  final_core + '\n', encoding='utf-8')

# Update integration assertions from schema-v1 Markdown structure to schema-v2
# presentation-tree structure.
path = Path('.github/workflows/core-integration-validation.yml')
text = path.read_text(encoding='utf-8')
text = text.replace('c9c618ab1181109a2cf16f6d5596e886513799ba', final_core)
text = text.replace(
  "$decoded.projection.presentation.schema_version -ne 1",
  "$decoded.projection.presentation.schema_version -ne 2")
text = text.replace(
  "$decoded.projection.schema_version -ne 1",
  "$decoded.projection.schema_version -ne 2")
text = text.replace(
  "'record-anchor-except-declared-atomic-unit'",
  "'presentation-tree'")
marker_check = "          if ($decoded.projection.presentation.structural_unit_marker_class -ne 'aicore-structural-unit') { throw 'Unexpected structural unit marker class.' }\n"
tree_check = "          if ($decoded.projection.presentation.tree.kind -ne 'conversation') { throw 'Presentation tree was not returned.' }\n"
text = text.replace(marker_check, tree_check)
path.write_text(text, encoding='utf-8')
