namespace AgentPanelSpeaker;

internal static class Program
{
  /// <summary>
  /// Starts the Windows Forms application.
  /// </summary>
  [STAThread]
  private static void Main()
  {
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
