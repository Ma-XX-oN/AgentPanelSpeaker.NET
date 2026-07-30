namespace AgentPanelSpeaker;

/// <summary>
/// Describes a request to leave a compact speech-profile editor.
/// </summary>
internal sealed class FocusTraversalRequestedEventArgs : EventArgs
{
  /// <summary>
  /// Initializes a traversal request.
  /// </summary>
  public FocusTraversalRequestedEventArgs(bool forward)
  {
    Forward = forward;
  }

  /// <summary>
  /// Gets whether focus should move forward rather than backward.
  /// </summary>
  public bool Forward { get; }
}
