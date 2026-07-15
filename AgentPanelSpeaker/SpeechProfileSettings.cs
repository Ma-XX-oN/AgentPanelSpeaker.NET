namespace AgentPanelSpeaker;

/// <summary>
/// Defines the synthesis settings for one content category.
/// </summary>
/// <param name="VoiceName">Installed voice name or Not Spoken.</param>
/// <param name="Rate">SAPI rate from -10 through 10.</param>
/// <param name="Pitch">Relative pitch in semitones from -10 through 10.</param>
internal sealed record SpeechProfileSettings(
  string VoiceName,
  int Rate,
  int Pitch)
{
  public const string NotSpoken = "Not Spoken";

  /// <summary>
  /// Gets the output volume from 0 through 100.
  /// </summary>
  public int Volume { get; init; } = 100;

  /// <summary>
  /// Gets whether this category is currently eligible for playback.
  /// </summary>
  public bool IsSpoken => !string.Equals(
    VoiceName,
    NotSpoken,
    StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// Creates a normalized profile bounded to supported ranges.
  /// </summary>
  public SpeechProfileSettings Normalize()
  {
    return this with
    {
      VoiceName = string.IsNullOrWhiteSpace(VoiceName)
        ? NotSpoken
        : VoiceName.Trim(),
      Rate = Math.Clamp(Rate, -10, 10),
      Pitch = Math.Clamp(Pitch, -10, 10),
      Volume = Math.Clamp(Volume, 0, 100)
    };
  }
}
