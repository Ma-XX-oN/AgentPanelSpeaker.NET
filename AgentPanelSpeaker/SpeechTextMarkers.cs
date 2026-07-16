namespace AgentPanelSpeaker;

/// <summary>
/// Defines non-spoken markers retained between cleanup and markup generation.
/// </summary>
internal static class SpeechTextMarkers
{
  /// <summary>
  /// Separates a Markdown heading from the prose that follows it.
  /// </summary>
  public const char HeadingPause = '\u2063';
}
