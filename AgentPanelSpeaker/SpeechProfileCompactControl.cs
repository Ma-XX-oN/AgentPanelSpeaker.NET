using System.Drawing.Drawing2D;

namespace AgentPanelSpeaker;

/// <summary>
/// Draws and edits one rate, pitch, and volume speech profile.
/// </summary>
internal sealed class SpeechProfileCompactControl : Control
{
  private static readonly Color RateColour = Color.FromArgb(214, 67, 67);
  private static readonly Color PitchColour = Color.FromArgb(46, 160, 67);
  private static readonly Color VolumeColour = Color.FromArgb(50, 107, 214);

  private readonly HoverPopupController _hoverPopupController;
  private SpeechProfilePopup? _popup;
  private bool _popupDragging;
  private bool _dark;
  private int _rate;
  private int _pitch;
  private int _volume = 100;

  /// <summary>
  /// Initializes a compact profile control.
  /// </summary>
  public SpeechProfileCompactControl(string profileName)
  {
    ProfileName = string.IsNullOrWhiteSpace(profileName)
      ? "Speech profile"
      : profileName.Trim();

    DoubleBuffered = true;
    ResizeRedraw = true;
    TabStop = true;
    MinimumSize = new Size(46, 42);
    Size = new Size(88, 52);
    Cursor = Cursors.Hand;
    Margin = new Padding(3);
    Anchor = AnchorStyles.Top;

    _hoverPopupController = new HoverPopupController(
      this,
      GetVisiblePopupControls,
      ShowEditorCore,
      CloseEditorCore,
      () => GetOrCreatePopup(FindForm() ?? throw new InvalidOperationException(
        "Speech profile is not attached to a form.")).FocusInitialSlider(),
      keepOpen: () => _popupDragging);

    AccessibleName = ProfileName;
    UpdateAccessibleDescription();
  }

  /// <summary>
  /// Raised after any profile value changes.
  /// </summary>
  public event EventHandler? ProfileChanged;

  /// <summary>
  /// Raised when the control or editor receives a transport key.
  /// </summary>
  public event EventHandler<TransportKeyPressedEventArgs>? TransportKeyPressed;

  /// <summary>
  /// Raised when Tab should move beyond this profile editor.
  /// </summary>
  public event EventHandler<FocusTraversalRequestedEventArgs>?
    FocusTraversalRequested;

  /// <summary>
  /// Gets the profile's accessible and editor title.
  /// </summary>
  public string ProfileName { get; }

  /// <summary>
  /// Gets or sets the SAPI rate from -10 through 10.
  /// </summary>
  [System.ComponentModel.DesignerSerializationVisibility(
    System.ComponentModel.DesignerSerializationVisibility.Hidden)]
  public int Rate
  {
    get => _rate;
    set => SetProfile(value, _pitch, _volume);
  }

  /// <summary>
  /// Gets or sets the relative pitch from -10 through 10.
  /// </summary>
  [System.ComponentModel.DesignerSerializationVisibility(
    System.ComponentModel.DesignerSerializationVisibility.Hidden)]
  public int Pitch
  {
    get => _pitch;
    set => SetProfile(_rate, value, _volume);
  }

  /// <summary>
  /// Gets or sets the volume from 0 through 100.
  /// </summary>
  [System.ComponentModel.DesignerSerializationVisibility(
    System.ComponentModel.DesignerSerializationVisibility.Hidden)]
  public int Volume
  {
    get => _volume;
    set => SetProfile(_rate, _pitch, value);
  }

  /// <summary>
  /// Gets whether this control's editor is visible.
  /// </summary>
  public bool IsEditorVisible =>
    _popup is { IsDisposed: false, Visible: true };

  /// <summary>
  /// Applies matching light or dark colours.
  /// </summary>
  public void ApplyTheme(bool dark)
  {
    _dark = dark;
    BackColor = dark
      ? Color.FromArgb(38, 38, 40)
      : Color.FromArgb(248, 248, 248);
    ForeColor = dark
      ? Color.FromArgb(238, 238, 238)
      : Color.FromArgb(28, 28, 28);
    if (_popup is { IsDisposed: false })
    {
      _popup.ApplyTheme(dark);
    }
    Invalidate();
  }

  /// <summary>
  /// Replaces all three values and raises one change event when needed.
  /// </summary>
  public void SetProfile(int rate, int pitch, int volume)
  {
    int boundedRate = Math.Clamp(rate, -10, 10);
    int boundedPitch = Math.Clamp(pitch, -10, 10);
    int boundedVolume = Math.Clamp(volume, 0, 100);
    if (_rate == boundedRate &&
        _pitch == boundedPitch &&
        _volume == boundedVolume)
    {
      return;
    }

    _rate = boundedRate;
    _pitch = boundedPitch;
    _volume = boundedVolume;
    UpdateAccessibleDescription();
    Invalidate();
    ProfileChanged?.Invoke(this, EventArgs.Empty);
  }

  /// <summary>
  /// Opens the editor and focuses its first active slider.
  /// </summary>
  public void OpenEditorFromKeyboard()
  {
    _hoverPopupController.OpenImmediately(focusPopup: true);
  }

  /// <summary>
  /// Closes the editor and optionally restores compact-control focus.
  /// </summary>
  public void CloseEditor(bool returnFocus)
  {
    _hoverPopupController.Close(returnFocus);
  }

  /// <summary>
  /// Closes the editor after Escape or Alt+F4.
  /// </summary>
  public void CloseEditorFromDismissKey()
  {
    _hoverPopupController.Close(returnFocus: true);
  }

  /// <summary>
  /// Completes external focus traversal.
  /// </summary>
  public void CompleteFocusTraversal()
  {
  }

  /// <inheritdoc />
  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _hoverPopupController.Dispose();
      _popup?.Dispose();
    }

    base.Dispose(disposing);
  }

  /// <inheritdoc />
  protected override void OnEnabledChanged(EventArgs eventArgs)
  {
    base.OnEnabledChanged(eventArgs);
    if (!Enabled)
    {
      CloseEditor(returnFocus: false);
    }
    Invalidate();
  }

  /// <inheritdoc />
  protected override void OnGotFocus(EventArgs eventArgs)
  {
    base.OnGotFocus(eventArgs);
    Invalidate();
  }

  /// <inheritdoc />
  protected override void OnLostFocus(EventArgs eventArgs)
  {
    base.OnLostFocus(eventArgs);
    Invalidate();
  }

  /// <inheritdoc />
  protected override void OnMouseEnter(EventArgs eventArgs)
  {
    base.OnMouseEnter(eventArgs);
    if (Enabled)
    {
      }
  }

  /// <inheritdoc />
  protected override void OnMouseDown(MouseEventArgs eventArgs)
  {
    base.OnMouseDown(eventArgs);
    if (!Enabled || eventArgs.Button != MouseButtons.Left)
    {
      return;
    }

    Focus();
    _hoverPopupController.OpenImmediately(focusPopup: true);
  }

  /// <inheritdoc />
  protected override void OnKeyDown(KeyEventArgs eventArgs)
  {
    if (eventArgs.Modifiers == Keys.None &&
        IsTransportKey(eventArgs.KeyCode))
    {
      RaiseTransportKey(eventArgs.KeyCode);
      eventArgs.Handled = true;
      eventArgs.SuppressKeyPress = true;
      return;
    }

    if (eventArgs.KeyCode is Keys.Enter or Keys.Space)
    {
      _hoverPopupController.OpenImmediately(focusPopup: true);
      eventArgs.Handled = true;
      eventArgs.SuppressKeyPress = true;
      return;
    }

    base.OnKeyDown(eventArgs);
  }

  /// <inheritdoc />
  protected override void OnPaint(PaintEventArgs eventArgs)
  {
    base.OnPaint(eventArgs);

    Graphics graphics = eventArgs.Graphics;
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

    Rectangle bounds = ClientRectangle;
    if (bounds.Width <= 2 || bounds.Height <= 2)
    {
      return;
    }

    using (var background = new SolidBrush(BackColor))
    {
      graphics.FillRectangle(background, bounds);
    }

    if (_volume == 0)
    {
      DrawMuted(graphics, Rectangle.Inflate(bounds, -5, -5));
    }
    else
    {
      DrawProfile(graphics, Rectangle.Inflate(bounds, -4, -4));
    }

    if (!Enabled)
    {
      using var veil = new SolidBrush(Color.FromArgb(120, BackColor));
      graphics.FillRectangle(veil, bounds);
    }

    bool editorFocused = _popup?.ContainsFocus ?? false;
    Color borderColour = Focused || editorFocused
      ? SystemColors.Highlight
      : Blend(ForeColor, BackColor, Enabled ? 0.42f : 0.24f);
    using var border = new Pen(
      borderColour,
      Focused || editorFocused ? 2.0f : 1.0f);
    Rectangle borderBounds = Rectangle.Inflate(bounds, -1, -1);
    graphics.DrawRectangle(border, borderBounds);
  }

  /// <summary>
  /// Prevents delayed closing while a popup slider is being dragged.
  /// </summary>
  internal void SetPopupDragging(bool dragging)
  {
    _popupDragging = dragging;
    _hoverPopupController.ReevaluateClose();
  }

  /// <summary>
  /// Closes the editor and asks the form to continue tab traversal.
  /// </summary>
  internal void MoveOutsideEditor(bool forward)
  {
    CloseEditor(returnFocus: false);
    EventHandler<FocusTraversalRequestedEventArgs>? handler =
      FocusTraversalRequested;
    if (handler is null)
    {
      return;
    }

    handler.Invoke(this, new FocusTraversalRequestedEventArgs(forward));
  }

  /// <summary>
  /// Handles transport keys while a slider has focus.
  /// </summary>
  internal bool TryHandleTransportKey(Keys keyData)
  {
    if ((keyData & Keys.Modifiers) != Keys.None)
    {
      return false;
    }

    Keys keyCode = keyData & Keys.KeyCode;
    if (!IsTransportKey(keyCode))
    {
      return false;
    }

    RaiseTransportKey(keyCode);
    return true;
  }

  private void UpdateAccessibleDescription()
  {
    AccessibleDescription = _volume == 0
      ? "Muted"
      : $"Rate {_rate}, pitch {_pitch}, volume {_volume}";
  }

  private void ShowEditorCore(bool focusEditor)
  {
    if (!Enabled)
    {
      return;
    }

    Form? owner = FindForm();
    if (owner is null)
    {
      return;
    }

    SpeechProfilePopup popup = GetOrCreatePopup(owner);
    popup.ApplyTheme(_dark);
    popup.SyncFromProfile();
    PositionPopup(owner, popup);
    if (focusEditor)
    {
      popup.PrepareInitialSlider();
    }
    popup.Visible = true;
    popup.BringToFront();
    _hoverPopupController.RefreshPopupControls();

    if (focusEditor)
    {
      popup.Select();
    }

    Invalidate();
  }

  private void CloseEditorCore(bool returnFocus)
  {
    if (_popup is { IsDisposed: false })
    {
      _popup.Visible = false;
    }

    _popupDragging = false;
    if (returnFocus && CanFocus)
    {
      Focus();
    }
    Invalidate();
  }

  private IEnumerable<Control> GetVisiblePopupControls()
  {
    if (_popup is { IsDisposed: false, Visible: true } popup)
    {
      yield return popup;
    }
  }

  private SpeechProfilePopup GetOrCreatePopup(Form owner)
  {
    SpeechProfilePopup? popup = _popup;
    if (popup is null || popup.IsDisposed)
    {
      popup = new SpeechProfilePopup(this);
      _popup = popup;
      owner.Controls.Add(popup);
      return popup;
    }

    if (!ReferenceEquals(popup.Parent, owner))
    {
      popup.Parent?.Controls.Remove(popup);
      owner.Controls.Add(popup);
    }

    return popup;
  }

  private void PositionPopup(Form owner, Control popup)
  {
    Rectangle anchorScreen = RectangleToScreen(ClientRectangle);
    Point anchorLocation = owner.PointToClient(anchorScreen.Location);
    int x = anchorLocation.X + ((anchorScreen.Width - popup.Width) / 2);
    int y = anchorLocation.Y + ((anchorScreen.Height - popup.Height) / 2);

    Rectangle client = owner.ClientRectangle;
    x = Math.Clamp(
      x,
      client.Left,
      Math.Max(client.Left, client.Right - popup.Width));
    y = Math.Clamp(
      y,
      client.Top,
      Math.Max(client.Top, client.Bottom - popup.Height));
    popup.Location = new Point(x, y);
  }

  private void DrawProfile(Graphics graphics, Rectangle bounds)
  {
    int rowGap = 2;
    int rowHeight = Math.Max(7, (bounds.Height - (rowGap * 2)) / 3);

    DrawProfileRow(
      graphics,
      new Rectangle(bounds.X, bounds.Y, bounds.Width, rowHeight),
      _rate,
      RateColour,
      signed: true);
    DrawProfileRow(
      graphics,
      new Rectangle(
        bounds.X,
        bounds.Y + rowHeight + rowGap,
        bounds.Width,
        rowHeight),
      _pitch,
      PitchColour,
      signed: true);
    DrawProfileRow(
      graphics,
      new Rectangle(
        bounds.X,
        bounds.Y + ((rowHeight + rowGap) * 2),
        bounds.Width,
        rowHeight),
      _volume,
      VolumeColour,
      signed: false);
  }

  private void DrawProfileRow(
    Graphics graphics,
    Rectangle row,
    int value,
    Color colour,
    bool signed)
  {
    var graphBounds = new Rectangle(
      row.X,
      row.Y + 1,
      Math.Max(2, row.Width),
      Math.Max(3, row.Height - 2));

    if (signed)
    {
      DrawSignedTriangle(graphics, graphBounds, value, colour);
    }
    else
    {
      DrawVolumeTriangle(graphics, graphBounds, value, colour);
    }
  }

  private void DrawSignedTriangle(
    Graphics graphics,
    Rectangle bounds,
    int value,
    Color colour)
  {
    float centreX = bounds.Left + ((bounds.Width - 1) / 2.0f);
    float midY = bounds.Top + ((bounds.Height - 1) / 2.0f);
    float top = bounds.Top;
    float bottom = bounds.Bottom - 1;

    if (value != 0)
    {
      bool positive = value > 0;
      PointF[] guide = positive
        ?
        [
          new PointF(centreX, midY),
          new PointF(bounds.Right - 1, midY),
          new PointF(bounds.Right - 1, top)
        ]
        :
        [
          new PointF(centreX, midY),
          new PointF(bounds.Left, midY),
          new PointF(bounds.Left, bottom)
        ];

      using (var guideBrush = new SolidBrush(
        Blend(colour, BackColor, 0.20f)))
      {
        graphics.FillPolygon(guideBrush, guide);
      }

      float proportion = Math.Abs(value) / 10.0f;
      float heightProportion = 0.34f + (0.66f * proportion);
      PointF[] active;
      if (positive)
      {
        float x = centreX +
          (((bounds.Right - 1) - centreX) * proportion);
        float y = midY - ((midY - top) * heightProportion);
        active =
        [
          new PointF(centreX, midY),
          new PointF(x, midY),
          new PointF(x, y)
        ];
      }
      else
      {
        float x = centreX - ((centreX - bounds.Left) * proportion);
        float y = midY + ((bottom - midY) * heightProportion);
        active =
        [
          new PointF(centreX, midY),
          new PointF(x, midY),
          new PointF(x, y)
        ];
      }

      using var activeBrush = new SolidBrush(colour);
      graphics.FillPolygon(activeBrush, active);
    }

    using var axisPen = new Pen(Blend(ForeColor, BackColor, 0.42f));
    graphics.DrawLine(axisPen, bounds.Left, midY, bounds.Right - 1, midY);
    graphics.DrawLine(axisPen, centreX, top, centreX, bottom);
  }

  private void DrawVolumeTriangle(
    Graphics graphics,
    Rectangle bounds,
    int value,
    Color colour)
  {
    float left = bounds.Left;
    float right = bounds.Right - 1;
    float top = bounds.Top;
    float bottom = bounds.Bottom - 1;
    PointF[] guide =
    [
      new PointF(left, bottom),
      new PointF(right, bottom),
      new PointF(right, top)
    ];

    using (var guideBrush = new SolidBrush(Blend(colour, BackColor, 0.20f)))
    {
      graphics.FillPolygon(guideBrush, guide);
    }

    float proportion = value / 100.0f;
    float x = left + ((right - left) * proportion);
    float y = bottom - ((bottom - top) * proportion);
    PointF[] active =
    [
      new PointF(left, bottom),
      new PointF(x, bottom),
      new PointF(x, y)
    ];

    using var activeBrush = new SolidBrush(colour);
    graphics.FillPolygon(activeBrush, active);
  }

  private void DrawMuted(Graphics graphics, Rectangle bounds)
  {
    int iconSize = Math.Max(12, Math.Min(bounds.Width, bounds.Height) - 6);
    var iconBounds = new Rectangle(
      bounds.Left + ((bounds.Width - iconSize) / 2),
      bounds.Top + ((bounds.Height - iconSize) / 2),
      iconSize,
      iconSize);

    Color iconColour = Blend(ForeColor, BackColor, 0.78f);
    using var iconPen = new Pen(iconColour, Math.Max(1.4f, iconSize / 12.0f));
    iconPen.StartCap = LineCap.Round;
    iconPen.EndCap = LineCap.Round;
    DrawLips(graphics, iconBounds, iconPen);

    using var slashPen = new Pen(
      Color.FromArgb(220, 60, 60),
      Math.Max(1.8f, iconSize / 9.0f));
    slashPen.StartCap = LineCap.Round;
    slashPen.EndCap = LineCap.Round;
    graphics.DrawLine(
      slashPen,
      iconBounds.Left + 1,
      iconBounds.Bottom - 1,
      iconBounds.Right - 1,
      iconBounds.Top + 1);
  }

  private static void DrawLips(Graphics graphics, Rectangle bounds, Pen pen)
  {
    using var path = new GraphicsPath();
    PointF left = new(bounds.Left + 1, bounds.Top + (bounds.Height * 0.52f));
    PointF right = new(
      bounds.Right - 1,
      bounds.Top + (bounds.Height * 0.52f));
    PointF topCentre = new(
      bounds.Left + (bounds.Width * 0.50f),
      bounds.Top + (bounds.Height * 0.26f));
    PointF bottomCentre = new(
      bounds.Left + (bounds.Width * 0.50f),
      bounds.Top + (bounds.Height * 0.76f));

    path.StartFigure();
    path.AddBezier(
      left,
      new PointF(bounds.Left + (bounds.Width * 0.28f), bounds.Top),
      new PointF(bounds.Left + (bounds.Width * 0.38f), topCentre.Y),
      topCentre);
    path.AddBezier(
      topCentre,
      new PointF(bounds.Left + (bounds.Width * 0.62f), topCentre.Y),
      new PointF(bounds.Left + (bounds.Width * 0.72f), bounds.Top),
      right);
    path.AddBezier(
      right,
      new PointF(bounds.Left + (bounds.Width * 0.72f), bounds.Bottom),
      new PointF(bounds.Left + (bounds.Width * 0.62f), bottomCentre.Y),
      bottomCentre);
    path.AddBezier(
      bottomCentre,
      new PointF(bounds.Left + (bounds.Width * 0.38f), bottomCentre.Y),
      new PointF(bounds.Left + (bounds.Width * 0.28f), bounds.Bottom),
      left);
    path.CloseFigure();
    graphics.DrawPath(pen, path);
    graphics.DrawLine(pen, left, right);
  }

  private void RaiseTransportKey(Keys keyCode)
  {
    TransportKeyPressed?.Invoke(
      this,
      new TransportKeyPressedEventArgs(keyCode));
  }

  private static bool IsTransportKey(Keys keyCode)
  {
    return keyCode is
      Keys.U or Keys.H or Keys.J or Keys.K or Keys.L or Keys.O or
      Keys.OemSemicolon or Keys.OemQuotes;
  }

  private static Color Blend(
    Color foreground,
    Color background,
    float amount)
  {
    float bounded = Math.Clamp(amount, 0.0f, 1.0f);
    int red = (int)Math.Round(
      background.R + ((foreground.R - background.R) * bounded));
    int green = (int)Math.Round(
      background.G + ((foreground.G - background.G) * bounded));
    int blue = (int)Math.Round(
      background.B + ((foreground.B - background.B) * bounded));
    return Color.FromArgb(red, green, blue);
  }
}
