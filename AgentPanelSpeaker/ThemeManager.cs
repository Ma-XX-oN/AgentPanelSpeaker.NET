using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace AgentPanelSpeaker;

/// <summary>
/// Applies a consistent light or dark palette to Windows Forms controls.
/// </summary>
internal static class ThemeManager
{
  private const int DwmUseImmersiveDarkMode = 20;

  private static readonly Color DarkWindow = Color.FromArgb(32, 32, 32);
  private static readonly Color DarkControl = Color.FromArgb(45, 45, 48);
  private static readonly Color DarkInput = Color.FromArgb(30, 30, 30);
  private static readonly Color DarkText = Color.FromArgb(240, 240, 240);
  private static readonly Color DarkDisabled = Color.FromArgb(145, 145, 145);

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
  /// Applies one theme to a form and all descendants.
  /// </summary>
  public static void Apply(Form form, AppTheme theme)
  {
    ArgumentNullException.ThrowIfNull(form);
    bool dark = IsDark(theme);
    ApplyControl(form, dark);
    TrySetDarkTitleBar(form.Handle, dark);
    form.Invalidate(true);
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
  private static void ApplyControl(Control control, bool dark)
  {
    Color window = dark ? DarkWindow : SystemColors.Control;
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
        comboBox.BackColor = input;
        comboBox.ForeColor = inputText;
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

      case Label label when !label.Enabled && dark:
        label.ForeColor = DarkDisabled;
        break;
    }

    foreach (Control child in control.Controls)
    {
      ApplyControl(child, dark);
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
