namespace AgentPanelSpeaker;

/// <summary>
/// Edits one compact speech profile using three keyboard-accessible sliders.
/// </summary>
internal sealed class SpeechProfilePopup : UserControl
{
  private readonly SpeechProfileCompactControl _ownerControl;
  private readonly Label _titleLabel;
  private readonly Label _rateLabel;
  private readonly Label _pitchLabel;
  private readonly Label _volumeLabel;
  private readonly Label _rateValue;
  private readonly Label _pitchValue;
  private readonly Label _volumeValue;
  private readonly TrackBar _rateSlider;
  private readonly TrackBar _pitchSlider;
  private readonly TrackBar _volumeSlider;
  private readonly List<Button> _testButtons = new();
  private bool _updating;

  /// <summary>
  /// Initializes the profile editor.
  /// </summary>
  public SpeechProfilePopup(SpeechProfileCompactControl ownerControl)
  {
    _ownerControl = ownerControl ??
      throw new ArgumentNullException(nameof(ownerControl));

    AutoScaleMode = AutoScaleMode.Dpi;
    Size = new Size(344, ownerControl.TestActions.Count > 0 ? 286 : 164);
    TabStop = false;
    Visible = false;

    _titleLabel = new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = _ownerControl.ProfileName,
      TextAlign = ContentAlignment.MiddleLeft,
      Font = new Font(
        SystemFonts.MessageBoxFont ?? Control.DefaultFont,
        FontStyle.Bold)
    };

    _rateLabel = CreateRowLabel("Rate");
    _pitchLabel = CreateRowLabel("Pitch");
    _volumeLabel = CreateRowLabel("Volume");
    _rateValue = CreateValueLabel();
    _pitchValue = CreateValueLabel();
    _volumeValue = CreateValueLabel();
    _rateSlider = CreateTrackBar(-10, 10, 1);
    _pitchSlider = CreateTrackBar(-10, 10, 1);
    _volumeSlider = CreateTrackBar(0, 100, 10);

    _rateSlider.ValueChanged += SliderValueChanged;
    _pitchSlider.ValueChanged += SliderValueChanged;
    _volumeSlider.ValueChanged += SliderValueChanged;
    WireSliderDragging(_rateSlider);
    WireSliderDragging(_pitchSlider);
    WireSliderDragging(_volumeSlider);

    Controls.Add(CreateLayout());
    Paint += PaintBorder;
  }

  /// <summary>
  /// Applies matching light or dark colours.
  /// </summary>
  public void ApplyTheme(bool dark)
  {
    if (IsDisposed)
    {
      return;
    }

    ThemeManager.ApplyPopup(this, dark);
  }

  /// <summary>
  /// Copies current profile values into the editor.
  /// </summary>
  public void SyncFromProfile()
  {
    _updating = true;
    try
    {
      _rateSlider.Value = _ownerControl.Rate;
      _pitchSlider.Value = _ownerControl.Pitch;
      _volumeSlider.Value = _ownerControl.Volume;
      UpdateEnabledState();
      UpdateValueLabels();
    }
    finally
    {
      _updating = false;
    }
  }

  /// <summary>
  /// Focuses Rate normally, or Volume when the profile is muted.
  /// </summary>
  public void FocusInitialSlider()
  {
    if (_ownerControl.Volume == 0)
    {
      _volumeSlider.Focus();
    }
    else
    {
      _rateSlider.Focus();
    }
  }

  /// <summary>
  /// Returns whether the pointer is inside the popup.
  /// </summary>
  public bool IsPointerInside()
  {
    return ClientRectangle.Contains(PointToClient(Cursor.Position));
  }

  /// <inheritdoc />
  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (HoverPopupController.HandleGlobalPopupKey(keyData, this))
    {
      return true;
    }

    if (_ownerControl.TryHandleTransportKey(keyData))
    {
      return true;
    }

    if (keyData == Keys.Escape)
    {
      _ownerControl.CloseEditorFromDismissKey();
      return true;
    }

    if (keyData == Keys.Tab)
    {
      MoveForward();
      return true;
    }

    if (keyData == (Keys.Shift | Keys.Tab))
    {
      MoveBackward();
      return true;
    }

    return base.ProcessCmdKey(ref message, keyData);
  }

  private Control CreateLayout()
  {
    bool hasTests = _ownerControl.TestActions.Count > 0;
    var layout = new TableLayoutPanel
    {
      ColumnCount = 3,
      RowCount = hasTests ? 7 : 4,
      Dock = DockStyle.Fill,
      Padding = new Padding(8),
      Margin = Padding.Empty
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
    if (hasTests)
    {
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
    }

    layout.Controls.Add(_titleLabel, 0, 0);
    layout.SetColumnSpan(_titleLabel, 3);
    AddSliderRow(layout, 1, _rateLabel, _rateSlider, _rateValue);
    AddSliderRow(layout, 2, _pitchLabel, _pitchSlider, _pitchValue);
    AddSliderRow(layout, 3, _volumeLabel, _volumeSlider, _volumeValue);
    if (hasTests)
    {
      AddTestButtons(layout);
    }
    return layout;
  }

  private void AddTestButtons(TableLayoutPanel layout)
  {
    var tests = new TableLayoutPanel
    {
      ColumnCount = 2,
      RowCount = 3,
      Dock = DockStyle.Fill,
      Margin = Padding.Empty,
      Padding = Padding.Empty
    };
    tests.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
    tests.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
    tests.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
    tests.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
    tests.RowStyles.Add(new RowStyle(SizeType.Percent, 33.334f));

    IReadOnlyList<SpeechProfileTestAction> actions = _ownerControl.TestActions;
    for (int index = 0; index < actions.Count; ++index)
    {
      SpeechProfileTestAction action = actions[index];
      var button = new Button
      {
        Dock = DockStyle.Fill,
        Margin = new Padding(3),
        Text = action.Text,
        AutoEllipsis = true,
        TabStop = true
      };
      button.Click += (_, _) => action.Invoke();
      _testButtons.Add(button);
      tests.Controls.Add(button, index % 2, index / 2);
    }

    layout.Controls.Add(tests, 0, 4);
    layout.SetColumnSpan(tests, 3);
    layout.SetRowSpan(tests, 3);
  }

  private static void AddSliderRow(
    TableLayoutPanel layout,
    int row,
    Label label,
    TrackBar slider,
    Label valueLabel)
  {
    slider.Dock = DockStyle.Fill;
    slider.Margin = new Padding(0, 0, 0, 1);
    valueLabel.Dock = DockStyle.Fill;
    valueLabel.Margin = Padding.Empty;

    layout.Controls.Add(label, 0, row);
    layout.Controls.Add(slider, 1, row);
    layout.Controls.Add(valueLabel, 2, row);
  }

  private static TrackBar CreateTrackBar(
    int minimum,
    int maximum,
    int tickFrequency)
  {
    return new TrackBar
    {
      AutoSize = false,
      Minimum = minimum,
      Maximum = maximum,
      TickFrequency = tickFrequency,
      TickStyle = TickStyle.BottomRight,
      SmallChange = 1,
      LargeChange = tickFrequency,
      TabStop = true
    };
  }

  private static Label CreateRowLabel(string text)
  {
    return new Label
    {
      AutoSize = false,
      Dock = DockStyle.Fill,
      Text = text,
      TextAlign = ContentAlignment.MiddleLeft,
      Margin = Padding.Empty
    };
  }

  private static Label CreateValueLabel()
  {
    return new Label
    {
      AutoSize = false,
      TextAlign = ContentAlignment.MiddleRight
    };
  }

  private void WireSliderDragging(TrackBar slider)
  {
    slider.MouseDown += (_, eventArgs) =>
    {
      if (eventArgs.Button == MouseButtons.Left)
      {
        _ownerControl.SetPopupDragging(true);
      }
    };
    slider.MouseUp += (_, _) => EndSliderDrag();
    slider.MouseCaptureChanged += (_, _) =>
    {
      if (!slider.Capture)
      {
        EndSliderDrag();
      }
    };
  }

  private void EndSliderDrag()
  {
    _ownerControl.SetPopupDragging(false);
  }

  private void SliderValueChanged(object? sender, EventArgs eventArgs)
  {
    if (_updating)
    {
      return;
    }

    _ownerControl.SetProfile(
      _rateSlider.Value,
      _pitchSlider.Value,
      _volumeSlider.Value);
    UpdateEnabledState();
    UpdateValueLabels();
  }

  private void UpdateEnabledState()
  {
    bool spoken = _volumeSlider.Value > 0;
    _rateSlider.Enabled = spoken;
    _pitchSlider.Enabled = spoken;
    _rateLabel.Enabled = spoken;
    _pitchLabel.Enabled = spoken;
    _rateValue.Enabled = spoken;
    _pitchValue.Enabled = spoken;
    _rateSlider.TabStop = spoken;
    _pitchSlider.TabStop = spoken;

    if (!spoken && (_rateSlider.ContainsFocus || _pitchSlider.ContainsFocus))
    {
      _volumeSlider.Focus();
    }
  }

  private void UpdateValueLabels()
  {
    _rateValue.Text = _rateSlider.Value.ToString();
    _pitchValue.Text = _pitchSlider.Value.ToString();
    _volumeValue.Text = _volumeSlider.Value.ToString();
  }

  private void MoveForward()
  {
    if (_ownerControl.Volume > 0 && _rateSlider.ContainsFocus)
    {
      _pitchSlider.Focus();
      return;
    }

    if (_ownerControl.Volume > 0 && _pitchSlider.ContainsFocus)
    {
      _volumeSlider.Focus();
      return;
    }

    if (_volumeSlider.ContainsFocus && _testButtons.Count > 0)
    {
      _testButtons[0].Focus();
      return;
    }

    int buttonIndex = _testButtons.FindIndex(button => button.ContainsFocus);
    if (buttonIndex >= 0 && buttonIndex + 1 < _testButtons.Count)
    {
      _testButtons[buttonIndex + 1].Focus();
      return;
    }

    _ownerControl.MoveOutsideEditor(forward: true);
  }

  private void MoveBackward()
  {
    int buttonIndex = _testButtons.FindIndex(button => button.ContainsFocus);
    if (buttonIndex > 0)
    {
      _testButtons[buttonIndex - 1].Focus();
      return;
    }

    if (buttonIndex == 0)
    {
      _volumeSlider.Focus();
      return;
    }

    if (_ownerControl.Volume > 0 && _volumeSlider.ContainsFocus)
    {
      _pitchSlider.Focus();
      return;
    }

    if (_ownerControl.Volume > 0 && _pitchSlider.ContainsFocus)
    {
      _rateSlider.Focus();
      return;
    }

    _ownerControl.MoveOutsideEditor(forward: false);
  }


  private void PaintBorder(object? sender, PaintEventArgs eventArgs)
  {
    Color borderColour = ThemeManager.GetBorder(
      BackColor.GetBrightness() < 0.35f);
    using var border = new Pen(borderColour, 1.0f);
    Rectangle bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
    eventArgs.Graphics.DrawRectangle(border, bounds);
  }


}
