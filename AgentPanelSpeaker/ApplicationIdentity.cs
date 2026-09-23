using System.Reflection;

namespace AgentPanelSpeaker;

/// <summary>
/// Provides application identity derived from build metadata.
/// </summary>
internal static class ApplicationIdentity
{
  private static readonly string ResolvedVersion = ResolveVersion();

  /// <summary>
  /// Gets the authoritative application semantic version.
  /// </summary>
  public static string Version => ResolvedVersion;

  /// <summary>
  /// Gets the main-window title using the authoritative application version.
  /// </summary>
  public static string WindowTitle => $"Agent Panel Speaker v{Version}";

  private static string ResolveVersion()
  {
    AssemblyInformationalVersionAttribute attribute =
      typeof(ApplicationIdentity).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>() ??
      throw new InvalidOperationException(
        "Application informational version metadata is missing.");

    if (string.IsNullOrWhiteSpace(attribute.InformationalVersion))
    {
      throw new InvalidOperationException(
        "Application informational version metadata is empty.");
    }

    return attribute.InformationalVersion;
  }
}
