namespace AgentPanelSpeaker;

/// <summary>
/// Identifies one installed provider voice and its descriptive UI label.
/// </summary>
/// <param name="Name">Stable provider name stored in settings.</param>
/// <param name="DisplayName">Descriptive name shown to the user.</param>
internal sealed record InstalledSpeechVoice(
  string Name,
  string DisplayName)
{
  /// <summary>
  /// Returns the descriptive label used by Windows Forms controls.
  /// </summary>
  public override string ToString()
  {
    return DisplayName;
  }
}
