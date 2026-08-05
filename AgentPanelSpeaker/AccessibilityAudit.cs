namespace AgentPanelSpeaker;

/// <summary>
/// Reports interactive controls that lack explicit accessible names.
/// </summary>
internal static class AccessibilityAudit
{
  public static void ReportMissing(Control root)
  {
#if DEBUG
    foreach (Control control in Enumerate(root))
    {
      if (!IsInteractive(control) || !string.IsNullOrWhiteSpace(control.AccessibleName))
      {
        continue;
      }

      DiagnosticLog.Write("accessibility.missing_name", new
      {
        form = root.GetType().Name,
        control = control.GetType().Name,
        control.Name,
        control.Text
      });
    }
#endif
  }

  private static IEnumerable<Control> Enumerate(Control root)
  {
    foreach (Control child in root.Controls)
    {
      yield return child;
      foreach (Control descendant in Enumerate(child))
      {
        yield return descendant;
      }
    }
  }

  private static bool IsInteractive(Control control)
  {
    return control is ButtonBase or ComboBox or TextBoxBase or NumericUpDown or
      TrackBar or TreeView or TabControl or LinkLabel or WebBrowser;
  }
}
