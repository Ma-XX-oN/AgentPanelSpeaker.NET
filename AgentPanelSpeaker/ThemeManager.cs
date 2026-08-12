using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace AgentPanelSpeaker;


/// <summary>
/// Tree view whose three-state selection artwork is owned by ThemeManager.
/// </summary>
internal sealed class ThemedThreeStateTreeView : TreeView
{
  public ThemedThreeStateTreeView()
  {
    StateImageList = ThemeManager.CreateTreeStateImages(dark: false);
  }
}


/// <summary>
/// Tab control whose dark-mode chrome is rendered by the application theme.
/// </summary>
internal sealed class ThemedTabControl : TabControl
{
  private const int WmPaint = 0x000F;
  private const int WmPrintClient = 0x0318;
  private bool _dark;

  public ThemedTabControl()
  {
    DrawMode = TabDrawMode.Normal;
  }

  /// <summary>
  /// Applies the resolved application theme to the tab headers and border.
  /// </summary>
  public void ApplyTheme(bool dark)
  {
    _dark = dark;
    DrawMode = dark ? TabDrawMode.OwnerDrawFixed : TabDrawMode.Normal;
    Invalidate();
  }

  protected override void OnDrawItem(DrawItemEventArgs e)
  {
    if (!_dark)
    {
      base.OnDrawItem(e);
      return;
    }

    DrawDarkTab(e.Graphics, e.Index);
  }

  protected override void WndProc(ref Message m)
  {
    base.WndProc(ref m);
    if (!_dark || (m.Msg != WmPaint && m.Msg != WmPrintClient) ||
        !IsHandleCreated)
    {
      return;
    }

    DrawDarkTabStrip();
    DrawDarkPageBorder();
  }

  private void DrawDarkTabStrip()
  {
    Rectangle page = DisplayRectangle;
    int stripHeight = Math.Max(0, Math.Min(Height, page.Top));
    if (stripHeight <= 0)
    {
      return;
    }

    using Graphics graphics = Graphics.FromHwnd(Handle);
    Color stripBackground = ThemeManager.GetUnselectedTabBackground();
    using (var brush = new SolidBrush(stripBackground))
    {
      graphics.FillRectangle(brush, 0, 0, Width, stripHeight);
    }

    // Native WinForms leaves the unused part of the header strip in a system
    // colour even with owner-drawn tabs. Repaint every tab after covering the
    // whole strip so no light system-colour band remains in dark mode.
    for (int index = 0; index < TabCount; ++index)
    {
      if (index != SelectedIndex)
      {
        DrawDarkTab(graphics, index);
      }
    }
    if (SelectedIndex >= 0 && SelectedIndex < TabCount)
    {
      DrawDarkTab(graphics, SelectedIndex);
    }
  }

  private void DrawDarkTab(Graphics graphics, int index)
  {
    bool selected = index == SelectedIndex;
    Rectangle bounds = GetTabRect(index);
    Color background = selected
      ? ThemeManager.GetSelectedTabBackground()
      : ThemeManager.GetUnselectedTabBackground();
    Color border = ThemeManager.GetTabBorder();
    Color foreground = ThemeManager.GetForeground(dark: true);

    using (var brush = new SolidBrush(background))
    {
      graphics.FillRectangle(brush, bounds);
    }
    using (var pen = new Pen(border))
    {
      graphics.DrawRectangle(
        pen,
        bounds.Left,
        bounds.Top,
        Math.Max(0, bounds.Width - 1),
        Math.Max(0, bounds.Height - 1));
    }

    TextRenderer.DrawText(
      graphics,
      TabPages[index].Text,
      Font,
      bounds,
      foreground,
      background,
      TextFormatFlags.HorizontalCenter |
      TextFormatFlags.VerticalCenter |
      TextFormatFlags.SingleLine |
      TextFormatFlags.NoPrefix |
      TextFormatFlags.EndEllipsis);
  }

  private void DrawDarkPageBorder()
  {
    Rectangle page = DisplayRectangle;
    if (page.Width <= 0 || page.Height <= 0)
    {
      return;
    }

    using Graphics graphics = Graphics.FromHwnd(Handle);
    Color border = ThemeManager.GetTabBorder();
    Color background = BackColor;

    // Cover the native page frame before drawing the subtler app border.
    // DrawDarkTabStrip already owns the complete header strip, so only erase
    // the page seam itself here; do not paint back upward through the tabs.
    int seamTop = Math.Max(0, page.Top - 1);
    int seamHeight = Math.Min(3, Math.Max(0, Height - seamTop));
    using (var erase = new SolidBrush(background))
    {
      graphics.FillRectangle(
        erase, page.Left - 3, page.Top - 3, 3, page.Height + 6);
      graphics.FillRectangle(
        erase, page.Right, page.Top - 3, 3, page.Height + 6);
      if (seamHeight > 0)
      {
        graphics.FillRectangle(erase, 0, seamTop, Width, seamHeight);
      }
      graphics.FillRectangle(
        erase, page.Left - 3, page.Bottom, page.Width + 6, 3);
    }

    using var pen = new Pen(border);
    graphics.DrawLine(
      pen,
      Math.Max(0, page.Left - 1),
      Math.Max(0, page.Top - 1),
      Math.Min(Width - 1, page.Right),
      Math.Max(0, page.Top - 1));
    graphics.DrawLine(
      pen,
      Math.Max(0, page.Left - 1),
      Math.Max(0, page.Top - 1),
      Math.Max(0, page.Left - 1),
      Math.Min(Height - 1, page.Bottom));
    graphics.DrawLine(
      pen,
      Math.Min(Width - 1, page.Right),
      Math.Max(0, page.Top - 1),
      Math.Min(Width - 1, page.Right),
      Math.Min(Height - 1, page.Bottom));
    graphics.DrawLine(
      pen,
      Math.Max(0, page.Left - 1),
      Math.Min(Height - 1, page.Bottom),
      Math.Min(Width - 1, page.Right),
      Math.Min(Height - 1, page.Bottom));
  }
}

/// <summary>
/// Applies the application light or dark palette to Windows Forms controls.
/// </summary>
internal static class ThemeManager
{
  private static int _diagnosticGeneration;

  public static int SetDiagnosticGeneration(int generation)
  {
    int previous = _diagnosticGeneration;
    _diagnosticGeneration = generation;
    return previous;
  }

  private const int DwmUseImmersiveDarkMode = 20;

  public const string VoiceSelectorTag = "voice-selector";

  private static readonly Color DarkWindow = Color.FromArgb(32, 32, 32);
  private static readonly Color DarkControl = Color.FromArgb(45, 45, 48);
  private static readonly Color DarkInput = Color.FromArgb(30, 30, 30);
  private static readonly Color DarkPopup = Color.FromArgb(47, 47, 50);
  private static readonly Color DarkText = Color.FromArgb(240, 240, 240);
  private static readonly Color DarkDisabled = Color.FromArgb(145, 145, 145);
  private static readonly Color DarkLink = Color.FromArgb(100, 180, 255);
  private static readonly Color DarkActiveLink = Color.FromArgb(160, 210, 255);
  private static readonly Color DarkBorder = Color.FromArgb(105, 105, 110);
  private static readonly Color DarkTabUnselected = Color.FromArgb(48, 48, 51);
  private static readonly Color DarkTabSelected = Color.FromArgb(62, 62, 66);
  private static readonly Color DarkCaution = Color.FromArgb(105, 82, 20);
  private static readonly Color DarkCautionText = Color.FromArgb(255, 236, 160);
  private static readonly Color LightPopup = Color.FromArgb(250, 250, 250);
  private static readonly Color LightText = Color.FromArgb(24, 24, 24);
  private static readonly Color LightBorder = Color.FromArgb(110, 110, 110);
  private static readonly Color LightCaution = Color.FromArgb(255, 235, 140);
  private static readonly Color LightCautionText = Color.FromArgb(55, 42, 0);
  private static readonly ConditionalWeakTable<Control, AppliedThemeState>
    AppliedThemeStates = new();

  /// <summary>
  /// Gets whether the effective theme is dark.
  /// </summary>
  public static bool IsDark(AppTheme theme)
  {
    return theme switch
    {
      AppTheme.Dark => true,
      AppTheme.Light => false,
      _ => IsWindowsAppThemeDark()
    };
  }

  /// <summary>
  /// Gets the standard popup background for the effective theme.
  /// </summary>
  public static Color GetPopupBackground(bool dark)
  {
    return dark ? DarkPopup : LightPopup;
  }

  /// <summary>
  /// Gets the standard foreground for the effective theme.
  /// </summary>
  public static Color GetForeground(bool dark)
  {
    return dark ? DarkText : LightText;
  }

  /// <summary>
  /// Gets the standard custom-control border colour for the effective theme.
  /// </summary>
  public static Color GetBorder(bool dark)
  {
    return dark ? DarkBorder : LightBorder;
  }

  internal static Color GetUnselectedTabBackground()
  {
    return DarkTabUnselected;
  }

  internal static Color GetSelectedTabBackground()
  {
    return DarkTabSelected;
  }

  internal static Color GetTabBorder()
  {
    return DarkBorder;
  }

  /// <summary>
  /// Gets the foreground a custom-painted control should use for its state.
  /// </summary>
  public static Color GetControlForeground(Control control)
  {
    ArgumentNullException.ThrowIfNull(control);
    if (control.Enabled)
    {
      return control.ForeColor;
    }
    return AppliedThemeStates.TryGetValue(
      control,
      out AppliedThemeState? state) && state.Dark
        ? DarkDisabled
        : SystemColors.GrayText;
  }

  /// <summary>
  /// Applies one theme to a form and all descendants.
  /// </summary>
  public static void Apply(Form form, AppTheme theme)
  {
    ArgumentNullException.ThrowIfNull(form);
    bool dark = IsDark(theme);
    LogThemeOperation("form_apply", "begin", form, dark);
    Apply(form, dark);
    LogThemeOperation("form_apply", "after-control-tree", form, dark);
    LogThemeOperation("title_bar", "begin", form, dark);
    TrySetDarkTitleBar(form.Handle, dark);
    LogThemeOperation("title_bar", "end", form, dark);
    LogThemeOperation("form_apply", "end", form, dark);
  }

  /// <summary>
  /// Applies an already-resolved effective theme to a control tree.
  /// </summary>
  public static void Apply(Control control, bool dark)
  {
    ArgumentNullException.ThrowIfNull(control);
    Color surface = dark ? DarkWindow : SystemColors.Control;
    LogThemeOperation("control_tree", "begin", control, dark);
    ApplyControl(control, dark, surface);
    LogThemeOperation("control_tree", "before-invalidate", control, dark);
    control.Invalidate(true);
    LogThemeOperation("control_tree", "end", control, dark);
  }

  /// <summary>
  /// Applies the centralized popup palette to a control tree.
  /// </summary>
  public static void ApplyPopup(Control control, bool dark)
  {
    ArgumentNullException.ThrowIfNull(control);
    LogThemeOperation("popup_tree", "begin", control, dark);
    ApplyControl(control, dark, GetPopupBackground(dark));
    LogThemeOperation("popup_tree", "before-invalidate", control, dark);
    control.Invalidate(true);
    LogThemeOperation("popup_tree", "end", control, dark);
  }

  /// <summary>
  /// Applies the active palette to a tooltip, including after theme changes.
  /// </summary>
  public static void ApplyToolTip(ToolTip toolTip, bool dark)
  {
    ArgumentNullException.ThrowIfNull(toolTip);

    // Keep ToolTip owner drawing enabled for the lifetime of the component.
    // Switching OwnerDraw on an already-created native tooltip during a theme
    // change can destabilize the underlying tooltip window.  Repaint it using
    // the current palette instead of changing its drawing mode at runtime.
    toolTip.Draw -= DrawToolTip;
    toolTip.BackColor = dark ? DarkPopup : SystemColors.Info;
    toolTip.ForeColor = dark ? DarkText : SystemColors.InfoText;
    toolTip.Tag = dark;
    toolTip.Draw += DrawToolTip;
    toolTip.OwnerDraw = true;

    DiagnosticLog.Write("theme.tooltip_applied", new
    {
      generation = _diagnosticGeneration,
      dark,
      ownerDraw = toolTip.OwnerDraw,
      backColor = toolTip.BackColor.ToArgb(),
      foreColor = toolTip.ForeColor.ToArgb()
    });
  }

  /// <summary>
  /// Creates the standard three-state tree images using the active palette.
  /// </summary>
  public static ImageList CreateTreeStateImages(bool dark)
  {
    Color foreground = GetForeground(dark);
    var images = new ImageList
    {
      ImageSize = new Size(14, 14),
      ColorDepth = ColorDepth.Depth32Bit
    };
    images.Images.Add(DrawTreeStateImage(CheckState.Unchecked, foreground));
    images.Images.Add(DrawTreeStateImage(CheckState.Checked, foreground));
    images.Images.Add(DrawTreeStateImage(CheckState.Indeterminate, foreground));
    return images;
  }

  /// <summary>
  /// Reads the Windows app-theme preference.
  /// </summary>
  private static bool IsWindowsAppThemeDark()
  {
    try
    {
      using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
      object? value = key?.GetValue("AppsUseLightTheme");
      return value is int integer && integer == 0;
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException or
      System.Security.SecurityException)
    {
      DiagnosticLog.Write("theme.read_failed", new
      {
        exception = exception.ToString()
      });
      return false;
    }
  }

  /// <summary>
  /// Applies palette values recursively.
  /// </summary>
  private static void ApplyControl(Control control, bool dark, Color surface)
  {
    LogThemeOperation("control", "begin", control, dark);
    Color window = surface;
    Color foreground = dark ? DarkText : SystemColors.ControlText;
    Color input = dark ? DarkInput : SystemColors.Window;
    Color inputText = dark ? DarkText : SystemColors.WindowText;

    LogThemeOperation("control-base-colours", "begin", control, dark);
    control.BackColor = window;
    control.ForeColor = foreground;
    LogThemeOperation("control-base-colours", "end", control, dark);

    LogThemeOperation("control-specific", "begin", control, dark);
    switch (control)
    {
      case TextBoxBase textBox:
        textBox.BackColor = input;
        textBox.ForeColor = inputText;
        break;

      case ComboBox comboBox:
        ApplyComboBoxTheme(comboBox, dark, input, inputText);
        break;

      case NumericUpDown numeric:
        numeric.BackColor = input;
        numeric.ForeColor = inputText;
        break;

      case CheckBox checkBox:
        checkBox.Paint -= DrawDarkDisabledCheckBox;
        checkBox.Paint += DrawDarkDisabledCheckBox;
        break;

      case Button button:
        button.Paint -= DrawDarkDisabledButton;
        button.Paint += DrawDarkDisabledButton;
        button.UseVisualStyleBackColor = !dark;
        button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
        button.BackColor = dark ? DarkControl : SystemColors.Control;
        button.ForeColor = foreground;
        if (dark)
        {
          button.FlatAppearance.BorderColor = DarkBorder;
        }
        break;

      case ThemedTabControl tabControl:
        tabControl.BackColor = window;
        tabControl.ForeColor = foreground;
        tabControl.ApplyTheme(dark);
        break;

      case TabPage tabPage:
        tabPage.BackColor = window;
        tabPage.ForeColor = foreground;
        break;

      case ListBox listBox:
        listBox.BackColor = input;
        listBox.ForeColor = inputText;
        break;

      case ThemedThreeStateTreeView threeStateTree:
        threeStateTree.BackColor = input;
        threeStateTree.ForeColor = inputText;
        ReplaceTreeStateImages(threeStateTree, dark);
        break;

      case TreeView treeView:
        treeView.BackColor = input;
        treeView.ForeColor = inputText;
        break;

      case TrackBar trackBar:
        trackBar.BackColor = surface;
        trackBar.ForeColor = GetForeground(dark);
        break;

      case LinkLabel linkLabel:
        linkLabel.LinkColor = dark ? DarkLink : SystemColors.HotTrack;
        linkLabel.ActiveLinkColor = dark
          ? DarkActiveLink
          : SystemColors.Highlight;
        linkLabel.VisitedLinkColor = linkLabel.LinkColor;
        break;

      case Label label when !label.Enabled && dark:
        label.ForeColor = DarkDisabled;
        break;
    }
    LogThemeOperation("control-specific", "end", control, dark);

    LogThemeOperation("control-state", "begin", control, dark);
    SetThemeState(control, dark);
    ApplyEnabledAppearance(control, dark);
    LogThemeOperation("control-state", "end", control, dark);

    LogThemeOperation("control-children", "begin", control, dark);
    foreach (Control child in control.Controls)
    {
      ApplyControl(child, dark, surface);
    }
    LogThemeOperation("control-children", "end", control, dark);
    LogThemeOperation("control", "end", control, dark);
  }

  private static void SetThemeState(Control control, bool dark)
  {
    AppliedThemeState state = AppliedThemeStates.GetValue(
      control,
      _ => new AppliedThemeState());
    state.Dark = dark;
    control.EnabledChanged -= ControlEnabledChanged;
    control.EnabledChanged += ControlEnabledChanged;
  }

  private static void ControlEnabledChanged(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control ||
        !AppliedThemeStates.TryGetValue(control, out AppliedThemeState? state))
    {
      return;
    }
    ApplyEnabledAppearance(control, state.Dark);
    control.Invalidate();
  }

  private static void ApplyEnabledAppearance(Control control, bool dark)
  {
    if (!dark)
    {
      return;
    }
    Color foreground = control.Enabled ? DarkText : DarkDisabled;
    switch (control)
    {
      case TextBoxBase:
      case ComboBox:
      case NumericUpDown:
      case Button:
      case CheckBox:
      case Label:
        control.ForeColor = foreground;
        break;
    }
  }

  private static void DrawToolTip(
    object? sender,
    DrawToolTipEventArgs eventArgs)
  {
    if (sender is not ToolTip toolTip)
    {
      return;
    }

    Rectangle bounds = eventArgs.Bounds;
    using (var background = new SolidBrush(toolTip.BackColor))
    {
      eventArgs.Graphics.FillRectangle(background, bounds);
    }

    var borderBounds = new Rectangle(
      bounds.Left,
      bounds.Top,
      Math.Max(0, bounds.Width - 1),
      Math.Max(0, bounds.Height - 1));
    bool dark = toolTip.Tag is bool darkTheme && darkTheme;
    Color borderColor = dark ? DarkBorder : SystemColors.WindowFrame;
    using (var border = new Pen(borderColor))
    {
      eventArgs.Graphics.DrawRectangle(border, borderBounds);
    }

    Rectangle textBounds = Rectangle.Inflate(bounds, -4, -2);
    TextRenderer.DrawText(
      eventArgs.Graphics,
      eventArgs.ToolTipText,
      eventArgs.Font,
      textBounds,
      toolTip.ForeColor,
      toolTip.BackColor,
      TextFormatFlags.Left |
      TextFormatFlags.VerticalCenter |
      TextFormatFlags.NoPrefix |
      TextFormatFlags.WordBreak);
  }

  private static void DrawDarkDisabledButton(
    object? sender,
    PaintEventArgs eventArgs)
  {
    if (sender is not Button button || button.Enabled ||
        !AppliedThemeStates.TryGetValue(button, out AppliedThemeState? state) ||
        !state.Dark)
    {
      return;
    }

    Rectangle bounds = button.ClientRectangle;
    if (bounds.Width <= 0 || bounds.Height <= 0)
    {
      return;
    }

    using (var background = new SolidBrush(DarkControl))
    {
      eventArgs.Graphics.FillRectangle(background, bounds);
    }

    var borderBounds = new Rectangle(
      bounds.Left,
      bounds.Top,
      Math.Max(0, bounds.Width - 1),
      Math.Max(0, bounds.Height - 1));
    using (var border = new Pen(DarkBorder))
    {
      eventArgs.Graphics.DrawRectangle(border, borderBounds);
    }

    if (button.Text.Length == 0)
    {
      return;
    }

    TextRenderer.DrawText(
      eventArgs.Graphics,
      button.Text,
      button.Font,
      bounds,
      DarkDisabled,
      DarkControl,
      TextFormatFlags.HorizontalCenter |
      TextFormatFlags.VerticalCenter |
      TextFormatFlags.SingleLine |
      TextFormatFlags.NoPrefix);
  }

  private static void DrawDarkDisabledCheckBox(
    object? sender,
    PaintEventArgs eventArgs)
  {
    if (sender is not CheckBox checkBox || checkBox.Enabled ||
        checkBox.BackColor.GetBrightness() >= 0.35f)
    {
      return;
    }

    eventArgs.Graphics.Clear(checkBox.BackColor);
    int boxSize = Math.Max(
      12,
      (int)Math.Round(14.0 * checkBox.DeviceDpi / 96.0));
    int top = Math.Max(0, (checkBox.ClientSize.Height - boxSize) / 2);
    var box = new Rectangle(0, top, boxSize - 1, boxSize - 1);
    using var border = new Pen(DarkDisabled, 1.2f);
    eventArgs.Graphics.DrawRectangle(border, box);

    if (checkBox.CheckState == CheckState.Checked)
    {
      using var pen = new Pen(DarkDisabled, 1.8f)
      {
        StartCap = System.Drawing.Drawing2D.LineCap.Round,
        EndCap = System.Drawing.Drawing2D.LineCap.Round,
        LineJoin = System.Drawing.Drawing2D.LineJoin.Round
      };
      eventArgs.Graphics.DrawLines(pen, new[]
      {
        new Point(boxSize * 2 / 10, top + boxSize * 5 / 10),
        new Point(boxSize * 4 / 10, top + boxSize * 7 / 10),
        new Point(boxSize * 8 / 10, top + boxSize * 3 / 10)
      });
    }
    else if (checkBox.CheckState == CheckState.Indeterminate)
    {
      using var brush = new SolidBrush(DarkDisabled);
      eventArgs.Graphics.FillRectangle(
        brush,
        boxSize * 2 / 10,
        top + boxSize * 4 / 10,
        boxSize * 6 / 10,
        Math.Max(2, boxSize * 2 / 10));
    }

    var textBounds = new Rectangle(
      boxSize + 5,
      0,
      Math.Max(0, checkBox.ClientSize.Width - boxSize - 5),
      checkBox.ClientSize.Height);
    TextRenderer.DrawText(
      eventArgs.Graphics,
      checkBox.Text,
      checkBox.Font,
      textBounds,
      DarkDisabled,
      checkBox.BackColor,
      TextFormatFlags.Left |
      TextFormatFlags.VerticalCenter |
      TextFormatFlags.SingleLine |
      TextFormatFlags.NoPrefix);
  }

  private static void ReplaceTreeStateImages(
    ThemedThreeStateTreeView treeView,
    bool dark)
  {
    ImageList? previous = treeView.StateImageList;
    treeView.StateImageList = CreateTreeStateImages(dark);
    previous?.Dispose();
  }

  private static Bitmap DrawTreeStateImage(CheckState state, Color foreground)
  {
    const int canvasSize = 14;
    const int targetGlyphSize = 12;
    var bitmap = new Bitmap(canvasSize, canvasSize);
    using Graphics graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.Transparent);

    if (System.Windows.Forms.VisualStyles.VisualStyleRenderer.IsSupported)
    {
      System.Windows.Forms.VisualStyles.CheckBoxState rendererState = state switch
      {
        CheckState.Checked =>
          System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal,
        CheckState.Indeterminate =>
          System.Windows.Forms.VisualStyles.CheckBoxState.MixedNormal,
        _ => System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal
      };
      Size glyphSize = CheckBoxRenderer.GetGlyphSize(graphics, rendererState);
      if (glyphSize.Width > 0 && glyphSize.Height > 0)
      {
        using var nativeGlyph = new Bitmap(glyphSize.Width, glyphSize.Height);
        using (Graphics nativeGraphics = Graphics.FromImage(nativeGlyph))
        {
          nativeGraphics.Clear(Color.Transparent);
          CheckBoxRenderer.DrawCheckBox(
            nativeGraphics,
            Point.Empty,
            rendererState);
        }

        float scale = Math.Min(
          (float)targetGlyphSize / glyphSize.Width,
          (float)targetGlyphSize / glyphSize.Height);
        int width = Math.Max(1, (int)Math.Round(glyphSize.Width * scale));
        int height = Math.Max(1, (int)Math.Round(glyphSize.Height * scale));
        int left = (canvasSize - width) / 2;
        int top = (canvasSize - height) / 2;
        graphics.InterpolationMode =
          System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode =
          System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.DrawImage(nativeGlyph, new Rectangle(left, top, width, height));
        return bitmap;
      }
    }

    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    Rectangle box = new(1, 1, 11, 11);
    using var border = new Pen(foreground, 1.3f);
    graphics.DrawRectangle(border, box);
    if (state == CheckState.Checked)
    {
      using var pen = new Pen(foreground, 1.8f)
      {
        StartCap = System.Drawing.Drawing2D.LineCap.Round,
        EndCap = System.Drawing.Drawing2D.LineCap.Round,
        LineJoin = System.Drawing.Drawing2D.LineJoin.Round
      };
      graphics.DrawLines(pen, new[]
      {
        new Point(3, 6), new Point(5, 9), new Point(10, 3)
      });
    }
    else if (state == CheckState.Indeterminate)
    {
      using var brush = new SolidBrush(foreground);
      graphics.FillRectangle(brush, 3, 5, 7, 3);
    }
    return bitmap;
  }

  /// <summary>
  /// Applies colours and owner drawing to one combo box.
  /// </summary>
  private static void ApplyComboBoxTheme(
    ComboBox comboBox,
    bool dark,
    Color background,
    Color foreground)
  {
    comboBox.BackColor = background;
    comboBox.ForeColor = foreground;
    comboBox.DrawItem -= DrawComboBoxItem;

    bool voiceSelector = string.Equals(
      comboBox.Tag as string,
      VoiceSelectorTag,
      StringComparison.Ordinal);
    if (dark || voiceSelector)
    {
      comboBox.DrawMode = DrawMode.OwnerDrawFixed;
      comboBox.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
      comboBox.DrawItem += DrawComboBoxItem;
      return;
    }

    comboBox.DrawMode = DrawMode.Normal;
    comboBox.FlatStyle = FlatStyle.Standard;
  }

  /// <summary>
  /// Draws the closed value and every dropdown item using the active palette.
  /// </summary>
  private static void DrawComboBoxItem(
    object? sender,
    DrawItemEventArgs eventArgs)
  {
    if (sender is not ComboBox comboBox)
    {
      return;
    }

    bool selected = (eventArgs.State & DrawItemState.Selected) != 0;
    string text = (eventArgs.Index >= 0 &&
      eventArgs.Index < comboBox.Items.Count
        ? comboBox.GetItemText(comboBox.Items[eventArgs.Index])
        : comboBox.GetItemText(comboBox.SelectedItem)) ?? string.Empty;
    bool voiceSelector = string.Equals(
      comboBox.Tag as string,
      VoiceSelectorTag,
      StringComparison.Ordinal);
    bool caution = voiceSelector && string.Equals(
      text,
      SpeechProfileSettings.NotSpoken,
      StringComparison.Ordinal);
    bool dark = comboBox.BackColor.GetBrightness() < 0.35f;
    Color background = caution
      ? dark ? DarkCaution : LightCaution
      : selected
        ? SystemColors.Highlight
        : comboBox.BackColor;
    Color foreground = !comboBox.Enabled
      ? DarkDisabled
      : caution
        ? dark ? DarkCautionText : LightCautionText
        : selected
          ? SystemColors.HighlightText
          : comboBox.ForeColor;

    using (var brush = new SolidBrush(background))
    {
      eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
    }

    var textBounds = new Rectangle(
      eventArgs.Bounds.X + 2,
      eventArgs.Bounds.Y,
      Math.Max(0, eventArgs.Bounds.Width - 4),
      eventArgs.Bounds.Height);
    TextRenderer.DrawText(
      eventArgs.Graphics,
      text,
      comboBox.Font,
      textBounds,
      foreground,
      background,
      TextFormatFlags.Left |
      TextFormatFlags.VerticalCenter |
      TextFormatFlags.EndEllipsis |
      TextFormatFlags.NoPrefix);

    if ((eventArgs.State & DrawItemState.Focus) != 0)
    {
      eventArgs.DrawFocusRectangle();
    }
  }

  private static void LogThemeOperation(
    string operation,
    string phase,
    Control control,
    bool dark)
  {
    DiagnosticLog.Write("theme.operation", new
    {
      generation = _diagnosticGeneration,
      operation,
      phase,
      dark,
      controlType = control.GetType().FullName,
      control.Name,
      control.Text,
      control.Enabled,
      control.Visible,
      control.IsDisposed,
      control.Disposing,
      control.IsHandleCreated,
      handle = control.IsHandleCreated ? control.Handle.ToInt64() : 0,
      childCount = control.Controls.Count,
      parentType = control.Parent?.GetType().FullName,
      parentName = control.Parent?.Name
    });
  }

  private sealed class AppliedThemeState
  {
    public bool Dark { get; set; }
  }

  /// <summary>
  /// Requests a matching dark or light non-client title bar.
  /// </summary>
  private static void TrySetDarkTitleBar(IntPtr handle, bool dark)
  {
    try
    {
      int enabled = dark ? 1 : 0;
      DiagnosticLog.Write("theme.native_titlebar_call", new
      {
        generation = _diagnosticGeneration,
        phase = "before",
        handle = handle.ToInt64(),
        dark,
        enabled
      });
      int result = DwmSetWindowAttribute(
        handle,
        DwmUseImmersiveDarkMode,
        ref enabled,
        sizeof(int));
      DiagnosticLog.Write("theme.native_titlebar_call", new
      {
        generation = _diagnosticGeneration,
        phase = "after",
        handle = handle.ToInt64(),
        dark,
        enabled,
        result
      });
    }
    catch (DllNotFoundException)
    {
    }
    catch (EntryPointNotFoundException)
    {
    }
  }

  #pragma warning disable SYSLIB1054
  [DllImport("dwmapi.dll")]
  private static extern int DwmSetWindowAttribute(
    IntPtr window,
    int attribute,
    ref int value,
    int valueSize);
  #pragma warning restore SYSLIB1054
}
