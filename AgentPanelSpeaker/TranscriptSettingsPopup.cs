using System.ComponentModel;

namespace AgentPanelSpeaker;

/// <summary>
/// Edits transcript-follow, highlight-colour, and fade settings immediately.
/// </summary>
internal sealed class TranscriptSettingsPopup : UserControl
{
  private readonly CheckBox _followCheckBox = new();
  private readonly Button _colourButton = new();
  private readonly TrackBar _fadeSlider = new();
  private readonly Label _fadeValue = new();
  private bool _dark;
  private bool _updating;

  public TranscriptSettingsPopup()
  {
    AutoScaleMode = AutoScaleMode.Dpi;
    Size = new Size(360, 148);
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
    _colourButton.AutoSize = false;
    _colourButton.Size = new Size(70, 24);
    _colourButton.Text = "Colour";
    _fadeSlider.AutoSize = false;
    _fadeSlider.Minimum = 0;
    _fadeSlider.Maximum = 8;
    _fadeSlider.TickFrequency = 1;
    _fadeSlider.SmallChange = 1;
    _fadeSlider.LargeChange = 1;
    _fadeSlider.Dock = DockStyle.Fill;
    _fadeValue.AutoSize = false;
    _fadeValue.Dock = DockStyle.Fill;
    _fadeValue.TextAlign = ContentAlignment.MiddleRight;

    var layout = new TableLayoutPanel
    {
      ColumnCount = 3,
      RowCount = 4,
      Dock = DockStyle.Fill,
      Padding = new Padding(8)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.Controls.Add(title, 0, 0);
    layout.SetColumnSpan(title, 3);
    layout.Controls.Add(_followCheckBox, 0, 1);
    layout.SetColumnSpan(_followCheckBox, 3);
    layout.Controls.Add(CreateLabel("Highlight Colour"), 0, 2);
    layout.Controls.Add(_colourButton, 1, 2);
    layout.Controls.Add(CreateLabel("Fade Duration"), 0, 3);
    layout.Controls.Add(_fadeSlider, 1, 3);
    layout.Controls.Add(_fadeValue, 2, 3);
    Controls.Add(layout);

    _followCheckBox.CheckedChanged += ValueChanged;
    _fadeSlider.ValueChanged += ValueChanged;
    _colourButton.Click += ColourButtonClicked;
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
      _followCheckBox.Checked = Settings.FollowSpeech;
      _fadeSlider.Value = Settings.FadeMilliseconds / 250;
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
    ApplyThemeRecursive(this, dark);
    UpdateDisplays();
    Invalidate(true);
  }

  public void FocusInitialControl()
  {
    _followCheckBox.Focus();
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
      if (_followCheckBox.ContainsFocus)
      {
        _colourButton.Focus();
      }
      else if (_colourButton.ContainsFocus)
      {
        _fadeSlider.Focus();
      }
      else
      {
        FocusTraversalRequested?.Invoke(
          this,
          new FocusTraversalRequestedEventArgs(forward: true));
      }
      return true;
    }
    if (keyData == (Keys.Shift | Keys.Tab))
    {
      if (_fadeSlider.ContainsFocus)
      {
        _colourButton.Focus();
      }
      else if (_colourButton.ContainsFocus)
      {
        _followCheckBox.Focus();
      }
      else
      {
        FocusTraversalRequested?.Invoke(
          this,
          new FocusTraversalRequestedEventArgs(forward: false));
      }
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
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
    Settings = Settings with
    {
      FollowSpeech = _followCheckBox.Checked,
      FadeMilliseconds = _fadeSlider.Value * 250
    };
    UpdateDisplays();
    SettingsChanged?.Invoke(this, EventArgs.Empty);
  }

  private void ColourButtonClicked(object? sender, EventArgs eventArgs)
  {
    Color current = Settings.GetHighlightColour(_dark);
    using var dialog = new ColorDialog
    {
      Color = current,
      FullOpen = true,
      AnyColor = true
    };
    if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
    {
      return;
    }
    Settings = _dark
      ? Settings with { DarkHighlightArgb = dialog.Color.ToArgb() }
      : Settings with { LightHighlightArgb = dialog.Color.ToArgb() };
    UpdateDisplays();
    SettingsChanged?.Invoke(this, EventArgs.Empty);
  }

  private void UpdateDisplays()
  {
    _fadeValue.Text = $"{Settings.FadeMilliseconds / 1000.0:0.00}s";
    _colourButton.BackColor = Settings.GetHighlightColour(_dark);
    _colourButton.ForeColor = GetContrastingColour(_colourButton.BackColor);
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

  private static Color GetContrastingColour(Color colour)
  {
    double luminance = 0.2126 * colour.R +
      0.7152 * colour.G +
      0.0722 * colour.B;
    return luminance < 135 ? Color.White : Color.Black;
  }

  private static void ApplyThemeRecursive(Control control, bool dark)
  {
    foreach (Control child in control.Controls)
    {
      if (child is not Button)
      {
        child.BackColor = control.BackColor;
        child.ForeColor = control.ForeColor;
      }
      ApplyThemeRecursive(child, dark);
    }
  }
}
