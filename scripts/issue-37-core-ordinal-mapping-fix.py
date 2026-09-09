from pathlib import Path

# The Core HTML renderer owns ordered-list semantics and emits the resolved
# ordinal as data-list-ordinal on each <li>.  AgentPanelSpeaker needs a hidden
# addressable text token only for speech/highlight mapping.  Add that mapping
# token locally without reparsing Markdown or changing canonical HTML semantics.

search_path = Path("AgentPanelSpeaker/TranscriptSearchIndex.cs")
text = search_path.read_text(encoding="utf-8")

regex_anchor = '''  private static readonly Regex RecordRegex = new(
    "class=\\\\\"record-anchor\\\\\"[^>]*data-jsonl-record=\\\\\"(?<record>[^\\\\\"]*)\\\\\"[^>]*data-source-id=\\\\\"(?<source>[^\\\\\"]*)\\\\\"",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
'''
regex_add = regex_anchor + '''  private static readonly Regex ListItemTagRegex = new(
    @"<li\\b[^>]*\\bdata-list-ordinal\\s*=\\s*\"(?<ordinal>-?\\d+)\"[^>]*>",
    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
'''
if "ListItemTagRegex" not in text:
  if text.count(regex_anchor) != 1:
    raise SystemExit(f"RecordRegex anchor count={text.count(regex_anchor)}")
  text = text.replace(regex_anchor, regex_add, 1)

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

call_anchor = '''  setAvailableWordMaps(wordMap || []);
  wrapWords(nodeMap || []);
'''
call_new = '''  setAvailableWordMaps(wordMap || []);
  ensureCoreOrdinalSpeechMaps();
  wrapWords(nodeMap || []);
'''
count = text.count(call_anchor)
if count != 2:
  raise SystemExit(f"word-map install anchor count={count}")
text = text.replace(call_anchor, call_new)

view_path.write_text(text, encoding="utf-8")
