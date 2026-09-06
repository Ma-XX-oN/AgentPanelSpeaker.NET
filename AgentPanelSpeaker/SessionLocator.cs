using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Locates active Claude and Codex JSONL session files and resolves titles.
/// </summary>
internal static partial class SessionLocator
{
  private const int MaximumTitleLength = 160;
  private const int MaximumClaudeTitleRecords = 4096;

  private static readonly ConcurrentDictionary<string, string>
    ClaudeTitleCache = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Finds the most recently written session for the requested source.
  /// </summary>
  /// <param name="source">Requested source or Auto.</param>
  /// <returns>The newest available session with display metadata.</returns>
  public static LocatedSession FindLatest(AgentSource source)
  {
    IEnumerable<SessionFile> candidates = source switch
    {
      AgentSource.Claude => EnumerateClaudeSessions(),
      AgentSource.Codex => EnumerateCodexSessions(),
      AgentSource.Auto => EnumerateClaudeSessions()
        .Concat(EnumerateCodexSessions()),
      _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };

    SessionFile? latest = candidates
      .OrderByDescending(candidate => candidate.LastWriteUtc)
      .FirstOrDefault();

    return latest is null
      ? throw new FileNotFoundException(
        $"No {source} session JSONL file was found.")
      : Enrich(latest);
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
      throw new FileNotFoundException(
        "The selected JSONL file does not exist.",
        fullPath);
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
    return Enrich(new SessionFile(
      source,
      info.FullName,
      info.LastWriteTimeUtc,
      info.Length));
  }

  /// <summary>
  /// Gets the most useful directory for the JSONL file picker.
  /// </summary>
  /// <param name="source">Selected source.</param>
  /// <param name="currentPath">Currently selected session path.</param>
  /// <returns>An existing initial directory.</returns>
  public static string GetBrowseInitialDirectory(
    AgentSource source,
    string? currentPath)
  {
    if (!string.IsNullOrWhiteSpace(currentPath))
    {
      string? currentDirectory = Path.GetDirectoryName(currentPath);
      if (!string.IsNullOrWhiteSpace(currentDirectory) &&
          Directory.Exists(currentDirectory))
      {
        return currentDirectory;
      }
    }

    string root = source switch
    {
      AgentSource.Claude => GetClaudeProjectsRoot(),
      AgentSource.Codex => GetCodexSessionsRoot(),
      AgentSource.Auto => GetMostUsefulAutoRoot(),
      _ => GetUserHome()
    };
    return Directory.Exists(root) ? root : GetUserHome();
  }

  /// <summary>
  /// Detects the source format from path conventions and initial records.
  /// </summary>
  /// <param name="path">Session JSONL path.</param>
  /// <returns>Claude or Codex.</returns>
  public static AgentSource DetectSource(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);

    string normalized = Path.GetFullPath(path).Replace('/', '\\');
    if (normalized.Contains(
          "\\.claude\\projects\\",
          StringComparison.OrdinalIgnoreCase))
    {
      return AgentSource.Claude;
    }

    if (normalized.Contains(
          "\\.codex\\sessions\\",
          StringComparison.OrdinalIgnoreCase))
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
      "The selected file does not contain a recognizable Claude or Codex " +
      "record.");
  }

  /// <summary>
  /// Adds a canonical identifier and readable title to one session file.
  /// </summary>
  private static LocatedSession Enrich(SessionFile file)
  {
    string sessionId = file.Source == AgentSource.Codex
      ? GetCodexSessionId(file.Path)
      : Path.GetFileNameWithoutExtension(file.Path);
    string title = file.Source switch
    {
      AgentSource.Codex => ReadCodexTitle(file.Path, sessionId),
      AgentSource.Claude => ReadClaudeTitle(file.Path),
      _ => string.Empty
    };

    return new LocatedSession(
      file.Source,
      file.Path,
      file.LastWriteUtc,
      file.Length,
      sessionId,
      title);
  }

  /// <summary>
  /// Reads a Codex thread name, falling back to its first user message.
  /// </summary>
  private static string ReadCodexTitle(string sessionPath, string sessionId)
  {
    try
    {
      using var client = new AIConversationCoreClient();
      AIConversationProjection projection = client.Project(
        AgentSource.Codex,
        ReadSharedLines(sessionPath).ToArray(),
        new AIConversationCoreProjectOptions(
          CodexSessionIndexPath: GetCodexSessionIndexPath()));
      string? title = projection.SessionMetadata?.Title;
      return string.IsNullOrWhiteSpace(title)
        ? sessionId
        : LimitTitle(title.Trim());
    }
    catch (Exception exception) when (
      exception is IOException or
      UnauthorizedAccessException or
      JsonException or
      InvalidOperationException or
      ArgumentException)
    {
      DiagnosticLog.Write("session.codex_title_failed", new
      {
        sessionPath,
        sessionId,
        exception = exception.Message
      });
      return sessionId;
    }
  }

  /// <summary>
  /// Gets the optional caller-discovered Codex session-index path.
  /// </summary>
  internal static string? GetCodexSessionIndexPath()
  {
    string path = Path.Combine(GetCodexHome(), "session_index.jsonl");
    return File.Exists(path) ? path : null;
  }

  /// <summary>
  /// Reads Claude's session title, with conversational fallbacks.
  /// </summary>
  private static string ReadClaudeTitle(string sessionPath)
  {
    if (ClaudeTitleCache.TryGetValue(
          sessionPath,
          out string? cachedTitle) &&
        cachedTitle is not null)
    {
      return cachedTitle;
    }

    string userFallback = string.Empty;
    string assistantFallback = string.Empty;
    try
    {
      foreach (string line in ReadSharedLines(sessionPath)
        .Take(MaximumClaudeTitleRecords))
      {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("isSidechain", out JsonElement sidechain) &&
            sidechain.ValueKind == JsonValueKind.True)
        {
          continue;
        }

        if (!TryGetString(root, "type", out string recordType))
        {
          continue;
        }

        if (recordType.Equals("ai-title", StringComparison.Ordinal) &&
            TryGetString(root, "aiTitle", out string resolvedTitle))
        {
          string candidate = FirstMeaningfulLine(resolvedTitle);
          if (candidate.Length != 0)
          {
            string title = LimitTitle(candidate);
            ClaudeTitleCache[sessionPath] = title;
            return title;
          }
          continue;
        }

        if (!root.TryGetProperty("message", out JsonElement message) ||
            !message.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.Array)
        {
          continue;
        }

        if (recordType.Equals("assistant", StringComparison.Ordinal) &&
            TryGetString(message, "model", out string model) &&
            model.Equals("<synthetic>", StringComparison.Ordinal))
        {
          continue;
        }

        foreach (JsonElement block in content.EnumerateArray())
        {
          if (!TryGetString(block, "type", out string blockType) ||
              !blockType.Equals("text", StringComparison.Ordinal) ||
              !TryGetString(block, "text", out string text))
          {
            continue;
          }

          string cleaned = SystemTagRegex().Replace(text, string.Empty).Trim();
          string title = FirstMeaningfulLine(cleaned);
          if (title.Length == 0)
          {
            continue;
          }

          if (recordType.Equals("user", StringComparison.Ordinal) &&
              userFallback.Length == 0)
          {
            userFallback = LimitTitle(title);
          }

          if (recordType.Equals("assistant", StringComparison.Ordinal) &&
              assistantFallback.Length == 0)
          {
            assistantFallback = LimitTitle(title);
          }
        }
      }
    }
    catch (Exception exception) when (
      exception is IOException or
      UnauthorizedAccessException or
      JsonException)
    {
    }

    if (userFallback.Length != 0)
    {
      return userFallback;
    }
    return assistantFallback.Length == 0
      ? Path.GetFileNameWithoutExtension(sessionPath)
      : assistantFallback;
  }

  /// <summary>
  /// Extracts the canonical UUID from a Codex rollout filename.
  /// </summary>
  private static string GetCodexSessionId(string path)
  {
    string stem = Path.GetFileNameWithoutExtension(path);
    if (!stem.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase))
    {
      return stem;
    }

    string[] parts = stem.Split('-');
    return parts.Length > 6 ? string.Join('-', parts.Skip(6)) : stem;
  }

  /// <summary>
  /// Enumerates Claude project sessions.
  /// </summary>
  private static IEnumerable<SessionFile> EnumerateClaudeSessions()
  {
    return EnumerateSessions(GetClaudeProjectsRoot(), AgentSource.Claude);
  }

  /// <summary>
  /// Enumerates Codex rollout/session files.
  /// </summary>
  private static IEnumerable<SessionFile> EnumerateCodexSessions()
  {
    return EnumerateSessions(GetCodexSessionsRoot(), AgentSource.Codex);
  }

  /// <summary>
  /// Safely enumerates JSONL files under one session root.
  /// </summary>
  private static IEnumerable<SessionFile> EnumerateSessions(
    string root,
    AgentSource source)
  {
    if (!Directory.Exists(root))
    {
      return Array.Empty<SessionFile>();
    }

    var sessions = new List<SessionFile>();
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
          sessions.Add(new SessionFile(
            source,
            info.FullName,
            info.LastWriteTimeUtc,
            info.Length));
        }
        catch (Exception exception) when (
          exception is IOException or UnauthorizedAccessException)
        {
        }
      }
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException)
    {
      return sessions;
    }

    return sessions;
  }

  /// <summary>
  /// Reads a shared JSONL file without blocking its writer.
  /// </summary>
  private static IEnumerable<string> ReadSharedLines(string path)
  {
    using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(
      stream,
      System.Text.Encoding.UTF8,
      detectEncodingFromByteOrderMarks: true,
      bufferSize: 64 * 1024,
      leaveOpen: false);

    while (reader.ReadLine() is string line)
    {
      if (!string.IsNullOrWhiteSpace(line))
      {
        yield return line;
      }
    }
  }

  /// <summary>
  /// Gets a JSON string property when present.
  /// </summary>
  private static bool TryGetString(
    JsonElement element,
    string propertyName,
    out string value)
  {
    value = string.Empty;
    if (!element.TryGetProperty(propertyName, out JsonElement property) ||
        property.ValueKind != JsonValueKind.String)
    {
      return false;
    }

    value = property.GetString() ?? string.Empty;
    return true;
  }

  /// <summary>
  /// Gets the first non-empty trimmed line.
  /// </summary>
  private static string FirstMeaningfulLine(string text)
  {
    foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
    {
      string candidate = line.Trim();
      if (candidate.Length != 0 && !IsMarkdownFence(candidate))
      {
        return candidate;
      }
    }

    return string.Empty;
  }

  /// <summary>
  /// Returns whether one complete line is only a Markdown fence marker.
  /// </summary>
  private static bool IsMarkdownFence(string line)
  {
    return line.StartsWith("```", StringComparison.Ordinal) ||
      line.StartsWith("~~~", StringComparison.Ordinal);
  }

  /// <summary>
  /// Bounds a session title for display.
  /// </summary>
  private static string LimitTitle(string title)
  {
    return title.Length <= MaximumTitleLength
      ? title
      : title[..MaximumTitleLength] + "…";
  }

  /// <summary>
  /// Gets the Claude projects root.
  /// </summary>
  private static string GetClaudeProjectsRoot()
  {
    string config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
      ?? Path.Combine(GetUserHome(), ".claude");
    return Path.Combine(config, "projects");
  }

  /// <summary>
  /// Gets the Codex home directory.
  /// </summary>
  private static string GetCodexHome()
  {
    return Environment.GetEnvironmentVariable("CODEX_HOME")
      ?? Path.Combine(GetUserHome(), ".codex");
  }

  /// <summary>
  /// Gets the Codex sessions root.
  /// </summary>
  private static string GetCodexSessionsRoot()
  {
    return Path.Combine(GetCodexHome(), "sessions");
  }

  /// <summary>
  /// Selects the newest existing source root for Auto browsing.
  /// </summary>
  private static string GetMostUsefulAutoRoot()
  {
    try
    {
      return Path.GetDirectoryName(FindLatest(AgentSource.Auto).Path)
        ?? GetUserHome();
    }
    catch (Exception exception) when (
      exception is IOException or
      UnauthorizedAccessException or
      InvalidOperationException)
    {
      string codex = GetCodexSessionsRoot();
      return Directory.Exists(codex) ? codex : GetClaudeProjectsRoot();
    }
  }

  /// <summary>
  /// Gets the current user's home directory.
  /// </summary>
  private static string GetUserHome()
  {
    string home = Environment.GetFolderPath(
      Environment.SpecialFolder.UserProfile);
    return string.IsNullOrWhiteSpace(home)
      ? throw new InvalidOperationException(
        "The current user profile directory could not be determined.")
      : home;
  }


  [GeneratedRegex(
    @"<(?:ide_opened_file|ide_selection|system[-_]reminder|system|env|" +
    @"claude_background_info|user[-_]prompt[-_]submit[-_]hook|" +
    @"command[-_]name|antml:[a-z_]+)[^>]*>.*?</[^>]+>",
    RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex SystemTagRegex();

  /// <summary>
  /// Stores file-system session data before title extraction.
  /// </summary>
  private sealed record SessionFile(
    AgentSource Source,
    string Path,
    DateTime LastWriteUtc,
    long Length);
}
