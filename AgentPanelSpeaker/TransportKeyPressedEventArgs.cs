namespace AgentPanelSpeaker;

/// <summary>
/// Provides a bare transport key pressed in a speech-profile control.
/// </summary>
internal sealed class TransportKeyPressedEventArgs : EventArgs
{
  /// <summary>
  /// Initializes the event arguments.
  /// </summary>
  public TransportKeyPressedEventArgs(Keys keyCode)
  {
    KeyCode = keyCode;
  }

  /// <summary>
  /// Gets the pressed key.
  /// </summary>
  public Keys KeyCode { get; }
}
