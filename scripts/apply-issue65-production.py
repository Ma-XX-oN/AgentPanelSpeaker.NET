from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
  return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
  (ROOT / path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one exact match, found {count}")
  return text.replace(old, new, 1)


def regex_once(text: str, pattern: str, replacement: str, label: str,
               flags: int = re.S) -> str:
  text, count = re.subn(pattern, replacement, text, count=1, flags=flags)
  if count != 1:
    raise RuntimeError(f"{label}: expected one regex match, found {count}")
  return text


# ---------------------------------------------------------------------------
# TranscriptNodeIdentityMap.cs: recordNumber is the downstream record identity.
# ---------------------------------------------------------------------------
path = "AgentPanelSpeaker/TranscriptNodeIdentityMap.cs"
text = read(path)
text = replace_once(
  text,
  "    IReadOnlyDictionary<int, string> sourceIds = BuildCanonicalSourceIds(\n"
  "      projection.Events);\n",
  "",
  "remove canonical source-id map")
text = regex_once(
  text,
  r"      int recordNumber = sourceIndex \+ 1;\n"
  r"      string sourceId = sourceIds\.TryGetValue\(sourceIndex, out string\? canonicalId\)\n"
  r"        \? canonicalId\n"
  r"        : recordNumber\.ToString\(CultureInfo\.InvariantCulture\);\n",
  "      int recordNumber = sourceIndex + 1;\n",
  "remove per-record source-id lookup")
text = replace_once(
  text,
  "        result.Add(new TranscriptNodeIdentity(\n"
  "          nextNodeId++,\n"
  "          recordNumber,\n"
  "          sourceId,\n"
  "          IsRenderedKind(source, node.Kind)\n",
  "        result.Add(new TranscriptNodeIdentity(\n"
  "          nextNodeId++,\n"
  "          recordNumber,\n"
  "          IsRenderedKind(source, node.Kind)\n",
  "remove SourceId constructor argument")
text = regex_once(
  text,
  r"\n  /// <summary>\n"
  r"  /// Builds record-index to persistent source-ID mappings from canonical\n"
  r"  /// provenance\..*?\n  private static IReadOnlyDictionary<int, string> BuildCanonicalSourceIds\(.*?\n  }\n"
  r"(?=\n  private static IReadOnlyList<string> BuildSegments)",
  "",
  "remove BuildCanonicalSourceIds")
text = replace_once(
  text,
  "/// Associates one monitor node identifier with its canonical source provenance\n"
  "/// and ordered speakable segments.\n",
  "/// Associates one monitor node identifier with its source-file record number\n"
  "/// and ordered speakable segments.\n",
  "update identity documentation")
text = replace_once(
  text,
  "internal sealed record TranscriptNodeIdentity(\n"
  "  long NodeId,\n"
  "  int RecordNumber,\n"
  "  string SourceId,\n"
  "  IReadOnlyList<string> Segments);",
  "internal sealed record TranscriptNodeIdentity(\n"
  "  long NodeId,\n"
  "  int RecordNumber,\n"
  "  IReadOnlyList<string> Segments);",
  "simplify TranscriptNodeIdentity")
write(path, text)


# ---------------------------------------------------------------------------
# TranscriptVirtualDocument.cs: use integer record numbers for all lookups.
# ---------------------------------------------------------------------------
path = "AgentPanelSpeaker/TranscriptVirtualDocument.cs"
text = read(path)
text = regex_once(
  text,
  r"  private static readonly Regex AnchorRegex = new\(\n"
  r"    .*?RegexOptions\.Compiled \| RegexOptions\.CultureInvariant\);",
  "  private static readonly Regex AnchorRegex = new(\n"
  "    \"<span class=\\\"record-anchor\\\"[^>]*data-jsonl-record=\\\"(?<record>[^\\\"]*)\\\"[^>]*></span>\",\n"
  "    RegexOptions.Compiled | RegexOptions.CultureInvariant);",
  "simplify virtual anchor regex")
text = replace_once(
  text,
  "  private readonly Dictionary<string, int> _recordIndexes;",
  "  private readonly Dictionary<int, int> _recordIndexes;",
  "integer record-index dictionary")
text = replace_once(
  text,
  "    _recordIndexes = new Dictionary<string, int>(StringComparer.Ordinal);\n"
  "    for (int index = 0; index < records.Length; ++index)\n"
  "    {\n"
  "      foreach (TranscriptVirtualIdentity identity in records[index].Identities)\n"
  "      {\n"
  "        _recordIndexes[MakeKey(identity.RecordNumber, identity.SourceId)] = index;\n"
  "      }\n"
  "    }",
  "    _recordIndexes = new Dictionary<int, int>();\n"
  "    for (int index = 0; index < records.Length; ++index)\n"
  "    {\n"
  "      foreach (TranscriptVirtualIdentity identity in records[index].Identities)\n"
  "      {\n"
  "        _recordIndexes[identity.RecordNumber] = index;\n"
  "      }\n"
  "    }",
  "record-number index population")
text = regex_once(
  text,
  r"    var identities = new List<TranscriptVirtualIdentity>\(\);\n"
  r"    var seen = new HashSet<string>\(StringComparer\.Ordinal\);\n"
  r"    foreach \(CanonicalHtmlSourceProjection source in unit\.Source \?\?\n"
  r"        Array\.Empty<CanonicalHtmlSourceProjection>\(\)\)\n"
  r"    \{\n"
  r"      if \(source\.RecordIndex is not int sourceIndex \|\| sourceIndex < 0\)\n"
  r"      \{\n"
  r"        continue;\n"
  r"      \}\n"
  r"      int recordNumber = sourceIndex \+ 1;\n"
  r"      string sourceId = string\.IsNullOrWhiteSpace\(source\.RecordId\)\n"
  r"        \? recordNumber\.ToString\(CultureInfo\.InvariantCulture\)\n"
  r"        : source\.RecordId;\n"
  r"      if \(seen\.Add\(MakeKey\(recordNumber, sourceId\)\)\)\n"
  r"      \{\n"
  r"        identities\.Add\(new TranscriptVirtualIdentity\(\n"
  r"          recordNumber,\n"
  r"          sourceId\)\);\n"
  r"      \}\n"
  r"    \}\n\n"
  r"    TranscriptVirtualIdentity primary = identities\.Count == 0\n"
  r"      \? new TranscriptVirtualIdentity\(0, string\.Empty\)\n"
  r"      : identities\[0\];",
  "    var identities = new List<TranscriptVirtualIdentity>();\n"
  "    var seen = new HashSet<int>();\n"
  "    foreach (CanonicalHtmlSourceProjection source in unit.Source ??\n"
  "        Array.Empty<CanonicalHtmlSourceProjection>())\n"
  "    {\n"
  "      if (source.RecordIndex is not int sourceIndex || sourceIndex < 0)\n"
  "      {\n"
  "        continue;\n"
  "      }\n"
  "      int recordNumber = sourceIndex + 1;\n"
  "      if (seen.Add(recordNumber))\n"
  "      {\n"
  "        identities.Add(new TranscriptVirtualIdentity(recordNumber));\n"
  "      }\n"
  "    }\n\n"
  "    TranscriptVirtualIdentity primary = identities.Count == 0\n"
  "      ? new TranscriptVirtualIdentity(0)\n"
  "      : identities[0];",
  "simplify Core-unit identities")
text = text.replace(
  "      primary.RecordNumber,\n      primary.SourceId,\n      html,",
  "      primary.RecordNumber,\n      html,")
text = text.replace(
  "          0,\n          string.Empty,\n          html,",
  "          0,\n          html,")
text = text.replace(
  "        ? new TranscriptVirtualIdentity(0, string.Empty)\n",
  "        ? new TranscriptVirtualIdentity(0)\n")
text = text.replace(
  "        primary.RecordNumber,\n        primary.SourceId,\n        recordHtml,",
  "        primary.RecordNumber,\n        recordHtml,")
text = replace_once(
  text,
  "  public bool TryGetIndex(int recordNumber, string sourceId, out int index)\n"
  "  {\n"
  "    return _recordIndexes.TryGetValue(MakeKey(recordNumber, sourceId), out index);\n"
  "  }",
  "  public bool TryGetIndex(int recordNumber, out int index)\n"
  "  {\n"
  "    return _recordIndexes.TryGetValue(recordNumber, out index);\n"
  "  }",
  "simplify TryGetIndex")
text = replace_once(
  text,
  "  public bool IsVisible(int recordNumber, string sourceId)\n"
  "  {\n"
  "    return TryGetIndex(recordNumber, sourceId, out int index) &&\n"
  "      IsVisible(index);\n"
  "  }",
  "  public bool IsVisible(int recordNumber)\n"
  "  {\n"
  "    return TryGetIndex(recordNumber, out int index) && IsVisible(index);\n"
  "  }",
  "simplify IsVisible")
text = replace_once(
  text,
  "    string sourceId = WebUtility.HtmlDecode(anchor.Groups[\"source\"].Value);\n"
  "    return new TranscriptVirtualIdentity(recordNumber, sourceId);",
  "    return new TranscriptVirtualIdentity(recordNumber);",
  "simplify ReadIdentity")
text = regex_once(
  text,
  r"\n  private static string MakeKey\(int recordNumber, string sourceId\)\n"
  r"  \{\n"
  r"    return sourceId \+ \"\\0\" \+ recordNumber\.ToString\(CultureInfo\.InvariantCulture\);\n"
  r"  \}\n",
  "\n",
  "remove virtual MakeKey")
text = replace_once(
  text,
  "internal sealed record TranscriptVirtualIdentity(\n"
  "  int RecordNumber,\n"
  "  string SourceId);",
  "internal sealed record TranscriptVirtualIdentity(int RecordNumber);",
  "simplify TranscriptVirtualIdentity")
text = replace_once(
  text,
  "internal sealed record TranscriptVirtualRecord(\n"
  "  int RecordNumber,\n"
  "  string SourceId,\n"
  "  string Html,",
  "internal sealed record TranscriptVirtualRecord(\n"
  "  int RecordNumber,\n"
  "  string Html,",
  "simplify TranscriptVirtualRecord")
write(path, text)


# ---------------------------------------------------------------------------
# TranscriptSearchIndex.cs: keep search in C# and return direct coordinates.
# ---------------------------------------------------------------------------
path = "AgentPanelSpeaker/TranscriptSearchIndex.cs"
text = read(path)
text = regex_once(
  text,
  r"  private static readonly Regex RecordRegex = new\(\n"
  r"    .*?RegexOptions\.Compiled \| RegexOptions\.CultureInvariant\);",
  "  private static readonly Regex RecordRegex = new(\n"
  "    \"class=\\\"record-anchor\\\"[^>]*data-jsonl-record=\\\"(?<record>[^\\\"]*)\\\"\",\n"
  "    RegexOptions.Compiled | RegexOptions.CultureInvariant);",
  "simplify search anchor regex")
text = replace_once(
  text,
  "  private readonly SearchRecord[] _allRecords;\n"
  "  private readonly SearchRecord[] _voicedRecords;\n"
  "  private readonly Dictionary<string, TranscriptRecordWordMap> _wordMaps;\n"
  "  private readonly Dictionary<long, TranscriptWordMap> _wordsById;\n\n"
  "  private TranscriptSearchIndex(\n"
  "    SearchRecord[] allRecords,\n"
  "    SearchRecord[] voicedRecords,\n"
  "    Dictionary<string, TranscriptRecordWordMap> wordMaps)\n"
  "  {\n"
  "    _allRecords = allRecords;\n"
  "    _voicedRecords = voicedRecords;\n"
  "    _wordMaps = wordMaps;\n"
  "    _wordsById = wordMaps.Values\n"
  "      .SelectMany(record => record.Words)\n"
  "      .ToDictionary(word => word.WordId);\n"
  "  }",
  "  private readonly SearchRecord[] _allRecords;\n"
  "  private readonly SearchRecord[] _voicedRecords;\n\n"
  "  private TranscriptSearchIndex(\n"
  "    SearchRecord[] allRecords,\n"
  "    SearchRecord[] voicedRecords)\n"
  "  {\n"
  "    _allRecords = allRecords;\n"
  "    _voicedRecords = voicedRecords;\n"
  "  }",
  "remove global word maps")
text = text.replace("    string sourceId = string.Empty;\n", "")
text = text.replace(
  "          sourceId = WebUtility.HtmlDecode(anchor.Groups[\"source\"].Value);\n",
  "")
text = replace_once(
  text,
  "        int localIndex = tokens.Count == 0 ||\n"
  "          tokens[^1].RecordNumber != recordNumber ||\n"
  "          !string.Equals(tokens[^1].SourceId, sourceId, StringComparison.Ordinal)\n"
  "            ? 0\n"
  "            : tokens[^1].RecordWordIndex + 1;\n"
  "        tokens.Add(new MutableToken(\n"
  "          token.Value,\n"
  "          recordNumber,\n"
  "          sourceId,\n"
  "          blockId,\n"
  "          tokens.Count,\n"
  "          localIndex,\n"
  "          spaceBefore));",
  "        int localIndex = tokens.Count == 0 ||\n"
  "          tokens[^1].RecordNumber != recordNumber\n"
  "            ? 0\n"
  "            : tokens[^1].RecordWordIndex + 1;\n"
  "        tokens.Add(new MutableToken(\n"
  "          token.Value,\n"
  "          recordNumber,\n"
  "          blockId,\n"
  "          localIndex,\n"
  "          spaceBefore));",
  "simplify search token construction")
text = replace_once(
  text,
  "    Dictionary<string, TranscriptRecordWordMap> wordMaps = BuildWordMaps(tokens);\n"
  "    return new TranscriptSearchIndex(allRecords, voicedRecords, wordMaps);",
  "    return new TranscriptSearchIndex(allRecords, voicedRecords);",
  "remove word-map build")
text = replace_once(
  text,
  "    out int recordNumber,\n"
  "    out string sourceId,\n"
  "    out int recordWordIndex)",
  "    out int recordNumber,\n"
  "    out int recordWordIndex)",
  "simplify voice origin signature")
text = replace_once(
  text,
  "          recordNumber = token.RecordNumber;\n"
  "          sourceId = token.SourceId;\n"
  "          recordWordIndex = token.RecordWordIndex;",
  "          recordNumber = token.RecordNumber;\n"
  "          recordWordIndex = token.RecordWordIndex;",
  "simplify voice origin result")
text = text.replace("    sourceId = string.Empty;\n", "")
text = regex_once(
  text,
  r"\n  /// <summary>\n"
  r"  /// Returns the stable word identities for the requested virtual records\..*?\n"
  r"  public bool TryResolveSpeechWord\(.*?\n  }\n"
  r"(?=\n  /// <summary>\n  /// Finds matches without touching the WebView DOM\.)",
  "",
  "remove search word-map and WordId resolver")
text = regex_once(
  text,
  r"      long\[\] wordIds = tokens\[first\.\.\(last \+ 1\)\]\n"
  r"        \.Select\(token => token\.WordId\)\n"
  r"        \.ToArray\(\);\n"
  r"      long seekWordId = voiced\.NodeId > 0 && voiced\.NodeWordIndex >= 0\n"
  r"        \? voiced\.WordId\n"
  r"        : 0;\n",
  "",
  "remove match WordIds")
text = replace_once(
  text,
  "      result.Add(new TranscriptSearchMatch(\n"
  "        result.Count + 1,\n"
  "        firstToken.RecordNumber,\n"
  "        firstToken.SourceId,\n"
  "        firstToken.RecordWordIndex,\n"
  "        tokens[last].RecordWordIndex,\n"
  "        wordIds,\n"
  "        seekWordId,\n"
  "        voiced.NodeId,\n"
  "        voiced.NodeWordIndex));",
  "      result.Add(new TranscriptSearchMatch(\n"
  "        result.Count + 1,\n"
  "        firstToken.RecordNumber,\n"
  "        firstToken.RecordWordIndex,\n"
  "        tokens[last].RecordWordIndex,\n"
  "        voiced.NodeId,\n"
  "        voiced.NodeWordIndex));",
  "simplify search match")
text = replace_once(
  text,
  "    foreach (IGrouping<string, MutableToken> group in source\n"
  "      .Where(token => !voicedOnly || token.NodeId > 0)\n"
  "      .GroupBy(token => token.RecordKey, StringComparer.Ordinal))",
  "    foreach (IGrouping<int, MutableToken> group in source\n"
  "      .Where(token => !voicedOnly || token.NodeId > 0)\n"
  "      .GroupBy(token => token.RecordNumber))",
  "group search corpus by record number")
text = replace_once(
  text,
  "        tokens.Add(new SearchToken(\n"
  "          start,\n"
  "          builder.Length,\n"
  "          token.WordId,\n"
  "          token.RecordNumber,\n"
  "          token.SourceId,\n"
  "          token.RecordWordIndex,\n"
  "          token.NodeId,\n"
  "          token.NodeWordIndex));",
  "        tokens.Add(new SearchToken(\n"
  "          start,\n"
  "          builder.Length,\n"
  "          token.RecordNumber,\n"
  "          token.RecordWordIndex,\n"
  "          token.NodeId,\n"
  "          token.NodeWordIndex));",
  "simplify SearchToken")
text = regex_once(
  text,
  r"\n  private static Dictionary<string, TranscriptRecordWordMap> BuildWordMaps\(.*?\n  }\n"
  r"(?=\n  private static void PopThroughTag)",
  "",
  "remove BuildWordMaps")
text = replace_once(
  text,
  "    var cursors = new Dictionary<string, int>(StringComparer.Ordinal);\n"
  "    foreach (TranscriptNodeIdentity identity in identities)\n"
  "    {\n"
  "      cancellationToken.ThrowIfCancellationRequested();\n"
  "      string key = MakeKey(identity.RecordNumber, identity.SourceId);\n"
  "      int cursor = cursors.TryGetValue(key, out int value) ? value : 0;",
  "    var cursors = new Dictionary<int, int>();\n"
  "    foreach (TranscriptNodeIdentity identity in identities)\n"
  "    {\n"
  "      cancellationToken.ThrowIfCancellationRequested();\n"
  "      int recordNumber = identity.RecordNumber;\n"
  "      int cursor = cursors.TryGetValue(recordNumber, out int value) ? value : 0;",
  "simplify voiced-token cursor key")
text = text.replace(
  "        int start = FindTokenSequence(tokens, key, target, cursor);",
  "        int start = FindTokenSequence(tokens, recordNumber, target, cursor);")
text = text.replace(
  "          start = FindTokenSequence(tokens, key, target, 0);",
  "          start = FindTokenSequence(tokens, recordNumber, target, 0);")
text = text.replace("        cursors[key] = cursor;", "        cursors[recordNumber] = cursor;")
text = replace_once(
  text,
  "    string key,\n"
  "    IReadOnlyList<string> target,",
  "    int recordNumber,\n"
  "    IReadOnlyList<string> target,",
  "simplify FindTokenSequence signature")
text = text.replace(
  "      if (!string.Equals(tokens[index].RecordKey, key, StringComparison.Ordinal))\n"
  "      {\n"
  "        continue;\n"
  "      }",
  "      if (tokens[index].RecordNumber != recordNumber)\n"
  "      {\n"
  "        continue;\n"
  "      }")
text = text.replace(
  "        if (!string.Equals(candidate.RecordKey, key, StringComparison.Ordinal) ||\n"
  "            !string.Equals(\n",
  "        if (candidate.RecordNumber != recordNumber ||\n"
  "            !string.Equals(\n")
text = regex_once(
  text,
  r"\n  private static string MakeKey\(int recordNumber, string sourceId\)\n"
  r"  \{\n"
  r"    return sourceId \+ \"\\0\" \+ recordNumber\.ToString\(CultureInfo\.InvariantCulture\);\n"
  r"  \}\n",
  "\n",
  "remove search MakeKey")
text = replace_once(
  text,
  "    public MutableToken(\n"
  "      string text,\n"
  "      int recordNumber,\n"
  "      string sourceId,\n"
  "      int blockId,\n"
  "      int renderedIndex,\n"
  "      int recordWordIndex,\n"
  "      bool spaceBefore)\n"
  "    {\n"
  "      Text = text;\n"
  "      RecordNumber = recordNumber;\n"
  "      SourceId = sourceId;\n"
  "      RecordKey = MakeKey(recordNumber, sourceId);\n"
  "      BlockId = blockId;\n"
  "      RenderedIndex = renderedIndex;\n"
  "      WordId = renderedIndex + 1L;\n"
  "      RecordWordIndex = recordWordIndex;\n"
  "      SpaceBefore = spaceBefore;\n"
  "    }\n\n"
  "    public string Text { get; }\n"
  "    public int RecordNumber { get; }\n"
  "    public string SourceId { get; }\n"
  "    public string RecordKey { get; }\n"
  "    public int BlockId { get; }\n"
  "    public int RenderedIndex { get; }\n"
  "    public long WordId { get; }\n",
  "    public MutableToken(\n"
  "      string text,\n"
  "      int recordNumber,\n"
  "      int blockId,\n"
  "      int recordWordIndex,\n"
  "      bool spaceBefore)\n"
  "    {\n"
  "      Text = text;\n"
  "      RecordNumber = recordNumber;\n"
  "      BlockId = blockId;\n"
  "      RecordWordIndex = recordWordIndex;\n"
  "      SpaceBefore = spaceBefore;\n"
  "    }\n\n"
  "    public string Text { get; }\n"
  "    public int RecordNumber { get; }\n"
  "    public int BlockId { get; }\n",
  "simplify MutableToken")
text = replace_once(
  text,
  "  private readonly record struct SearchToken(\n"
  "    int Start,\n"
  "    int End,\n"
  "    long WordId,\n"
  "    int RecordNumber,\n"
  "    string SourceId,\n"
  "    int RecordWordIndex,\n"
  "    long NodeId,\n"
  "    int NodeWordIndex);",
  "  private readonly record struct SearchToken(\n"
  "    int Start,\n"
  "    int End,\n"
  "    int RecordNumber,\n"
  "    int RecordWordIndex,\n"
  "    long NodeId,\n"
  "    int NodeWordIndex);",
  "simplify SearchToken record")
text = replace_once(
  text,
  "internal sealed record TranscriptSearchMatch(\n"
  "  int FileOrdinal,\n"
  "  int RecordNumber,\n"
  "  string SourceId,\n"
  "  int StartWordIndex,\n"
  "  int EndWordIndex,\n"
  "  IReadOnlyList<long> WordIds,\n"
  "  long SeekWordId,\n"
  "  long NodeId,\n"
  "  int NodeWordIndex);\n\n"
  "internal sealed record TranscriptRecordWordMap(\n"
  "  int RecordNumber,\n"
  "  string SourceId,\n"
  "  IReadOnlyList<TranscriptWordMap> Words);\n\n"
  "internal sealed record TranscriptWordMap(\n"
  "  long WordId,\n"
  "  long NodeId,\n"
  "  int NodeWordIndex);",
  "internal sealed record TranscriptSearchMatch(\n"
  "  int FileOrdinal,\n"
  "  int RecordNumber,\n"
  "  int StartWordIndex,\n"
  "  int EndWordIndex,\n"
  "  long NodeId,\n"
  "  int NodeWordIndex);",
  "simplify search result contract")
write(path, text)


# ---------------------------------------------------------------------------
# TranscriptStructureProbe.cs: diagnostics key records by recordNumber too.
# ---------------------------------------------------------------------------
path = "AgentPanelSpeaker/TranscriptStructureProbe.cs"
text = read(path)
text = regex_once(
  text,
  r"  private static readonly Regex RecordAnchorRegex = new\(\n"
  r"    .*?RegexOptions\.Compiled \| RegexOptions\.CultureInvariant \| RegexOptions\.IgnoreCase\);",
  "  private static readonly Regex RecordAnchorRegex = new(\n"
  "    \"<span\\\\s+class=\\\\\"record-anchor\\\\\"[^>]*\" +\n"
  "    \"data-jsonl-record=\\\\\"(?<record>[^\\\\\"]*)\\\\\"[^>]*></span>\",\n"
  "    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);",
  "simplify structure anchor regex")
text = text.replace(
  "      string sourceId = WebUtility.HtmlDecode(anchor.Groups[\"source\"].Value);\n",
  "")
text = text.replace(
  "        recordNumber,\n        sourceId,\n        turnId,",
  "        recordNumber,\n        turnId,")
text = text.replace(
  "        .OrderBy(entry => entry.RecordNumber)\n        .ThenBy(entry => entry.SourceId, StringComparer.Ordinal)\n",
  "        .OrderBy(entry => entry.RecordNumber)\n")
text = text.replace("        left.SourceId,\n", "")
text = text.replace(
  "            sourceId: anchor.getAttribute('data-source-id') || '',\n",
  "")
text = text.replace(
  "          ReadInt32(item, \"recordNumber\"),\n          ReadString(item, \"sourceId\"),\n          ReadString(item, \"turnId\"),",
  "          ReadInt32(item, \"recordNumber\"),\n          ReadString(item, \"turnId\"),")
text = regex_once(
  text,
  r"        string sourceId = ReadString\(source, \"record_id\"\);\n"
  r"        if \(sourceId\.Length == 0\)\n"
  r"        \{\n"
  r"          sourceId = recordNumber\.ToString\(CultureInfo\.InvariantCulture\);\n"
  r"        \}\n",
  "",
  "remove structure source id")
text = text.replace(
  "          recordNumber,\n          sourceId,\n          turnId.Length == 0 ? string.Empty : \"presentation:\" + turnId,",
  "          recordNumber,\n          turnId.Length == 0 ? string.Empty : \"presentation:\" + turnId,")
text = text.replace("        entry.SourceId,\n", "")
text = regex_once(
  text,
  r"  private static string EntryKey\(TranscriptStructureEntry entry\)\n"
  r"  \{\n"
  r"    return entry\.SourceId \+ \"\\0\" \+\n"
  r"      entry\.RecordNumber\.ToString\(CultureInfo\.InvariantCulture\);\n"
  r"  \}",
  "  private static string EntryKey(TranscriptStructureEntry entry)\n"
  "  {\n"
  "    return entry.RecordNumber.ToString(CultureInfo.InvariantCulture);\n"
  "  }",
  "simplify structure key")
text = replace_once(
  text,
  "internal sealed record TranscriptStructureEntry(\n"
  "  int RecordNumber,\n"
  "  string SourceId,\n"
  "  string TurnId,",
  "internal sealed record TranscriptStructureEntry(\n"
  "  int RecordNumber,\n"
  "  string TurnId,",
  "simplify structure entry")
write(path, text)


# ---------------------------------------------------------------------------
# TranscriptView.cs: remove sourceId lookup and global word-map transfer layer.
# ---------------------------------------------------------------------------
path = "AgentPanelSpeaker/TranscriptView.cs"
text = read(path)
text = replace_once(text, "    public int WordMapCount { get; set; }\n", "", "remove WordMapCount")
text = replace_once(text, "    public int WordCount { get; set; }\n", "", "remove WordCount")
text = replace_once(text, "    public long WordMapMilliseconds { get; set; }\n", "", "remove WordMapMilliseconds")
text = replace_once(text, "    string OriginSourceId,\n", "", "remove pending origin source")
text = replace_once(
  text,
  "        document.TryGetIndex(\n"
  "          identity.RecordNumber,\n"
  "          identity.SourceId,\n"
  "          out int index) &&",
  "        document.TryGetIndex(identity.RecordNumber, out int index) &&",
  "playback record lookup")
text = replace_once(
  text,
  "      _ = RenderWindowForRecordAsync(\n"
  "        identity.RecordNumber,\n"
  "        identity.SourceId,\n"
  "        \"playback-position\",",
  "      _ = RenderWindowForRecordAsync(\n"
  "        identity.RecordNumber,\n"
  "        \"playback-position\",",
  "playback window request")
text = replace_once(
  text,
  "      _searchIndex = index;\n"
  "      await InstallCurrentWindowSearchMapsAsync(\n"
  "        index,\n"
  "        generation,\n"
  "        path,\n"
  "        cancellation.Token);\n"
  "      cancellation.Token.ThrowIfCancellationRequested();\n",
  "      _searchIndex = index;\n"
  "      cancellation.Token.ThrowIfCancellationRequested();\n",
  "remove deferred word-map installation")
text = regex_once(
  text,
  r"\n  /// <summary>\n"
  r"  /// Installs search-owned stable word IDs into the current browser window.*?\n"
  r"  private async Task InstallCurrentWindowSearchMapsAsync\(.*?\n  }\n"
  r"(?=\n  private void CancelSearchIndexBuild)",
  "",
  "remove InstallCurrentWindowSearchMapsAsync")
# Web message protocol source/word-id removal.
text = regex_once(
  text,
  r"      if \(type == \"stable-word-map-failure\"\)\n"
  r"      \{.*?\n      \}\n",
  "",
  "remove stable word-map failure handler")
for line in [
  "          firstWordId = ReadOptionalString(root, \"firstWordId\"),\n",
  "          seekWordId = ReadOptionalString(root, \"seekWordId\"),\n",
  "          expectedWordCount = ReadOptionalInt32(root, \"expectedWordCount\"),\n",
  "          resolvedWordCount = ReadOptionalInt32(root, \"resolvedWordCount\"),\n",
]:
  text = text.replace(line, "")
text = text.replace(
  "            ReadOptionalInt32(root, \"anchorRecordNumber\"),\n"
  "            ReadOptionalString(root, \"anchorSourceId\"),\n"
  "            ReadOptionalDouble(root, \"anchorOffset\"),",
  "            ReadOptionalInt32(root, \"anchorRecordNumber\"),\n"
  "            ReadOptionalDouble(root, \"anchorOffset\"),")
text = replace_once(
  text,
  "        string sourceId = ReadOptionalString(root, \"sourceId\");\n"
  "        if (recordNumber is int validRecordNumber)\n",
  "        if (recordNumber is int validRecordNumber)\n",
  "remove window-request source")
text = text.replace(
  "          _ = RenderWindowForRecordAsync(\n"
  "            validRecordNumber,\n"
  "            sourceId,\n"
  "            ReadOptionalString(root, \"reason\"),",
  "          _ = RenderWindowForRecordAsync(\n"
  "            validRecordNumber,\n"
  "            ReadOptionalString(root, \"reason\"),")
# Remove find-seek WordId fallback, preserving direct coordinates only.
text = regex_once(
  text,
  r"        long\? wordId = ReadOptionalInt64\(root, \"wordId\"\);\n"
  r"        TranscriptSearchIndex\? searchIndex = _searchIndex;\n"
  r"        if \(wordId is long validWordId &&.*?\n        \}\n"
  r"        return;",
  "        return;",
  "remove WordId seek fallback")
# Find request/origin C#.
text = replace_once(
  text,
  "      ReadOptionalInt32(root, \"originRecordNumber\") ?? 0,\n"
  "      ReadOptionalString(root, \"originSourceId\"),\n"
  "      ReadOptionalInt32(root, \"originWordIndex\") ?? -1);",
  "      ReadOptionalInt32(root, \"originRecordNumber\") ?? 0,\n"
  "      ReadOptionalInt32(root, \"originWordIndex\") ?? -1);",
  "simplify pending find request")
text = text.replace("    string originSourceId = pending.OriginSourceId;\n", "")
text = replace_once(
  text,
  "          out int voiceRecordNumber,\n"
  "          out string voiceSourceId,\n"
  "          out int voiceRecordWordIndex))\n"
  "    {\n"
  "      originRecordNumber = voiceRecordNumber;\n"
  "      originSourceId = voiceSourceId;\n"
  "      originWordIndex = voiceRecordWordIndex;",
  "          out int voiceRecordNumber,\n"
  "          out int voiceRecordWordIndex))\n"
  "    {\n"
  "      originRecordNumber = voiceRecordNumber;\n"
  "      originWordIndex = voiceRecordWordIndex;",
  "simplify voice find origin")
text = text.replace(
  "        matches = matches.Where(match => visibleDocument.IsVisible(\n"
  "          match.RecordNumber,\n"
  "          match.SourceId)).ToArray();",
  "        matches = matches.Where(match =>\n"
  "          visibleDocument.IsVisible(match.RecordNumber)).ToArray();")
text = text.replace(
  "        originRecordNumber,\n        originSourceId,\n        originWordIndex);",
  "        originRecordNumber,\n        originWordIndex);")
text = text.replace("        originSourceId,\n", "")
text = replace_once(
  text,
  "    int recordNumber,\n"
  "    string sourceId,\n"
  "    int wordIndex)",
  "    int recordNumber,\n"
  "    int wordIndex)",
  "simplify RotateMatchesAfterOrigin signature")
text = replace_once(
  text,
  "      bool sameSource = string.Equals(\n"
  "        match.SourceId,\n"
  "        sourceId,\n"
  "        StringComparison.Ordinal);\n"
  "      if (match.RecordNumber > recordNumber ||\n"
  "          (match.RecordNumber == recordNumber && sameSource &&\n"
  "           match.StartWordIndex > wordIndex))",
  "      if (match.RecordNumber > recordNumber ||\n"
  "          (match.RecordNumber == recordNumber &&\n"
  "           match.StartWordIndex > wordIndex))",
  "rotate matches by record only")
# Initial/playback record lookup helpers.
text = text.replace(
  "      document.TryGetIndex(identity.RecordNumber, identity.SourceId, out index);",
  "      document.TryGetIndex(identity.RecordNumber, out index);")
# BuildReplaceDomScript identities, no word map.
text = regex_once(
  text,
  r"    var keys = window\.Records\n"
  r"      \.SelectMany\(record => record\.Identities\.Count != 0\n"
  r"        \? record\.Identities\n"
  r"        : new\[\]\n"
  r"        \{\n"
  r"          new TranscriptVirtualIdentity\(record\.RecordNumber, record\.SourceId\)\n"
  r"        \}\)\n"
  r"      \.Select\(identity => identity\.SourceId \+ \"\\0\" \+ identity\.RecordNumber\)\n"
  r"      \.ToHashSet\(StringComparer\.Ordinal\);\n"
  r"    IReadOnlyList<TranscriptNodeIdentity> identities = _identities\n"
  r"      \.Where\(identity => keys\.Contains\(\n"
  r"        identity\.SourceId \+ \"\\0\" \+ identity\.RecordNumber\)\)\n"
  r"      \.ToArray\(\);\n"
  r"    IReadOnlyList<TranscriptRecordWordMap> wordMaps = _searchIndex\?\.GetWordMaps\(\n"
  r"      window\.Records\) \?\? Array\.Empty<TranscriptRecordWordMap>\(\);",
  "    HashSet<int> keys = window.Records\n"
  "      .SelectMany(record => record.Identities.Count != 0\n"
  "        ? record.Identities\n"
  "        : new[] { new TranscriptVirtualIdentity(record.RecordNumber) })\n"
  "      .Select(identity => identity.RecordNumber)\n"
  "      .ToHashSet();\n"
  "    IReadOnlyList<TranscriptNodeIdentity> identities = _identities\n"
  "      .Where(identity => keys.Contains(identity.RecordNumber))\n"
  "      .ToArray();",
  "simplify DOM identity selection")
text = replace_once(
  text,
  "      JsonSerializer.Serialize(preserve) + \",\" +\n"
  "      JsonSerializer.Serialize(identities) + \",\" +\n"
  "      JsonSerializer.Serialize(wordMaps) + \",\" +\n"
  "      JsonSerializer.Serialize(expectedStructure?.Entries ??",
  "      JsonSerializer.Serialize(preserve) + \",\" +\n"
  "      JsonSerializer.Serialize(identities) + \",\" +\n"
  "      JsonSerializer.Serialize(expectedStructure?.Entries ??",
  "remove DOM word map serialization")
# Window builder signatures and implementation.
text = text.replace("    string? anchorSourceId = null,\n", "")
text = text.replace("      anchorSourceId,\n", "")
text = regex_once(
  text,
  r"    var keys = window\.Records\n"
  r"      \.SelectMany\(record => record\.Identities\.Count != 0\n"
  r"        \? record\.Identities\n"
  r"        : new\[\]\n"
  r"        \{\n"
  r"          new TranscriptVirtualIdentity\(record\.RecordNumber, record\.SourceId\)\n"
  r"        \}\)\n"
  r"      \.Select\(identity => identity\.SourceId \+ \"\\0\" \+ identity\.RecordNumber\)\n"
  r"      \.ToHashSet\(StringComparer\.Ordinal\);\n"
  r"    IReadOnlyList<TranscriptNodeIdentity> identities = _identities\n"
  r"      \.Where\(identity => keys\.Contains\(identity\.SourceId \+ \"\\0\" \+ identity\.RecordNumber\)\)\n"
  r"      \.ToArray\(\);",
  "    HashSet<int> keys = window.Records\n"
  "      .SelectMany(record => record.Identities.Count != 0\n"
  "        ? record.Identities\n"
  "        : new[] { new TranscriptVirtualIdentity(record.RecordNumber) })\n"
  "      .Select(identity => identity.RecordNumber)\n"
  "      .ToHashSet();\n"
  "    IReadOnlyList<TranscriptNodeIdentity> identities = _identities\n"
  "      .Where(identity => keys.Contains(identity.RecordNumber))\n"
  "      .ToArray();",
  "simplify window identity selection")
text = regex_once(
  text,
  r"\n    var wordMapTimer = Stopwatch\.StartNew\(\);\n"
  r"    IReadOnlyList<TranscriptRecordWordMap> wordMaps = _searchIndex\?\.GetWordMaps\(\n"
  r"      window\.Records\) \?\? Array\.Empty<TranscriptRecordWordMap>\(\);\n"
  r"    if \(metrics is not null\)\n"
  r"    \{\n"
  r"      metrics\.WordMapCount = wordMaps\.Count;\n"
  r"      metrics\.WordCount = wordMaps\.Sum\(record => record\.Words\.Count\);\n"
  r"      metrics\.WordMapMilliseconds = wordMapTimer\.ElapsedMilliseconds;\n"
  r"    \}\n",
  "\n",
  "remove window word-map lookup")
text = replace_once(
  text,
  "      JsonSerializer.Serialize(preserve) + \",\" +\n"
  "      JsonSerializer.Serialize(identities) + \",\" +\n"
  "      JsonSerializer.Serialize(wordMaps) + \",\" +\n"
  "      window.StartIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + \",\" +",
  "      JsonSerializer.Serialize(preserve) + \",\" +\n"
  "      JsonSerializer.Serialize(identities) + \",\" +\n"
  "      window.StartIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + \",\" +",
  "remove window word-map serialization")
text = text.replace("      JsonSerializer.Serialize(anchorSourceId) + \",\" +\n", "")
# C# render APIs use only recordNumber.
text = text.replace(
  "  private async Task RenderWindowForRecordAsync(\n"
  "    int recordNumber,\n"
  "    string sourceId,\n"
  "    string reason,",
  "  private async Task RenderWindowForRecordAsync(\n"
  "    int recordNumber,\n"
  "    string reason,")
text = text.replace(
  "        !document.TryGetIndex(recordNumber, sourceId, out int focalIndex))",
  "        !document.TryGetIndex(recordNumber, out int focalIndex))")
text = text.replace("        sourceId,\n        navigationGeneration,\n", "        navigationGeneration,\n")
text = text.replace("    string anchorSourceId,\n", "")
text = text.replace("        anchorSourceId: anchorSourceId,\n", "")
text = text.replace(
  "          identity.RecordNumber,\n          identity.SourceId,\n          reason,",
  "          identity.RecordNumber,\n          reason,")
# Remove obsolete word-map metrics from diagnostics.
for line in [
  "        wordMapCount = scriptMetrics.WordMapCount,\n",
  "        wordCount = scriptMetrics.WordCount,\n",
  "        wordMapMilliseconds = scriptMetrics.WordMapMilliseconds,\n",
]:
  text = text.replace(line, "")
# JS global structures and helper functions.
text = text.replace("let displayWordsById = new Map();\n", "")
text = text.replace("let availableWordMapsByRecord = new Map();\n", "")
text = replace_once(
  text,
  "    const sourceId = String(item.SourceId ?? item.sourceId ?? '');\n"
  "    result.add(makeRecordKey(recordNumber, sourceId));",
  "    result.add(makeRecordKey(recordNumber));",
  "nodeRecordKeys record-only")
text = regex_once(
  text,
  r"\nfunction setAvailableWordMaps\(wordMap\) \{.*?\n\}\n"
  r"(?=\nfunction ensureCoreOrdinalSpeechMaps)",
  "",
  "remove setAvailableWordMaps")
text = replace_once(
  text,
  "        currentKey = makeRecordKey(\n"
  "          String(element.dataset.jsonlRecord || ''),\n"
  "          element.dataset.sourceId || '');",
  "        currentKey = makeRecordKey(\n"
  "          String(element.dataset.jsonlRecord || ''));",
  "word wrapping record key")
# Structure JS keys and payloads by recordNumber.
text = text.replace("    const sourceId = String(entry.SourceId ?? entry.sourceId ?? '');\n", "")
text = text.replace("    result.set(sourceId + '\\u0000' + recordNumber, {\n", "    result.set(String(recordNumber), {\n")
text = text.replace("      sourceId,\n", "")
text = text.replace("      sourceId: anchor.getAttribute('data-source-id') || '',\n", "")
text = text.replace("        sourceId: left.sourceId,\n", "")
text = text.replace("        sourceId: right.sourceId,\n", "")
text = replace_once(
  text,
  "function structureAnchorSelector(recordNumber, sourceId) {\n"
  "  return '.record-anchor[data-jsonl-record=\"' +\n"
  "    CSS.escape(String(recordNumber)) + '\"][data-source-id=\"' +\n"
  "    CSS.escape(String(sourceId || '')) + '\"]';\n"
  "}",
  "function structureAnchorSelector(recordNumber) {\n"
  "  return '.record-anchor[data-jsonl-record=\"' +\n"
  "    CSS.escape(String(recordNumber)) + '\"]';\n"
  "}",
  "structure selector record-only")
text = replace_once(
  text,
  "function inputContextForRecord(html, recordNumber, sourceId) {\n"
  "  const recordNeedle = 'data-jsonl-record=\"' + String(recordNumber) + '\"';\n"
  "  const sourceNeedle = 'data-source-id=\"' + String(sourceId || '') + '\"';\n"
  "  let index = html.indexOf(sourceNeedle);\n"
  "  if (index < 0) index = html.indexOf(recordNeedle);",
  "function inputContextForRecord(html, recordNumber) {\n"
  "  const recordNeedle = 'data-jsonl-record=\"' + String(recordNumber) + '\"';\n"
  "  let index = html.indexOf(recordNeedle);",
  "structure input context record-only")
text = text.replace("function domContextForRecord(recordNumber, sourceId) {", "function domContextForRecord(recordNumber) {")
text = text.replace("    structureAnchorSelector(recordNumber, sourceId));", "    structureAnchorSelector(recordNumber));")
text = text.replace("    sourceId: diff.sourceId,\n", "")
text = text.replace("      diff.recordNumber,\n      diff.sourceId),", "      diff.recordNumber),")
text = text.replace("    domContext: domContextForRecord(diff.recordNumber, diff.sourceId)", "    domContext: domContextForRecord(diff.recordNumber)")
# DOM/window JS signatures and calls remove wordMap and anchor source.
text = text.replace(
  "  nodeMap,\n  wordMap,\n  expectedStructure = [],",
  "  nodeMap,\n  expectedStructure = [],")
text = text.replace("  setAvailableWordMaps(wordMap || []);\n", "")
text = text.replace("  assignStableWordScopes(wordMap || []);\n", "")
text = text.replace(
  "  nodeMap,\n  wordMap,\n  startIndex = -1,",
  "  nodeMap,\n  startIndex = -1,")
text = text.replace("  anchorSourceId = null,\n", "")
# remove stable word timing phase after node scopes
text = regex_once(
  text,
  r"  phaseStarted = performance\.now\(\);\n"
  r"  assignStableWordScopes\(wordMap \|\| \[\]\);\n"
  r"  const stableWordScopesMilliseconds = performance\.now\(\) - phaseStarted;\n",
  "",
  "remove stable-word phase")
text = text.replace("    wordMapCount:Array.isArray(wordMap) ? wordMap.length : 0,\n", "")
text = text.replace("    stableWordScopesMilliseconds:Math.round(stableWordScopesMilliseconds),\n", "")
text = text.replace(
  "function replaceTranscript(html, preserve, nodeMap, wordMap = []) {\n"
  "  replaceTranscriptWindow(\n"
  "    html, preserve, nodeMap, wordMap, -1, -1, 0, 0);\n"
  "}",
  "function replaceTranscript(html, preserve, nodeMap) {\n"
  "  replaceTranscriptWindow(html, preserve, nodeMap, -1, -1, 0, 0);\n"
  "}")
text = replace_once(
  text,
  "function makeRecordKey(recordNumber, sourceId) {\n"
  "  return sourceId + '\\u0000' + recordNumber;\n"
  "}",
  "function makeRecordKey(recordNumber) {\n"
  "  return String(recordNumber);\n"
  "}",
  "record-only browser key")
text = text.replace("  let sourceId = '';\n", "")
text = text.replace("      sourceId = element.dataset.sourceId || '';\n", "")
text = text.replace("    element.dataset.sourceId = sourceId;\n", "")
text = text.replace("    if (!recordNumber && !sourceId) continue;\n", "    if (!recordNumber) continue;\n")
text = text.replace("    const key = makeRecordKey(recordNumber, sourceId);\n", "    const key = makeRecordKey(recordNumber);\n")
# Remove stable-ID functions and replace lazy materialization with record-local wrapping.
text = regex_once(
  text,
  r"\nfunction assignStableWordScopes\(wordMap\) \{.*?\n\}\n\n"
  r"function installSearchWordMaps\(wordMap\) \{.*?\n\}\n\n"
  r"function materializeRecordWords\(recordNumber, sourceId\) \{.*?\n\}\n"
  r"(?=\nfunction findSequence)",
  "\nfunction materializeRecordWords(recordNumber) {\n"
  "  const key = makeRecordKey(recordNumber);\n"
  "  if (displayWordsByRecord.has(key)) return true;\n"
  "  const selector = '.record-anchor[data-jsonl-record=\\\"' +\n"
  "    CSS.escape(String(recordNumber)) + '\\\"]';\n"
  "  if (!transcript.querySelector(selector)) return false;\n\n"
  "  const beforeWordCount = words.length;\n"
  "  const started = performance.now();\n"
  "  wrapWordsForRecordKeys(new Set([key]), false);\n"
  "  assignRecordScopes();\n"
  "  const materialized = displayWordsByRecord.has(key);\n"
  "  chrome.webview.postMessage({\n"
  "    type:'lazy-word-materialized',\n"
  "    recordNumber:Number(recordNumber),\n"
  "    addedWordCount:words.length - beforeWordCount,\n"
  "    totalWordCount:words.length,\n"
  "    elapsedMilliseconds:Math.round(performance.now() - started),\n"
  "    materialized\n"
  "  });\n"
  "  return materialized;\n"
  "}\n",
  "replace lazy word materialization")
# findSequence no source constraint.
text = replace_once(
  text,
  "  requiredNodeId,\n"
  "  requiredRecordNumber,\n"
  "  requiredSourceId) {",
  "  requiredNodeId,\n"
  "  requiredRecordNumber) {",
  "simplify findSequence signature")
text = text.replace(
  "          (requiredRecordNumber !== null &&\n"
  "           candidate.dataset.recordNumber !== requiredRecordNumber) ||\n"
  "          (requiredSourceId !== null &&\n"
  "           candidate.dataset.sourceId !== requiredSourceId)) {",
  "          (requiredRecordNumber !== null &&\n"
  "           candidate.dataset.recordNumber !== requiredRecordNumber)) {")
# Calls had six args; remove final source null.
text = text.replace(
  "        null,\n        null,\n        null);",
  "        null,\n        null);")
text = text.replace(
  "          null,\n          null,\n          null);",
  "          null,\n          null);")
# Mapping summaries/node scopes no source/global stable counts.
text = text.replace("  const stableCounts = new Map();\n", "")
text = regex_once(
  text,
  r"    if \(word\.dataset\.wordId\) \{\n"
  r"      stableCounts\.set\(nodeId, \(stableCounts\.get\(nodeId\) \|\| 0\) \+ 1\);\n"
  r"    \}\n",
  "",
  "remove stable mapping count")
text = text.replace("    const sourceId = String(item.SourceId ?? item.sourceId ?? '');\n", "")
text = text.replace("      sourceId,\n", "")
text = text.replace("      stableWordCount: stableCounts.get(nodeId) || 0\n", "")
text = text.replace("      sourceId: word.dataset.sourceId || '',\n", "")
text = text.replace("      wordId: word.dataset.wordId || ''\n", "")
text = text.replace("    const key = makeRecordKey(recordNumber, sourceId);\n", "    const key = makeRecordKey(recordNumber);\n")
text = text.replace(
  "      const failureKey = nodeId + ':' + recordNumber + ':' +\n"
  "        sourceId + ':' + segment;",
  "      const failureKey = nodeId + ':' + recordNumber + ':' + segment;")
text = text.replace("          sourceId,\n", "")
# Find result normalization/navigation uses direct record-relative coordinates.
text = text.replace("    sourceId: String(match.SourceId ?? match.sourceId ?? ''),\n", "")
text = text.replace("    wordIds: (match.WordIds ?? match.wordIds ?? []).map(value => String(value)),\n", "")
text = text.replace("    seekWordId: String(match.SeekWordId ?? match.seekWordId ?? ''),\n", "")
text = text.replace("  const key = makeRecordKey(String(match.recordNumber), match.sourceId);", "  const key = makeRecordKey(match.recordNumber);")
text = text.replace("      materializeRecordWords(match.recordNumber, match.sourceId)) {", "      materializeRecordWords(match.recordNumber)) {")
text = text.replace("      sourceId:match.sourceId,\n", "")
text = regex_once(
  text,
  r"  const matchedWords = match\.wordIds\n"
  r"    \.map\(wordId => displayWordsById\.get\(String\(wordId\)\)\)\n"
  r"    \.filter\(word => !!word\);\n"
  r"  if \(!match\.wordIds\.length \|\| matchedWords\.length !== match\.wordIds\.length\) \{.*?\n  \}\n",
  "  const matchedWords = recordWords.slice(\n"
  "    match.startWordIndex, match.endWordIndex + 1);\n"
  "  const expectedWordCount = match.endWordIndex - match.startWordIndex + 1;\n"
  "  if (expectedWordCount <= 0 || matchedWords.length !== expectedWordCount) {\n"
  "    reportFind('navigation-record-index-missing', {\n"
  "      trigger,\n"
  "      expectedWordCount,\n"
  "      resolvedWordCount:matchedWords.length\n"
  "    });\n"
  "    return;\n"
  "  }\n",
  "highlight Find by record-local coordinates")
text = text.replace("        sourceId:word.dataset.sourceId || '',\n", "")
text = text.replace("  return {kind:'voice', recordNumber:0, sourceId:'', wordIndex:-1};", "  return {kind:'voice', recordNumber:0, wordIndex:-1};")
text = text.replace("    originSourceId:origin.sourceId,\n", "")
# Voiced-result/seek uses direct NodeId + NodeWordIndex only.
text = replace_once(
  text,
  "function isVoicedFindMatch(match) {\n"
  "  return !!match && Number(match.seekWordId || 0) > 0;\n"
  "}",
  "function isVoicedFindMatch(match) {\n"
  "  return !!match && Number(match.nodeId || 0) > 0 &&\n"
  "    Number(match.nodeWordIndex ?? -1) >= 0;\n"
  "}",
  "voiced Find predicate")
text = text.replace("    seekWordId:match.seekWordId\n", "    nodeId:match.nodeId,\n    nodeWordIndex:match.nodeWordIndex\n")
text = replace_once(
  text,
  "    nodeId:Number(match.nodeId),\n"
  "    nodeWordIndex:Number(match.nodeWordIndex),\n"
  "    wordId:Number(match.seekWordId)\n",
  "    nodeId:Number(match.nodeId),\n"
  "    nodeWordIndex:Number(match.nodeWordIndex)\n",
  "direct find seek payload")
# Anchor restoration and shift requests use record number only.
text = text.replace(
  "      CSS.escape(String(anchorRecordNumber)) + '\"][data-source-id=\"' +\n"
  "      CSS.escape(String(anchorSourceId || '')) + '\"]';",
  "      CSS.escape(String(anchorRecordNumber)) + '\"]';")
text = text.replace("    anchorSourceId:anchor.dataset.sourceId || '',\n", "")
# Remove any remaining display word-id reset/reference in hot path diagnostics.
text = text.replace("  displayWordsById = new Map();\n", "")
# Production C# no source in shift method invocations or remaining identity lookup.
text = text.replace("            identity.SourceId,\n", "")
# Comments.
text = text.replace(
  "  /// Builds the immutable full-session search corpus away from the UI thread,\n"
  "  /// then attaches stable word IDs to whichever virtual window is current.\n",
  "  /// Builds the immutable full-session search corpus away from the UI thread.\n")
write(path, text)


# ---------------------------------------------------------------------------
# Program.cs: expose the permanent issue #65 suite and include it in `all`.
# ---------------------------------------------------------------------------
path = "AgentPanelSpeaker/Program.cs"
text = read(path)
marker = (
  "      if (args.Length == 2 &&\n"
  "          string.Equals(args[1], \"startup-performance\", StringComparison.OrdinalIgnoreCase))\n"
  "      {\n"
  "        Environment.ExitCode = RunNamedSuite(\n"
  "          \"startup-performance\",\n"
  "          () => RunWithWinFormsMessageLoop(\n"
  "            Issue60StartupPerformanceRegressionTestRunner.Run));\n"
  "        return;\n"
  "      }\n")
addition = marker + (
  "\n      if (args.Length == 2 &&\n"
  "          string.Equals(args[1], \"redundancy\", StringComparison.OrdinalIgnoreCase))\n"
  "      {\n"
  "        Environment.ExitCode = RunNamedSuite(\n"
  "          \"redundancy\",\n"
  "          Issue65RedundancyRegressionTestRunner.Run);\n"
  "        return;\n"
  "      }\n")
text = replace_once(text, marker, addition, "add redundancy named suite")
text = replace_once(
  text,
  "      int startupPerformance = RunIsolatedTestSuite(\"startup-performance\");\n",
  "      int startupPerformance = RunIsolatedTestSuite(\"startup-performance\");\n"
  "      int redundancy = RunIsolatedTestSuite(\"redundancy\");\n",
  "run redundancy in all")
text = replace_once(
  text,
  "                             realSessionFixes == 0 &&\n"
  "                             startupPerformance == 0\n",
  "                             realSessionFixes == 0 &&\n"
  "                             startupPerformance == 0 &&\n"
  "                             redundancy == 0\n",
  "include redundancy status")
write(path, text)

print("Issue #65 production simplification staged.")
