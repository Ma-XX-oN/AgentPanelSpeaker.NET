namespace AgentPanelSpeaker;

/// <summary>
/// Identifies a voiced transcript word selected by the find popup.
/// </summary>
internal sealed class FindSeekRequestedEventArgs : EventArgs
{
  public FindSeekRequestedEventArgs(long nodeId, int nodeWordIndex)
  {
    NodeId = nodeId;
    NodeWordIndex = nodeWordIndex;
  }

  public long NodeId { get; }
  public int NodeWordIndex { get; }
}
