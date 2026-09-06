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
  /// Starts the Windows Forms application or a command-line worker/test mode.
  /// </summary>
  [STAThread]
  private static void Main(string[] args)
  {
    if (args.Length == 1 && args[0] == "--regex-search-worker")
    {
      Environment.ExitCode = RegexSearchWorker.Run();
      return;
    }

    if (args.Length == 1 && args[0] == "--test")
    {
      Environment.ExitCode = RegressionTestRunner.Run();
      return;
    }

    Console.CancelKeyPress += (_, eventArgs) =>
    {
      Volatile.Write(ref _externalTerminationRequested, 1);
      eventArgs.Cancel = false;
    };

    DiagnosticLog.Initialize();
    Application.SetUnhandledExceptionMode(
      UnhandledExceptionMode.CatchException);
    Application.ThreadException += (_, eventArgs) =>
      DiagnosticLog.WriteException(
        "app.thread_exception",
        eventArgs.Exception,
        source: "Windows Forms UI thread",
        isTerminating: false);
    AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
      DiagnosticLog.WriteException(
        "app.unhandled_exception",
        eventArgs.ExceptionObject as Exception,
        source: "AppDomain",
        isTerminating: eventArgs.IsTerminating,
        rawException: eventArgs.ExceptionObject);
    TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
    {
      DiagnosticLog.WriteException(
        "app.unobserved_task_exception",
        eventArgs.Exception,
        source: "TaskScheduler",
        isTerminating: false);
      eventArgs.SetObserved();
    };

    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
  }
}
