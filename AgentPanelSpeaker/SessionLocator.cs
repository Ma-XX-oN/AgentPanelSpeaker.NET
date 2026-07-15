namespace AgentPanelSpeaker;

/// <summary>
/// Locates active Claude and Codex JSONL session files.
/// </summary>
internal static class SessionLocator
{
  /// <summary>
  /// Finds the most recently written session for the requested source.
  /// </summary>
  /// <param name="source">Requested source or Auto.</param>
  /// <returns>The newest available session.</returns>
  public static LocatedSession FindLatest(AgentSource source)
  {
    IEnumerable<LocatedSession> candidates = source switch
    {
      AgentSource.Claude => EnumerateClaudeSessions(),
      AgentSource.Codex => EnumerateCodexSessions(),
      AgentSource.Auto => EnumerateClaudeSessions()
        .Concat(EnumerateCodexSessions()),
      _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };

    LocatedSession? latest = candidates
      .OrderByDescending(candidate => candidate.LastWriteUtc)
      .FirstOrDefault();

    return latest ?? throw new FileNotFoundException(
      $"No {source} session JSONL file was found.");
  }

  /// <summary>
  /// Builds session metadata for a user-selected JSONL file.
  /// </summary>
  /// <param name="path">Absolute or relative JSONL path.</param>
  /// <param name="requestedSource">Preferred source or Auto.</param>
  /// <returns>Resolved session metadata.</returns>
  public static LocatedSession FromPath(
    string path,
    AgentSource requestedSource)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);

    string fullPath = Path.GetFullPath(path);
    var info = new FileInfo(fullPath);
    if (!info.Exists)
    {
      throw new FileNotFoundException("The selected JSONL file does not exist.", fullPath);
    }

    AgentSource detectedSource = DetectSource(fullPath);
    if (requestedSource != AgentSource.Auto &&
        requestedSource != detectedSource)
    {
      throw new InvalidDataException(
        $"The selected file is {detectedSource}, not {requestedSource}.");
    }

    AgentSource source = requestedSource == AgentSource.Auto
      ? detectedSource
      : requestedSource;
    return new LocatedSession(
      source,
      info.FullName,
      info.LastWriteTimeUtc,
      info.Length);
  }

  /// <summary>
  /// Detects the source format from path conventions and initial records.
  /// </summary>
  /// <param name="path">Session JSONL path.</param>
  /// <returns>Claude or Codex.</returns>
  public static AgentSource DetectSource(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);

    string normalized = Path.GetFullPath(path)
      .Replace('/', '\\');
    if (normalized.Contains("\\.claude\\projects\\", StringComparison.OrdinalIgnoreCase))
    {
      return AgentSource.Claude;
    }

    if (normalized.Contains("\\.codex\\sessions\\", StringComparison.OrdinalIgnoreCase))
    {
      return AgentSource.Codex;
    }

    foreach (string line in File.ReadLines(path).Take(32))
    {
      AgentSource? detected = JsonlRecordExtractor.DetectSource(line);
      if (detected is not null)
      {
        return detected.Value;
      }
    }

    throw new InvalidDataException(
      "The selected file does not contain a recognizable Claude or Codex record.");
  }

  /// <summary>
  /// Enumerates Claude project sessions.
  /// </summary>
  private static IEnumerable<LocatedSession> EnumerateClaudeSessions()
  {
    string config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
      ?? Path.Combine(GetUserHome(), ".claude");
    string projects = Path.Combine(config, "projects");
    return EnumerateSessions(projects, AgentSource.Claude);
  }

  /// <summary>
  /// Enumerates Codex rollout/session files.
  /// </summary>
  private static IEnumerable<LocatedSession> EnumerateCodexSessions()
  {
    string home = Environment.GetEnvironmentVariable("CODEX_HOME")
      ?? Path.Combine(GetUserHome(), ".codex");
    string sessions = Path.Combine(home, "sessions");
    return EnumerateSessions(sessions, AgentSource.Codex);
  }

  /// <summary>
  /// Safely enumerates JSONL files under one session root.
  /// </summary>
  private static IEnumerable<LocatedSession> EnumerateSessions(
    string root,
    AgentSource source)
  {
    if (!Directory.Exists(root))
    {
      return Array.Empty<LocatedSession>();
    }

    var sessions = new List<LocatedSession>();
    try
    {
      foreach (string path in Directory.EnumerateFiles(
        root,
        "*.jsonl",
        SearchOption.AllDirectories))
      {
        try
        {
          var info = new FileInfo(path);
          sessions.Add(new LocatedSession(
            source,
            info.FullName,
            info.LastWriteTimeUtc,
            info.Length));
        }
        catch (IOException)
        {
          // A session can rotate while the directory is being enumerated.
        }
        catch (UnauthorizedAccessException)
        {
          // Skip paths that cannot be inspected.
        }
      }
    }
    catch (IOException)
    {
      return sessions;
    }
    catch (UnauthorizedAccessException)
    {
      return sessions;
    }

    return sessions;
  }

  /// <summary>
  /// Gets the current user's home directory.
  /// </summary>
  private static string GetUserHome()
  {
    string home = Environment.GetFolderPath(
      Environment.SpecialFolder.UserProfile);
    if (string.IsNullOrWhiteSpace(home))
    {
      throw new InvalidOperationException(
        "The current user profile directory could not be determined.");
    }

    return home;
  }
}
