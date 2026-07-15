using System.Diagnostics;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Writes structured diagnostics for UI Automation tree traversal, transcript
/// tracking, speech emission, and per-monitor DPI changes.
/// </summary>
internal static class DiagnosticLog
{
  private static readonly object Sync = new();
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = false
  };
  private static readonly string LogDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AgentPanelSpeaker",
    "Logs");
  private static readonly string CurrentLogPath = Path.Combine(
    LogDirectory,
    $"AgentPanelSpeaker-{DateTime.Now:yyyyMMdd-HHmmss}-" +
    $"{Environment.ProcessId}.jsonl");
  private static bool _initialized;

  /// <summary>
  /// Gets the current diagnostic log path.
  /// </summary>
  public static string FilePath => CurrentLogPath;

  /// <summary>
  /// Creates the log directory and writes the application-start record.
  /// </summary>
  public static void Initialize()
  {
    lock (Sync)
    {
      if (_initialized)
      {
        return;
      }

      Directory.CreateDirectory(LogDirectory);
      _initialized = true;
    }

    Write("app.start", new
    {
      version = 15,
      processId = Environment.ProcessId,
      processPath = Environment.ProcessPath,
      osVersion = Environment.OSVersion.VersionString,
      framework = Environment.Version.ToString(),
      is64BitProcess = Environment.Is64BitProcess,
      commandLine = Environment.CommandLine
    });
  }

  /// <summary>
  /// Writes one JSON Lines diagnostic record.
  /// </summary>
  /// <param name="eventName">Stable event identifier.</param>
  /// <param name="data">Event-specific serializable data.</param>
  public static void Write(string eventName, object? data = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

    try
    {
      if (!_initialized)
      {
        Initialize();
      }

      var record = new DiagnosticRecord(
        DateTime.UtcNow,
        Environment.CurrentManagedThreadId,
        eventName,
        data);
      string json = JsonSerializer.Serialize(record, JsonOptions);

      lock (Sync)
      {
        File.AppendAllText(
          CurrentLogPath,
          json + Environment.NewLine,
          System.Text.Encoding.UTF8);
      }
    }
    catch
    {
      // Diagnostics must never stop monitoring or speech.
    }
  }

  /// <summary>
  /// Opens File Explorer with the current log selected.
  /// </summary>
  public static void OpenCurrentLogInExplorer()
  {
    Initialize();
    Process.Start(new ProcessStartInfo
    {
      FileName = "explorer.exe",
      Arguments = $"/select,\"{CurrentLogPath}\"",
      UseShellExecute = true
    });
  }

  /// <summary>
  /// Stores one structured log record.
  /// </summary>
  /// <param name="Utc">UTC event time.</param>
  /// <param name="ThreadId">Managed thread identifier.</param>
  /// <param name="Event">Stable event identifier.</param>
  /// <param name="Data">Event-specific data.</param>
  private sealed record DiagnosticRecord(
    DateTime Utc,
    int ThreadId,
    string Event,
    object? Data);
}
