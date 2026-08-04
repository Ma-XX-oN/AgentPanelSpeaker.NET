namespace AgentPanelSpeaker;

internal static class Program
{
  private static int _externalTerminationRequested;

  /// <summary>
  /// Gets whether the process is being terminated by its console host.
  /// </summary>
  internal static bool ExternalTerminationRequested =>
    Volatile.Read(ref _externalTerminationRequested) != 0;

  /// <summary>
  /// Starts the Windows Forms application.
  /// </summary>
  [STAThread]
  private static void Main(string[] args)
  {
    if (args.Length == 1 && args[0] == "--regex-search-worker")
    {
      Environment.ExitCode = RegexSearchWorker.Run();
      return;
    }

    Console.CancelKeyPress += (_, eventArgs) =>
    {
      Volatile.Write(ref _externalTerminationRequested, 1);
      eventArgs.Cancel = false;
    };

    DiagnosticLog.Initialize();
    Application.ThreadException += (_, eventArgs) =>
      DiagnosticLog.Write("app.thread_exception", new
      {
        exception = eventArgs.Exception.ToString()
      });
    AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
      DiagnosticLog.Write("app.unhandled_exception", new
      {
        exception = eventArgs.ExceptionObject?.ToString(),
        eventArgs.IsTerminating
      });
    TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
    {
      DiagnosticLog.Write("app.unobserved_task_exception", new
      {
        exception = eventArgs.Exception.ToString()
      });
      eventArgs.SetObserved();
    };

    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
  }
}
