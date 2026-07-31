namespace AgentPanelSpeaker;

/// <summary>
/// Identifies one navigable transcript fragment and its source JSONL node.
/// </summary>
/// <param name="NodeId">Stable JSONL-node identifier for grouping.</param>
/// <param name="Category">Conversation role that owns the text.</param>
/// <param name="Kind">Prose sentence or fenced-code line.</param>
/// <param name="Text">Text to speak.</param>
/// <param name="FenceType">Normalized fence type, or empty for prose.</param>
/// <param name="FenceBlockId">Node-local fenced-block identifier.</param>
/// <param name="FenceLineIndex">Zero-based non-empty line index.</param>
/// <param name="FenceLineCount">Number of non-empty lines in the block.</param>
/// <param name="PauseAfter">
/// Whether a structural Markdown boundary follows this fragment.
/// </param>
/// <param name="NodeTimestampUtc">
/// Source-node timestamp normalized to UTC when available.
/// </param>
/// <param name="StartsUserTurn">
/// Whether this fragment belongs to an actual User prompt that starts a turn.
/// </param>
internal sealed record SpeechFragment(
  long NodeId,
  ContentCategory Category,
  SpeechFragmentKind Kind,
  string Text,
  string FenceType = "",
  int FenceBlockId = -1,
  int FenceLineIndex = -1,
  int FenceLineCount = 0,
  bool PauseAfter = false,
  DateTimeOffset? NodeTimestampUtc = null,
  bool StartsUserTurn = false);

/// <summary>
/// Identifies how existing history should begin playback.
/// </summary>
internal enum PlaybackStartMode
{
  LiveEnd,
  LatestTurn,
  Beginning
}

/// <summary>
/// Identifies a non-spoken terminal marker for one completed AI turn.
/// </summary>
internal sealed record TurnCompletion(DateTimeOffset TimestampUtc);

/// <summary>
/// Carries indexed history, terminal completion markers, background-work
/// timing, and its requested initial playback mode.
/// </summary>
internal sealed record SpeechHistorySnapshot(
  IReadOnlyList<SpeechFragment> Fragments,
  IReadOnlyList<TurnCompletion> Completions,
  IReadOnlyList<BackgroundWorkEvent> BackgroundWorkEvents,
  PlaybackStartMode StartMode);
