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
internal sealed record ExtractedNode(
  string Kind,
  ContentCategory Category,
  string Text,
  string? Timestamp,
  bool StartsUserTurn = false);

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
/// Carries extraction output and diagnostic classification for one record.
/// </summary>
internal sealed record ExtractionResult(
  IReadOnlyList<ExtractedNode> Nodes,
  string Decision,
  string RecordType,
  string PayloadType,
  string? CompletionTimestamp = null,
  CodexInputRequest? InputRequest = null,
  CodexInputResponse? InputResponse = null);
