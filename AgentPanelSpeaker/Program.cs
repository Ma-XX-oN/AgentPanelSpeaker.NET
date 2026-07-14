namespace AgentPanelSpeaker;

internal static class Program
{
  /// <summary>
  /// Starts the Windows Forms application.
  /// </summary>
  [STAThread]
  private static void Main()
  {
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
  }
}
