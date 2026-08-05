using Microsoft.CSharp.RuntimeBinder;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Writes structured diagnostics for session selection, JSONL extraction,
/// speech emission, and per-monitor DPI changes.
/// </summary>
internal static class DiagnosticLog
{
  private const int SelectItemFlags = 0x1 | 0x4 | 0x8 | 0x10;

  #pragma warning disable SYSLIB1054
  [DllImport("user32.dll")]
  private static extern bool SetForegroundWindow(IntPtr window);
  #pragma warning restore SYSLIB1054
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
      version = "27",
      processId = Environment.ProcessId,
      processPath = Environment.ProcessPath,
      osVersion = Environment.OSVersion.VersionString,
      framework = Environment.Version.ToString(),
      is64BitProcess = Environment.Is64BitProcess,
      commandLine = Environment.CommandLine,
      claudeConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),
      codexHome = Environment.GetEnvironmentVariable("CODEX_HOME")
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
  /// Writes complete exception diagnostics synchronously.
  /// </summary>
  public static void WriteException(
    string eventName,
    Exception? exception,
    string source,
    bool isTerminating,
    object? rawException = null)
  {
    Write(eventName, new
    {
      source,
      isTerminating,
      exceptionType = exception?.GetType().FullName,
      exception?.Message,
      stackTrace = exception?.StackTrace,
      exceptionText = exception?.ToString(),
      innerExceptions = EnumerateInnerExceptions(exception),
      rawExceptionType = rawException?.GetType().FullName,
      rawExceptionText = rawException?.ToString()
    });
  }

  private static object[] EnumerateInnerExceptions(Exception? exception)
  {
    var innerExceptions = new List<object>();
    for (Exception? current = exception?.InnerException;
         current is not null;
         current = current.InnerException)
    {
      innerExceptions.Add(new
      {
        type = current.GetType().FullName,
        current.Message,
        current.StackTrace,
        text = current.ToString()
      });
    }
    return innerExceptions.ToArray();
  }

  /// <summary>
  /// Opens File Explorer with the current log selected.
  /// </summary>
  public static void OpenCurrentLogInExplorer()
  {
    Initialize();
    var thread = new Thread(OpenCurrentLogInExplorerWorker)
    {
      IsBackground = true,
      Name = "AgentPanelSpeaker Explorer reuse"
    };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
  }

  private static void OpenCurrentLogInExplorerWorker()
  {
    try
    {
      if (TrySelectInExistingExplorerWindow())
      {
        return;
      }

      Process.Start(new ProcessStartInfo
      {
        FileName = "explorer.exe",
        Arguments = $"/select,\"{CurrentLogPath}\"",
        UseShellExecute = true
      });
    }
    catch (Exception exception)
    {
      Write("diagnostic.explorer_open_failed", new
      {
        exception = exception.ToString()
      });
    }
  }

  /// <summary>
  /// Reuses an Explorer window, preferring one already showing the log folder.
  /// </summary>
  private static bool TrySelectInExistingExplorerWindow()
  {
    object? shell = null;
    object? windows = null;
    try
    {
      Type? shellType = Type.GetTypeFromProgID("Shell.Application");
      if (shellType is null)
      {
        return false;
      }

      shell = Activator.CreateInstance(shellType);
      if (shell is null)
      {
        return false;
      }
      windows = ((dynamic)shell).Windows();
      if (windows is null)
      {
        return false;
      }
      int count = Convert.ToInt32(((dynamic)windows).Count);
      string directory = Path.GetDirectoryName(CurrentLogPath) ?? string.Empty;
      string fileName = Path.GetFileName(CurrentLogPath);

      for (int index = 0; index < count; ++index)
      {
        if (TryUseExplorerWindow(
              windows,
              index,
              directory,
              fileName,
              navigate: false))
        {
          return true;
        }
      }

      for (int index = 0; index < count; ++index)
      {
        if (TryUseExplorerWindow(
              windows,
              index,
              directory,
              fileName,
              navigate: true))
        {
          return true;
        }
      }
    }
    catch (Exception exception) when (
      exception is COMException or InvalidCastException or
      RuntimeBinderException or ArgumentException)
    {
      Write("diagnostic.explorer_reuse_failed", new
      {
        exception = exception.ToString()
      });
    }
    finally
    {
      ReleaseComObject(windows);
      ReleaseComObject(shell);
    }
    return false;
  }

  private static bool TryUseExplorerWindow(
    object windows,
    int index,
    string directory,
    string fileName,
    bool navigate)
  {
    object? window = null;
    try
    {
      window = ((dynamic)windows).Item(index);
      if (window is null || !IsExplorerWindow(window))
      {
        return false;
      }

      if (!IsWindowAtDirectory(window, directory))
      {
        if (!navigate)
        {
          return false;
        }
        ((dynamic)window).Navigate2(directory);
        if (!WaitForExplorerDirectory(window, directory))
        {
          return false;
        }
      }

      return SelectExplorerItem(window, fileName);
    }
    catch (Exception exception) when (
      exception is COMException or InvalidCastException or
      RuntimeBinderException or ArgumentException)
    {
      _ = exception;
      return false;
    }
    finally
    {
      ReleaseComObject(window);
    }
  }

  private static bool IsExplorerWindow(object? window)
  {
    if (window is null)
    {
      return false;
    }
    string fullName = Convert.ToString(((dynamic)window).FullName) ??
      string.Empty;
    return string.Equals(
      Path.GetFileName(fullName),
      "explorer.exe",
      StringComparison.OrdinalIgnoreCase);
  }

  private static bool WaitForExplorerDirectory(
    object window,
    string directory)
  {
    DateTime deadline = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < deadline)
    {
      if (!Convert.ToBoolean(((dynamic)window).Busy) &&
          IsWindowAtDirectory(window, directory))
      {
        return true;
      }
      Thread.Sleep(50);
    }
    return IsWindowAtDirectory(window, directory);
  }

  private static bool IsWindowAtDirectory(object window, string directory)
  {
    string locationUrl = Convert.ToString(((dynamic)window).LocationURL) ??
      string.Empty;
    if (!Uri.TryCreate(locationUrl, UriKind.Absolute, out Uri? location) ||
        !location.IsFile)
    {
      return false;
    }
    return string.Equals(
      Path.GetFullPath(location.LocalPath).TrimEnd(Path.DirectorySeparatorChar),
      Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
      StringComparison.OrdinalIgnoreCase);
  }

  private static bool SelectExplorerItem(object window, string fileName)
  {
    object? document = null;
    object? folder = null;
    object? item = null;
    try
    {
      document = ((dynamic)window).Document;
      folder = ((dynamic)document).Folder;
      item = ((dynamic)folder).ParseName(fileName);
      if (item is null)
      {
        return false;
      }
      ((dynamic)document).SelectItem(item, SelectItemFlags);
      long handle = Convert.ToInt64(((dynamic)window).HWND);
      _ = SetForegroundWindow(new IntPtr(handle));
      return true;
    }
    finally
    {
      ReleaseComObject(item);
      ReleaseComObject(folder);
      ReleaseComObject(document);
    }
  }

  private static void ReleaseComObject(object? value)
  {
    if (value is not null && Marshal.IsComObject(value))
    {
      _ = Marshal.FinalReleaseComObject(value);
    }
  }

  /// <summary>
  /// Stores one structured log record.
  /// </summary>
  private sealed record DiagnosticRecord(
    DateTime Utc,
    int ThreadId,
    string Event,
    object? Data);
}
