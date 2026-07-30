using Cyotek.Windows.Forms;
using System.ComponentModel;

namespace AgentPanelSpeaker;

/// <summary>
/// Edits transcript-follow, highlight-colour, fade, and tracking settings.
/// </summary>
internal sealed class TranscriptSettingsPopup : UserControl
{
  private readonly CheckBox _followCheckBox = new();
  private readonly ColorWheel _colourWheel = new();
  private readonly ColorEditor _colourEditor = new();
  private readonly Panel _previousSwatch = new();
  private readonly Panel _currentSwatch = new();
  private readonly TrackBar _fadeSlider = new();
  private readonly Label _fadeValue = new();
  private readonly TrackBar _trackingSlider = new();
  private readonly Label _trackingValue = new();
  private Color _previousColour;
  private bool _dark;
  private bool _updating;

  public TranscriptSettingsPopup()
  {
    AutoScaleMode = AutoScaleMode.Dpi;
    Size = new Size(590, 330);
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

    _followCheckBox.AutoSize = true;
    _followCheckBox.Text = "Follow Speech";
    _followCheckBox.TabIndex = 0;

    _colourWheel.Dock = DockStyle.Fill;
    _colourWheel.Margin = new Padding(0, 3, 8, 3);
    _colourWheel.TabIndex = 1;

    _colourEditor.Dock = DockStyle.Fill;
    _colourEditor.Margin = new Padding(0, 3, 0, 3);
    _colourEditor.Orientation = Orientation.Horizontal;
    _colourEditor.TabIndex = 2;

    ConfigureSwatch(_previousSwatch, "Previous highlight colour");
    ConfigureSwatch(_currentSwatch, "Current highlight colour");
    _previousSwatch.Cursor = Cursors.Hand;
    _previousSwatch.TabStop = true;
    _previousSwatch.TabIndex = 3;
    _currentSwatch.TabStop = false;

    ConfigureSlider(_fadeSlider, 0, 8, 1);
    _fadeSlider.TabIndex = 4;
    ConfigureValueLabel(_fadeValue);

    ConfigureSlider(_trackingSlider, 1, 8, 1);
    _trackingSlider.TabIndex = 5;
    ConfigureValueLabel(_trackingValue);

    var colourLayout = new TableLayoutPanel
    {
      ColumnCount = 2,
      RowCount = 1,
      Dock = DockStyle.Fill,
      Margin = Padding.Empty
    };
    colourLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
    colourLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    colourLayout.Controls.Add(_colourWheel, 0, 0);
    colourLayout.Controls.Add(_colourEditor, 1, 0);

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
    swatchLayout.Controls.Add(CreateInlineLabel("Current"));
    swatchLayout.Controls.Add(_currentSwatch);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 3,
      RowCount = 7,
      Dock = DockStyle.Fill,
      Padding = new Padding(10)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 2));

    layout.Controls.Add(title, 0, 0);
    layout.SetColumnSpan(title, 3);
    layout.Controls.Add(_followCheckBox, 0, 1);
    layout.SetColumnSpan(_followCheckBox, 3);
    layout.Controls.Add(colourLayout, 0, 2);
    layout.SetColumnSpan(colourLayout, 3);
    layout.Controls.Add(swatchLayout, 0, 3);
    layout.SetColumnSpan(swatchLayout, 3);
    AddSliderRow(layout, 4, "Fade Duration", _fadeSlider, _fadeValue);
    AddSliderRow(
      layout,
      5,
      "Tracking Update",
      _trackingSlider,
      _trackingValue);
    Controls.Add(layout);

    _followCheckBox.CheckedChanged += ValueChanged;
    _colourWheel.ColorChanged += ColourWheelChanged;
    _colourEditor.ColorChanged += ColourEditorChanged;
    _previousSwatch.Click += PreviousSwatchClicked;
    _previousSwatch.KeyDown += PreviousSwatchKeyDown;
    _fadeSlider.ValueChanged += ValueChanged;
    _trackingSlider.ValueChanged += ValueChanged;
    Paint += PaintBorder;
    WireHoverEvents(this);
    WireBackgroundFocus(this);
  }

  public event EventHandler? SettingsChanged;

  public event EventHandler<TransportKeyPressedEventArgs>? TransportKeyPressed;

  public event EventHandler<FocusTraversalRequestedEventArgs>?
    FocusTraversalRequested;

  public event EventHandler? DismissRequested;

  public event EventHandler? PointerEntered;

  public event EventHandler? PointerLeft;

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
      _followCheckBox.Checked = Settings.FollowSpeech;
      Color colour = Settings.GetHighlightColour(dark);
      SetPickerColour(colour);
      _previousColour = colour;
      _fadeSlider.Value = FadeStepFromMilliseconds(
        Settings.FadeMilliseconds);
      _trackingSlider.Value = Math.Clamp(
        Settings.HighlightUpdateMilliseconds / 5,
        1,
        8);
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
    _previousColour = CurrentColour;
    UpdateDisplays();
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

    Color colour = Settings.GetHighlightColour(dark);
    _updating = true;
    try
    {
      SetPickerColour(colour);
      _previousColour = colour;
    }
    finally
    {
      _updating = false;
    }
    UpdateDisplays();
    Invalidate(true);
  }

  public void FocusInitialControl()
  {
    _followCheckBox.Focus();
  }

  public bool IsPointerInside()
  {
    return ClientRectangle.Contains(PointToClient(Cursor.Position));
  }

  protected override void OnMouseDown(MouseEventArgs eventArgs)
  {
    base.OnMouseDown(eventArgs);
    if (eventArgs.Button == MouseButtons.Left && !ContainsFocus)
    {
      FocusInitialControl();
    }
  }

  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    Keys code = keyData & Keys.KeyCode;
    Keys modifiers = keyData & Keys.Modifiers;
    bool supportedModifiers = modifiers == Keys.None || modifiers == Keys.Alt;
    bool transportKey = code is Keys.U or Keys.H or Keys.J or Keys.K or
      Keys.L or Keys.OemSemicolon or Keys.O or Keys.OemQuotes;
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
    if (keyData == Keys.Tab && _trackingSlider.ContainsFocus)
    {
      FocusTraversalRequested?.Invoke(
        this,
        new FocusTraversalRequestedEventArgs(forward: true));
      return true;
    }
    if (keyData == (Keys.Shift | Keys.Tab) &&
        _followCheckBox.ContainsFocus)
    {
      FocusTraversalRequested?.Invoke(
        this,
        new FocusTraversalRequestedEventArgs(forward: false));
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  private Color CurrentColour => _colourEditor.Color;

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

  private static void ConfigureSwatch(Panel swatch, string accessibleName)
  {
    swatch.Size = new Size(58, 22);
    swatch.Margin = new Padding(4, 3, 12, 3);
    swatch.BorderStyle = BorderStyle.FixedSingle;
    swatch.AccessibleName = accessibleName;
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

  private void ColourWheelChanged(object? sender, EventArgs eventArgs)
  {
    if (_updating)
    {
      return;
    }
    _updating = true;
    try
    {
      _colourEditor.Color = _colourWheel.Color;
    }
    finally
    {
      _updating = false;
    }
    ValueChanged(sender, eventArgs);
  }

  private void ColourEditorChanged(object? sender, EventArgs eventArgs)
  {
    if (_updating)
    {
      return;
    }
    _updating = true;
    try
    {
      _colourWheel.Color = _colourEditor.Color;
    }
    finally
    {
      _updating = false;
    }
    ValueChanged(sender, eventArgs);
  }

  private void PreviousSwatchClicked(object? sender, EventArgs eventArgs)
  {
    RestorePreviousColour();
  }

  private void PreviousSwatchKeyDown(object? sender, KeyEventArgs eventArgs)
  {
    if (eventArgs.KeyCode is Keys.Enter or Keys.Space)
    {
      RestorePreviousColour();
      eventArgs.Handled = true;
      eventArgs.SuppressKeyPress = true;
    }
  }

  private void RestorePreviousColour()
  {
    Color current = CurrentColour;
    _updating = true;
    try
    {
      SetPickerColour(_previousColour);
      _previousColour = current;
    }
    finally
    {
      _updating = false;
    }
    ValueChanged(this, EventArgs.Empty);
  }

  private void ValueChanged(object? sender, EventArgs eventArgs)
  {
    if (_updating)
    {
      return;
    }

    Color colour = CurrentColour;
    Settings = ((_dark
      ? Settings with { DarkHighlightArgb = colour.ToArgb() }
      : Settings with { LightHighlightArgb = colour.ToArgb() }) with
    {
      FollowSpeech = _followCheckBox.Checked,
      FadeMilliseconds = FadeMillisecondsFromStep(_fadeSlider.Value),
      HighlightUpdateMilliseconds = _trackingSlider.Value * 5
    }).Normalize();
    UpdateDisplays();
    SettingsChanged?.Invoke(this, EventArgs.Empty);
  }

  private void SetPickerColour(Color colour)
  {
    _colourWheel.Color = colour;
    _colourEditor.Color = colour;
  }

  private void UpdateDisplays()
  {
    _fadeValue.Text = $"{_fadeSlider.Value / 16.0:0.####}s";
    _trackingValue.Text = $"{_trackingSlider.Value * 5} ms";
    _currentSwatch.BackColor = CurrentColour;
    _previousSwatch.BackColor = _previousColour;
    _currentSwatch.Invalidate();
    _previousSwatch.Invalidate();
  }

  private static int FadeMillisecondsFromStep(int step)
  {
    return (int)Math.Round(Math.Clamp(step, 0, 8) * 62.5);
  }

  private static int FadeStepFromMilliseconds(int milliseconds)
  {
    return Math.Clamp((int)Math.Round(milliseconds / 62.5), 0, 8);
  }

  private void PaintBorder(object? sender, PaintEventArgs eventArgs)
  {
    Color colour = _dark
      ? Color.FromArgb(105, 105, 110)
      : Color.FromArgb(110, 110, 110);
    using var pen = new Pen(colour);
    Rectangle bounds = ClientRectangle;
    bounds.Width -= 1;
    bounds.Height -= 1;
    eventArgs.Graphics.DrawRectangle(pen, bounds);
  }

  private void WireHoverEvents(Control control)
  {
    control.MouseEnter += (_, _) => PointerEntered?.Invoke(this, EventArgs.Empty);
    control.MouseLeave += (_, _) =>
    {
      if (!IsPointerInside())
      {
        PointerLeft?.Invoke(this, EventArgs.Empty);
      }
    };
    foreach (Control child in control.Controls)
    {
      WireHoverEvents(child);
    }
    control.ControlAdded += (_, eventArgs) =>
    {
      Control? child = eventArgs.Control;
      if (child is not null)
      {
        WireHoverEvents(child);
        WireBackgroundFocus(child);
      }
    };
  }

  private void WireBackgroundFocus(Control control)
  {
    if (control is Label or Panel or TableLayoutPanel or FlowLayoutPanel)
    {
      control.MouseDown += (_, eventArgs) =>
      {
        if (eventArgs.Button == MouseButtons.Left)
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

    if (control is TextBoxBase or ComboBox or NumericUpDown)
    {
      control.BackColor = _dark
        ? Color.FromArgb(35, 35, 38)
        : Color.White;
    }

    foreach (Control child in control.Controls)
    {
      ApplyThemeRecursive(child);
    }
  }
}
