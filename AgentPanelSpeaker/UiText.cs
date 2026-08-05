using System.Globalization;
using System.Resources;

namespace AgentPanelSpeaker;

/// <summary>
/// Resolves translated UI text and applies accessibility metadata.
/// Configuration serialization remains culture-invariant and does not use this
/// service.
/// </summary>
internal static class UiText
{
  private static readonly ResourceManager ResourceManager = new(
    "AgentPanelSpeaker.Resources.Strings",
    typeof(UiText).Assembly);

  public static string Get(string key)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ??
      throw new InvalidOperationException($"Missing UI resource '{key}'.");
  }

  public static string? GetOptional(string key)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    return ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
  }

  public static string Format(string key, params object[] arguments)
  {
    return string.Format(
      CultureInfo.CurrentUICulture,
      Get(key),
      arguments);
  }

  public static void Apply(
    Control control,
    string resourcePrefix,
    ToolTip? toolTip = null)
  {
    ArgumentNullException.ThrowIfNull(control);
    ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);

    control.AccessibleName = Get($"{resourcePrefix}.Name");
    control.AccessibleDescription =
      GetOptional($"{resourcePrefix}.Description") ?? string.Empty;

    string? text = GetOptional($"{resourcePrefix}.Text");
    if (text is not null)
    {
      control.Text = text;
    }

    string? tip = GetOptional($"{resourcePrefix}.Tooltip");
    if (toolTip is not null && tip is not null)
    {
      toolTip.SetToolTip(control, tip);
    }
  }
}
