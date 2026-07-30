namespace AgentPanelSpeaker;

/// <summary>
/// Defines presentation and automatic-follow behaviour for the rendered
/// transcript.
/// </summary>
internal sealed record TranscriptSettings(
  bool FollowSpeech,
  int LightHighlightArgb,
  int DarkHighlightArgb,
  int FadeMilliseconds,
  int HighlightUpdateMilliseconds,
  bool Maximized)
{
  public static TranscriptSettings Default { get; } = new(
    FollowSpeech: true,
    LightHighlightArgb: Color.FromArgb(255, 222, 149).ToArgb(),
    DarkHighlightArgb: Color.FromArgb(122, 83, 26).ToArgb(),
    FadeMilliseconds: 250,
    HighlightUpdateMilliseconds: 10,
    Maximized: false);

  public TranscriptSettings Normalize()
  {
    int fadeStep = Math.Clamp(
      (int)Math.Round(FadeMilliseconds * 64.0 / 1000.0),
      0,
      32);
    int boundedFade = (int)Math.Round(fadeStep * 1000.0 / 64.0);
    return this with
    {
      FadeMilliseconds = boundedFade,
      HighlightUpdateMilliseconds = HighlightUpdateMilliseconds <= 0
        ? Default.HighlightUpdateMilliseconds
        : Math.Clamp(
            (int)Math.Round(HighlightUpdateMilliseconds / 5.0) * 5,
            5,
            40),
      LightHighlightArgb = NormalizeColour(
        LightHighlightArgb,
        Default.LightHighlightArgb),
      DarkHighlightArgb = NormalizeColour(
        DarkHighlightArgb,
        Default.DarkHighlightArgb)
    };
  }

  public Color GetHighlightColour(bool dark)
  {
    return Color.FromArgb(dark ? DarkHighlightArgb : LightHighlightArgb);
  }

  private static int NormalizeColour(int argb, int fallback)
  {
    Color colour = Color.FromArgb(argb);
    return colour.A == 0 ? fallback : colour.ToArgb();
  }
}
