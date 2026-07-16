namespace AgentPanelSpeaker;

/// <summary>
/// Edits and tests the optional Bluetooth wake-tone prefix.
/// </summary>
internal sealed class AudioWakeSettingsDialog : Form
{
  private readonly SpeechService _speech;
  private readonly ComboBox _testProfileComboBox = new();
  private readonly CheckBox _enabledCheckBox = new();
  private readonly NumericUpDown _quietNumeric = new();
  private readonly NumericUpDown _frequencyNumeric = new();
  private readonly TrackBar _volumeSlider = new();
  private readonly Label _volumeValueLabel = new();
  private readonly NumericUpDown _playNumeric = new();
  private readonly NumericUpDown _settleNumeric = new();
  private readonly NumericUpDown _ipaDelayNumeric = new();
  private readonly Button _testButton = new();
  private readonly Button _testPhraseButton = new();
  private readonly Button _okButton = new();
  private readonly Button _cancelButton = new();

  /// <summary>
  /// Initializes the audio-wake settings editor.
  /// </summary>
  public AudioWakeSettingsDialog(
    AudioWakeSettings settings,
    SpeechService speech,
    IReadOnlyList<AudioWakeTestProfile> testProfiles,
    AppTheme theme)
  {
    ArgumentNullException.ThrowIfNull(testProfiles);
    _speech = speech;
    Text = "Bluetooth audio wake";
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    MinimumSize = new Size(600, 540);
    Size = new Size(660, 600);

    var explanation = new Label
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      MaximumSize = new Size(610, 0),
      Text =
        "Some Bluetooth devices enter a power-saving state after silence and " +
        "can clip the beginning of the next sentence. When enabled, Agent " +
        "Panel Speaker emits a short high-frequency tone after the " +
        "configured " +
        "quiet period, keeps one audio stream open through the settling " +
        "silence, and then speaks. The tone is best-effort: codecs, drivers, " +
        "and speakers may filter it " +
        "or make it audible."
    };

    _enabledCheckBox.AutoSize = true;
    _enabledCheckBox.Text = "Enable wake prefix";
    ConfigureNumeric(_quietNumeric, 0, 60000, 25);
    ConfigureNumeric(_frequencyNumeric, 8000, 22000, 100);
    ConfigureNumeric(_playNumeric, 10, 5000, 10);
    ConfigureNumeric(_settleNumeric, 0, 5000, 10);
    ConfigureNumeric(_ipaDelayNumeric, 0, 5000, 10);
    _testProfileComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    _testProfileComboBox.Width = 280;
    foreach (AudioWakeTestProfile testProfile in testProfiles)
    {
      if (testProfile.Profile.IsSpoken)
      {
        _testProfileComboBox.Items.Add(testProfile.Normalize());
      }
    }
    if (_testProfileComboBox.Items.Count != 0)
    {
      _testProfileComboBox.SelectedIndex = 0;
    }

    _volumeSlider.AutoSize = false;
    _volumeSlider.Minimum = 0;
    _volumeSlider.Maximum = 100;
    _volumeSlider.TickFrequency = 10;
    _volumeSlider.SmallChange = 1;
    _volumeSlider.LargeChange = 10;
    _volumeSlider.Height = 32;
    _volumeSlider.Width = 280;
    _volumeValueLabel.AutoSize = true;
    _volumeValueLabel.TextAlign = ContentAlignment.MiddleRight;

    AudioWakeSettings normalized = settings.Normalize();
    _enabledCheckBox.Checked = normalized.Enabled;
    _quietNumeric.Value = normalized.QuietDurationMilliseconds;
    _frequencyNumeric.Value = normalized.FrequencyHertz;
    _volumeSlider.Value = normalized.ToneVolume;
    _playNumeric.Value = normalized.PlayDurationMilliseconds;
    _settleNumeric.Value = normalized.SettleDurationMilliseconds;
    _ipaDelayNumeric.Value = normalized.IpaExampleDelayMilliseconds;
    UpdateVolumeLabel();

    _testButton.AutoSize = true;
    _testButton.Text = "Test wake tone";
    _testPhraseButton.AutoSize = true;
    _testPhraseButton.Text = "Test wake + phrase";
    _okButton.AutoSize = true;
    _okButton.DialogResult = DialogResult.OK;
    _okButton.Text = "OK";
    _cancelButton.AutoSize = true;
    _cancelButton.DialogResult = DialogResult.Cancel;
    _cancelButton.Text = "Cancel";
    AcceptButton = _okButton;
    CancelButton = _cancelButton;

    _enabledCheckBox.CheckedChanged += (_, _) => UpdateEnabledState();
    _volumeSlider.ValueChanged += (_, _) => UpdateVolumeLabel();
    _testButton.Click += (_, _) => TestWakeTone();
    _testPhraseButton.Click += (_, _) => TestWakePhrase();
    _testProfileComboBox.SelectedIndexChanged += (_, _) =>
      UpdateEnabledState();
    _speech.SpeakingStateChanged += SpeechStateChanged;

    var volumePanel = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    volumePanel.Controls.Add(_volumeSlider);
    volumePanel.Controls.Add(_volumeValueLabel);

    var settingsTable = new TableLayoutPanel
    {
      AutoSize = true,
      ColumnCount = 2,
      Dock = DockStyle.Fill
    };
    settingsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    settingsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    AddRow(settingsTable, "Quiet duration (ms):", _quietNumeric, 0);
    AddRow(settingsTable, "Tone frequency (Hz):", _frequencyNumeric, 1);
    AddRow(settingsTable, "Tone volume:", volumePanel, 2);
    AddRow(settingsTable, "Tone play duration (ms):", _playNumeric, 3);
    AddRow(settingsTable, "Settle duration (ms):", _settleNumeric, 4);
    AddRow(
      settingsTable,
      "Test phrase profile:",
      _testProfileComboBox,
      5);
    AddRow(settingsTable, "IPA sound/word delay (ms):", _ipaDelayNumeric, 6);

    var buttons = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.RightToLeft,
      WrapContents = false
    };
    buttons.Controls.Add(_cancelButton);
    buttons.Controls.Add(_okButton);
    buttons.Controls.Add(_testPhraseButton);
    buttons.Controls.Add(_testButton);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 1,
      Dock = DockStyle.Fill,
      Padding = new Padding(12),
      RowCount = 5
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.Controls.Add(explanation, 0, 0);
    layout.Controls.Add(_enabledCheckBox, 0, 1);
    layout.Controls.Add(settingsTable, 0, 2);
    layout.Controls.Add(new Panel(), 0, 3);
    layout.Controls.Add(buttons, 0, 4);
    Controls.Add(layout);

    UpdateEnabledState();
    ThemeManager.Apply(this, theme);
  }

  /// <summary>
  /// Gets the normalized values currently displayed by the dialog.
  /// </summary>
  public AudioWakeSettings CurrentSettings => new(
    _enabledCheckBox.Checked,
    Decimal.ToInt32(_quietNumeric.Value),
    Decimal.ToInt32(_frequencyNumeric.Value),
    _volumeSlider.Value,
    Decimal.ToInt32(_playNumeric.Value),
    Decimal.ToInt32(_settleNumeric.Value),
    Decimal.ToInt32(_ipaDelayNumeric.Value));

  /// <summary>
  /// Releases event subscriptions.
  /// </summary>
  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _speech.SpeakingStateChanged -= SpeechStateChanged;
    }
    base.Dispose(disposing);
  }

  /// <summary>
  /// Enables settings that only matter when the prefix is active.
  /// </summary>
  private void UpdateEnabledState()
  {
    bool enabled = _enabledCheckBox.Checked;
    _quietNumeric.Enabled = enabled;
    _frequencyNumeric.Enabled = enabled;
    _volumeSlider.Enabled = enabled;
    _volumeValueLabel.Enabled = enabled;
    _playNumeric.Enabled = enabled;
    _settleNumeric.Enabled = enabled;
    _testButton.Enabled = enabled && !_speech.IsSpeaking;
    _testProfileComboBox.Enabled = enabled &&
      !_speech.IsSpeaking &&
      _testProfileComboBox.Items.Count != 0;
    _testPhraseButton.Enabled = enabled &&
      !_speech.IsSpeaking &&
      SelectedTestProfile is not null;
  }

  /// <summary>
  /// Updates the displayed tone-volume percentage.
  /// </summary>
  private void UpdateVolumeLabel()
  {
    _volumeValueLabel.Text = $"{_volumeSlider.Value}%";
  }

  /// <summary>
  /// Starts a wake-tone-only test without disrupting speech.
  /// </summary>
  private void TestWakeTone()
  {
    if (_speech.IsSpeaking)
    {
      return;
    }
    _speech.TestWakeTone(CurrentSettings with { Enabled = true });
    UpdateEnabledState();
  }

  /// <summary>
  /// Tests the complete tone, settling silence, and speech handoff.
  /// </summary>
  private void TestWakePhrase()
  {
    AudioWakeTestProfile? testProfile = SelectedTestProfile;
    if (_speech.IsSpeaking || testProfile is null)
    {
      return;
    }
    _speech.TestWakePhrase(
      "Yes, that makes sense.",
      testProfile.Profile,
      CurrentSettings with { Enabled = true });
    UpdateEnabledState();
  }

  private AudioWakeTestProfile? SelectedTestProfile =>
    _testProfileComboBox.SelectedItem as AudioWakeTestProfile;

  /// <summary>
  /// Refreshes the test button when speech starts or stops.
  /// </summary>
  private void SpeechStateChanged(bool speaking)
  {
    if (IsDisposed || Disposing)
    {
      return;
    }
    try
    {
      BeginInvoke(new Action(UpdateEnabledState));
    }
    catch (InvalidOperationException) when (IsDisposed || Disposing)
    {
    }
  }

  private static void ConfigureNumeric(
    NumericUpDown control,
    decimal minimum,
    decimal maximum,
    decimal increment)
  {
    control.Minimum = minimum;
    control.Maximum = maximum;
    control.Increment = increment;
    control.ThousandsSeparator = true;
    control.Width = 130;
  }

  private static void AddRow(
    TableLayoutPanel table,
    string label,
    Control control,
    int row)
  {
    table.RowCount = Math.Max(table.RowCount, row + 1);
    table.Controls.Add(new Label
    {
      AutoSize = true,
      Margin = new Padding(3, 7, 8, 0),
      Text = label
    }, 0, row);
    table.Controls.Add(control, 1, row);
  }
}

/// <summary>
/// Names one content profile available to the wake-plus-phrase test.
/// </summary>
internal sealed record AudioWakeTestProfile(
  string Name,
  SpeechProfileSettings Profile)
{
  public AudioWakeTestProfile Normalize()
  {
    return this with { Profile = Profile.Normalize() };
  }

  public override string ToString()
  {
    return Name;
  }
}
