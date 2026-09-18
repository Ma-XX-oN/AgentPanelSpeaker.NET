using System.Runtime.CompilerServices;

namespace AgentPanelSpeaker;

/// <summary>
/// Adds centralized tooltips only when a control has useful purpose metadata.
/// Visible labels, current values, editor contents, and control types are not
/// tooltip text. Existing curated AppToolTip captions always win, and controls
/// whose hover owns a richer popup remain explicitly exempt.
/// </summary>
internal static class TooltipCoverage
{
  private static AppToolTip? _coverageToolTip;
  private static bool? _coverageToolTipDark;

  /// <summary>
  /// Installs the application-wide audit after forms have completed their
  /// synchronous construction. Repeated idle passes also catch dynamically
  /// created controls and controls that become interactive later.
  /// </summary>
  [ModuleInitializer]
  internal static void Initialize()
  {
    Application.Idle += ApplicationIdle;
  }

  /// <summary>
  /// Applies semantic tooltip metadata synchronously to one control tree.
  /// Controls without useful metadata intentionally receive no tooltip.
  /// </summary>
  internal static void EnsureTree(Control root, bool dark)
  {
    ArgumentNullException.ThrowIfNull(root);
    if (root.IsDisposed)
    {
      return;
    }

    AppToolTip toolTip = EnsureCoverageToolTip(dark);
    foreach (Control control in Enumerate(root))
    {
      if (control.IsDisposed || !IsInteractive(control) ||
          IsHoverPopupExempt(control) ||
          AppToolTip.HasCentralToolTip(control))
      {
        continue;
      }

      string caption = GetPurposeCaption(control);
      if (caption.Length == 0)
      {
        continue;
      }

      toolTip.SetToolTip(control, caption);
      DiagnosticLog.Write("tooltip.semantic_applied", new
      {
        controlType = control.GetType().FullName,
        control.Name,
        control.AccessibleName,
        source = "accessible-description",
        caption
      });
    }
  }

  /// <summary>
  /// Returns an executable contract for the generic TestProbe.
  /// </summary>
  internal static object GetContractSnapshot()
  {
    using var root = new TableLayoutPanel
    {
      ColumnCount = 2,
      RowCount = 4
    };
    using var label = new Label { Text = "Rate:" };
    using var slider = new TrackBar();
    using var button = new Button { Text = "Apply" };
    using var passive = new Label { Text = "Passive" };
    using var curated = new Button { Text = "Curated" };
    using var hoverPopupAnchor = new SpeechProfileCompactControl("Profile");
    using var transcriptSettingsAnchor = new GlyphButton
    {
      AccessibleName = "Transcript Settings"
    };
    using var curatedToolTip = new AppToolTip();

    root.Controls.Add(label, 0, 0);
    root.Controls.Add(slider, 1, 0);
    root.Controls.Add(button, 0, 1);
    root.SetColumnSpan(button, 2);
    root.Controls.Add(passive, 0, 2);
    root.Controls.Add(curated, 1, 2);
    root.Controls.Add(hoverPopupAnchor, 0, 3);
    root.SetColumnSpan(hoverPopupAnchor, 2);
    curatedToolTip.SetToolTip(curated, "Curated tooltip");
    curatedToolTip.SetToolTip(hoverPopupAnchor, "Must not be shown");
    curatedToolTip.SetToolTip(
      transcriptSettingsAnchor,
      "Must be suppressed before parenting");

    EnsureTree(root, dark: false);

    bool labelledSliderCovered = AppToolTip.HasCentralToolTip(slider);
    bool textButtonCovered = AppToolTip.HasCentralToolTip(button);
    bool curatedTooltipPreserved = AppToolTip.HasCentralToolTip(curated);
    bool passiveLabelExcluded = !AppToolTip.HasCentralToolTip(passive);
    bool explicitHoverPopupTooltipSuppressed =
      !AppToolTip.HasCentralToolTip(hoverPopupAnchor);
    bool unparentedTranscriptSettingsTooltipSuppressed =
      !AppToolTip.HasCentralToolTip(transcriptSettingsAnchor);

    return new
    {
      labelledSliderCovered,
      textButtonCovered,
      curatedTooltipPreserved,
      passiveLabelExcluded,
      explicitHoverPopupTooltipSuppressed,
      unparentedTranscriptSettingsTooltipSuppressed
    };
  }

  /// <summary>
  /// Gets whether hovering or focusing this control intentionally owns a richer
  /// popup surface. AppToolTip uses the same classification to reject accidental
  /// tooltip registrations on those anchors.
  /// </summary>
  internal static bool IsHoverPopupExempt(Control control)
  {
    ArgumentNullException.ThrowIfNull(control);

    // SpeechProfileCompactControl owns a HoverPopupController for its entire
    // surface. A tooltip would compete with the speech-profile popup.
    if (control is SpeechProfileCompactControl)
    {
      return true;
    }

    string accessibleName = control.AccessibleName ?? string.Empty;

    // MainForm assigns this tooltip before parenting the glyph button, so the
    // identity itself must be sufficient to suppress that competing tooltip.
    if (control is GlyphButton &&
        string.Equals(
          accessibleName,
          "Transcript Settings",
          StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    Form? form = control.FindForm();

    // The changed-settings link opens ChangedSettingsPopup on hover/focus.
    if (form is SaveChangedSettingsDialog && control is LinkLabel)
    {
      return true;
    }

    // These TranscriptSettingsPopup controls are nested hover-popup anchors.
    if (form is TranscriptSettingsPopup &&
        (string.Equals(
           accessibleName,
           "Edit highlight colour",
           StringComparison.OrdinalIgnoreCase) ||
         string.Equals(
           accessibleName,
           "Advanced transcript settings",
           StringComparison.OrdinalIgnoreCase)))
    {
      return true;
    }

    return false;
  }

  private static void ApplicationIdle(object? sender, EventArgs eventArgs)
  {
    Form[] forms = Application.OpenForms.Cast<Form>().ToArray();
    foreach (Form form in forms)
    {
      if (form.IsDisposed)
      {
        continue;
      }
      bool dark = form.BackColor.GetBrightness() < 0.35f;
      EnsureTree(form, dark);
    }
  }

  private static AppToolTip EnsureCoverageToolTip(bool dark)
  {
    _coverageToolTip ??= new AppToolTip();
    if (_coverageToolTipDark != dark)
    {
      ThemeManager.ApplyToolTip(_coverageToolTip, dark);
      _coverageToolTipDark = dark;
    }
    return _coverageToolTip;
  }

  private static IEnumerable<Control> Enumerate(Control root)
  {
    yield return root;
    foreach (Control child in root.Controls)
    {
      foreach (Control descendant in Enumerate(child))
      {
        yield return descendant;
      }
    }
  }

  private static bool IsInteractive(Control control)
  {
    if (control is Form or Panel or TableLayoutPanel or FlowLayoutPanel or
        SplitContainer or Splitter or GroupBox or TabPage)
    {
      return false;
    }

    if (control is LinkLabel or ButtonBase or ComboBox or NumericUpDown or
        TrackBar or TreeView or TabControl or ListBox)
    {
      return true;
    }

    if (control is TextBoxBase textBox)
    {
      return textBox.TabStop;
    }

    if (control is Label)
    {
      return control.Cursor == Cursors.Hand;
    }

    return control.TabStop || control.Cursor == Cursors.Hand;
  }

  private static string GetPurposeCaption(Control control)
  {
    string description = Clean(control.AccessibleDescription);
    if (description.Length == 0 || IsRedundantVisibleText(control, description))
    {
      return string.Empty;
    }

    return description;
  }

  private static bool IsRedundantVisibleText(
    Control control,
    string description)
  {
    if (string.Equals(
          Clean(control.Text),
          description,
          StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
          Clean(control.AccessibleName),
          description,
          StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    Control root = GetRoot(control);
    foreach (Control candidate in Enumerate(root))
    {
      if (ReferenceEquals(candidate, control) || candidate is not Label)
      {
        continue;
      }

      if (string.Equals(
            Clean(candidate.Text),
            description,
            StringComparison.Ordinal))
      {
        return true;
      }
    }

    return false;
  }

  private static Control GetRoot(Control control)
  {
    Control root = control;
    while (root.Parent is not null)
    {
      root = root.Parent;
    }
    return root;
  }

  private static string Clean(string? value)
  {
    return (value ?? string.Empty).Trim();
  }
}
