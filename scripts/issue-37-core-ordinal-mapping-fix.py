from pathlib import Path

# The Core HTML renderer owns ordered-list semantics and emits the resolved
# ordinal as data-list-ordinal on each <li>.  AgentPanelSpeaker needs a hidden
# addressable text token only for speech/highlight mapping.  Add that mapping
# token locally without reparsing Markdown or changing canonical HTML semantics.

search_path = Path("AgentPanelSpeaker/TranscriptSearchIndex.cs")
text = search_path.read_text(encoding="utf-8")

regex_anchor = '''  private static readonly HashSet<string> BlockTags = new(
'''
regex = r'''  private static readonly Regex ListItemTagRegex = new(
    "<li\\b[^>]*\\bdata-list-ordinal\\s*=\\s*\"(?<ordinal>-?\\d+)\"[^>]*>",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
'''
if "ListItemTagRegex" not in text:
  if text.count(regex_anchor) != 1:
    raise SystemExit(f"BlockTags anchor count={text.count(regex_anchor)}")
  text = text.replace(regex_anchor, regex + regex_anchor, 1)

loop_anchor = '''    foreach (Match part in HtmlPartRegex.Matches(html))
'''
loop_new = '''    string mappingHtml = AddCoreOrdinalMappingTokens(html);
    foreach (Match part in HtmlPartRegex.Matches(mappingHtml))
'''
if "AddCoreOrdinalMappingTokens(html)" not in text:
  if text.count(loop_anchor) != 1:
    raise SystemExit(f"HTML parse loop anchor count={text.count(loop_anchor)}")
  text = text.replace(loop_anchor, loop_new, 1)

method_anchor = '''  /// <summary>
  /// Resolves an authoritative speech node/word position to its rendered
  /// transcript search coordinates.
  /// </summary>
'''
method = r'''  /// <summary>
  /// Adds non-visual mapping tokens for Core-owned ordered-list ordinals.
  /// </summary>
  /// <remarks>
  /// Core already resolved the semantic ordinal and exposed it through
  /// <c>data-list-ordinal</c>.  This method does not infer list semantics; it
  /// only makes that Core metadata addressable by the existing speech-token
  /// mapper.  Existing mapping spans are preserved without duplication.
  /// </remarks>
  private static string AddCoreOrdinalMappingTokens(string html)
  {
    MatchCollection matches = ListItemTagRegex.Matches(html);
    if (matches.Count == 0)
    {
      return html;
    }

    var result = new StringBuilder(html.Length + matches.Count * 80);
    int cursor = 0;
    foreach (Match match in matches)
    {
      result.Append(html, cursor, match.Index - cursor);
      result.Append(match.Value);
      cursor = match.Index + match.Length;

      int probe = cursor;
      while (probe < html.Length && char.IsWhiteSpace(html[probe]))
      {
        ++probe;
      }
      if (html.AsSpan(probe).StartsWith(
          "<span class=\"speech-ordinal-map\"",
          StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string ordinal = match.Groups["ordinal"].Value;
      result.Append(
        "<span class=\"speech-ordinal-map\" aria-hidden=\"true\" " +
        "style=\"display:none\">" + ordinal + ". </span>");
    }
    result.Append(html, cursor, html.Length - cursor);
    return result.ToString();
  }

'''
if "private static string AddCoreOrdinalMappingTokens" not in text:
  if text.count(method_anchor) != 1:
    raise SystemExit(f"SearchIndex method anchor count={text.count(method_anchor)}")
  text = text.replace(method_anchor, method + method_anchor, 1)

search_path.write_text(text, encoding="utf-8")

view_path = Path("AgentPanelSpeaker/TranscriptView.cs")
text = view_path.read_text(encoding="utf-8")

wrap_anchor = '''function wrapWordsForRecordKeys(recordKeys, reset) {
'''
helper = r'''function ensureCoreOrdinalSpeechMaps() {
  for (const item of transcript.querySelectorAll('li[data-list-ordinal]')) {
    if (item.querySelector(':scope > .speech-ordinal-map')) continue;
    const ordinal = String(item.dataset.listOrdinal || '').trim();
    if (!/^-?\d+$/.test(ordinal)) continue;
    const marker = document.createElement('span');
    marker.className = 'speech-ordinal-map';
    marker.setAttribute('aria-hidden', 'true');
    marker.style.display = 'none';
    marker.textContent = ordinal + '. ';
    item.insertBefore(marker, item.firstChild);
  }
}

'''
if "function ensureCoreOrdinalSpeechMaps()" not in text:
  if text.count(wrap_anchor) != 1:
    raise SystemExit(f"wrapWordsForRecordKeys anchor count={text.count(wrap_anchor)}")
  text = text.replace(wrap_anchor, helper + wrap_anchor, 1)

wrap_call_anchor = '''function wrapWords(nodeMap = null) {
  words = [];
  lexicalWords = [];
  wrapWordsForRecordKeys(
'''
wrap_call_new = '''function wrapWords(nodeMap = null) {
  words = [];
  lexicalWords = [];
  ensureCoreOrdinalSpeechMaps();
  wrapWordsForRecordKeys(
'''
if "ensureCoreOrdinalSpeechMaps();\n  wrapWordsForRecordKeys(" not in text:
  if text.count(wrap_call_anchor) != 1:
    raise SystemExit(f"wrapWords call anchor count={text.count(wrap_call_anchor)}")
  text = text.replace(wrap_call_anchor, wrap_call_new, 1)

view_path.write_text(text, encoding="utf-8")

# Issue #24's production-path acceptance runner predates the Core-unit migration.
# Keep the oracle aligned with the live #37 path: Core units -> virtual window ->
# BuildReplaceWindowScript.  The test must not label or exercise the retired DOM
# construction path as production.
acceptance_path = Path(
  "AgentPanelSpeaker/Issue24ProductionPathRegressionTestRunner.cs")
text = acceptance_path.read_text(encoding="utf-8")

comment_replacements = {
  "  /// replaceTranscriptDom payload builder, the actual WebView2 DOM consumer,\n":
    "  /// virtual-window payload builder, the actual WebView2 DOM consumer,\n",
  "  /// Executes the exact production replaceTranscriptDom output in the actual\n":
    "  /// Executes the exact production replaceTranscriptWindow output in the actual\n",
  "      $\"{description} replaceTranscriptDom payload\");\n":
    "      $\"{description} replaceTranscriptWindow payload\");\n"
}
for old, new in comment_replacements.items():
  if old in text:
    text = text.replace(old, new)

units_anchor = '''      TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(
        presentation.Html);
'''
units_new = '''      TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(
        presentation.Units);
'''
if "TranscriptVirtualDocument.Build(\n        presentation.Units);" not in text:
  if text.count(units_anchor) != 1:
    raise SystemExit(
      f"Production Core-unit document anchor count={text.count(units_anchor)}")
  text = text.replace(units_anchor, units_new, 1)

production_call_anchor = '''      string replaceScript = BuildProductionReplaceScript(
        window,
        presentation.Nodes,
        identities,
        searchIndex);
'''
production_call_new = '''      string replaceScript = BuildProductionReplaceScript(
        window,
        identities,
        searchIndex);
'''
if "window,\n        presentation.Nodes,\n        identities" in text:
  if text.count(production_call_anchor) != 1:
    raise SystemExit(
      f"Production replacement call anchor count={text.count(production_call_anchor)}")
  text = text.replace(production_call_anchor, production_call_new, 1)

secondary_anchor = '''    TranscriptDomNode[] domNodes =
    {
      new("html", null, null, null, html, null)
    };
    string replaceScript = BuildProductionReplaceScript(
      document.CreateFullWindow(),
      domNodes,
      identities,
      searchIndex);
'''
secondary_new = '''    string replaceScript = BuildProductionReplaceScript(
      document.CreateFullWindow(),
      identities,
      searchIndex);
'''
if "TranscriptDomNode[] domNodes" in text:
  if text.count(secondary_anchor) != 1:
    raise SystemExit(
      f"Secondary production replacement anchor count={text.count(secondary_anchor)}")
  text = text.replace(secondary_anchor, secondary_new, 1)

signature_anchor = '''  private static string BuildProductionReplaceScript(
    TranscriptWindow window,
    IReadOnlyList<TranscriptDomNode> domNodes,
    IReadOnlyList<TranscriptNodeIdentity> identities,
    TranscriptSearchIndex searchIndex)
'''
signature_new = '''  private static string BuildProductionReplaceScript(
    TranscriptWindow window,
    IReadOnlyList<TranscriptNodeIdentity> identities,
    TranscriptSearchIndex searchIndex)
'''
if "IReadOnlyList<TranscriptDomNode> domNodes" in text:
  if text.count(signature_anchor) != 1:
    raise SystemExit(
      f"Production helper signature anchor count={text.count(signature_anchor)}")
  text = text.replace(signature_anchor, signature_new, 1)

reflection_anchor = '''    MethodInfo? buildReplaceDom = typeof(TranscriptView).GetMethod(
      "BuildReplaceDomScript",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Require(webViewField is not null &&
        identitiesField is not null &&
        searchIndexField is not null &&
        buildReplaceDom is not null,
      "Production transcript payload members could not be located.");
'''
reflection_new = '''    MethodInfo? buildReplaceWindow = typeof(TranscriptView).GetMethod(
      "BuildReplaceWindowScript",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Require(webViewField is not null &&
        identitiesField is not null &&
        searchIndexField is not null &&
        buildReplaceWindow is not null,
      "Production transcript payload members could not be located.");
'''
if "MethodInfo? buildReplaceDom" in text:
  if text.count(reflection_anchor) != 1:
    raise SystemExit(
      f"Production builder reflection anchor count={text.count(reflection_anchor)}")
  text = text.replace(reflection_anchor, reflection_new, 1)

invoke_anchor = '''    string script = buildReplaceDom!.Invoke(productionView, new object?[]
    {
      window,
      domNodes,
      false,
      null,
      null
    }) as string ?? string.Empty;
    Require(script.Length != 0,
      "Production BuildReplaceDomScript returned no browser payload.");
'''
invoke_new = '''    string script = buildReplaceWindow!.Invoke(productionView, new object?[]
    {
      window,
      false,
      null,
      null,
      null,
      null,
      null,
      null,
      null
    }) as string ?? string.Empty;
    Require(script.Length != 0,
      "Production BuildReplaceWindowScript returned no browser payload.");
'''
if "buildReplaceDom!.Invoke" in text:
  if text.count(invoke_anchor) != 1:
    raise SystemExit(
      f"Production builder invocation anchor count={text.count(invoke_anchor)}")
  text = text.replace(invoke_anchor, invoke_new, 1)

acceptance_path.write_text(text, encoding="utf-8")
