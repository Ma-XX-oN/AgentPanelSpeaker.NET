namespace AgentPanelSpeaker;

/// <summary>
/// Configures the optional high-frequency audio prefix used to wake a
/// power-saving Bluetooth connection before speech begins.
/// </summary>
internal sealed record AudioWakeSettings(
  bool Enabled,
  int QuietDurationMilliseconds,
  int FrequencyHertz,
  int ToneVolume,
  int PlayDurationMilliseconds,
  int SettleDurationMilliseconds,
  int IpaExampleDelayMilliseconds)
{
  /// <summary>
  /// Gets conservative default wake behaviour.
  /// </summary>
  public static AudioWakeSettings Default { get; } = new(
    Enabled: false,
    QuietDurationMilliseconds: 3000,
    FrequencyHertz: 21000,
    ToneVolume: 15,
    PlayDurationMilliseconds: 150,
    SettleDurationMilliseconds: 250,
    IpaExampleDelayMilliseconds: 500);

  /// <summary>
  /// Bounds every value to a safe UI-supported range.
  /// </summary>
  public AudioWakeSettings Normalize()
  {
    return this with
    {
      QuietDurationMilliseconds = Math.Clamp(
        QuietDurationMilliseconds,
        0,
        60000),
      FrequencyHertz = Math.Clamp(FrequencyHertz, 8000, 22000),
      ToneVolume = Math.Clamp(ToneVolume, 0, 100),
      PlayDurationMilliseconds = Math.Clamp(
        PlayDurationMilliseconds,
        10,
        5000),
      SettleDurationMilliseconds = Math.Clamp(
        SettleDurationMilliseconds,
        0,
        5000),
      IpaExampleDelayMilliseconds = Math.Clamp(
        IpaExampleDelayMilliseconds,
        0,
        5000)
    };
  }
}
