using System.ComponentModel;

namespace AgentPanelSpeaker;

/// <summary>
/// Edits common and advanced transcript display settings.
/// </summary>
internal sealed class TranscriptSettingsPopup : UserControl
{
  private readonly Button _previousSwatch = new();
  private readonly Button _currentSwatch = new();
  private readonly TrackBar _fadeSlider = new();
  private readonly Label _fadeValue = new();
  private readonly TrackBar _trackingSlider = new();
  private readonly Label _trackingValue = new();
  private readonly Button _advancedButton = new();
  private readonly System.Windows.Forms.Timer _colourNotificationTimer = new();
  private TranscriptColourPopup? _colourPopup;
  private TranscriptAdvancedSettingsPopup? _advancedPopup;
  private Color _previousColour;
  private Color _currentColour;
  private bool _dark;
  private bool _updating;
  private bool _colourNotificationPending;
  private HoverPopupController.PopupHandle? _colourPopupHandle;
  private HoverPopupController.PopupHandle? _advancedPopupHandle;

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

    _advancedButton.AutoSize = false;
    _advancedButton.Dock = DockStyle.Left;
    _advancedButton.Size = new Size(104, 28);
    _advancedButton.Text = "Advanced >";
    _advancedButton.TextAlign = ContentAlignment.MiddleLeft;
    _advancedButton.TabIndex = 4;
    _advancedButton.AccessibleName = "Advanced transcript settings";

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
    layout.Controls.Add(_advancedButton, 0, 4);
    layout.SetColumnSpan(_advancedButton, 3);
    Controls.Add(layout);

    _previousSwatch.Click += (_, _) => RestorePreviousColour();
    _fadeSlider.ValueChanged += ValueChanged;
    _trackingSlider.ValueChanged += ValueChanged;

    _colourNotificationTimer.Interval = 75;
    _colourNotificationTimer.Tick += (_, _) =>
    {
      _colourNotificationTimer.Stop();
      PublishPendingColourChange();
    };
    _currentSwatch.Click += (_, _) =>
      _colourPopupHandle?.OpenImmediately(focusPopup: true);
    _advancedButton.Click += (_, _) =>
      _advancedPopupHandle?.OpenImmediately(focusPopup: true);

    Paint += PaintBorder;
    WireBackgroundFocus(this);
  }

  public event EventHandler? SettingsChanged;
  public event EventHandler<TransportKeyPressedEventArgs>? TransportKeyPressed;
  public event EventHandler<FocusTraversalRequestedEventArgs>?
    FocusTraversalRequested;
  public event EventHandler? DismissRequested;

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
      if (_advancedPopup is { IsDisposed: false } advancedPopup)
      {
        advancedPopup.SetQueueCapacity(Settings.HighlightQueueCapacity);
      }
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

  /// <summary>
  /// Gets whether the nested colour editor is currently open.
  /// </summary>
  public bool IsColourPopupOpen => _colourPopupHandle?.IsOpen ?? false;

  /// <summary>
  /// Gets whether the nested advanced popup is currently open.
  /// </summary>
  public bool IsAdvancedPopupOpen => _advancedPopupHandle?.IsOpen ?? false;

  /// <summary>
  /// Gets whether any nested transcript-settings popup is open.
  /// </summary>
  public bool IsNestedPopupOpen => IsColourPopupOpen || IsAdvancedPopupOpen;

  /// <summary>
  /// Closes only the nested colour editor.
  /// </summary>
  public void CloseNestedPopupFromDismissKey()
  {
    if (_advancedPopupHandle?.IsOpen == true)
    {
      _advancedPopupHandle.Close(returnFocus: true);
      return;
    }
    _colourPopupHandle?.Close(returnFocus: true);
  }

  public void PrepareForHide()
  {
    FlushPendingColourChange();
    _colourPopupHandle?.Close(returnFocus: false);
    _advancedPopupHandle?.Close(returnFocus: false);
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
    if (_advancedPopup is { IsDisposed: false } advancedPopup)
    {
      advancedPopup.ApplyTheme(dark);
      advancedPopup.SetQueueCapacity(Settings.HighlightQueueCapacity);
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

  /// <summary>
  /// Registers both nested transcript-settings popups in the root popup tree.
  /// </summary>
  public void RegisterPopupTree(HoverPopupController popupTree)
  {
    ArgumentNullException.ThrowIfNull(popupTree);
    _colourPopupHandle = popupTree.RegisterChild(
      _currentSwatch,
      GetVisibleColourPopupControls,
      ShowColourPopupCore,
      CloseColourPopupCore);
    _advancedPopupHandle = popupTree.RegisterChild(
      _advancedButton,
      GetVisibleAdvancedPopupControls,
      ShowAdvancedPopupCore,
      CloseAdvancedPopupCore);
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _colourNotificationTimer.Dispose();
      _colourPopup?.Dispose();
      _advancedPopup?.Dispose();
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
      if (IsNestedPopupOpen)
      {
        CloseNestedPopupFromDismissKey();
      }
      else
      {
        DismissRequested?.Invoke(this, EventArgs.Empty);
      }
      return true;
    }
    if (keyData == Keys.Tab && _advancedButton.ContainsFocus)
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
    _advancedPopupHandle?.Close(returnFocus: false);
    TranscriptColourPopup popup = GetOrCreateColourPopup();
    popup.ApplyTheme(_dark);
    popup.SetColours(_currentColour, _previousColour);
    PositionColourPopup(popup);
    popup.Visible = true;
    popup.BringToFront();
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
      _colourPopupHandle?.Close(returnFocus: true);
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

  private void ShowAdvancedPopupCore(bool focusPopup)
  {
    _colourPopupHandle?.Close(returnFocus: false);
    TranscriptAdvancedSettingsPopup popup = GetOrCreateAdvancedPopup();
    popup.ApplyTheme(_dark);
    popup.SetQueueCapacity(Settings.HighlightQueueCapacity);
    PositionAdvancedPopup(popup);
    popup.Visible = true;
    popup.BringToFront();
    if (focusPopup)
    {
      popup.FocusInitialControl();
    }
  }

  private TranscriptAdvancedSettingsPopup GetOrCreateAdvancedPopup()
  {
    if (_advancedPopup is { IsDisposed: false } existing)
    {
      return existing;
    }

    Form owner = FindForm() ?? throw new InvalidOperationException(
      "Transcript settings are not attached to a form.");
    var popup = new TranscriptAdvancedSettingsPopup();
    popup.ValueChanged += (_, _) =>
    {
      Settings = (Settings with
      {
        HighlightQueueCapacity = popup.QueueCapacity
      }).Normalize();
      SettingsChanged?.Invoke(this, EventArgs.Empty);
    };
    popup.DismissRequested += (_, _) =>
      _advancedPopupHandle?.Close(returnFocus: true);
    _advancedPopup = popup;
    owner.Controls.Add(popup);
    return popup;
  }

  private void PositionAdvancedPopup(TranscriptAdvancedSettingsPopup popup)
  {
    Form? owner = FindForm();
    if (owner is null)
    {
      return;
    }

    Point anchorScreen = _advancedButton.PointToScreen(Point.Empty);
    Rectangle ownerBounds = owner.RectangleToScreen(owner.ClientRectangle);
    int x = Math.Clamp(
      anchorScreen.X,
      ownerBounds.Left,
      Math.Max(ownerBounds.Left, ownerBounds.Right - popup.Width));
    int preferredY = _advancedButton.PointToScreen(
      new Point(0, _advancedButton.Height + 2)).Y;
    int y = preferredY + popup.Height <= ownerBounds.Bottom
      ? preferredY
      : anchorScreen.Y - popup.Height - 2;
    y = Math.Clamp(
      y,
      ownerBounds.Top,
      Math.Max(ownerBounds.Top, ownerBounds.Bottom - popup.Height));
    popup.Location = owner.PointToClient(new Point(x, y));
  }

  private void CloseAdvancedPopupCore(bool returnFocus)
  {
    if (_advancedPopup is { IsDisposed: false } popup)
    {
      popup.Visible = false;
    }
    if (returnFocus && _advancedButton.CanFocus)
    {
      _advancedButton.Focus();
    }
  }

  private IEnumerable<Control> GetVisibleAdvancedPopupControls()
  {
    if (_advancedPopup is { IsDisposed: false, Visible: true } popup)
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
      HighlightQueueCapacity = Settings.HighlightQueueCapacity
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
