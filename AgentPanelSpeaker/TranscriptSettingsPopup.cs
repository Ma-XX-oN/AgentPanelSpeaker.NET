using System.ComponentModel;

namespace AgentPanelSpeaker;

/// <summary>
/// Edits transcript-follow, highlight-colour, and fade settings immediately.
/// </summary>
internal sealed class TranscriptSettingsPopup : UserControl
{
  private readonly CheckBox _followCheckBox = new();
  private readonly Panel _colourSwatch = new();
  private readonly TrackBar _redSlider = new();
  private readonly TrackBar _greenSlider = new();
  private readonly TrackBar _blueSlider = new();
  private readonly Label _redValue = new();
  private readonly Label _greenValue = new();
  private readonly Label _blueValue = new();
  private readonly TrackBar _fadeSlider = new();
  private readonly Label _fadeValue = new();
  private bool _dark;
  private bool _updating;

  public TranscriptSettingsPopup()
  {
    AutoScaleMode = AutoScaleMode.Dpi;
    Size = new Size(390, 254);
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
    _colourSwatch.Size = new Size(74, 22);
    _colourSwatch.Tag = "colour-swatch";
    _colourSwatch.BorderStyle = BorderStyle.FixedSingle;
    _colourSwatch.Margin = new Padding(3, 3, 3, 3);
    ConfigureColourSlider(_redSlider);
    ConfigureColourSlider(_greenSlider);
    ConfigureColourSlider(_blueSlider);
    ConfigureValueLabel(_redValue);
    ConfigureValueLabel(_greenValue);
    ConfigureValueLabel(_blueValue);
    _fadeSlider.AutoSize = false;
    _fadeSlider.Minimum = 0;
    _fadeSlider.Maximum = 8;
    _fadeSlider.TickFrequency = 1;
    _fadeSlider.SmallChange = 1;
    _fadeSlider.LargeChange = 1;
    _fadeSlider.Dock = DockStyle.Fill;
    ConfigureValueLabel(_fadeValue);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 3,
      RowCount = 8,
      Dock = DockStyle.Fill,
      Padding = new Padding(8)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.Controls.Add(title, 0, 0);
    layout.SetColumnSpan(title, 3);
    layout.Controls.Add(_followCheckBox, 0, 1);
    layout.SetColumnSpan(_followCheckBox, 3);
    layout.Controls.Add(CreateLabel("Highlight Colour"), 0, 2);
    layout.Controls.Add(_colourSwatch, 1, 2);
    AddSliderRow(layout, 3, "Red", _redSlider, _redValue);
    AddSliderRow(layout, 4, "Green", _greenSlider, _greenValue);
    AddSliderRow(layout, 5, "Blue", _blueSlider, _blueValue);
    AddSliderRow(layout, 6, "Fade Duration", _fadeSlider, _fadeValue);
    Controls.Add(layout);

    _followCheckBox.CheckedChanged += ValueChanged;
    _redSlider.ValueChanged += ValueChanged;
    _greenSlider.ValueChanged += ValueChanged;
    _blueSlider.ValueChanged += ValueChanged;
    _fadeSlider.ValueChanged += ValueChanged;
    Paint += PaintBorder;
    WireHoverEvents(this);
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
      _redSlider.Value = colour.R;
      _greenSlider.Value = colour.G;
      _blueSlider.Value = colour.B;
      _fadeSlider.Value = FadeStepFromMilliseconds(
        Settings.FadeMilliseconds);
      UpdateDisplays();
    }
    finally
    {
      _updating = false;
    }
    ApplyTheme(dark);
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
      _redSlider.Value = colour.R;
      _greenSlider.Value = colour.G;
      _blueSlider.Value = colour.B;
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
    if (keyData == Keys.Tab)
    {
      MoveFocus(forward: true);
      return true;
    }
    if (keyData == (Keys.Shift | Keys.Tab))
    {
      MoveFocus(forward: false);
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  private static void ConfigureColourSlider(TrackBar slider)
  {
    slider.AutoSize = false;
    slider.Minimum = 0;
    slider.Maximum = 255;
    slider.TickStyle = TickStyle.None;
    slider.SmallChange = 1;
    slider.LargeChange = 16;
    slider.Dock = DockStyle.Fill;
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

  private void ValueChanged(object? sender, EventArgs eventArgs)
  {
    if (_updating)
    {
      return;
    }
    Color colour = Color.FromArgb(
      _redSlider.Value,
      _greenSlider.Value,
      _blueSlider.Value);
    Settings = (_dark
      ? Settings with { DarkHighlightArgb = colour.ToArgb() }
      : Settings with { LightHighlightArgb = colour.ToArgb() }) with
    {
      FollowSpeech = _followCheckBox.Checked,
      FadeMilliseconds = FadeMillisecondsFromStep(_fadeSlider.Value)
    };
    UpdateDisplays();
    SettingsChanged?.Invoke(this, EventArgs.Empty);
  }

  private void UpdateDisplays()
  {
    _redValue.Text = _redSlider.Value.ToString();
    _greenValue.Text = _greenSlider.Value.ToString();
    _blueValue.Text = _blueSlider.Value.ToString();
    _fadeValue.Text = $"{_fadeSlider.Value / 16.0:0.####}s";
    _colourSwatch.BackColor = Color.FromArgb(
      _redSlider.Value,
      _greenSlider.Value,
      _blueSlider.Value);
    _colourSwatch.Invalidate();
  }

  private void MoveFocus(bool forward)
  {
    Control[] order =
    {
      _followCheckBox,
      _redSlider,
      _greenSlider,
      _blueSlider,
      _fadeSlider
    };
    int index = Array.FindIndex(order, control => control.ContainsFocus);
    int next = index + (forward ? 1 : -1);
    if (index >= 0 && next >= 0 && next < order.Length)
    {
      order[next].Focus();
      return;
    }
    FocusTraversalRequested?.Invoke(
      this,
      new FocusTraversalRequestedEventArgs(forward));
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
      }
    };
  }

  private static void ApplyThemeRecursive(Control control)
  {
    foreach (Control child in control.Controls)
    {
      if (child is not Panel panel || panel.Tag is not "colour-swatch")
      {
        child.BackColor = control.BackColor;
        child.ForeColor = control.ForeColor;
      }
      ApplyThemeRecursive(child);
    }
  }
}
