using System.Diagnostics;
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
  private const int ShownTimeoutMilliseconds = 10000;

  private bool _disposed;

  /// <summary>
  /// Creates and shows an off-screen production MainForm, then drains its
  /// initial Shown lifecycle before allowing a test to install fixture state.
  /// </summary>
  public MainFormTestLease()
  {
    Form = new MainForm
    {
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
    SuppressPersistedSessionRestore(Form);

    bool shown = false;
    void ShownObserved(object? sender, EventArgs eventArgs) => shown = true;
    Form.Shown += ShownObserved;
    try
    {
      Form.Show();
      _ = Form.Handle;
      var timer = Stopwatch.StartNew();
      while (!shown)
      {
        if (timer.ElapsedMilliseconds >= ShownTimeoutMilliseconds)
        {
          throw new TimeoutException(
            "Timed out waiting for the off-screen MainForm Shown lifecycle.");
        }
        Application.DoEvents();
        Thread.Sleep(1);
      }
    }
    finally
    {
      Form.Shown -= ShownObserved;
    }
  }

  /// <summary>
  /// Gets the production form owned by this lease.
  /// </summary>
  public MainForm Form { get; }

  /// <summary>
  /// Prevents the test host from asynchronously restoring a developer/user
  /// session when MainForm raises Shown. Acceptance fixtures install their own
  /// deterministic session only after that lifecycle has completed; allowing
  /// persisted state or a delayed Shown event here can race those fixtures and
  /// falsely look like a history rebuild.
  /// </summary>
  private static void SuppressPersistedSessionRestore(MainForm form)
  {
    FieldInfo manualPathField = typeof(MainForm).GetField(
      "_pathIsManual",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "MainForm manual-path field was not found.");
    FieldInfo sessionPathField = typeof(MainForm).GetField(
      "_sessionPathTextBox",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "MainForm session-path control was not found.");

    manualPathField.SetValue(form, false);
    if (sessionPathField.GetValue(form) is not TextBox sessionPath)
    {
      throw new InvalidOperationException(
        "MainForm session-path control had an unexpected type.");
    }
    sessionPath.Clear();
  }

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
