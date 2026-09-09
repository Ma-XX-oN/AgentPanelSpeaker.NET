namespace AgentPanelSpeaker;

/// <summary>
/// Owns one off-screen MainForm for UI acceptance tests and closes it through
/// the real FormClosing path before disposal. MainForm registers application-
/// wide message and system-event hooks that are detached by that production
/// close path; direct Dispose() would bypass that lifecycle contract.
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
  /// Runs the normal close lifecycle and then releases the form.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    if (!Form.IsDisposed)
    {
      Form.Close();
      Application.DoEvents();
      Form.Dispose();
    }
  }
}
