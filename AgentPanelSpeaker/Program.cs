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

    if (args.Length >= 1 && args[0] == "--test")
    {
      if (args.Length > 2)
      {
        Console.Error.WriteLine("Usage: AgentPanelSpeaker.exe --test [suite]");
        Environment.ExitCode = 2;
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "extended", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = ExtendedRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "additional", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = AdditionalRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "environment", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = EnvironmentRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2)
      {
        Environment.ExitCode = RegressionTestRunner.Run(args[1]);
        return;
      }

      int primary = RegressionTestRunner.Run();
      int extended = ExtendedRegressionTestRunner.Run();
      int additional = AdditionalRegressionTestRunner.Run();
      int environment = EnvironmentRegressionTestRunner.Run();
      Environment.ExitCode = primary == 0 &&
                             extended == 0 &&
                             additional == 0 &&
                             environment == 0
        ? 0
        : 1;
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
