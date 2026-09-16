namespace AgentPanelSpeaker;

/// <summary>
/// Carries one PCM buffer and word boundaries measured from its beginning.
/// </summary>
internal sealed record SpeechTrackingDegradation(
  string Backend,
  string VoiceName,
  string Reason);

/// <summary>
/// Carries one PCM buffer, exact word boundaries, and an explicit reason when
/// playback must degrade to whole-fragment highlighting.
/// </summary>
internal sealed record SpeechPlaybackBuffer(
  PcmWaveData Wave,
  IReadOnlyList<SpeechWordBoundary> WordBoundaries,
  SpeechTrackingDegradation? TrackingDegradation = null);
