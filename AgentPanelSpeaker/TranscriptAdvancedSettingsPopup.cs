namespace AgentPanelSpeaker;

/// <summary>
/// Edits advanced transcript playback-display settings.
/// </summary>
internal sealed class TranscriptAdvancedSettingsPopup : UserControl
{
  private const string BufferingDescription =
    "Controls how many pending word-highlight positions are retained when " +
    "speech produces updates faster than the transcript display can process. " +
    "Lower values keep the cursor closer to the spoken audio by discarding " +
    "older pending positions. Higher values preserve more intermediate " +
    "movements but can make the visible cursor lag behind speech.";

  private readonly TrackBar _queueCapacitySlider = new();
  private readonly Label _queueCapacityValue = new();
  private bool _dark;
  private bool _updating;

  public TranscriptAdvancedSettingsPopup()
  {
    AutoScaleMode = AutoScaleMode.Dpi;
    Width = 540;
    TabStop = false;
    Visible = false;

    var title = new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = "Advanced Transcript Settings",
      TextAlign = ContentAlignment.MiddleLeft,
      Font = new Font(
        SystemFonts.MessageBoxFont ?? Control.DefaultFont,
        FontStyle.Bold)
    };

    var settingTitle = new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = "Highlight buffering",
      TextAlign = ContentAlignment.MiddleLeft,
      Font = new Font(
        SystemFonts.MessageBoxFont ?? Control.DefaultFont,
        FontStyle.Bold)
    };

    var description = new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = BufferingDescription,
      TextAlign = ContentAlignment.TopLeft
    };

    _queueCapacitySlider.AutoSize = false;
    _queueCapacitySlider.Minimum = 1;
    _queueCapacitySlider.Maximum = 16;
    _queueCapacitySlider.TickFrequency = 1;
    _queueCapacitySlider.SmallChange = 1;
    _queueCapacitySlider.LargeChange = 4;
    _queueCapacitySlider.Dock = DockStyle.Fill;
    _queueCapacitySlider.TabIndex = 0;
    _queueCapacitySlider.AccessibleName = "Highlight buffering";
    _queueCapacitySlider.AccessibleDescription = BufferingDescription;

    _queueCapacityValue.AutoSize = false;
    _queueCapacityValue.Dock = DockStyle.Fill;
    _queueCapacityValue.TextAlign = ContentAlignment.MiddleRight;

    var scaleLabels = new TableLayoutPanel
    {
      ColumnCount = 2,
      RowCount = 1,
      Dock = DockStyle.Fill,
      Margin = Padding.Empty
    };
    scaleLabels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
    scaleLabels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
    scaleLabels.Controls.Add(new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = "Latest only (recommended)",
      TextAlign = ContentAlignment.MiddleLeft
    }, 0, 0);
    scaleLabels.Controls.Add(new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = "Retain 16 positions",
      TextAlign = ContentAlignment.MiddleRight
    }, 1, 0);

    var sliderLayout = new TableLayoutPanel
    {
      ColumnCount = 2,
      RowCount = 1,
      Dock = DockStyle.Fill,
      Margin = Padding.Empty
    };
    sliderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    sliderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
    sliderLayout.Controls.Add(_queueCapacitySlider, 0, 0);
    sliderLayout.Controls.Add(_queueCapacityValue, 1, 0);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 1,
      RowCount = 5,
      Dock = DockStyle.Fill,
      Padding = new Padding(14)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    Size descriptionSize = TextRenderer.MeasureText(
      BufferingDescription,
      description.Font,
      new Size(Width - layout.Padding.Horizontal, int.MaxValue),
      TextFormatFlags.TextBoxControl |
      TextFormatFlags.WordBreak |
      TextFormatFlags.NoPadding);
    int descriptionHeight = descriptionSize.Height + 6;

    layout.RowStyles.Add(new RowStyle(
      SizeType.Absolute,
      descriptionHeight));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
    Height = layout.Padding.Vertical +
      28 +
      28 +
      descriptionHeight +
      42 +
      24 +
      2;
    layout.Controls.Add(title, 0, 0);
    layout.Controls.Add(settingTitle, 0, 1);
    layout.Controls.Add(description, 0, 2);
    layout.Controls.Add(sliderLayout, 0, 3);
    layout.Controls.Add(scaleLabels, 0, 4);
    Controls.Add(layout);

    _queueCapacitySlider.ValueChanged += (_, _) =>
    {
      UpdateValueText();
      if (!_updating)
      {
        ValueChanged?.Invoke(this, EventArgs.Empty);
      }
    };

    Paint += PaintBorder;
  }

  public event EventHandler? ValueChanged;
  public event EventHandler? DismissRequested;

  public int QueueCapacity => _queueCapacitySlider.Value;

  public void SetQueueCapacity(int capacity)
  {
    _updating = true;
    try
    {
      _queueCapacitySlider.Value = Math.Clamp(capacity, 1, 16);
      UpdateValueText();
    }
    finally
    {
      _updating = false;
    }
  }

  public void ApplyTheme(bool dark)
  {
    _dark = dark;
    ApplyThemeRecursive(this);
    Invalidate(true);
  }

  public void PrepareInitialControl()
  {
    ActiveControl = _queueCapacitySlider;
  }

  public void FocusInitialControl()
  {
    PrepareInitialControl();
    Select();
  }

  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (keyData == Keys.Escape || keyData == (Keys.Alt | Keys.F4))
    {
      DismissRequested?.Invoke(this, EventArgs.Empty);
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  private void UpdateValueText()
  {
    int value = _queueCapacitySlider.Value;
    _queueCapacityValue.Text = value == 1
      ? "1 position"
      : $"{value} positions";
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
