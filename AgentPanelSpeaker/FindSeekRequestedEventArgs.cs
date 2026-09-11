namespace AgentPanelSpeaker;

/// <summary>
/// Identifies a voiced transcript word selected by the find popup.
/// </summary>
internal sealed class FindSeekRequestedEventArgs : EventArgs
{
  public FindSeekRequestedEventArgs(
    long nodeId,
    int nodeWordIndex,
    string source = "find")
  {
    NodeId = nodeId;
    NodeWordIndex = nodeWordIndex;
    Source = source;
  }

  public long NodeId { get; }
  public int NodeWordIndex { get; }
  public string Source { get; }
}
