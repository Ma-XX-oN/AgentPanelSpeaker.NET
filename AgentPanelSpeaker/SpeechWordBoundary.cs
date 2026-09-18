namespace AgentPanelSpeaker;

/// <summary>
/// Identifies one spoken token and its start position in the playback buffer.
/// <c>WordIndex</c> is zero-based within the current engine utterance;
/// SpeechService translates it into a SpeechFragment-relative offset. It is
/// never an AIConversationCore canonical WordId.
/// </summary>
internal sealed record SpeechWordBoundary(
  TimeSpan AudioPosition,
  int WordIndex,
  int CharacterPosition,
  int CharacterCount,
  string Text,
  bool Exact,
  int WordCount = 1);
