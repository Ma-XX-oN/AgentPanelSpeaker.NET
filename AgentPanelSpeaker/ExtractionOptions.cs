namespace AgentPanelSpeaker;

/// <summary>
/// Describes one conversational text node extracted from a JSONL record.
/// </summary>
/// <param name="Kind">Source record/block kind.</param>
/// <param name="Category">Conversation role and speech profile.</param>
/// <param name="Text">Raw Markdown text.</param>
/// <param name="Timestamp">Source timestamp when present.</param>
/// <param name="StartsUserTurn">
/// Whether this node is an actual User prompt that starts a timed turn.
/// </param>
/// <param name="RevisionStatus">
/// Core-owned Codex revision status (`original`, `superseded`, `edited`), or
/// null for ordinary/non-revision content.
/// </param>
/// <param name="RevisionDepth">
/// Core-owned zero-based revision depth, or null for non-revision content.
/// </param>
/// <param name="ProjectionVisible">
/// Core-owned effective visibility for the projection that produced this node.
/// </param>
/// <param name="RevisionHistoryControlled">
/// Whether Core marks this node as belonging to a revision lineage whose
/// historical visibility can be changed without changing canonical identity.
/// </param>
/// <param name="HistoricalRevision">
/// Whether Core classifies this node as historical revision content.
/// </param>
internal sealed record ExtractedNode(
  string Kind,
  ContentCategory Category,
  string Text,
  string? Timestamp,
  bool StartsUserTurn = false,
  string? RevisionStatus = null,
  int? RevisionDepth = null,
  bool ProjectionVisible = true,
  bool RevisionHistoryControlled = false,
  bool HistoricalRevision = false);

/// <summary>
/// Describes one labelled option in a Codex input question.
/// </summary>
internal sealed record CodexInputOption(
  string Label,
  string Description);

/// <summary>
/// Retains the information needed to narrate one input selection.
/// </summary>
internal sealed record CodexInputQuestion(
  string Id,
  bool IsSecret,
  IReadOnlyList<CodexInputOption> Options);

/// <summary>
/// Identifies one pending Codex request_user_input function call.
/// </summary>
internal sealed record CodexInputRequest(
  string CallId,
  IReadOnlyList<CodexInputQuestion> Questions);

/// <summary>
/// Identifies the answers returned for one Codex request_user_input call.
/// </summary>
internal sealed record CodexInputResponse(
  string CallId,
  IReadOnlyDictionary<string, IReadOnlyList<string>> Answers,
  string? Timestamp);

/// <summary>
/// Records one background-agent lifecycle transition for timing queries.
/// </summary>
/// <param name="Id">Stable tool-use or task identifier.</param>
/// <param name="Description">Readable task description when available.</param>
/// <param name="StartUtc">Recorded or duration-derived start timestamp.</param>
/// <param name="EndUtc">Completion timestamp, or null while running.</param>
internal sealed record BackgroundWorkEvent(
  string Id,
  string Description,
  DateTimeOffset StartUtc,
  DateTimeOffset? EndUtc);

/// <summary>
/// Carries extraction output and diagnostic classification for one record.
/// </summary>
internal sealed record ExtractionResult(
  IReadOnlyList<ExtractedNode> Nodes,
  string Decision,
  string RecordType,
  string PayloadType,
  string? CompletionTimestamp = null,
  CodexInputRequest? InputRequest = null,
  CodexInputResponse? InputResponse = null,
  IReadOnlyList<BackgroundWorkEvent>? BackgroundWorkEvents = null);
