using System.ComponentModel;

namespace AgentPanelSpeaker;

/// <summary>
/// Edits common and advanced transcript display settings.
/// </summary>
internal sealed class TranscriptSettingsPopup : PopupFormBase
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
    FormBorderStyle = FormBorderStyle.None;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.Manual;
    Size = new Size(430, 196);
    TabStop = false;

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
  }

  public void ApplyTheme(bool dark)
  {
    _dark = dark;
    ThemeManager.ApplyPopup(this, dark);
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
      CloseColourPopupCore,
      () => GetOrCreateColourPopup().FocusInitialControl());
    _advancedPopupHandle = popupTree.RegisterChild(
      _advancedButton,
      GetVisibleAdvancedPopupControls,
      ShowAdvancedPopupCore,
      CloseAdvancedPopupCore,
      () => GetOrCreateAdvancedPopup().FocusInitialControl());
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

  protected override bool ShowWithoutActivation => true;

  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (HoverPopupController.HandleGlobalPopupKey(keyData, this))
    {
      return true;
    }

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
    TranscriptColourPopup popup = GetOrCreateColourPopup();
    popup.ApplyTheme(_dark);
    popup.SetColours(_currentColour, _previousColour);
    popup.FitToVisibleControls();
    PositionColourPopup(popup);
    ShowOwnedPopup(popup);
  }

  private TranscriptColourPopup GetOrCreateColourPopup()
  {
    if (_colourPopup is { IsDisposed: false } existing)
    {
      return existing;
    }

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
    return popup;
  }

  private void PositionColourPopup(TranscriptColourPopup popup)
  {
    const int gap = 2;
    Rectangle workArea = Screen.FromControl(_currentSwatch).WorkingArea;
    Rectangle ownerBounds = Bounds;
    Point anchor = _currentSwatch.PointToScreen(Point.Empty);
    int maxX = Math.Max(workArea.Left, workArea.Right - popup.Width);
    int x = Math.Clamp(
      anchor.X + _currentSwatch.Width - popup.Width,
      workArea.Left,
      maxX);

    int above = ownerBounds.Top - popup.Height - gap;
    int below = ownerBounds.Bottom + gap;
    int y;
    if (above >= workArea.Top)
    {
      y = above;
    }
    else if (below + popup.Height <= workArea.Bottom)
    {
      y = below;
    }
    else
    {
      y = Math.Clamp(
        anchor.Y - popup.Height - gap,
        workArea.Top,
        Math.Max(workArea.Top, workArea.Bottom - popup.Height));
    }

    popup.Location = new Point(x, y);
    DiagnosticLog.Write("popup.colour_position", new
    {
      popupBounds = new Rectangle(popup.Location, popup.Size),
      ownerBounds,
      workArea,
      anchor,
      overlapsOwner = new Rectangle(popup.Location, popup.Size)
        .IntersectsWith(ownerBounds)
    });
  }

  private void CloseColourPopupCore(bool returnFocus)
  {
    WriteNestedCloseFocusDiagnostics(
      "colour.before-hide",
      _colourPopup,
      _currentSwatch,
      returnFocus);
    if (_colourPopup is { IsDisposed: false } popup)
    {
      popup.Hide();
    }
    WriteNestedCloseFocusDiagnostics(
      "colour.after-hide",
      _colourPopup,
      _currentSwatch,
      returnFocus);
    FlushPendingColourChange();
    if (returnFocus && _currentSwatch.CanFocus)
    {
      bool wasActive = ReferenceEquals(Form.ActiveForm, this);
      Activate();
      WriteNestedCloseFocusDiagnostics(
        "colour.after-activate",
        _colourPopup,
        _currentSwatch,
        returnFocus,
        wasActive: wasActive);
      bool focused = _currentSwatch.Focus();
      WriteNestedCloseFocusDiagnostics(
        "colour.after-target-focus",
        _colourPopup,
        _currentSwatch,
        returnFocus,
        focusResult: focused);
      QueueNestedCloseFocusSettledDiagnostics(
        "colour.settled",
        _colourPopup,
        _currentSwatch,
        returnFocus);
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
    TranscriptAdvancedSettingsPopup popup = GetOrCreateAdvancedPopup();
    popup.ApplyTheme(_dark);
    popup.SetQueueCapacity(Settings.HighlightQueueCapacity);
    PositionAdvancedPopup(popup);
    ShowOwnedPopup(popup);
  }

  private TranscriptAdvancedSettingsPopup GetOrCreateAdvancedPopup()
  {
    if (_advancedPopup is { IsDisposed: false } existing)
    {
      return existing;
    }

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
    return popup;
  }

  private void PositionAdvancedPopup(TranscriptAdvancedSettingsPopup popup)
  {
    Point anchorScreen = _advancedButton.PointToScreen(Point.Empty);
    Rectangle workArea = Screen.FromControl(_advancedButton).WorkingArea;
    int x = Math.Clamp(
      anchorScreen.X,
      workArea.Left,
      Math.Max(workArea.Left, workArea.Right - popup.Width));
    int preferredY = _advancedButton.PointToScreen(
      new Point(0, _advancedButton.Height + 2)).Y;
    int y = preferredY + popup.Height <= workArea.Bottom
      ? preferredY
      : anchorScreen.Y - popup.Height - 2;
    y = Math.Clamp(
      y,
      workArea.Top,
      Math.Max(workArea.Top, workArea.Bottom - popup.Height));
    popup.Location = new Point(x, y);
  }

  private void CloseAdvancedPopupCore(bool returnFocus)
  {
    WriteNestedCloseFocusDiagnostics(
      "advanced.before-hide",
      _advancedPopup,
      _advancedButton,
      returnFocus);
    if (_advancedPopup is { IsDisposed: false } popup)
    {
      popup.Hide();
    }
    WriteNestedCloseFocusDiagnostics(
      "advanced.after-hide",
      _advancedPopup,
      _advancedButton,
      returnFocus);
    if (returnFocus && _advancedButton.CanFocus)
    {
      bool wasActive = ReferenceEquals(Form.ActiveForm, this);
      Activate();
      WriteNestedCloseFocusDiagnostics(
        "advanced.after-activate",
        _advancedPopup,
        _advancedButton,
        returnFocus,
        wasActive: wasActive);
      bool focused = _advancedButton.Focus();
      WriteNestedCloseFocusDiagnostics(
        "advanced.after-target-focus",
        _advancedPopup,
        _advancedButton,
        returnFocus,
        focusResult: focused);
      QueueNestedCloseFocusSettledDiagnostics(
        "advanced.settled",
        _advancedPopup,
        _advancedButton,
        returnFocus);
    }
  }

  private IEnumerable<Control> GetVisibleAdvancedPopupControls()
  {
    if (_advancedPopup is { IsDisposed: false, Visible: true } popup)
    {
      yield return popup;
    }
  }

  private void WriteNestedCloseFocusDiagnostics(
    string stage,
    Form? child,
    Control target,
    bool returnFocus,
    bool? wasActive = null,
    bool? focusResult = null)
  {
    DiagnosticLog.Write("popup.nested_close_focus", new
    {
      stage,
      returnFocus,
      wasActive,
      focusResult,
      parentType = GetType().FullName,
      parentVisible = Visible,
      parentContainsFocus = ContainsFocus,
      parentActiveControl = DescribeFocusControl(ActiveControl),
      parentFocusedControl = DescribeFocusControl(FindFocusedControl(this)),
      activeForm = DescribeFocusControl(Form.ActiveForm),
      child = DescribeFocusControl(child),
      childVisible = child?.Visible,
      childContainsFocus = child?.ContainsFocus,
      childActiveControl = DescribeFocusControl(child?.ActiveControl),
      childFocusedControl = DescribeFocusControl(FindFocusedControl(child)),
      target = DescribeFocusControl(target)
    });
  }

  private void QueueNestedCloseFocusSettledDiagnostics(
    string stage,
    Form? child,
    Control target,
    bool returnFocus)
  {
    if (!IsHandleCreated)
    {
      return;
    }
    BeginInvoke((MethodInvoker)(() =>
      WriteNestedCloseFocusDiagnostics(stage, child, target, returnFocus)));
  }

  private static Control? FindFocusedControl(Control? root)
  {
    if (root is null)
    {
      return null;
    }
    if (root.Focused)
    {
      return root;
    }
    foreach (Control child in root.Controls)
    {
      Control? focused = FindFocusedControl(child);
      if (focused is not null)
      {
        return focused;
      }
    }
    return null;
  }

  private static object? DescribeFocusControl(Control? control)
  {
    return control is null
      ? null
      : new
      {
        type = control.GetType().FullName,
        control.Name,
        control.Text,
        control.Visible,
        control.Enabled,
        control.CanFocus,
        control.Focused,
        control.ContainsFocus,
        control.IsHandleCreated,
        control.IsDisposed,
        bounds = control.Bounds.ToString()
      };
  }

  private void ShowOwnedPopup(Form popup)
  {
    if (popup is not PopupFormBase popupForm)
    {
      throw new InvalidOperationException(
        "Nested transcript popups must derive from PopupFormBase.");
    }

    popupForm.ShowAboveOwner(this);
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
    using var pen = new Pen(ThemeManager.GetBorder(_dark));
    Rectangle bounds = ClientRectangle;
    bounds.Width -= 1;
    bounds.Height -= 1;
    eventArgs.Graphics.DrawRectangle(pen, bounds);
  }


}
