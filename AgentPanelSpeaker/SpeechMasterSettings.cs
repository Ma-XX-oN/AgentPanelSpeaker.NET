namespace AgentPanelSpeaker;

/// <summary>
/// Applies global rate, pitch, and volume adjustments without rewriting role profiles.
/// </summary>
internal sealed record SpeechMasterSettings(int Rate, int Pitch, int Volume)
{
  public static SpeechMasterSettings Default { get; } = new(0, 0, 100);

  public SpeechMasterSettings Normalize() => new(
    Math.Clamp(Rate, -10, 10),
    Math.Clamp(Pitch, -10, 10),
    Math.Clamp(Volume, 0, 100));

  public SpeechProfileSettings Apply(SpeechProfileSettings profile)
  {
    SpeechMasterSettings normalized = Normalize();
    SpeechProfileSettings child = profile.Normalize();
    return child with
    {
      Rate = Math.Clamp(child.Rate + normalized.Rate, -10, 10),
      Pitch = Math.Clamp(child.Pitch + normalized.Pitch, -10, 10),
      Volume = Math.Clamp(
        (int)Math.Round(child.Volume * normalized.Volume / 100.0),
        0,
        100)
    };
  }
}
