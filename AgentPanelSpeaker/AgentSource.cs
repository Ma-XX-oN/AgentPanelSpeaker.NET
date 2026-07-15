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
internal sealed record LocatedSession(
  AgentSource Source,
  string Path,
  DateTime LastWriteUtc,
  long Length);
