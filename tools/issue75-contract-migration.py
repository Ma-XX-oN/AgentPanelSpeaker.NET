from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
  target = Path(path)
  text = target.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{path}: expected one replacement target, found {count}")
  target.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


replace_once(
  "tools/AIConversationCore-worker.mjs",
  """  if (request?.operation === 'session_close') {\n    sessions.delete(request.session_id);\n    return {\n      ok: true,\n      core_commit: CORE_COMMIT\n    };\n  }\n""",
  """  if (request?.operation === 'session_locate_word') {\n    const entry = requireSession(request.session_id);\n    const projection = entry.session.project(options);\n    return {\n      ok: true,\n      core_commit: CORE_COMMIT,\n      location: core.locateCanonicalWord(\n        projection.events,\n        request.word_id,\n        options\n      )\n    };\n  }\n\n  if (request?.operation === 'session_close') {\n    sessions.delete(request.session_id);\n    return {\n      ok: true,\n      core_commit: CORE_COMMIT\n    };\n  }\n""")

replace_once(
  "AgentPanelSpeaker/AIConversationCoreClient.cs",
  """  /// <summary>\n  /// Releases one worker-retained canonical session.\n  /// </summary>\n  public void CloseRetainedSession(string sessionId)\n""",
  """  /// <summary>\n  /// Resolves one canonical transcript-global word in an already-retained\n  /// session using Core's numeric identity lookup. Visible text is never a\n  /// lookup key and no consumer-side word-to-unit map participates.\n  /// </summary>\n  public AIConversationCoreWordLocation? LocateRetainedWord(\n    string sessionId,\n    long wordId,\n    AIConversationCoreProjectOptions? options = null)\n  {\n    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);\n    if (wordId < 1)\n    {\n      throw new ArgumentOutOfRangeException(nameof(wordId));\n    }\n    AIConversationCoreProjectOptions effective = options ?? new();\n    var request = new CoreRequest(\n      \"session_locate_word\",\n      sessionId,\n      null,\n      null,\n      ToCoreOptions(effective),\n      null,\n      wordId);\n    CoreResponse response = SendRequest(request);\n    return response.Location;\n  }\n\n  /// <summary>\n  /// Releases one worker-retained canonical session.\n  /// </summary>\n  public void CloseRetainedSession(string sessionId)\n""")

replace_once(
  "AgentPanelSpeaker/AIConversationCoreClient.cs",
  """    [property: JsonPropertyName(\"supplementary_sources\")]\n      IReadOnlyDictionary<string, object>? SupplementarySources);\n""",
  """    [property: JsonPropertyName(\"supplementary_sources\")]\n      IReadOnlyDictionary<string, object>? SupplementarySources,\n    [property: JsonPropertyName(\"word_id\")] long? WordId = null);\n""")

replace_once(
  "AgentPanelSpeaker/AIConversationCoreClient.cs",
  """    [property: JsonPropertyName(\"diagnostics\")]\n      AIConversationCoreSessionDiagnostics? Diagnostics,\n    [property: JsonPropertyName(\"error\")] string? Error);\n}\n\n/// <summary>\n/// Structured canonical projection returned by AIConversationCore.\n/// </summary>\n""",
  """    [property: JsonPropertyName(\"diagnostics\")]\n      AIConversationCoreSessionDiagnostics? Diagnostics,\n    [property: JsonPropertyName(\"location\")]\n      AIConversationCoreWordLocation? Location,\n    [property: JsonPropertyName(\"error\")] string? Error);\n}\n\n/// <summary>\n/// Exact Core-owned canonical word and containing HTML unit.\n/// </summary>\ninternal sealed record AIConversationCoreWordLocation(\n  [property: JsonPropertyName(\"word\")] CanonicalSpeechWordProjection Word,\n  [property: JsonPropertyName(\"unit\")] CanonicalHtmlUnitProjection Unit);\n\n/// <summary>\n/// Structured canonical projection returned by AIConversationCore.\n/// </summary>\n""")

replace_once(
  "AgentPanelSpeaker/TranscriptVirtualDocument.cs",
  """  private readonly TranscriptVirtualRecord[] _records;\n  private readonly Dictionary<int, int> _recordIndexes;\n  private readonly double[] _heights;\n""",
  """  private readonly TranscriptVirtualRecord[] _records;\n  private readonly Dictionary<int, int> _recordIndexes;\n  private readonly Dictionary<string, int> _unitIndexes;\n  private readonly double[] _heights;\n""")

replace_once(
  "AgentPanelSpeaker/TranscriptVirtualDocument.cs",
  """    _heights = records.Select(record => record.EstimatedHeight).ToArray();\n    _recordIndexes = new Dictionary<int, int>();\n    for (int index = 0; index < records.Length; ++index)\n    {\n      foreach (TranscriptVirtualIdentity identity in records[index].Identities)\n      {\n        _recordIndexes[identity.RecordNumber] = index;\n      }\n    }\n""",
  """    _heights = records.Select(record => record.EstimatedHeight).ToArray();\n    _recordIndexes = new Dictionary<int, int>();\n    _unitIndexes = new Dictionary<string, int>(StringComparer.Ordinal);\n    for (int index = 0; index < records.Length; ++index)\n    {\n      string? unitId = records[index].UnitId;\n      if (!string.IsNullOrEmpty(unitId) && !_unitIndexes.TryAdd(unitId, index))\n      {\n        throw new InvalidOperationException(\n          $\"Duplicate AIConversationCore HTML unit ID: {unitId}\");\n      }\n      foreach (TranscriptVirtualIdentity identity in records[index].Identities)\n      {\n        _recordIndexes[identity.RecordNumber] = index;\n      }\n    }\n""")

replace_once(
  "AgentPanelSpeaker/TranscriptVirtualDocument.cs",
  """      identities,\n      html.Contains(\n        \"data-revision-historical=\\\"true\\\"\",\n        StringComparison.OrdinalIgnoreCase)));\n""",
  """      identities,\n      html.Contains(\n        \"data-revision-historical=\\\"true\\\"\",\n        StringComparison.OrdinalIgnoreCase),\n      UnitId: unit.Id));\n""")

replace_once(
  "AgentPanelSpeaker/TranscriptVirtualDocument.cs",
  """  public bool TryGetIndex(int recordNumber, out int index)\n  {\n    return _recordIndexes.TryGetValue(recordNumber, out index);\n  }\n\n  /// <summary>\n  /// Changes effective historical visibility without changing canonical record\n""",
  """  public bool TryGetIndex(int recordNumber, out int index)\n  {\n    return _recordIndexes.TryGetValue(recordNumber, out index);\n  }\n\n  /// <summary>\n  /// Resolves a Core-owned HTML unit identity to its virtual-document index.\n  /// Word-to-unit resolution remains exclusively inside AIConversationCore.\n  /// </summary>\n  public bool TryGetUnitIndex(string unitId, out int index)\n  {\n    ArgumentNullException.ThrowIfNull(unitId);\n    return _unitIndexes.TryGetValue(unitId, out index);\n  }\n\n  /// <summary>\n  /// Changes effective historical visibility without changing canonical record\n""")

replace_once(
  "AgentPanelSpeaker/TranscriptVirtualDocument.cs",
  """internal sealed record TranscriptVirtualRecord(\n  int RecordNumber,\n  string Html,\n  double EstimatedHeight,\n  IReadOnlyList<TranscriptVirtualIdentity> Identities,\n  bool HistoricalRevision = false);\n""",
  """internal sealed record TranscriptVirtualRecord(\n  int RecordNumber,\n  string Html,\n  double EstimatedHeight,\n  IReadOnlyList<TranscriptVirtualIdentity> Identities,\n  bool HistoricalRevision = false,\n  string? UnitId = null);\n""")
