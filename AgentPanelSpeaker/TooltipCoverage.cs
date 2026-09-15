using System.Runtime.CompilerServices;

namespace AgentPanelSpeaker;

/// <summary>
/// Ensures every interactive Windows Forms control is accounted for by the
/// centralized AppToolTip path unless hovering that control intentionally opens
/// a richer popup. Existing curated AppToolTip captions always win; this policy
/// supplies a caption only when a control has no centralized caption.
/// </summary>
internal static class TooltipCoverage
{
  private static AppToolTip? _coverageToolTip;
  private static bool? _coverageToolTipDark;

  /// <summary>
  /// Installs the application-wide audit after forms have completed their
  /// synchronous construction, so explicit per-control tooltips are registered
  /// before coverage is evaluated. Repeated idle passes also catch dynamically
  /// created controls and controls that become interactive later.
  /// </summary>
  [ModuleInitializer]
  internal static void Initialize()
  {
    Application.Idle += ApplicationIdle;
  }

  /// <summary>
  /// Applies the coverage policy synchronously to one control tree.
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

      string caption = BuildCaption(control, out string source);
      toolTip.SetToolTip(control, caption);
      DiagnosticLog.Write("tooltip.coverage_applied", new
      {
        controlType = control.GetType().FullName,
        control.Name,
        control.Text,
        control.AccessibleName,
        source,
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

    EnsureTree(root, dark: false);

    bool labelledSliderCovered = AppToolTip.HasCentralToolTip(slider);
    bool textButtonCovered = AppToolTip.HasCentralToolTip(button);
    bool curatedTooltipPreserved = AppToolTip.HasCentralToolTip(curated);
    bool passiveLabelExcluded = !AppToolTip.HasCentralToolTip(passive);
    bool explicitHoverPopupTooltipSuppressed =
      !AppToolTip.HasCentralToolTip(hoverPopupAnchor);

    return new
    {
      labelledSliderCovered,
      textButtonCovered,
      curatedTooltipPreserved,
      passiveLabelExcluded,
      explicitHoverPopupTooltipSuppressed
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

    Form? form = control.FindForm();
    string accessibleName = control.AccessibleName ?? string.Empty;

    // Main transcript settings is itself a hover-popup anchor.
    if (form is MainForm &&
        string.Equals(
          accessibleName,
          "Transcript Settings",
          StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

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

  private static string BuildCaption(Control control, out string source)
  {
    string description = Clean(control.AccessibleDescription);
    if (description.Length > 0)
    {
      source = "accessible-description";
      return description;
    }

    string accessibleName = Clean(control.AccessibleName);
    if (accessibleName.Length > 0)
    {
      source = "accessible-name";
      return PhraseFor(control, accessibleName);
    }

    string text = Clean(control.Text);
    if (text.Length > 0)
    {
      source = "control-text";
      return PhraseFor(control, text);
    }

    string label = FindAssociatedLabel(control);
    if (label.Length > 0)
    {
      source = "associated-label";
      return PhraseFor(control, label);
    }

    string ancestor = FindAncestorIdentity(control);
    if (ancestor.Length > 0)
    {
      source = "ancestor";
      return PhraseFor(control, ancestor);
    }

    source = "control-type";
    return control.GetType().Name switch
    {
      "WebView2" => "Transcript viewer.",
      "ColorWheel" => "Select a colour.",
      "ColorEditor" => "Edit colour values.",
      _ when control is ComboBox => "Choose a value.",
      _ when control is NumericUpDown => "Adjust this numeric value.",
      _ when control is TrackBar => "Adjust this setting.",
      _ when control is TextBoxBase textBox && textBox.ReadOnly =>
        "View this value.",
      _ when control is TextBoxBase => "Edit this value.",
      _ when control is TreeView => "Browse and select items.",
      _ when control is TabControl => "Switch between available views.",
      _ when control is ButtonBase => "Activate this control.",
      _ => "Use this control."
    };
  }

  private static string PhraseFor(Control control, string identity)
  {
    string subject = Clean(identity).TrimEnd('.', ':');
    if (subject.Length == 0)
    {
      return "Use this control.";
    }

    if (control is CheckBox)
    {
      return $"Toggle {LowerInitial(subject)}.";
    }
    if (control is RadioButton)
    {
      return $"Select {LowerInitial(subject)}.";
    }
    if (control is LinkLabel)
    {
      return $"Open {LowerInitial(subject)}.";
    }
    if (control is ComboBox)
    {
      return $"Choose {LowerInitial(subject)}.";
    }
    if (control is NumericUpDown or TrackBar)
    {
      return $"Adjust {LowerInitial(subject)}.";
    }
    if (control is TextBoxBase textBox)
    {
      return textBox.ReadOnly
        ? $"View {LowerInitial(subject)}."
        : $"Edit {LowerInitial(subject)}.";
    }
    if (control is TreeView or ListBox)
    {
      return $"Select {LowerInitial(subject)}.";
    }
    if (control is TabControl)
    {
      return $"Switch {LowerInitial(subject)}.";
    }

    return subject.EndsWith(".", StringComparison.Ordinal)
      ? subject
      : subject + ".";
  }

  private static string FindAssociatedLabel(Control control)
  {
    Control? parent = control.Parent;
    if (parent is null)
    {
      return string.Empty;
    }

    if (parent is TableLayoutPanel table)
    {
      TableLayoutPanelCellPosition position = table.GetPositionFromControl(control);
      Label? best = null;
      int bestColumn = int.MinValue;
      foreach (Control sibling in parent.Controls)
      {
        if (sibling is not Label label)
        {
          continue;
        }
        TableLayoutPanelCellPosition labelPosition =
          table.GetPositionFromControl(label);
        if (labelPosition.Row == position.Row &&
            labelPosition.Column < position.Column &&
            labelPosition.Column > bestColumn)
        {
          best = label;
          bestColumn = labelPosition.Column;
        }
      }
      string tableLabel = Clean(best?.Text);
      if (tableLabel.Length > 0)
      {
        return tableLabel;
      }
    }

    int index = parent.Controls.GetChildIndex(control, throwException: false);
    for (int siblingIndex = index + 1;
         siblingIndex < parent.Controls.Count;
         ++siblingIndex)
    {
      if (parent.Controls[siblingIndex] is Label label)
      {
        string text = Clean(label.Text);
        if (text.Length > 0)
        {
          return text;
        }
      }
    }

    return string.Empty;
  }

  private static string FindAncestorIdentity(Control control)
  {
    for (Control? ancestor = control.Parent;
         ancestor is not null;
         ancestor = ancestor.Parent)
    {
      string description = Clean(ancestor.AccessibleDescription);
      if (description.Length > 0)
      {
        return description;
      }
      string name = Clean(ancestor.AccessibleName);
      if (name.Length > 0)
      {
        return name;
      }
      if (ancestor is GroupBox or TabPage)
      {
        string text = Clean(ancestor.Text);
        if (text.Length > 0)
        {
          return text;
        }
      }
    }
    return string.Empty;
  }

  private static string Clean(string? value)
  {
    return (value ?? string.Empty)
      .Replace("&", string.Empty, StringComparison.Ordinal)
      .Trim();
  }

  private static string LowerInitial(string value)
  {
    if (value.Length == 0 || !char.IsUpper(value[0]))
    {
      return value;
    }
    if (value.Length == 1)
    {
      return char.ToLowerInvariant(value[0]).ToString();
    }
    return char.ToLowerInvariant(value[0]) + value[1..];
  }
}
