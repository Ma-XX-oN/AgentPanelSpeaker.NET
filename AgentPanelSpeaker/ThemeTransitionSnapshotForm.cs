using System.Drawing;

namespace AgentPanelSpeaker;

/// <summary>
/// Displays a non-activating bitmap snapshot over the main window while a
/// theme transition mutates and redraws the real control hierarchy.
/// </summary>
internal sealed class ThemeTransitionSnapshotForm : Form
{
  private const int WsExNoActivate = 0x08000000;
  private const int WsExToolWindow = 0x00000080;
  private const int WsExTransparent = 0x00000020;
  private const int WmNcHitTest = 0x0084;
  private const int HtTransparent = -1;

  private readonly Bitmap _snapshot;

  /// <summary>
  /// Creates an overlay containing the supplied snapshot at the exact screen
  /// bounds of the window being covered.
  /// </summary>
  public ThemeTransitionSnapshotForm(Bitmap snapshot, Rectangle bounds)
  {
    _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    AutoScaleMode = AutoScaleMode.None;
    FormBorderStyle = FormBorderStyle.None;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.Manual;
    Bounds = bounds;
  }

  /// <inheritdoc />
  protected override bool ShowWithoutActivation => true;

  /// <inheritdoc />
  protected override CreateParams CreateParams
  {
    get
    {
      CreateParams parameters = base.CreateParams;
      parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTransparent;
      return parameters;
    }
  }

  /// <inheritdoc />
  protected override void OnPaint(PaintEventArgs eventArgs)
  {
    eventArgs.Graphics.DrawImageUnscaled(_snapshot, Point.Empty);
  }

  /// <inheritdoc />
  protected override void WndProc(ref Message message)
  {
    if (message.Msg == WmNcHitTest)
    {
      message.Result = new IntPtr(HtTransparent);
      return;
    }

    base.WndProc(ref message);
  }

  /// <inheritdoc />
  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _snapshot.Dispose();
    }

    base.Dispose(disposing);
  }
}
