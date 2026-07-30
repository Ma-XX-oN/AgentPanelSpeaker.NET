namespace AgentPanelSpeaker;

/// <summary>
/// Carries one PCM buffer and word boundaries measured from its beginning.
/// </summary>
internal sealed record SpeechPlaybackBuffer(
  PcmWaveData Wave,
  IReadOnlyList<SpeechWordBoundary> WordBoundaries);
