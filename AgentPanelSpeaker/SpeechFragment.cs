namespace AgentPanelSpeaker;

/// <summary>
/// Identifies one spoken sentence and its source JSONL assistant record.
/// </summary>
/// <param name="NodeId">Stable record identifier for rewind grouping.</param>
/// <param name="Text">Text to speak.</param>
internal sealed record SpeechFragment(long NodeId, string Text);
