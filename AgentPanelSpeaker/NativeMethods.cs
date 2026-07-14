using System.Runtime.InteropServices;

namespace AgentPanelSpeaker;

/// <summary>
/// Provides the Win32 calls required to bind a selected screen region to its
/// owning top-level window.
/// </summary>
internal static class NativeMethods
{
  internal const uint GetAncestorRoot = 2;

  /// <summary>
  /// Returns the window beneath a screen coordinate.
  /// </summary>
  /// <param name="point">Screen coordinate.</param>
  /// <returns>The window handle, or zero when no window exists there.</returns>
  [DllImport("user32.dll")]
  internal static extern IntPtr WindowFromPoint(NativePoint point);

  /// <summary>
  /// Reads the cursor position in physical screen pixels.
  /// </summary>
  /// <param name="point">Destination physical screen coordinate.</param>
  /// <returns>True on success.</returns>
  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  internal static extern bool GetPhysicalCursorPos(out NativePoint point);

  /// <summary>
  /// Returns an ancestor of a window.
  /// </summary>
  /// <param name="window">Starting window.</param>
  /// <param name="flags">Ancestor kind.</param>
  /// <returns>The ancestor handle.</returns>
  [DllImport("user32.dll")]
  internal static extern IntPtr GetAncestor(IntPtr window, uint flags);

  /// <summary>
  /// Reads a window's screen rectangle.
  /// </summary>
  /// <param name="window">Window handle.</param>
  /// <param name="rectangle">Destination rectangle.</param>
  /// <returns>True on success.</returns>
  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  internal static extern bool GetWindowRect(
    IntPtr window,
    out NativeRectangle rectangle);

  /// <summary>
  /// Reads the process owning a window.
  /// </summary>
  /// <param name="window">Window handle.</param>
  /// <param name="processId">Owning process identifier.</param>
  /// <returns>The owning thread identifier.</returns>
  [DllImport("user32.dll")]
  internal static extern uint GetWindowThreadProcessId(
    IntPtr window,
    out uint processId);

  /// <summary>
  /// Determines whether a handle still identifies a window.
  /// </summary>
  /// <param name="window">Window handle.</param>
  /// <returns>True when the window still exists.</returns>
  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  internal static extern bool IsWindow(IntPtr window);

  /// <summary>
  /// Stores a Win32 screen coordinate.
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct NativePoint
  {
    internal int X;
    internal int Y;

    /// <summary>
    /// Initializes a coordinate.
    /// </summary>
    /// <param name="x">Horizontal coordinate.</param>
    /// <param name="y">Vertical coordinate.</param>
    internal NativePoint(int x, int y)
    {
      X = x;
      Y = y;
    }

    /// <summary>
    /// Converts this coordinate to a drawing point.
    /// </summary>
    /// <returns>The equivalent drawing point.</returns>
    internal readonly System.Drawing.Point ToDrawingPoint()
    {
      return new System.Drawing.Point(X, Y);
    }
  }

  /// <summary>
  /// Stores a Win32 rectangle.
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct NativeRectangle
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    /// <summary>
    /// Converts this rectangle to a drawing rectangle.
    /// </summary>
    /// <returns>The equivalent drawing rectangle.</returns>
    internal System.Drawing.Rectangle ToDrawingRectangle()
    {
      return System.Drawing.Rectangle.FromLTRB(
        Left,
        Top,
        Right,
        Bottom);
    }
  }
}
