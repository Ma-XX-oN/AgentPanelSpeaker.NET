namespace AgentPanelSpeaker;

/// <summary>
/// Identifies one spoken word and its start position in the playback buffer.
/// </summary>
internal sealed record SpeechWordBoundary(
  TimeSpan AudioPosition,
  int WordIndex,
  int CharacterPosition,
  int CharacterCount,
  string Text,
  bool Exact);
