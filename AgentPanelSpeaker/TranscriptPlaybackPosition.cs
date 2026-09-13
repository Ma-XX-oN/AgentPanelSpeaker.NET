namespace AgentPanelSpeaker;

/// <summary>
/// Describes the transcript marker corresponding to monitored playback.
/// <c>WordIndex</c> is the zero-based offset inside the active SpeechFragment.
/// <c>WordId</c> is the globally unique AIConversationCore canonical word
/// identity and does not reset at fragment boundaries.
/// </summary>
internal sealed record TranscriptPlaybackPosition(
  TranscriptPlaybackState State,
  string FragmentText,
  int WordIndex,
  string Word,
  long NodeId,
  int CharacterPosition,
  int CharacterCount,
  long BoundaryTimestamp,
  long? WordId = null,
  long? FragmentId = null,
  IReadOnlyList<long>? WordIds = null,
  TranscriptPlaybackHighlightMode HighlightMode =
    TranscriptPlaybackHighlightMode.Word);

internal enum TranscriptPlaybackHighlightMode
{
  Word,
  Fragment
}

internal enum TranscriptPlaybackState
{
  None,
  Speaking,
  Paused,
  PausedAtLiveEnd,
  WaitingAtLiveEnd
}
