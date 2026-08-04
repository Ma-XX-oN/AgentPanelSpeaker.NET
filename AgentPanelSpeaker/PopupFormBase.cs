namespace AgentPanelSpeaker;

/// <summary>
/// Provides shared keyboard and native-close routing for titleless popup forms.
/// </summary>
internal abstract class PopupFormBase : Form
{
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
}
