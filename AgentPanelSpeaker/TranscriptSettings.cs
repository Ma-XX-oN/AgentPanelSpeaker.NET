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
  bool Maximized)
{
  public static TranscriptSettings Default { get; } = new(
    FollowSpeech: true,
    LightHighlightArgb: Color.FromArgb(255, 222, 149).ToArgb(),
    DarkHighlightArgb: Color.FromArgb(122, 83, 26).ToArgb(),
    FadeMilliseconds: 250,
    Maximized: false);

  public TranscriptSettings Normalize()
  {
    int boundedFade = Math.Clamp(FadeMilliseconds, 0, 2000);
    boundedFade = (int)Math.Round(boundedFade / 250.0) * 250;
    return this with
    {
      FadeMilliseconds = boundedFade,
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
