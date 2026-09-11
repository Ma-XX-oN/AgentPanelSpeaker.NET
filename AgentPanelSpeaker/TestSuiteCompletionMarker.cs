namespace AgentPanelSpeaker;

/// <summary>
/// Defines the machine-readable completion boundary for regression suites.
/// A successful process exit is not sufficient evidence if the suite never
/// reached this marker.
/// </summary>
internal static class TestSuiteCompletionMarker
{
  private const string Prefix = "TEST-SUITE-COMPLETE";

  /// <summary>
  /// Formats the final marker emitted after one selected suite has completed.
  /// </summary>
  /// <param name="suite">Stable suite name.</param>
  /// <param name="exitCode">Final suite exit code.</param>
  /// <returns>The marker written to standard output.</returns>
  public static string Format(string suite, int exitCode)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(suite);
    return $"{Prefix} {suite} exit={exitCode}";
  }

  /// <summary>
  /// Returns whether captured child output proves that the requested suite
  /// reached its own successful completion boundary.
  /// </summary>
  /// <param name="output">Captured child standard output.</param>
  /// <param name="suite">Requested suite name.</param>
  /// <param name="exitCode">Observed child process exit code.</param>
  /// <returns>True only for exit code zero plus the exact success marker.</returns>
  public static bool IsSuccessful(string output, string suite, int exitCode)
  {
    ArgumentNullException.ThrowIfNull(output);
    ArgumentException.ThrowIfNullOrWhiteSpace(suite);
    return exitCode == 0 && output
      .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
      .Any(line => string.Equals(
        line.Trim(),
        Format(suite, 0),
        StringComparison.Ordinal));
  }
}
