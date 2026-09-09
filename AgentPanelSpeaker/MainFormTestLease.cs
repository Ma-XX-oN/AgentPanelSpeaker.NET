using System.Reflection;

namespace AgentPanelSpeaker;

/// <summary>
/// Owns one off-screen MainForm for UI acceptance tests and tears it down
/// through the production MainFormClosing cleanup path before disposal.
/// MainForm registers application-wide message and system-event hooks that are
/// detached by that path; direct Dispose() would bypass the lifecycle contract.
/// </summary>
internal sealed class MainFormTestLease : IDisposable
{
  private bool _disposed;

  /// <summary>
  /// Creates and shows an off-screen production MainForm.
  /// </summary>
  public MainFormTestLease()
  {
    Form = new MainForm
    {
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
    Form.Show();
    _ = Form.Handle;
  }

  /// <summary>
  /// Gets the production form owned by this lease.
  /// </summary>
  public MainForm Form { get; }

  /// <summary>
  /// Runs the production immediate-termination cleanup and releases the form.
  /// The shutdown close reason bypasses the interactive save-settings prompt,
  /// which is not part of these acceptance tests, while exercising the same
  /// global-hook/resource teardown used by MainFormClosing.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    if (Form.IsDisposed)
    {
      return;
    }

    MethodInfo closing = typeof(MainForm).GetMethod(
      "MainFormClosing",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "MainForm production close handler was not found.");
    var eventArgs = new FormClosingEventArgs(
      CloseReason.WindowsShutDown,
      cancel: false);
    closing.Invoke(Form, new object?[] { Form, eventArgs });
    if (eventArgs.Cancel)
    {
      throw new InvalidOperationException(
        "MainForm production close handler cancelled immediate test teardown.");
    }

    Application.DoEvents();
    Form.Dispose();
  }
}
