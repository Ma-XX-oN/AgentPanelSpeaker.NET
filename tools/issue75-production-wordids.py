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

# Attach a Core block ID only where one ExtractedNode is directly backed by one
# rendered canonical block. App-synthesized narration and split queued-command
# narration intentionally have no transcript word identity.
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

# Only the subagent result is direct canonical content. The start/finished
# sentences are app-owned announcements and therefore stay identity-free.
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
  '''    string text,\n    string? timestamp,\n    bool startsUserTurn = false,\n    string? canonicalBlockId = null)\n  {\n    if (!string.IsNullOrWhiteSpace(text))\n    {\n      nodes.Add(new ExtractedNode(\n        kind,\n        category,\n        text.Trim(),\n        timestamp,\n        startsUserTurn,\n        CanonicalBlockId: canonicalBlockId));\n    }\n  }\n\n  /// <summary>\n  /// Attaches Core-owned word records to direct canonical-block speech nodes.\n  /// Block provenance and block_word_index are authoritative; visible text is\n  /// never searched to decide which Core word belongs to a node.\n  /// </summary>\n  private static IReadOnlyList<ExtractedNode> AttachCanonicalWords(\n    IReadOnlyList<ExtractedNode> nodes,\n    AIConversationProjection projection)\n  {\n    CanonicalSpeechWordProjection[] allWords = (projection.HtmlUnits ??\n      Array.Empty<CanonicalHtmlUnitProjection>())\n      .SelectMany(unit => unit.SpeechWords ??\n        Array.Empty<CanonicalSpeechWordProjection>())\n      .ToArray();\n\n    return nodes.Select(node =>\n    {\n      if (string.IsNullOrEmpty(node.CanonicalBlockId))\n      {\n        return node;\n      }\n\n      CanonicalSpeechWordProjection[] words = allWords\n        .Where(word => string.Equals(\n          word.Provenance?.BlockId,\n          node.CanonicalBlockId,\n          StringComparison.Ordinal))\n        .OrderBy(word => word.Provenance!.BlockWordIndex)\n        .ToArray();\n      if (words.Length == 0)\n      {\n        throw new InvalidDataException(\n          $"Core block {node.CanonicalBlockId} has speech text but no " +\n          "canonical word projection.");\n      }\n      for (int index = 0; index < words.Length; ++index)\n      {\n        if (words[index].Provenance?.BlockWordIndex != index)\n        {\n          throw new InvalidDataException(\n            $"Core block {node.CanonicalBlockId} has a non-contiguous " +\n            "canonical block_word_index sequence.");\n        }\n      }\n      return node with { CanonicalWords = words };\n    }).ToArray();\n  }\n\n  /// <summary>\n  /// Enumerates canonical blocks from one event.\n''')

replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''    IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(node.Text);\n    if (parts.Count == 0)\n''',
  '''    IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(node.Text);\n    IReadOnlyList<IReadOnlyList<long>?> partWordIds =\n      MapCanonicalWordIds(node, parts);\n    if (parts.Count == 0)\n''')

replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''      bool startsUserTurn = node.StartsUserTurn && partIndex == 0;\n      if (part.Kind == SpeechFragmentKind.Prose)\n''',
  '''      bool startsUserTurn = node.StartsUserTurn && partIndex == 0;\n      IReadOnlyList<long>? canonicalPartWordIds = partWordIds[partIndex];\n      if (part.Kind == SpeechFragmentKind.Prose)\n''')

replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''        for (int sentenceIndex = 0;\n             sentenceIndex < sentences.Count;\n             ++sentenceIndex)\n        {\n          SentenceSegment sentence = sentences[sentenceIndex];\n          fragments.Add(new SpeechFragment(\n''',
  '''        int sentenceWordOffset = 0;\n        for (int sentenceIndex = 0;\n             sentenceIndex < sentences.Count;\n             ++sentenceIndex)\n        {\n          SentenceSegment sentence = sentences[sentenceIndex];\n          int sentenceWordCount = SpeechTokenization.Matches(sentence.Text).Count;\n          IReadOnlyList<long>? sentenceWordIds = SliceCanonicalWordIds(\n            canonicalPartWordIds,\n            sentenceWordOffset,\n            sentenceWordCount);\n          sentenceWordOffset = checked(sentenceWordOffset + sentenceWordCount);\n          fragments.Add(new SpeechFragment(\n''')

replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''            ProjectionVisible: node.ProjectionVisible,\n            RevisionHistoryControlled: node.RevisionHistoryControlled,\n            HistoricalRevision: node.HistoricalRevision));\n        }\n      }\n      else\n''',
  '''            ProjectionVisible: node.ProjectionVisible,\n            RevisionHistoryControlled: node.RevisionHistoryControlled,\n            HistoricalRevision: node.HistoricalRevision,\n            WordIds: sentenceWordIds));\n        }\n        if (canonicalPartWordIds is not null &&\n            sentenceWordOffset != canonicalPartWordIds.Count)\n        {\n          throw new InvalidDataException(\n            "Sentence segmentation changed the canonical word count.");\n        }\n      }\n      else\n''')

# The second constructor occurrence is the non-prose/fenced-code path.
old = '''          ProjectionVisible: node.ProjectionVisible,\n          RevisionHistoryControlled: node.RevisionHistoryControlled,\n          HistoricalRevision: node.HistoricalRevision));\n      }\n    }\n    DiagnosticLog.Write("jsonl.node_accepted", new\n'''
new = '''          ProjectionVisible: node.ProjectionVisible,\n          RevisionHistoryControlled: node.RevisionHistoryControlled,\n          HistoricalRevision: node.HistoricalRevision,\n          WordIds: canonicalPartWordIds));\n      }\n    }\n    DiagnosticLog.Write("jsonl.node_accepted", new\n'''
replace_once('AgentPanelSpeaker/JsonlSessionMonitor.cs', old, new)

replace_once(
  'AgentPanelSpeaker/JsonlSessionMonitor.cs',
  '''  /// <summary>\n  /// Retains one request_user_input call until its matching output arrives.\n''',
  '''  /// <summary>\n  /// Carries the Core block_word_index sequence through app speech cleanup.\n  /// The mapping is positional inside one already-proven canonical block and\n  /// every token text is checked only as an invariant. No text search, fuzzy\n  /// alignment, or alternate identity path is permitted.\n  /// </summary>\n  private static IReadOnlyList<IReadOnlyList<long>?> MapCanonicalWordIds(\n    ExtractedNode node,\n    IReadOnlyList<SpeechTextPart> parts)\n  {\n    if (node.CanonicalWords is null)\n    {\n      return Enumerable.Repeat<IReadOnlyList<long>?>(null, parts.Count).ToArray();\n    }\n\n    int cursor = 0;\n    var mapped = new List<IReadOnlyList<long>?>(parts.Count);\n    foreach (SpeechTextPart part in parts)\n    {\n      MatchCollection tokens = SpeechTokenization.Matches(part.Text);\n      var ids = new long[tokens.Count];\n      for (int index = 0; index < tokens.Count; ++index)\n      {\n        if (cursor >= node.CanonicalWords.Count)\n        {\n          throw new InvalidDataException(\n            $"Speech cleanup produced more words than Core block " +\n            $"{node.CanonicalBlockId}.");\n        }\n        CanonicalSpeechWordProjection canonical = node.CanonicalWords[cursor];\n        if (!string.Equals(\n              canonical.Text,\n              tokens[index].Value,\n              StringComparison.Ordinal))\n        {\n          throw new InvalidDataException(\n            $"Speech/Core word invariant mismatch in block " +\n            $"{node.CanonicalBlockId} at block word {cursor}: Core=" +\n            $"{canonical.Text}, speech={tokens[index].Value}.");\n        }\n        ids[index] = canonical.Id;\n        ++cursor;\n      }\n      mapped.Add(ids);\n    }\n\n    if (cursor != node.CanonicalWords.Count)\n    {\n      throw new InvalidDataException(\n        $"Speech cleanup consumed {cursor} of {node.CanonicalWords.Count} " +\n        $"Core words in block {node.CanonicalBlockId}.");\n    }\n    return mapped;\n  }\n\n  /// <summary>\n  /// Returns the canonical IDs for one sentence slice inside a speech part.\n  /// </summary>\n  private static IReadOnlyList<long>? SliceCanonicalWordIds(\n    IReadOnlyList<long>? wordIds,\n    int offset,\n    int count)\n  {\n    if (wordIds is null)\n    {\n      return null;\n    }\n    if (offset < 0 || count < 0 || offset + count > wordIds.Count)\n    {\n      throw new InvalidDataException(\n        "Sentence word range exceeds its canonical speech-part identity range.");\n    }\n    return wordIds.Skip(offset).Take(count).ToArray();\n  }\n\n  /// <summary>\n  /// Retains one request_user_input call until its matching output arrives.\n''')
