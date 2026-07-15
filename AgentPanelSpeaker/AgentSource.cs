namespace AgentPanelSpeaker;

/// <summary>
/// Identifies the JSONL session format to monitor.
/// </summary>
internal enum AgentSource
{
  Auto,
  Claude,
  Codex
}

/// <summary>
/// Describes one located session file.
/// </summary>
/// <param name="Source">Session JSONL format.</param>
/// <param name="Path">Absolute JSONL path.</param>
/// <param name="LastWriteUtc">Latest observed file write time.</param>
/// <param name="Length">Current file length in bytes.</param>
/// <param name="SessionId">Canonical session identifier.</param>
/// <param name="Title">Human-readable session title.</param>
internal sealed record LocatedSession(
  AgentSource Source,
  string Path,
  DateTime LastWriteUtc,
  long Length,
  string SessionId,
  string Title)
{
  /// <summary>
  /// Gets the preferred human-readable session label.
  /// </summary>
  public string DisplayName => string.IsNullOrWhiteSpace(Title)
    ? SessionId
    : Title;
}
