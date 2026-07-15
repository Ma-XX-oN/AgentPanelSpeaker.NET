using System.Globalization;
using System.Security;

namespace AgentPanelSpeaker;

/// <summary>
/// Builds SSML used for pitch-adjusted System.Speech prompts.
/// </summary>
internal static class SpeechSsmlBuilder
{
  private const int PitchPercentPerStep = 5;

  /// <summary>
  /// Builds one SSML document with an audible relative pitch adjustment.
  /// </summary>
  public static string BuildPitchDocument(
    string text,
    CultureInfo culture,
    int pitchSetting)
  {
    ArgumentNullException.ThrowIfNull(text);
    ArgumentNullException.ThrowIfNull(culture);
    int pitchPercent = GetPitchPercent(pitchSetting);
    string escaped = SecurityElement.Escape(text) ?? string.Empty;
    string pitch = pitchPercent > 0
      ? $"+{pitchPercent}%"
      : $"{pitchPercent}%";
    return
      $"<speak version=\"1.0\" " +
      $"xmlns=\"http://www.w3.org/2001/10/synthesis\" " +
      $"xml:lang=\"{culture.Name}\"><prosody pitch=\"{pitch}\">" +
      $"{escaped}</prosody></speak>";
  }

  /// <summary>
  /// Converts the UI pitch setting into the relative SSML percentage.
  /// </summary>
  public static int GetPitchPercent(int pitchSetting)
  {
    return Math.Clamp(pitchSetting, -10, 10) * PitchPercentPerStep;
  }
}
