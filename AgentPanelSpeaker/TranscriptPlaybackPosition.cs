namespace AgentPanelSpeaker;

/// <summary>
/// Describes the transcript marker corresponding to monitored playback.
/// </summary>
internal sealed record TranscriptPlaybackPosition(
  TranscriptPlaybackState State,
  string FragmentText,
  int WordIndex,
  string Word,
  long NodeId,
  int CharacterPosition,
  int CharacterCount,
  long BoundaryTimestamp);

internal enum TranscriptPlaybackState
{
  None,
  Speaking,
  Paused,
  PausedAtLiveEnd,
  WaitingAtLiveEnd
}
