using Microsoft.Win32;
using System.Runtime.InteropServices;

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
/// Applies the application light or dark palette to Windows Forms controls.
/// </summary>
internal static class ThemeManager
{
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
  private static readonly Color DarkCaution = Color.FromArgb(105, 82, 20);
  private static readonly Color DarkCautionText = Color.FromArgb(255, 236, 160);
  private static readonly Color LightPopup = Color.FromArgb(250, 250, 250);
  private static readonly Color LightText = Color.FromArgb(24, 24, 24);
  private static readonly Color LightBorder = Color.FromArgb(110, 110, 110);
  private static readonly Color LightCaution = Color.FromArgb(255, 235, 140);
  private static readonly Color LightCautionText = Color.FromArgb(55, 42, 0);

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

  /// <summary>
  /// Applies one theme to a form and all descendants.
  /// </summary>
  public static void Apply(Form form, AppTheme theme)
  {
    ArgumentNullException.ThrowIfNull(form);
    bool dark = IsDark(theme);
    Apply(form, dark);
    TrySetDarkTitleBar(form.Handle, dark);
  }

  /// <summary>
  /// Applies an already-resolved effective theme to a control tree.
  /// </summary>
  public static void Apply(Control control, bool dark)
  {
    ArgumentNullException.ThrowIfNull(control);
    Color surface = dark ? DarkWindow : SystemColors.Control;
    ApplyControl(control, dark, surface);
    control.Invalidate(true);
  }

  /// <summary>
  /// Applies the centralized popup palette to a control tree.
  /// </summary>
  public static void ApplyPopup(Control control, bool dark)
  {
    ArgumentNullException.ThrowIfNull(control);
    ApplyControl(control, dark, GetPopupBackground(dark));
    control.Invalidate(true);
  }

  /// <summary>
  /// Creates the standard three-state tree images using the active palette.
  /// </summary>
  public static ImageList CreateTreeStateImages(bool dark)
  {
    Color foreground = GetForeground(dark);
    var images = new ImageList
    {
      ImageSize = new Size(16, 16),
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
    Color window = surface;
    Color foreground = dark ? DarkText : SystemColors.ControlText;
    Color input = dark ? DarkInput : SystemColors.Window;
    Color inputText = dark ? DarkText : SystemColors.WindowText;

    control.BackColor = window;
    control.ForeColor = foreground;

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

      case Button button:
        button.UseVisualStyleBackColor = !dark;
        button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
        button.BackColor = dark ? DarkControl : SystemColors.Control;
        button.ForeColor = foreground;
        if (dark)
        {
          button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        }
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

    foreach (Control child in control.Controls)
    {
      ApplyControl(child, dark, surface);
    }
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
    var bitmap = new Bitmap(16, 16);
    using Graphics graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.Transparent);
    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    Rectangle box = new(1, 1, 13, 13);
    using var border = new Pen(foreground, 1.5f);
    graphics.DrawRectangle(border, box);
    if (state == CheckState.Checked)
    {
      using var pen = new Pen(foreground, 2.0f)
      {
        StartCap = System.Drawing.Drawing2D.LineCap.Round,
        EndCap = System.Drawing.Drawing2D.LineCap.Round,
        LineJoin = System.Drawing.Drawing2D.LineJoin.Round
      };
      graphics.DrawLines(pen, new[]
      {
        new Point(3, 7), new Point(6, 10), new Point(12, 4)
      });
    }
    else if (state == CheckState.Indeterminate)
    {
      using var brush = new SolidBrush(foreground);
      graphics.FillRectangle(brush, 4, 6, 8, 3);
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

  /// <summary>
  /// Requests a matching dark or light non-client title bar.
  /// </summary>
  private static void TrySetDarkTitleBar(IntPtr handle, bool dark)
  {
    try
    {
      int enabled = dark ? 1 : 0;
      _ = DwmSetWindowAttribute(
        handle,
        DwmUseImmersiveDarkMode,
        ref enabled,
        sizeof(int));
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
