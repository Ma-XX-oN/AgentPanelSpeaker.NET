using System.Runtime.InteropServices;
using System.Text;

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
  private const uint GwHwndNext = 2;
  private const uint GwHwndPrev = 3;
  private const uint GwOwner = 4;
  private const int GwlStyle = -16;
  private const int GwlExStyle = -20;

  /// <summary>
  /// Shows this popup as an owned window and explicitly places it above its
  /// owner without activating it.
  /// </summary>
  public void ShowAboveOwner(Form owner)
  {
    ArgumentNullException.ThrowIfNull(owner);

    WriteWindowDiagnostics("before-owner", owner);
    Owner = owner;
    WriteWindowDiagnostics("after-owner", owner);

    if (!Visible)
    {
      Show();
    }
    WriteWindowDiagnostics("after-show", owner);

    bool positioned = SetWindowPos(
      Handle,
      HwndTop,
      0,
      0,
      0,
      0,
      SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    int lastError = Marshal.GetLastWin32Error();
    DiagnosticLog.Write("popup.zorder.set_window_pos", new
    {
      popupType = GetType().FullName,
      positioned,
      lastError
    });
    WriteWindowDiagnostics("after-set-window-pos", owner);

    if (IsHandleCreated)
    {
      BeginInvoke((Action)(() => WriteWindowDiagnostics("settled", owner)));
    }
  }

  /// <summary>
  /// Records native activation and Z-order state around popup ownership and
  /// close transitions without changing window activation.
  /// </summary>
  internal static void WriteActivationDiagnostics(
    string stage,
    Form owner,
    Form? popup = null)
  {
    ArgumentNullException.ThrowIfNull(owner);
    try
    {
      IntPtr ownerHandle = owner.IsHandleCreated ? owner.Handle : IntPtr.Zero;
      IntPtr popupHandle = popup is { IsHandleCreated: true }
        ? popup.Handle
        : IntPtr.Zero;
      IntPtr foregroundHandle = GetForegroundWindow();
      IntPtr activeHandle = GetActiveWindow();
      _ = GetWindowThreadProcessId(foregroundHandle, out uint foregroundProcessId);
      DiagnosticLog.Write("popup.activation_zorder", new
      {
        stage,
        owner = DescribeWindow(ownerHandle),
        popup = DescribeWindow(popupHandle),
        managedActiveForm = Form.ActiveForm is Form activeForm
          ? DescribeWindow(activeForm.IsHandleCreated ? activeForm.Handle : IntPtr.Zero)
          : null,
        foreground = DescribeWindow(foregroundHandle),
        foregroundProcessId,
        foregroundIsCurrentProcess =
          foregroundProcessId == (uint)Environment.ProcessId,
        active = DescribeWindow(activeHandle),
        ownerContainsFocus = owner.ContainsFocus,
        popupContainsFocus = popup?.ContainsFocus ?? false,
        ownerRank = GetTopLevelRank(ownerHandle),
        popupRank = GetTopLevelRank(popupHandle),
        processWindows = GetCurrentProcessTopLevelWindows()
      });
    }
    catch (Exception exception)
    {
      DiagnosticLog.WriteException(
        "popup.activation_zorder.logging_failed",
        exception,
        stage,
        isTerminating: false);
    }
  }

  private void WriteWindowDiagnostics(string stage, Form owner)
  {
    try
    {
      IntPtr popupHandle = IsHandleCreated ? Handle : IntPtr.Zero;
      IntPtr ownerHandle = owner.IsHandleCreated ? owner.Handle : IntPtr.Zero;
      DiagnosticLog.Write("popup.zorder", new
      {
        stage,
        popupType = GetType().FullName,
        popup = DescribeWindow(popupHandle),
        owner = DescribeWindow(ownerHandle),
        managedOwnerMatches = ReferenceEquals(Owner, owner),
        nativeOwner = Hex(GetWindow(popupHandle, GwOwner)),
        parent = Hex(GetParent(popupHandle)),
        previous = Hex(GetWindow(popupHandle, GwHwndPrev)),
        next = Hex(GetWindow(popupHandle, GwHwndNext)),
        foreground = Hex(GetForegroundWindow()),
        active = Hex(GetActiveWindow()),
        popupRank = GetTopLevelRank(popupHandle),
        ownerRank = GetTopLevelRank(ownerHandle),
        processWindows = GetCurrentProcessTopLevelWindows()
      });
    }
    catch (Exception exception)
    {
      DiagnosticLog.WriteException(
        "popup.zorder.logging_failed",
        exception,
        $"{GetType().FullName}:{stage}",
        isTerminating: false);
    }
  }

  private static object DescribeWindow(IntPtr handle)
  {
    if (handle == IntPtr.Zero)
    {
      return new { hwnd = "0x0" };
    }

    _ = GetWindowRect(handle, out Rect rect);
    return new
    {
      hwnd = Hex(handle),
      className = GetClassNameText(handle),
      text = GetWindowTextValue(handle),
      visible = IsWindowVisible(handle),
      style = $"0x{unchecked((uint)GetWindowLong(handle, GwlStyle)):X8}",
      exStyle = $"0x{unchecked((uint)GetWindowLong(handle, GwlExStyle)):X8}",
      owner = Hex(GetWindow(handle, GwOwner)),
      parent = Hex(GetParent(handle)),
      rect = new { rect.Left, rect.Top, rect.Right, rect.Bottom }
    };
  }

  private static object[] GetCurrentProcessTopLevelWindows()
  {
    var windows = new List<object>();
    IntPtr current = GetTopWindow(IntPtr.Zero);
    int rank = 0;
    while (current != IntPtr.Zero && rank < 256)
    {
      _ = GetWindowThreadProcessId(current, out uint processId);
      if (processId == (uint)Environment.ProcessId)
      {
        windows.Add(new
        {
          rank,
          hwnd = Hex(current),
          className = GetClassNameText(current),
          text = GetWindowTextValue(current),
          visible = IsWindowVisible(current),
          owner = Hex(GetWindow(current, GwOwner))
        });
      }
      current = GetWindow(current, GwHwndNext);
      rank++;
    }
    return windows.ToArray();
  }

  private static int GetTopLevelRank(IntPtr target)
  {
    if (target == IntPtr.Zero)
    {
      return -1;
    }

    IntPtr current = GetTopWindow(IntPtr.Zero);
    for (int rank = 0; current != IntPtr.Zero && rank < 256; rank++)
    {
      if (current == target)
      {
        return rank;
      }
      current = GetWindow(current, GwHwndNext);
    }
    return -1;
  }

  private static string GetClassNameText(IntPtr handle)
  {
    var text = new StringBuilder(256);
    _ = GetClassName(handle, text, text.Capacity);
    return text.ToString();
  }

  private static string GetWindowTextValue(IntPtr handle)
  {
    var text = new StringBuilder(256);
    _ = GetWindowText(handle, text, text.Capacity);
    return text.ToString();
  }

  private static string Hex(IntPtr value) => $"0x{value.ToInt64():X}";

  [StructLayout(LayoutKind.Sequential)]
  private struct Rect
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
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

  [DllImport("user32.dll")]
  private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

  [DllImport("user32.dll")]
  private static extern IntPtr GetParent(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern IntPtr GetTopWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern IntPtr GetForegroundWindow();

  [DllImport("user32.dll")]
  private static extern IntPtr GetActiveWindow();

  [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
  private static extern int GetWindowLong(IntPtr hWnd, int index);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool IsWindowVisible(IntPtr hWnd);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int GetClassName(
    IntPtr hWnd,
    StringBuilder className,
    int maxCount);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int GetWindowText(
    IntPtr hWnd,
    StringBuilder text,
    int maxCount);

  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(
    IntPtr hWnd,
    out uint processId);
}
