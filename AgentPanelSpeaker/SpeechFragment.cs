namespace AgentPanelSpeaker;

/// <summary>
/// Identifies one spoken sentence or idle-flushed fragment and its source
/// accessibility node.
/// </summary>
/// <param name="NodeId">Stable node identifier for rewind grouping.</param>
/// <param name="Text">Text to speak.</param>
internal sealed record SpeechFragment(long NodeId, string Text);
