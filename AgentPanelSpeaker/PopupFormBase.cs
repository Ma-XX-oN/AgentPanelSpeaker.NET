using System.Runtime.InteropServices;

namespace AgentPanelSpeaker;

/// <summary>
/// Provides shared keyboard and native-close routing for titleless popup forms.
/// </summary>
internal abstract class PopupFormBase : Form
{
  private static readonly IntPtr HwndTop = IntPtr.Zero;
  private const uint SwpNoSize = 0x0001;
  private const uint SwpNoMove = 0x0002;
  private const uint SwpNoActivate = 0x0010;
  private const uint SwpNoOwnerZOrder = 0x0200;

  /// <summary>
  /// Shows this popup as an owned window and explicitly places it above its
  /// owner without activating it.
  /// </summary>
  public void ShowAboveOwner(Form owner)
  {
    ArgumentNullException.ThrowIfNull(owner);
    Owner = owner;
    if (!Visible)
    {
      Show();
    }
    SetWindowPos(
      Handle,
      HwndTop,
      0,
      0,
      0,
      0,
      SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
  }

  /// <inheritdoc />
  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (HoverPopupController.HandleGlobalPopupKey(keyData, this))
    {
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  /// <inheritdoc />
  protected override void OnFormClosing(FormClosingEventArgs eventArgs)
  {
    if (eventArgs.CloseReason == CloseReason.UserClosing &&
        HoverPopupController.CloseDeepestGlobal(
          returnFocus: false,
          keyboardClose: true))
    {
      eventArgs.Cancel = true;
      return;
    }
    base.OnFormClosing(eventArgs);
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool SetWindowPos(
    IntPtr hWnd,
    IntPtr hWndInsertAfter,
    int x,
    int y,
    int cx,
    int cy,
    uint flags);
}
