namespace AgentPanelSpeaker;

/// <summary>
/// Provides a translucent full-desktop overlay for selecting the transcript
/// area to monitor.
/// </summary>
internal sealed class RegionSelectionForm : Form
{
  private readonly System.Drawing.Pen _borderPen = new(
    System.Drawing.Color.Red,
    2.0f);
  private readonly System.Drawing.Brush _selectionBrush =
    new System.Drawing.SolidBrush(
      System.Drawing.Color.FromArgb(60, System.Drawing.Color.Red));
  private System.Drawing.Point _startScreenPoint;
  private System.Drawing.Rectangle _selection;
  private System.Drawing.Rectangle _screenSelection;
  private bool _dragging;

  /// <summary>
  /// Initializes the region selector.
  /// </summary>
  public RegionSelectionForm()
  {
    AutoScaleMode = AutoScaleMode.None;
    BackColor = System.Drawing.Color.Black;
    Bounds = SystemInformation.VirtualScreen;
    Cursor = Cursors.Cross;
    DoubleBuffered = true;
    FormBorderStyle = FormBorderStyle.None;
    KeyPreview = true;
    Opacity = 0.20;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.Manual;
    TopMost = true;

    DiagnosticLog.Write("selection.overlay_created", new
    {
      bounds = RectangleToString(Bounds),
      virtualScreen = RectangleToString(SystemInformation.VirtualScreen),
      deviceDpi = DeviceDpi
    });
  }

  /// <summary>
  /// Gets the selected screen rectangle after a successful dialog result.
  /// </summary>
  public System.Drawing.Rectangle SelectedRegion { get; private set; }

  /// <summary>
  /// Begins a selection drag.
  /// </summary>
  /// <param name="eventArgs">Mouse arguments.</param>
  protected override void OnMouseDown(MouseEventArgs eventArgs)
  {
    base.OnMouseDown(eventArgs);

    if (eventArgs.Button != MouseButtons.Left)
    {
      return;
    }

    _startScreenPoint = GetPhysicalCursorPosition();
    DiagnosticLog.Write("selection.drag_started", new
    {
      point = PointToString(_startScreenPoint),
      deviceDpi = DeviceDpi,
      bounds = RectangleToString(Bounds)
    });
    _selection = System.Drawing.Rectangle.Empty;
    _screenSelection = System.Drawing.Rectangle.Empty;
    _dragging = true;
    Invalidate();
  }

  /// <summary>
  /// Updates the current selection drag.
  /// </summary>
  /// <param name="eventArgs">Mouse arguments.</param>
  protected override void OnMouseMove(MouseEventArgs eventArgs)
  {
    base.OnMouseMove(eventArgs);

    if (!_dragging)
    {
      return;
    }

    UpdateSelection(GetPhysicalCursorPosition());
    Invalidate();
  }

  /// <summary>
  /// Completes a valid selection drag.
  /// </summary>
  /// <param name="eventArgs">Mouse arguments.</param>
  protected override void OnMouseUp(MouseEventArgs eventArgs)
  {
    base.OnMouseUp(eventArgs);

    if (!_dragging || eventArgs.Button != MouseButtons.Left)
    {
      return;
    }

    _dragging = false;
    UpdateSelection(GetPhysicalCursorPosition());
    if (_screenSelection.Width < 20 || _screenSelection.Height < 20)
    {
      _selection = System.Drawing.Rectangle.Empty;
      _screenSelection = System.Drawing.Rectangle.Empty;
      Invalidate();
      return;
    }

    SelectedRegion = _screenSelection;
    DiagnosticLog.Write("selection.drag_completed", new
    {
      physicalRegion = RectangleToString(SelectedRegion),
      overlayClientRegion = RectangleToString(_selection),
      deviceDpi = DeviceDpi
    });
    DialogResult = DialogResult.OK;
    Close();
  }

  /// <summary>
  /// Cancels selection when Escape is pressed.
  /// </summary>
  /// <param name="eventArgs">Key arguments.</param>
  protected override void OnKeyDown(KeyEventArgs eventArgs)
  {
    if (eventArgs.KeyCode == Keys.Escape)
    {
      DiagnosticLog.Write("selection.cancelled");
      DialogResult = DialogResult.Cancel;
      Close();
      return;
    }

    base.OnKeyDown(eventArgs);
  }

  /// <summary>
  /// Draws the instructions and current selection rectangle.
  /// </summary>
  /// <param name="eventArgs">Paint arguments.</param>
  protected override void OnPaint(PaintEventArgs eventArgs)
  {
    base.OnPaint(eventArgs);

    const string instructions =
      "Drag around the Claude/Codex transcript text.  Press Esc to cancel.";
    System.Drawing.Font messageBoxFont =
      SystemFonts.MessageBoxFont ?? Control.DefaultFont;
    using var font = new System.Drawing.Font(
      messageBoxFont.FontFamily,
      16.0f,
      System.Drawing.FontStyle.Bold);
    using var textBrush = new System.Drawing.SolidBrush(
      System.Drawing.Color.White);
    eventArgs.Graphics.DrawString(
      instructions,
      font,
      textBrush,
      new System.Drawing.PointF(24.0f, 24.0f));

    if (_selection.IsEmpty)
    {
      return;
    }

    eventArgs.Graphics.FillRectangle(_selectionBrush, _selection);
    eventArgs.Graphics.DrawRectangle(_borderPen, _selection);
  }

  /// <summary>
  /// Releases drawing resources.
  /// </summary>
  /// <param name="disposing">Whether managed resources are available.</param>
  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _borderPen.Dispose();
      _selectionBrush.Dispose();
    }

    base.Dispose(disposing);
  }

  /// <summary>
  /// Updates both the physical screen selection and its client-area drawing
  /// rectangle.
  /// </summary>
  /// <param name="currentScreenPoint">Current physical cursor position.</param>
  private void UpdateSelection(System.Drawing.Point currentScreenPoint)
  {
    _screenSelection = NormalizeRectangle(
      _startScreenPoint,
      currentScreenPoint);

    System.Drawing.Point topLeft = PointToClient(
      _screenSelection.Location);
    System.Drawing.Point bottomRight = PointToClient(
      new System.Drawing.Point(
        _screenSelection.Right,
        _screenSelection.Bottom));
    _selection = NormalizeRectangle(topLeft, bottomRight);
  }

  /// <summary>
  /// Reads the cursor in the same physical coordinate system used by UI
  /// Automation bounding rectangles.
  /// </summary>
  /// <returns>The current physical screen position.</returns>
  private static System.Drawing.Point GetPhysicalCursorPosition()
  {
    if (!NativeMethods.GetPhysicalCursorPos(out var point))
    {
      throw new System.ComponentModel.Win32Exception(
        System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
        "The physical cursor position could not be read.");
    }

    return point.ToDrawingPoint();
  }

  /// <summary>
  /// Formats a rectangle for diagnostics.
  /// </summary>
  /// <param name="rectangle">Rectangle to format.</param>
  /// <returns>Left, top, width, and height.</returns>
  private static string RectangleToString(
    System.Drawing.Rectangle rectangle)
  {
    return $"{rectangle.Left},{rectangle.Top} " +
      $"{rectangle.Width}x{rectangle.Height}";
  }

  /// <summary>
  /// Formats a point for diagnostics.
  /// </summary>
  /// <param name="point">Point to format.</param>
  /// <returns>X and Y coordinates.</returns>
  private static string PointToString(System.Drawing.Point point)
  {
    return $"{point.X},{point.Y}";
  }

  /// <summary>
  /// Creates a positive rectangle between two points.
  /// </summary>
  /// <param name="first">First point.</param>
  /// <param name="second">Second point.</param>
  /// <returns>The normalized rectangle.</returns>
  private static System.Drawing.Rectangle NormalizeRectangle(
    System.Drawing.Point first,
    System.Drawing.Point second)
  {
    return System.Drawing.Rectangle.FromLTRB(
      Math.Min(first.X, second.X),
      Math.Min(first.Y, second.Y),
      Math.Max(first.X, second.X),
      Math.Max(first.Y, second.Y));
  }
}
