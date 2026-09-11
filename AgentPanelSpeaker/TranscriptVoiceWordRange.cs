namespace AgentPanelSpeaker;

/// <summary>
/// Identifies one contiguous node-global lexical-word range that is currently
/// eligible for speech navigation.
/// </summary>
/// <param name="NodeId">Stable JSONL node identifier.</param>
/// <param name="StartNodeWordIndex">
/// Zero-based lexical-word index within the complete node speech namespace.
/// </param>
/// <param name="WordCount">Number of currently eligible words in the range.</param>
internal sealed record TranscriptVoiceWordRange(
  long NodeId,
  int StartNodeWordIndex,
  int WordCount);
