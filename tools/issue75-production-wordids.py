from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
  target = Path(path)
  text = target.read_text(encoding='utf-8')
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f'{path}: expected one replacement target, found {count}')
  target.write_text(text.replace(old, new), encoding='utf-8', newline='\n')


replace_once(
  'AgentPanelSpeaker/ExtractionOptions.cs',
  '''  bool ProjectionVisible = true,\n  bool RevisionHistoryControlled = false,\n  bool HistoricalRevision = false);\n''',
  '''  bool ProjectionVisible = true,\n  bool RevisionHistoryControlled = false,\n  bool HistoricalRevision = false,\n  string? CanonicalBlockId = null,\n  IReadOnlyList<CanonicalSpeechWordProjection>? CanonicalWords = null);\n''')

# Direct canonical blocks retain their Core block identity. App-synthesized
# announcements deliberately remain identity-free.
replace_once(
  'AgentPanelSpeaker/CanonicalProjectionExtractor.cs',
  '''      AddNode(nodes, nodeKind, category, text, timestamp);\n''',
  '''      AddNode(\n        nodes,\n        nodeKind,\n        category,\n        text,\n        timestamp,\n        canonicalBlockId: GetString(block, "id"));\n''')
replace_once(
  'AgentPanelSpeaker/CanonicalProjectionExtractor.cs',
  '''        GetString(block, "text"),\n        timestamp,\n        startsUserTurn: first);\n''',
  '''        GetString(block, "text"),\n        timestamp,\n        startsUserTurn: first,\n        canonicalBlockId: GetString(block, "id"));\n''')
replace_once(
  'AgentPanelSpeaker/CanonicalProjectionExtractor.cs',
  '''        GetString(block, "text"),\n        timestamp,\n        startsUserTurn && first);\n''',
  '''        GetString(block, "text"),\n        timestamp,\n        startsUserTurn && first,\n        canonicalBlockId: GetString(block, "id"));\n''')
replace_once(
  'AgentPanelSpeaker/CanonicalProjectionExtractor.cs',
  '''    AddNode(\n      nodes,\n      "claude.subagent.result",\n      ContentCategory.SubagentAssistant,\n      output,\n      timestamp);\n''',
  '''    AddNode(\n      nodes,\n      "claude.subagent.result",\n      ContentCategory.SubagentAssistant,\n      output,\n      timestamp,\n      canonicalBlockId: GetString(block.Value, "id"));\n''')
replace_once(
  'AgentPanelSpeaker/CanonicalProjectionExtractor.cs',
  '''    return new ExtractionResult(\n      nodes,\n      decision,\n''',
  '''    return new ExtractionResult(\n      AttachCanonicalWords(nodes, projection),\n      decision,\n''')
replace_once(
  'AgentPanelSpeaker/CanonicalProjectionExtractor.cs',
  '''    string text,\n    string? timestamp,\n    bool startsUserTurn = false)\n  {\n    if (!string.IsNullOrWhiteSpace(text))\n    {\n      nodes.Add(new ExtractedNode(\n        kind,\n        category,\n        text.Trim(),\n        timestamp,\n        startsUserTurn));\n    }\n  }\n\n  /// <summary>\n  /// Enumerates canonical blocks from one event.\n''',
  '''    string text,\n    string? timestamp,\n    bool startsUserTurn = false,\n    string? canonicalBlockId = null)\n  {\n    if (!string.IsNullOrWhiteSpace(text))\n    {\n      nodes.Add(new ExtractedNode(\n        kind,\n        category,\n        text.Trim(),\n        timestamp,\n        startsUserTurn,\n        CanonicalBlockId: canonicalBlockId));\n    }\n  }\n\n  /// <summary>\n  /// Attaches Core-owned word records to direct canonical-block speech nodes.\n  /// Provenance.block_id and block_word_index are authoritative. Visible text\n  /// is never searched or retokenized to recover transcript identity.\n  /// </summary>\n  private static IReadOnlyList<ExtractedNode> AttachCanonicalWords(\n    IReadOnlyList<ExtractedNode> nodes,\n    AIConversationProjection projection)\n  {\n    CanonicalSpeechWordProjection[] allWords = (projection.HtmlUnits ??\n      Array.Empty<CanonicalHtmlUnitProjection>())\n      .SelectMany(unit => unit.SpeechWords ??\n        Array.Empty<CanonicalSpeechWordProjection>())\n      .ToArray();\n\n    return nodes.Select(node =>\n    {\n      if (string.IsNullOrEmpty(node.CanonicalBlockId))\n      {\n        return node;\n      }\n\n      CanonicalSpeechWordProjection[] words = allWords\n        .Where(word => string.Equals(\n          word.Provenance?.BlockId,\n          node.CanonicalBlockId,\n          StringComparison.Ordinal))\n        .OrderBy(word => word.Provenance!.BlockWordIndex)\n        .ToArray();\n      if (words.Length == 0)\n      {\n        throw new InvalidDataException(\n          $"Core block {node.CanonicalBlockId} has speech text but no " +\n          "canonical word projection.");\n      }\n      for (int index = 0; index < words.Length; ++index)\n      {\n        if (words[index].Provenance?.BlockWordIndex != index)\n        {\n          throw new InvalidDataException(\n            $"Core block {node.CanonicalBlockId} has a non-contiguous " +\n            "canonical block_word_index sequence.");\n        }\n      }\n      return node with { CanonicalWords = words };\n    }).ToArray();\n  }\n\n  /// <summary>\n  /// Enumerates canonical blocks from one event.\n''')

replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''    IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(node.Text);\n    if (parts.Count == 0)\n''',
  '''    IReadOnlyList<SpeechTextPart> parts;\n    IReadOnlyList<IReadOnlyList<long>?> partWordIds;\n    if (node.CanonicalWords is { Count: > 0 } canonicalWords)\n    {\n      (parts, partWordIds) = BuildCanonicalSpeechParts(canonicalWords);\n    }\n    else\n    {\n      parts = TextCleaner.ParseForSpeech(node.Text);\n      partWordIds = Enumerable\n        .Repeat<IReadOnlyList<long>?>(null, parts.Count)\n        .ToArray();\n    }\n    if (parts.Count == 0)\n''')

replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''      bool startsUserTurn = node.StartsUserTurn && partIndex == 0;\n      if (part.Kind == SpeechFragmentKind.Prose)\n      {\n''',
  '''      bool startsUserTurn = node.StartsUserTurn && partIndex == 0;\n      IReadOnlyList<long>? canonicalPartWordIds = partWordIds[partIndex];\n      if (canonicalPartWordIds is not null)\n      {\n        fragments.Add(new SpeechFragment(\n          nodeId,\n          fragmentCategory,\n          part.Kind,\n          part.Text,\n          part.FenceType,\n          part.FenceBlockId,\n          part.FenceLineIndex,\n          part.FenceLineCount,\n          PauseAfter: part.PauseAfter,\n          NodeTimestampUtc: nodeTimestampUtc,\n          StartsUserTurn: startsUserTurn,\n          RevisionStatus: node.RevisionStatus,\n          RevisionDepth: node.RevisionDepth,\n          ProjectionVisible: node.ProjectionVisible,\n          RevisionHistoryControlled: node.RevisionHistoryControlled,\n          HistoricalRevision: node.HistoricalRevision,\n          WordIds: canonicalPartWordIds));\n      }\n      else if (part.Kind == SpeechFragmentKind.Prose)\n      {\n''')

replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''  /// <summary>\n  /// Retains one request_user_input call until its matching output arrives.\n''',
  r'''  /// <summary>
  /// Builds transcript-backed speech parts directly from the authoritative Core
  /// word stream. Core separators preserve word adjacency; APS never retokenizes
  /// these words to decide identity. Ordered-list ordinals such as `1.` remain
  /// one canonical word and begin their own list-item part after a line break.
  /// </summary>
  private static (
    IReadOnlyList<SpeechTextPart> Parts,
    IReadOnlyList<IReadOnlyList<long>?> WordIds) BuildCanonicalSpeechParts(
      IReadOnlyList<CanonicalSpeechWordProjection> words)
  {
    var groups = new List<List<CanonicalSpeechWordProjection>>();
    var current = new List<CanonicalSpeechWordProjection>();
    string currentFence = string.Empty;

    foreach (CanonicalSpeechWordProjection word in words)
    {
      string fence = FenceType(word);
      bool fenceChanged = current.Count != 0 &&
        !string.Equals(fence, currentFence, StringComparison.OrdinalIgnoreCase);
      bool newFenceLine = current.Count != 0 &&
        fence.Length != 0 &&
        word.SeparatorBefore.Contains('\n');
      bool newOrderedItem = current.Count != 0 &&
        fence.Length == 0 &&
        word.SeparatorBefore.Contains('\n') &&
        IsOrderedListOrdinal(word.Text);
      if (fenceChanged || newFenceLine || newOrderedItem)
      {
        groups.Add(current);
        current = new List<CanonicalSpeechWordProjection>();
      }
      if (current.Count == 0)
      {
        currentFence = fence;
      }
      current.Add(word);
    }
    if (current.Count != 0)
    {
      groups.Add(current);
    }

    var parts = new List<SpeechTextPart>();
    var ids = new List<IReadOnlyList<long>?>();
    int fenceLineIndex = 0;
    int fenceLineCount = groups.Count(group => FenceType(group[0]).Length != 0);
    foreach (List<CanonicalSpeechWordProjection> group in groups)
    {
      string fenceType = FenceType(group[0]);
      if (fenceType.Length != 0)
      {
        string line = ReconstructCanonicalWords(group, preserveWhitespace: true);
        if (line.Length != 0)
        {
          parts.Add(new SpeechTextPart(
            SpeechFragmentKind.FencedCodeLine,
            line,
            fenceType,
            FenceBlockId: 0,
            FenceLineIndex: fenceLineIndex++,
            FenceLineCount: fenceLineCount,
            PauseAfter: true,
            SpeechTextStyle.Main));
          ids.Add(group.Select(word => word.Id).ToArray());
        }
        continue;
      }

      AddCanonicalProseParts(group, parts, ids);
    }
    return (parts, ids);
  }

  /// <summary>
  /// Splits one Core prose/list-item word run at canonical sentence punctuation.
  /// Structural ordered-list ordinals contain their own dot and are therefore
  /// not punctuation tokens here.
  /// </summary>
  private static void AddCanonicalProseParts(
    IReadOnlyList<CanonicalSpeechWordProjection> words,
    ICollection<SpeechTextPart> parts,
    ICollection<IReadOnlyList<long>?> ids)
  {
    int start = 0;
    for (int index = 0; index < words.Count; ++index)
    {
      if (words[index].Text is not ("." or "?" or "!"))
      {
        continue;
      }
      int end = index + 1;
      while (end < words.Count &&
             words[end].SeparatorBefore.Length == 0 &&
             words[end].Text is "\"" or "'" or ")" or "]" or "}")
      {
        ++end;
      }
      AddCanonicalProsePart(words, start, end, parts, ids, pauseAfter: false);
      start = end;
      index = end - 1;
    }
    if (start < words.Count)
    {
      AddCanonicalProsePart(
        words,
        start,
        words.Count,
        parts,
        ids,
        pauseAfter: true);
    }
    else if (parts.Count != 0 && parts.Last().PauseAfter is false)
    {
      SpeechTextPart last = parts.Last();
      parts.Remove(last);
      parts.Add(last with { PauseAfter = true });
    }
  }

  private static void AddCanonicalProsePart(
    IReadOnlyList<CanonicalSpeechWordProjection> words,
    int start,
    int end,
    ICollection<SpeechTextPart> parts,
    ICollection<IReadOnlyList<long>?> ids,
    bool pauseAfter)
  {
    CanonicalSpeechWordProjection[] slice = words.Skip(start).Take(end - start).ToArray();
    if (slice.Length == 0)
    {
      return;
    }
    parts.Add(new SpeechTextPart(
      SpeechFragmentKind.Prose,
      ReconstructCanonicalWords(slice, preserveWhitespace: false),
      string.Empty,
      FenceBlockId: -1,
      FenceLineIndex: -1,
      FenceLineCount: 0,
      PauseAfter: pauseAfter,
      SpeechTextStyle.Main));
    ids.Add(slice.Select(word => word.Id).ToArray());
  }

  private static string ReconstructCanonicalWords(
    IReadOnlyList<CanonicalSpeechWordProjection> words,
    bool preserveWhitespace)
  {
    var text = new StringBuilder();
    for (int index = 0; index < words.Count; ++index)
    {
      CanonicalSpeechWordProjection word = words[index];
      if (index != 0 && word.SeparatorBefore.Length != 0)
      {
        text.Append(preserveWhitespace ? word.SeparatorBefore : " ");
      }
      text.Append(word.Text);
    }
    return text.ToString().Trim();
  }

  private static string FenceType(CanonicalSpeechWordProjection word)
  {
    if (!word.Groups.Contains("fenced_code", StringComparer.Ordinal))
    {
      return string.Empty;
    }
    const string prefix = "fence:";
    string? group = word.Groups.FirstOrDefault(value =>
      value.StartsWith(prefix, StringComparison.Ordinal));
    return group is null ? "untyped" : group[prefix.Length..];
  }

  private static bool IsOrderedListOrdinal(string text)
  {
    if (text.Length < 2 || text[^1] is not ('.' or ')'))
    {
      return false;
    }
    return text[..^1].All(char.IsDigit);
  }

  /// <summary>
  /// Retains one request_user_input call until its matching output arrives.
''')
