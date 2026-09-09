using System.Diagnostics;

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

      ApplicationConfiguration.Initialize();

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

      if (args.Length == 2 &&
          string.Equals(args[1], "core", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = CoreRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "speech-ordinals-focused", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = Issue24SpeechOrdinalRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "speech-ordinals-production", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = Issue24ProductionPathRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "speech-ordinals", StringComparison.OrdinalIgnoreCase))
      {
        int focused = RunIsolatedTestSuite("speech-ordinals-focused");
        int production = RunIsolatedTestSuite("speech-ordinals-production");
        Environment.ExitCode = focused == 0 && production == 0 ? 0 : 1;
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "user-context-speech", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = Issue26UserContextSpeechRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "live-end", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = Issue30LiveEndRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "rolled-back-visibility", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = Issue35RolledBackVisibilityRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "rolled-back-speech", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = Issue35SpeechTransitionRegressionTestRunner.Run();
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "output-oracle", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = RunWithWinFormsMessageLoop(
          Issue44IndependentOutputOracleRegressionTestRunner.Run);
        Console.WriteLine(
          $"TEST-SUITE-COMPLETE output-oracle exit={Environment.ExitCode}");
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
      int core = CoreRegressionTestRunner.Run();

      // UI-bearing and real-engine suites run in fresh processes. MainForm,
      // WebView2, and the speech worker own lifecycle-sensitive resources;
      // process isolation prevents one suite's teardown from contaminating the
      // next acceptance test.
      int speechOrdinalsFocused = RunIsolatedTestSuite("speech-ordinals-focused");
      int userContextSpeech = RunIsolatedTestSuite("user-context-speech");
      int environment = RunIsolatedTestSuite("environment");
      int speechOrdinalProduction = RunIsolatedTestSuite("speech-ordinals-production");
      int liveEnd = RunIsolatedTestSuite("live-end");
      int rolledBackVisibility = RunIsolatedTestSuite("rolled-back-visibility");
      int rolledBackSpeech = RunIsolatedTestSuite("rolled-back-speech");
      int independentOutputOracle = RunIsolatedTestSuite("output-oracle");

      Environment.ExitCode = primary == 0 &&
                             extended == 0 &&
                             additional == 0 &&
                             core == 0 &&
                             speechOrdinalsFocused == 0 &&
                             userContextSpeech == 0 &&
                             environment == 0 &&
                             speechOrdinalProduction == 0 &&
                             liveEnd == 0 &&
                             rolledBackVisibility == 0 &&
                             rolledBackSpeech == 0 &&
                             independentOutputOracle == 0
        ? 0
        : 1;
      Console.WriteLine($"TEST-SUITE-COMPLETE all exit={Environment.ExitCode}");
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

  /// <summary>
  /// Runs a browser-output test suite while an actual WinForms message loop is
  /// active. Production TranscriptView rendering awaits background preparation
  /// and must resume on its owning UI thread, just as it does in the real app.
  /// </summary>
  /// <param name="suite">Test suite to execute on the WinForms UI thread.</param>
  /// <returns>The suite exit code, or one if the harness itself fails.</returns>
  private static int RunWithWinFormsMessageLoop(Func<int> suite)
  {
    ArgumentNullException.ThrowIfNull(suite);

    int exitCode = 1;
    Exception? harnessFailure = null;
    using var host = new Form
    {
      Width = 1,
      Height = 1,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
    host.Shown += (_, _) => host.BeginInvoke(new Action(() =>
    {
      try
      {
        exitCode = suite();
      }
      catch (Exception exception)
      {
        harnessFailure = exception;
      }
      finally
      {
        host.Close();
      }
    }));

    Application.Run(host);
    if (harnessFailure is not null)
    {
      Console.Error.WriteLine("FAIL  test-harness/winforms-message-loop");
      Console.Error.WriteLine(
        $"      {harnessFailure.GetType().Name}: {harnessFailure.Message}");
      return 1;
    }
    return exitCode;
  }

  /// <summary>
  /// Runs one lifecycle-sensitive regression suite in a fresh copy of this
  /// executable. The child inherits stdout/stderr so CI retains normal evidence.
  /// </summary>
  private static int RunIsolatedTestSuite(string suite)
  {
    string? executable = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(executable))
    {
      Console.Error.WriteLine(
        $"FAIL  test-isolation/{suite}: current executable path is unavailable.");
      return 1;
    }

    using var process = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = executable,
        UseShellExecute = false
      }
    };
    process.StartInfo.ArgumentList.Add("--test");
    process.StartInfo.ArgumentList.Add(suite);
    if (!process.Start())
    {
      Console.Error.WriteLine($"FAIL  test-isolation/{suite}: process did not start.");
      return 1;
    }
    process.WaitForExit();
    return process.ExitCode;
  }
}
