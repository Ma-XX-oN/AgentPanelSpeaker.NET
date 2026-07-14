using System.Windows.Automation;

namespace AgentPanelSpeaker;

/// <summary>
/// Identifies a stable top-level window plus a transcript region expressed
/// relative to that window.
/// </summary>
internal sealed class TranscriptTarget
{
  private readonly double _leftRatio;
  private readonly double _topRatio;
  private readonly double _rightRatio;
  private readonly double _bottomRatio;

  /// <summary>
  /// Initializes a transcript target.
  /// </summary>
  /// <param name="windowHandle">Owning top-level window.</param>
  /// <param name="processId">Owning process identifier.</param>
  /// <param name="windowRectangle">Window rectangle at selection time.</param>
  /// <param name="region">Selected transcript region.</param>
  private TranscriptTarget(
    IntPtr windowHandle,
    int processId,
    System.Drawing.Rectangle windowRectangle,
    System.Drawing.Rectangle region)
  {
    WindowHandle = windowHandle;
    ProcessId = processId;

    _leftRatio = Ratio(region.Left - windowRectangle.Left,
      windowRectangle.Width);
    _topRatio = Ratio(region.Top - windowRectangle.Top,
      windowRectangle.Height);
    _rightRatio = Ratio(region.Right - windowRectangle.Left,
      windowRectangle.Width);
    _bottomRatio = Ratio(region.Bottom - windowRectangle.Top,
      windowRectangle.Height);
  }

  /// <summary>
  /// Gets the owning top-level window.
  /// </summary>
  public IntPtr WindowHandle { get; }

  /// <summary>
  /// Gets the owning process identifier.
  /// </summary>
  public int ProcessId { get; }

  /// <summary>
  /// Creates a target from a selected screen region.
  /// </summary>
  /// <param name="region">Selected screen region.</param>
  /// <returns>The stable target.</returns>
  public static TranscriptTarget Create(System.Drawing.Rectangle region)
  {
    if (region.Width < 20 || region.Height < 20)
    {
      throw new ArgumentException(
        "The selected transcript region is too small.",
        nameof(region));
    }

    int centerX = checked(region.Left + region.Width / 2);
    int centerY = checked(region.Top + region.Height / 2);
    IntPtr child = NativeMethods.WindowFromPoint(
      new NativeMethods.NativePoint(centerX, centerY));
    if (child == IntPtr.Zero)
    {
      throw new InvalidOperationException(
        "No window was found beneath the selected region.");
    }

    IntPtr root = NativeMethods.GetAncestor(
      child,
      NativeMethods.GetAncestorRoot);
    if (root == IntPtr.Zero)
    {
      root = child;
    }

    if (!NativeMethods.GetWindowRect(root, out var nativeWindow))
    {
      throw new InvalidOperationException(
        "The selected window rectangle could not be read.");
    }

    System.Drawing.Rectangle window = nativeWindow.ToDrawingRectangle();
    System.Drawing.Rectangle clipped = System.Drawing.Rectangle.Intersect(
      region,
      window);
    if (clipped.Width < 20 || clipped.Height < 20)
    {
      throw new InvalidOperationException(
        "The selected region does not overlap the target window.");
    }

    NativeMethods.GetWindowThreadProcessId(root, out uint processId);
    if (processId == 0 || processId > int.MaxValue)
    {
      throw new InvalidOperationException(
        "The selected window process could not be identified.");
    }

    return new TranscriptTarget(
      root,
      checked((int)processId),
      window,
      clipped);
  }

  /// <summary>
  /// Reacquires the current UI Automation root for the target window.
  /// </summary>
  /// <returns>The current root element.</returns>
  public AutomationElement GetAutomationRoot()
  {
    if (!NativeMethods.IsWindow(WindowHandle))
    {
      throw new InvalidOperationException(
        "The selected VS Code window no longer exists.");
    }

    NativeMethods.GetWindowThreadProcessId(
      WindowHandle,
      out uint currentProcessId);
    if (currentProcessId != ProcessId)
    {
      throw new InvalidOperationException(
        "The selected window handle now belongs to another process.");
    }

    return AutomationElement.FromHandle(WindowHandle);
  }

  /// <summary>
  /// Recomputes the selected transcript region after the window moves or
  /// resizes.
  /// </summary>
  /// <returns>The current screen region.</returns>
  public System.Drawing.Rectangle GetScreenRegion()
  {
    if (!NativeMethods.GetWindowRect(
          WindowHandle,
          out var nativeWindow))
    {
      throw new InvalidOperationException(
        "The selected window rectangle could not be read.");
    }

    System.Drawing.Rectangle window = nativeWindow.ToDrawingRectangle();
    int left = window.Left + Scale(_leftRatio, window.Width);
    int top = window.Top + Scale(_topRatio, window.Height);
    int right = window.Left + Scale(_rightRatio, window.Width);
    int bottom = window.Top + Scale(_bottomRatio, window.Height);

    return System.Drawing.Rectangle.FromLTRB(
      Math.Min(left, right),
      Math.Min(top, bottom),
      Math.Max(left, right),
      Math.Max(top, bottom));
  }

  /// <summary>
  /// Creates a readable target description.
  /// </summary>
  /// <returns>Window, process, and region information.</returns>
  public string Describe()
  {
    System.Drawing.Rectangle region = GetScreenRegion();
    return $"PID {ProcessId}; region {region.Left},{region.Top} " +
      $"{region.Width}x{region.Height}";
  }

  /// <summary>
  /// Converts a pixel offset to a clamped window-relative ratio.
  /// </summary>
  /// <param name="value">Pixel offset.</param>
  /// <param name="extent">Window extent.</param>
  /// <returns>A ratio in the inclusive range zero through one.</returns>
  private static double Ratio(int value, int extent)
  {
    if (extent <= 0)
    {
      throw new ArgumentOutOfRangeException(
        nameof(extent),
        extent,
        "The window extent must be positive.");
    }

    return Math.Clamp((double)value / extent, 0.0, 1.0);
  }

  /// <summary>
  /// Converts a window-relative ratio back to pixels.
  /// </summary>
  /// <param name="ratio">Clamped ratio.</param>
  /// <param name="extent">Current window extent.</param>
  /// <returns>The scaled pixel offset.</returns>
  private static int Scale(double ratio, int extent)
  {
    return checked((int)Math.Round(ratio * extent));
  }
}
