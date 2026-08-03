using System.ComponentModel;

namespace AgentPanelSpeaker;

/// <summary>
/// Edits transcript colour, fade, tracking, and queue settings.
/// </summary>
internal sealed class TranscriptSettingsPopup : UserControl
{
  private readonly Button _previousSwatch = new();
  private readonly Button _currentSwatch = new();
  private readonly TrackBar _fadeSlider = new();
  private readonly Label _fadeValue = new();
  private readonly TrackBar _trackingSlider = new();
  private readonly Label _trackingValue = new();
  private readonly NumericUpDown _queueCapacityNumeric = new();
  private readonly Label _queueCapacityValue = new();
  private readonly System.Windows.Forms.Timer _colourNotificationTimer = new();
  private TranscriptColourPopup? _colourPopup;
  private Color _previousColour;
  private Color _currentColour;
  private bool _dark;
  private bool _updating;
  private bool _colourNotificationPending;
  private readonly HoverPopupController _colourHoverController;

  public TranscriptSettingsPopup()
  {
    AutoScaleMode = AutoScaleMode.Dpi;
    Size = new Size(430, 196);
    TabStop = false;
    Visible = false;

    var title = new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = "Transcript Settings",
      TextAlign = ContentAlignment.MiddleLeft,
      Font = new Font(
        SystemFonts.MessageBoxFont ?? Control.DefaultFont,
        FontStyle.Bold)
    };

    ConfigureSwatch(_previousSwatch, "Previous highlight colour");
    ConfigureSwatch(_currentSwatch, "Edit highlight colour");
    _previousSwatch.Cursor = Cursors.Hand;
    _currentSwatch.Cursor = Cursors.Hand;
    _previousSwatch.TabIndex = 0;
    _currentSwatch.TabIndex = 1;

    ConfigureSlider(_fadeSlider, 0, 32, 2);
    _fadeSlider.TabIndex = 2;
    ConfigureValueLabel(_fadeValue);

    ConfigureSlider(_trackingSlider, 1, 8, 1);
    _trackingSlider.TabIndex = 3;
    ConfigureValueLabel(_trackingValue);

    _queueCapacityNumeric.Minimum = 1;
    _queueCapacityNumeric.Maximum = 16;
    _queueCapacityNumeric.Value = 1;
    _queueCapacityNumeric.Dock = DockStyle.Fill;
    _queueCapacityNumeric.TextAlign = HorizontalAlignment.Right;
    _queueCapacityNumeric.TabIndex = 4;
    ConfigureValueLabel(_queueCapacityValue);
    _queueCapacityValue.Text = "positions";

    var swatchLayout = new FlowLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = false,
      Margin = Padding.Empty
    };
    swatchLayout.Controls.Add(CreateInlineLabel("Previous"));
    swatchLayout.Controls.Add(_previousSwatch);
    swatchLayout.Controls.Add(CreateInlineLabel("Current / edit"));
    swatchLayout.Controls.Add(_currentSwatch);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 3,
      RowCount = 5,
      Dock = DockStyle.Fill,
      Padding = new Padding(10)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.Controls.Add(title, 0, 0);
    layout.SetColumnSpan(title, 3);
    layout.Controls.Add(swatchLayout, 0, 1);
    layout.SetColumnSpan(swatchLayout, 3);
    AddSliderRow(layout, 2, "Fade Duration", _fadeSlider, _fadeValue);
    AddSliderRow(layout, 3, "Tracking Update", _trackingSlider, _trackingValue);
    AddSliderRow(
      layout,
      4,
      "Highlight Queue",
      _queueCapacityNumeric,
      _queueCapacityValue);
    Controls.Add(layout);

    _previousSwatch.Click += (_, _) => RestorePreviousColour();
    _fadeSlider.ValueChanged += ValueChanged;
    _trackingSlider.ValueChanged += ValueChanged;
    _queueCapacityNumeric.ValueChanged += ValueChanged;

    _colourNotificationTimer.Interval = 75;
    _colourNotificationTimer.Tick += (_, _) =>
    {
      _colourNotificationTimer.Stop();
      PublishPendingColourChange();
    };
    _colourHoverController = new HoverPopupController(
      _currentSwatch,
      GetVisibleColourPopupControls,
      ShowColourPopupCore,
      CloseColourPopupCore);
    _currentSwatch.Click += (_, _) =>
      _colourHoverController.OpenImmediately(focusPopup: true);

    Paint += PaintBorder;
    WireBackgroundFocus(this);
  }

  public event EventHandler? SettingsChanged;
  public event EventHandler<TransportKeyPressedEventArgs>? TransportKeyPressed;
  public event EventHandler<FocusTraversalRequestedEventArgs>?
    FocusTraversalRequested;
  public event EventHandler? DismissRequested;
  public event EventHandler? HoverRegionChanged;

  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  public TranscriptSettings Settings { get; private set; } =
    TranscriptSettings.Default;


  public void SetSettings(TranscriptSettings settings, bool dark)
  {
    _dark = dark;
    Settings = settings.Normalize();
    _updating = true;
    try
    {
      _currentColour = Settings.GetHighlightColour(dark);
      _previousColour = _currentColour;
      _fadeSlider.Value = FadeStepFromMilliseconds(Settings.FadeMilliseconds);
      _trackingSlider.Value = Math.Clamp(
        Settings.HighlightUpdateMilliseconds / 5,
        1,
        8);
      _queueCapacityNumeric.Value = Math.Clamp(
        Settings.HighlightQueueCapacity,
        1,
        16);
      UpdateDisplays();
    }
    finally
    {
      _updating = false;
    }
    ApplyTheme(dark);
  }

  public void PrepareForDisplay()
  {
    _previousColour = _currentColour;
    UpdateDisplays();
  }

  public void PrepareForHide()
  {
    FlushPendingColourChange();
    _colourHoverController.Close(returnFocus: false);
  }

  public void ApplyTheme(bool dark)
  {
    _dark = dark;
    BackColor = dark
      ? Color.FromArgb(47, 47, 50)
      : Color.FromArgb(250, 250, 250);
    ForeColor = dark
      ? Color.FromArgb(240, 240, 240)
      : Color.FromArgb(24, 24, 24);
    ApplyThemeRecursive(this);
    _currentColour = Settings.GetHighlightColour(dark);
    _previousColour = _currentColour;
    if (_colourPopup is { IsDisposed: false } colourPopup)
    {
      colourPopup.ApplyTheme(dark);
      colourPopup.SetColours(_currentColour, _previousColour);
    }
    UpdateDisplays();
    Invalidate(true);
  }

  public void FocusInitialControl()
  {
    _previousSwatch.Focus();
  }

  public bool IsPointerInside()
  {
    return ClientRectangle.Contains(PointToClient(Cursor.Position));
  }

  public IEnumerable<Control> GetHoverRegionControls()
  {
    yield return this;
    if (_colourPopup is { IsDisposed: false, Visible: true } popup)
    {
      yield return popup;
    }
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _colourHoverController.Dispose();
      _colourNotificationTimer.Dispose();
      _colourPopup?.Dispose();
    }
    base.Dispose(disposing);
  }

  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    Keys code = keyData & Keys.KeyCode;
    Keys modifiers = keyData & Keys.Modifiers;
    bool supportedModifiers = modifiers == Keys.None || modifiers == Keys.Alt;
    bool transportKey = code is Keys.U or Keys.H or Keys.J or Keys.K or
      Keys.L or Keys.OemSemicolon or Keys.O or Keys.OemQuotes or
      Keys.Oemplus;
    if (supportedModifiers && transportKey)
    {
      TransportKeyPressed?.Invoke(
        this,
        new TransportKeyPressedEventArgs(keyData));
      return true;
    }
    if (keyData == Keys.Escape || keyData == (Keys.Alt | Keys.F4))
    {
      DismissRequested?.Invoke(this, EventArgs.Empty);
      return true;
    }
    if (keyData == Keys.Tab && _queueCapacityNumeric.ContainsFocus)
    {
      FocusTraversalRequested?.Invoke(
        this,
        new FocusTraversalRequestedEventArgs(forward: true));
      return true;
    }
    if (keyData == (Keys.Shift | Keys.Tab) && _previousSwatch.ContainsFocus)
    {
      FocusTraversalRequested?.Invoke(
        this,
        new FocusTraversalRequestedEventArgs(forward: false));
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  private void ShowColourPopupCore(bool focusPopup)
  {
    TranscriptColourPopup popup = GetOrCreateColourPopup();
    popup.ApplyTheme(_dark);
    popup.SetColours(_currentColour, _previousColour);
    PositionColourPopup(popup);
    popup.Visible = true;
    popup.BringToFront();
    HoverRegionChanged?.Invoke(this, EventArgs.Empty);
    _colourHoverController.RefreshPopupControls();
    if (focusPopup)
    {
      popup.FocusInitialControl();
    }
  }

  private TranscriptColourPopup GetOrCreateColourPopup()
  {
    if (_colourPopup is { IsDisposed: false } existing)
    {
      return existing;
    }

    Form owner = FindForm() ?? throw new InvalidOperationException(
      "Transcript settings are not attached to a form.");
    var popup = new TranscriptColourPopup();
    popup.ColourChanged += (_, _) =>
    {
      _currentColour = popup.Colour;
      _previousColour = popup.PreviousColour;
      ValueChanged(popup, EventArgs.Empty);
    };
    popup.DismissRequested += (_, _) =>
      _colourHoverController.Close(returnFocus: true);
    _colourPopup = popup;
    owner.Controls.Add(popup);
    return popup;
  }

  private void PositionColourPopup(TranscriptColourPopup popup)
  {
    Form? owner = FindForm();
    if (owner is null)
    {
      return;
    }
    Point below = _currentSwatch.PointToScreen(
      new Point(_currentSwatch.Width - popup.Width, _currentSwatch.Height + 2));
    Rectangle ownerBounds = owner.RectangleToScreen(owner.ClientRectangle);
    int maxX = Math.Max(ownerBounds.Left, ownerBounds.Right - popup.Width);
    int maxY = Math.Max(ownerBounds.Top, ownerBounds.Bottom - popup.Height);
    int x = Math.Clamp(below.X, ownerBounds.Left, maxX);
    int y = below.Y + popup.Height <= ownerBounds.Bottom
      ? below.Y
      : _currentSwatch.PointToScreen(Point.Empty).Y - popup.Height - 2;
    popup.Location = owner.PointToClient(new Point(
      x,
      Math.Clamp(y, ownerBounds.Top, maxY)));
  }

  private void CloseColourPopupCore(bool returnFocus)
  {
    if (_colourPopup is { IsDisposed: false } popup)
    {
      popup.Visible = false;
    }
    FlushPendingColourChange();
    HoverRegionChanged?.Invoke(this, EventArgs.Empty);
    if (returnFocus && _currentSwatch.CanFocus)
    {
      _currentSwatch.Focus();
    }
  }

  private IEnumerable<Control> GetVisibleColourPopupControls()
  {
    if (_colourPopup is { IsDisposed: false, Visible: true } popup)
    {
      yield return popup;
    }
  }

  private void RestorePreviousColour()
  {
    Color current = _currentColour;
    _currentColour = _previousColour;
    _previousColour = current;
    ValueChanged(this, EventArgs.Empty);
  }

  private void ValueChanged(object? sender, EventArgs eventArgs)
  {
    if (_updating)
    {
      return;
    }
    Settings = ((_dark
      ? Settings with { DarkHighlightArgb = _currentColour.ToArgb() }
      : Settings with { LightHighlightArgb = _currentColour.ToArgb() }) with
    {
      FadeMilliseconds = FadeMillisecondsFromStep(_fadeSlider.Value),
      HighlightUpdateMilliseconds = _trackingSlider.Value * 5,
      HighlightQueueCapacity = Decimal.ToInt32(_queueCapacityNumeric.Value)
    }).Normalize();
    UpdateDisplays();
    if (_colourPopup is { IsDisposed: false, Visible: true } popup)
    {
      popup.SetColours(_currentColour, _previousColour);
    }
    if (sender is TranscriptColourPopup)
    {
      _colourNotificationPending = true;
      if (!_colourNotificationTimer.Enabled)
      {
        _colourNotificationTimer.Start();
      }
    }
    else
    {
      _colourNotificationTimer.Stop();
      _colourNotificationPending = false;
      SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
  }

  private void FlushPendingColourChange()
  {
    _colourNotificationTimer.Stop();
    PublishPendingColourChange();
  }

  private void PublishPendingColourChange()
  {
    if (!_colourNotificationPending)
    {
      return;
    }
    _colourNotificationPending = false;
    SettingsChanged?.Invoke(this, EventArgs.Empty);
  }

  private void UpdateDisplays()
  {
    _fadeValue.Text = $"{_fadeSlider.Value / 64.0:0.####}s";
    _trackingValue.Text = $"{_trackingSlider.Value * 5} ms";
    SetSwatchColour(_currentSwatch, _currentColour);
    SetSwatchColour(_previousSwatch, _previousColour);
    _currentSwatch.Invalidate();
    _previousSwatch.Invalidate();
  }

  private static int FadeMillisecondsFromStep(int step)
  {
    return (int)Math.Round(Math.Clamp(step, 0, 32) * 1000.0 / 64.0);
  }

  private static int FadeStepFromMilliseconds(int milliseconds)
  {
    return Math.Clamp(
      (int)Math.Round(milliseconds * 64.0 / 1000.0),
      0,
      32);
  }

  private static bool IsPointerInside(Control control)
  {
    return control.ClientRectangle.Contains(control.PointToClient(Cursor.Position));
  }

  private static void ConfigureSlider(
    TrackBar slider,
    int minimum,
    int maximum,
    int tickFrequency)
  {
    slider.AutoSize = false;
    slider.Minimum = minimum;
    slider.Maximum = maximum;
    slider.TickFrequency = tickFrequency;
    slider.SmallChange = 1;
    slider.LargeChange = 1;
    slider.Dock = DockStyle.Fill;
  }

  private static void ConfigureSwatch(Button swatch, string accessibleName)
  {
    swatch.Size = new Size(58, 22);
    swatch.Margin = new Padding(4, 3, 12, 3);
    swatch.AccessibleName = accessibleName;
    swatch.TabStop = true;
    swatch.Text = string.Empty;
    swatch.FlatStyle = FlatStyle.Flat;
    swatch.FlatAppearance.BorderSize = 1;
    swatch.UseVisualStyleBackColor = false;
  }

  private static void SetSwatchColour(Button swatch, Color colour)
  {
    swatch.BackColor = colour;
    swatch.FlatAppearance.MouseOverBackColor = colour;
    swatch.FlatAppearance.MouseDownBackColor = colour;
  }

  private static void ConfigureValueLabel(Label label)
  {
    label.AutoSize = false;
    label.Dock = DockStyle.Fill;
    label.TextAlign = ContentAlignment.MiddleRight;
  }

  private static void AddSliderRow(
    TableLayoutPanel layout,
    int row,
    string label,
    Control slider,
    Control value)
  {
    layout.Controls.Add(CreateLabel(label), 0, row);
    layout.Controls.Add(slider, 1, row);
    layout.Controls.Add(value, 2, row);
  }

  private static Label CreateLabel(string text)
  {
    return new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = text,
      TextAlign = ContentAlignment.MiddleLeft
    };
  }

  private static Label CreateInlineLabel(string text)
  {
    return new Label
    {
      AutoSize = true,
      Margin = new Padding(0, 6, 0, 0),
      Text = text
    };
  }

  private void PaintBorder(object? sender, PaintEventArgs eventArgs)
  {
    using var pen = new Pen(_dark
      ? Color.FromArgb(105, 105, 110)
      : Color.FromArgb(110, 110, 110));
    Rectangle bounds = ClientRectangle;
    bounds.Width -= 1;
    bounds.Height -= 1;
    eventArgs.Graphics.DrawRectangle(pen, bounds);
  }

  private void WireBackgroundFocus(Control control)
  {
    if (control is Label or Panel or TableLayoutPanel or FlowLayoutPanel)
    {
      control.MouseDown += (_, eventArgs) =>
      {
        if (eventArgs.Button == MouseButtons.Left &&
            !ReferenceEquals(control, _previousSwatch) &&
            !ReferenceEquals(control, _currentSwatch))
        {
          FocusInitialControl();
        }
      };
    }
    foreach (Control child in control.Controls)
    {
      WireBackgroundFocus(child);
    }
  }

  private void ApplyThemeRecursive(Control control)
  {
    Color background = _dark
      ? Color.FromArgb(47, 47, 50)
      : Color.FromArgb(250, 250, 250);
    Color foreground = _dark
      ? Color.FromArgb(240, 240, 240)
      : Color.FromArgb(24, 24, 24);
    control.BackColor = background;
    control.ForeColor = foreground;
    foreach (Control child in control.Controls)
    {
      ApplyThemeRecursive(child);
    }
  }
}
