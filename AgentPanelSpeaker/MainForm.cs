namespace AgentPanelSpeaker;

/// <summary>
/// Provides JSONL session selection, role-specific speech settings, navigation,
/// persistence, and diagnostics.
/// </summary>
internal sealed class MainForm : Form
{
  private readonly JsonlSessionMonitor _monitor = new();
  private readonly SpeechService _speech = new();
  private readonly ToolTip _toolTip = new();
  private readonly System.Windows.Forms.Timer _fenceDebounceTimer = new();
  private readonly Label _instructionsLabel = new();
  private readonly ComboBox _sourceComboBox = new();
  private readonly TextBox _sessionTitleTextBox = new();
  private readonly TextBox _sessionPathTextBox = new();
  private readonly Button _detectLatestButton = new();
  private readonly Button _browseButton = new();
  private readonly CheckBox _followLatestCheckBox = new();
  private readonly TextBox _previewTextBox = new();
  private readonly NumericUpDown _pollNumeric = new();
  private readonly TextBox _fenceTypesTextBox = new();
  private readonly CheckBox _speakExistingCheckBox = new();
  private readonly Button _startButton = new();
  private readonly Button _stopButton = new();
  private readonly Button _cancelSpeechButton = new();
  private readonly Button _rewindSentenceButton = new();
  private readonly Button _forwardSentenceButton = new();
  private readonly Button _rewindNodeButton = new();
  private readonly Button _forwardNodeButton = new();
  private readonly Button _saveSettingsButton = new();
  private readonly Button _resetSettingsButton = new();
  private readonly Button _openLogButton = new();
  private readonly TextBox _logTextBox = new();
  private readonly Dictionary<ContentCategory, VoiceRowControls> _voiceRows = new();
  private readonly IReadOnlyList<string> _installedVoices;
  private readonly UserSettingsStore _settingsStore;

  private bool _pathIsManual;
  private bool _loadingSettings;
  private bool _closing;
  private int _monitorSession;

  /// <summary>
  /// Initializes controls, settings, event handlers, and policy providers.
  /// </summary>
  public MainForm()
  {
    _installedVoices = _speech.GetInstalledVoiceNames();
    _settingsStore = new UserSettingsStore(_installedVoices);
    _speech.SetPolicyProviders(
      _settingsStore.GetProfile,
      _settingsStore.IsFenceTypeSpoken);
    InitializeControls();
    PopulateSources();
    PopulateVoiceRows();
    LoadSettingsIntoControls(_settingsStore.Current);
    ConnectEvents();
    UpdateControlState();
    AppendLog($"Diagnostic log: {DiagnosticLog.FilePath}");
    AppendLog($"Settings: {UserSettingsStore.FilePath}");
  }

  /// <summary>
  /// Creates and arranges all Windows Forms controls.
  /// </summary>
  private void InitializeControls()
  {
    Text = "Agent Panel Speaker v21";
    AutoScaleMode = AutoScaleMode.Font;
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(900, 720);
    Size = new Size(1120, 900);

    _instructionsLabel.AutoSize = true;
    _instructionsLabel.Dock = DockStyle.Fill;
    _instructionsLabel.Text =
      "Reads Claude/Codex JSONL directly. Tool calls, results, commands, " +
      "diffs, and status records are excluded.";

    _sourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    _sourceComboBox.Width = 110;
    ConfigureReadOnlyTextBox(_sessionTitleTextBox);
    ConfigureReadOnlyTextBox(_sessionPathTextBox);
    ConfigureButton(_detectLatestButton, "Detect latest");
    ConfigureButton(_browseButton, "Browse JSONL");
    ConfigureTransportButton(_rewindNodeButton, "⏮", "Previous JSONL node (Alt+G)");
    ConfigureTransportButton(_rewindSentenceButton, "⏪", "Previous sentence/code line (Alt+H)");
    ConfigureTransportButton(_startButton, "▶", "Start monitoring (Alt+J)");
    ConfigureTransportButton(_stopButton, "⏹", "Stop monitoring and speech (Alt+K)");
    ConfigureTransportButton(_forwardSentenceButton, "⏩", "Next sentence/code line (Alt+L)");
    ConfigureTransportButton(_forwardNodeButton, "⏭", "Next JSONL node (Alt+;)");
    ConfigureButton(_cancelSpeechButton, "Silence");
    _toolTip.SetToolTip(_cancelSpeechButton, "Silence speech; continue monitoring (Alt+')");
    ConfigureButton(_saveSettingsButton, "Save settings");
    ConfigureButton(_resetSettingsButton, "Reset defaults");
    ConfigureButton(_openLogButton, "Open diagnostic log");

    _followLatestCheckBox.AutoSize = true;
    _followLatestCheckBox.Text = "Follow newest session";
    _previewTextBox.Multiline = true;
    _previewTextBox.ReadOnly = true;
    _previewTextBox.ScrollBars = ScrollBars.Vertical;
    _previewTextBox.Dock = DockStyle.Fill;
    _previewTextBox.Font = new Font(FontFamily.GenericMonospace, 9.0f);
    _logTextBox.Multiline = true;
    _logTextBox.ReadOnly = true;
    _logTextBox.ScrollBars = ScrollBars.Vertical;
    _logTextBox.Dock = DockStyle.Fill;
    _logTextBox.Font = new Font(FontFamily.GenericMonospace, 9.0f);
    ConfigureNumeric(_pollNumeric, 50, 2000, 150, 80);
    _fenceTypesTextBox.Width = 430;
    _speakExistingCheckBox.AutoSize = true;
    _speakExistingCheckBox.Text = "Speak last existing enabled message on start";
    _fenceDebounceTimer.Interval = 1000;

    var sessionControls = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    sessionControls.Controls.AddRange(new Control[]
    {
      MakeInlineLabel("Source:"), _sourceComboBox, _detectLatestButton,
      _browseButton, _followLatestCheckBox
    });

    var speechTable = new TableLayoutPanel
    {
      AutoSize = true,
      ColumnCount = 6,
      RowCount = 4,
      Dock = DockStyle.Fill,
      CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
    };
    speechTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    speechTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    for (int index = 0; index < 4; ++index)
    {
      speechTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    }
    AddSpeechHeader(speechTable);
    AddVoiceRow(speechTable, 1, ContentCategory.Assistant, "Assistant messages");
    AddVoiceRow(speechTable, 2, ContentCategory.Reasoning, "Reasoning/thinking");
    AddVoiceRow(speechTable, 3, ContentCategory.User, "User messages");

    var options = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = true
    };
    options.Controls.AddRange(new Control[]
    {
      MakeInlineLabel("Spoken fenced-code types:"), _fenceTypesTextBox,
      MakeInlineLabel("Poll ms:"), _pollNumeric, _speakExistingCheckBox
    });

    var transport = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    transport.Controls.AddRange(new Control[]
    {
      _rewindNodeButton, _rewindSentenceButton, _startButton, _stopButton,
      _forwardSentenceButton, _forwardNodeButton, _cancelSpeechButton
    });
    var utility = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    utility.Controls.AddRange(new Control[]
    {
      _saveSettingsButton, _resetSettingsButton, _openLogButton
    });

    var controls = new TableLayoutPanel
    {
      AutoSize = true,
      ColumnCount = 2,
      Dock = DockStyle.Fill
    };
    controls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    controls.Controls.Add(transport, 0, 0);
    controls.Controls.Add(utility, 1, 0);

    var layout = new TableLayoutPanel
    {
      ColumnCount = 1,
      RowCount = 12,
      Dock = DockStyle.Fill,
      Padding = new Padding(10)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58.0f));
    layout.Controls.Add(_instructionsLabel, 0, 0);
    layout.Controls.Add(sessionControls, 0, 1);
    layout.Controls.Add(CreateSessionDetailsLayout(), 0, 2);
    layout.Controls.Add(MakeSectionLabel("Recent accepted conversation text:"), 0, 3);
    layout.Controls.Add(_previewTextBox, 0, 4);
    layout.Controls.Add(MakeSectionLabel("Speech by content:"), 0, 5);
    layout.Controls.Add(speechTable, 0, 6);
    layout.Controls.Add(options, 0, 7);
    layout.Controls.Add(controls, 0, 8);
    layout.Controls.Add(MakeSectionLabel("Activity:"), 0, 10);
    layout.Controls.Add(_logTextBox, 0, 11);
    Controls.Add(layout);
  }

  /// <summary>
  /// Connects all UI and worker events.
  /// </summary>
  private void ConnectEvents()
  {
    _sourceComboBox.SelectedIndexChanged += SourceSelectionChanged;
    _followLatestCheckBox.CheckedChanged += (_, _) => SaveControlsToSettings();
    _pollNumeric.ValueChanged += (_, _) => SaveControlsToSettings();
    _speakExistingCheckBox.CheckedChanged += (_, _) => SaveControlsToSettings();
    _fenceTypesTextBox.TextChanged += FenceTypesTextChanged;
    _fenceDebounceTimer.Tick += FenceDebounceTimerTick;
    _detectLatestButton.Click += async (_, _) => await DetectLatestAsync();
    _browseButton.Click += BrowseButtonClicked;
    _startButton.Click += StartButtonClicked;
    _stopButton.Click += StopButtonClicked;
    _cancelSpeechButton.Click += CancelSpeechButtonClicked;
    _rewindSentenceButton.Click += (_, _) => NavigateSpeech(
      _speech.TryRewindSentence,
      "Previous sentence/code line");
    _forwardSentenceButton.Click += (_, _) => NavigateSpeech(
      _speech.TryForwardSentence,
      "Next sentence/code line");
    _rewindNodeButton.Click += (_, _) => NavigateSpeech(
      _speech.TryRewindNode,
      "Previous JSONL node");
    _forwardNodeButton.Click += (_, _) => NavigateSpeech(
      _speech.TryForwardNode,
      "Next JSONL node");
    _saveSettingsButton.Click += (_, _) => SaveSettingsExplicitly();
    _resetSettingsButton.Click += (_, _) => ResetSettings();
    _openLogButton.Click += OpenLogButtonClicked;
    ResizeEnd += (_, _) => SaveControlsToSettings(includeWindowPlacement: true);
    Shown += MainFormShown;
    FormClosing += MainFormClosing;
    _monitor.TextReady += MonitorTextReady;
    _monitor.HistoryLoaded += MonitorHistoryLoaded;
    _monitor.SessionChanged += session => PostToUi(() => SetSessionDisplay(session));
    _monitor.MessagesChanged += MonitorMessagesChanged;
    _monitor.StatusChanged += status => PostToUi(() => AppendLog(status));
    _monitor.Faulted += exception => PostToUi(() =>
      AppendLog($"Monitoring failed: {exception.Message}"));
    _speech.Activity += message => PostToUi(() => AppendLog(message));
    _speech.SpeakingStateChanged += _ => PostToUi(UpdateAllVoiceRowStates);
  }

  /// <summary>
  /// Adds table column headers.
  /// </summary>
  private static void AddSpeechHeader(TableLayoutPanel table)
  {
    string[] headings =
    {
      "Content", "Voice", "Rate", "Pitch", "Volume", "Test"
    };
    for (int column = 0; column < headings.Length; ++column)
    {
      table.Controls.Add(MakeSectionLabel(headings[column]), column, 0);
    }
  }

  /// <summary>
  /// Adds one role-specific voice row.
  /// </summary>
  private void AddVoiceRow(
    TableLayoutPanel table,
    int row,
    ContentCategory category,
    string label)
  {
    var voice = new ComboBox
    {
      DropDownStyle = ComboBoxStyle.DropDownList,
      Dock = DockStyle.Fill
    };
    var rate = CreateNumeric(-10, 10, 0, 60);
    var pitch = CreateNumeric(-10, 10, 0, 60);
    VolumeSliderControls volume = CreateVolumeSlider();
    var test = new Button { AutoSize = true, Text = "🔊" };
    var controls = new VoiceRowControls(
      voice,
      rate,
      pitch,
      volume.Slider,
      volume.ValueLabel,
      test);
    _voiceRows.Add(category, controls);
    table.Controls.Add(MakeInlineLabel(label), 0, row);
    table.Controls.Add(voice, 1, row);
    table.Controls.Add(rate, 2, row);
    table.Controls.Add(pitch, 3, row);
    table.Controls.Add(volume.Container, 4, row);
    table.Controls.Add(test, 5, row);
    voice.SelectedIndexChanged += (_, _) => VoiceRowChanged(category);
    rate.ValueChanged += (_, _) => VoiceRowChanged(category);
    pitch.ValueChanged += (_, _) => VoiceRowChanged(category);
    volume.Slider.ValueChanged += (_, _) =>
      VolumeSliderChanged(category);
    test.Click += (_, _) => TestVoice(category);
    volume.Slider.AccessibleName = $"{label} volume";
    _toolTip.SetToolTip(volume.Slider, $"{label} volume");
    _toolTip.SetToolTip(test, $"Test {label.ToLowerInvariant()} voice");
  }

  /// <summary>
  /// Populates source choices.
  /// </summary>
  private void PopulateSources()
  {
    _sourceComboBox.Items.AddRange(new object[]
    {
      AgentSource.Auto, AgentSource.Codex, AgentSource.Claude
    });
  }

  /// <summary>
  /// Populates every role voice list with Not Spoken and installed voices.
  /// </summary>
  private void PopulateVoiceRows()
  {
    foreach (VoiceRowControls row in _voiceRows.Values)
    {
      row.Voice.Items.Add(SpeechProfileSettings.NotSpoken);
      foreach (string voice in _installedVoices)
      {
        row.Voice.Items.Add(voice);
      }
    }
  }

  /// <summary>
  /// Loads one settings snapshot into the controls.
  /// </summary>
  private void LoadSettingsIntoControls(UserSettings settings)
  {
    _loadingSettings = true;
    try
    {
      _sourceComboBox.SelectedItem = settings.Source;
      _followLatestCheckBox.Checked = settings.FollowNewestSession;
      _pathIsManual = !string.IsNullOrWhiteSpace(settings.ManualSessionPath);
      _sessionPathTextBox.Text = settings.ManualSessionPath ?? string.Empty;
      _pollNumeric.Value = settings.PollIntervalMilliseconds;
      _speakExistingCheckBox.Checked = settings.SpeakLastExistingEnabledMessage;
      _fenceTypesTextBox.Text = settings.SpokenFencedCodeTypes;
      LoadVoiceRow(ContentCategory.Assistant, settings.Assistant);
      LoadVoiceRow(ContentCategory.Reasoning, settings.Reasoning);
      LoadVoiceRow(ContentCategory.User, settings.User);
      if (settings.HasWindowPlacement)
      {
        StartPosition = FormStartPosition.Manual;
        Bounds = EnsureVisible(new Rectangle(
          settings.WindowX,
          settings.WindowY,
          settings.WindowWidth,
          settings.WindowHeight));
      }
    }
    finally
    {
      _loadingSettings = false;
    }
  }

  /// <summary>
  /// Loads one profile row.
  /// </summary>
  private void LoadVoiceRow(
    ContentCategory category,
    SpeechProfileSettings profile)
  {
    VoiceRowControls row = _voiceRows[category];
    row.Voice.SelectedItem = row.Voice.Items.Contains(profile.VoiceName)
      ? profile.VoiceName
      : SpeechProfileSettings.NotSpoken;
    row.Rate.Value = profile.Rate;
    row.Pitch.Value = profile.Pitch;
    row.Volume.Value = profile.Volume;
    UpdateVolumeLabel(row);
    UpdateVoiceRowState(category);
  }


  /// <summary>
  /// Clears stale fixed-session state after a deliberate source change.
  /// </summary>
  private void SourceSelectionChanged(object? sender, EventArgs eventArgs)
  {
    if (_loadingSettings || _monitor.IsRunning)
    {
      return;
    }
    _pathIsManual = false;
    _sessionTitleTextBox.Clear();
    _sessionPathTextBox.Clear();
    _loadingSettings = true;
    _followLatestCheckBox.Checked = true;
    _loadingSettings = false;
    SaveControlsToSettings();
    UpdateControlState();
  }

  /// <summary>
  /// Applies one-second debouncing to fenced-type edits.
  /// </summary>
  private void FenceTypesTextChanged(object? sender, EventArgs eventArgs)
  {
    if (_loadingSettings)
    {
      return;
    }
    _fenceDebounceTimer.Stop();
    _fenceDebounceTimer.Start();
  }

  /// <summary>
  /// Applies and saves fenced types after one quiet second.
  /// </summary>
  private void FenceDebounceTimerTick(object? sender, EventArgs eventArgs)
  {
    _fenceDebounceTimer.Stop();
    ApplyFenceTypesImmediately();
  }

  /// <summary>
  /// Normalizes, displays, logs, and saves the fenced-type CSV.
  /// </summary>
  private void ApplyFenceTypesImmediately()
  {
    FencedCodeTypeSet parsed = FencedCodeTypeSet.Parse(_fenceTypesTextBox.Text);
    _loadingSettings = true;
    _fenceTypesTextBox.Text = parsed.NormalizedCsv;
    _loadingSettings = false;
    SaveControlsToSettings();
    AppendLog(
      "Spoken fenced-code types updated: " +
      (parsed.OrderedTypes.Count == 0 ? "none" : parsed.NormalizedCsv));
  }

  /// <summary>
  /// Handles a role-profile change and persists it immediately.
  /// </summary>
  private void VoiceRowChanged(ContentCategory category)
  {
    if (_loadingSettings)
    {
      return;
    }
    UpdateVoiceRowState(category);
    SaveControlsToSettings();
  }

  /// <summary>
  /// Updates one volume label and persists the current profile.
  /// </summary>
  private void VolumeSliderChanged(ContentCategory category)
  {
    VoiceRowControls row = _voiceRows[category];
    UpdateVolumeLabel(row);
    VoiceRowChanged(category);
  }

  /// <summary>
  /// Displays one volume slider's current percentage.
  /// </summary>
  private static void UpdateVolumeLabel(VoiceRowControls row)
  {
    row.VolumeValue.Text = $"{row.Volume.Value}%";
  }

  /// <summary>
  /// Enables profile controls when spoken and test buttons when idle.
  /// </summary>
  private void UpdateVoiceRowState(ContentCategory category)
  {
    VoiceRowControls row = _voiceRows[category];
    bool enabled = !string.Equals(
      row.Voice.SelectedItem?.ToString(),
      SpeechProfileSettings.NotSpoken,
      StringComparison.Ordinal);
    row.Rate.Enabled = enabled;
    row.Pitch.Enabled = enabled;
    row.Volume.Enabled = enabled;
    row.VolumeValue.Enabled = enabled;
    row.Test.Enabled = enabled && !_speech.IsSpeaking;
  }

  /// <summary>
  /// Refreshes all test buttons after speech starts or stops.
  /// </summary>
  private void UpdateAllVoiceRowStates()
  {
    foreach (ContentCategory category in _voiceRows.Keys)
    {
      UpdateVoiceRowState(category);
    }
  }

  /// <summary>
  /// Tests one role profile without changing transcript history.
  /// </summary>
  private void TestVoice(ContentCategory category)
  {
    if (_speech.IsSpeaking)
    {
      return;
    }

    try
    {
      _speech.SpeakUntracked(
        $"{category} speech is working.",
        ReadVoiceProfile(category));
    }
    catch (Exception exception) when (
      exception is ArgumentException or InvalidOperationException)
    {
      AppendLog($"Voice test failed: {exception.Message}");
    }
  }

  /// <summary>
  /// Starts monitoring the selected or latest session.
  /// </summary>
  private async void StartButtonClicked(object? sender, EventArgs eventArgs)
  {
    try
    {
      ApplyFenceTypesImmediately();
      if (string.IsNullOrWhiteSpace(_sessionPathTextBox.Text) &&
          !await DetectLatestAsync())
      {
        return;
      }
      _speech.BeginLiveSession();
      Interlocked.Increment(ref _monitorSession);
      string? explicitPath = _pathIsManual || !_followLatestCheckBox.Checked
        ? _sessionPathTextBox.Text
        : null;
      _monitor.Start(new MonitorSettings(
        GetSelectedSource(),
        explicitPath,
        _followLatestCheckBox.Checked,
        _speakExistingCheckBox.Checked,
        TimeSpan.FromMilliseconds((double)_pollNumeric.Value)));
      AppendLog("Monitoring started; existing history is being indexed.");
      UpdateControlState();
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException or
      InvalidDataException or InvalidOperationException or ArgumentException)
    {
      AppendLog($"Unable to start: {exception.Message}");
    }
  }

  /// <summary>
  /// Stops monitoring and speech immediately.
  /// </summary>
  private void StopButtonClicked(object? sender, EventArgs eventArgs)
  {
    Interlocked.Increment(ref _monitorSession);
    _speech.CancelAll();
    _monitor.Stop();
    AppendLog("Monitoring and speech stopped.");
    UpdateControlState();
  }

  /// <summary>
  /// Cancels speech while monitoring continues.
  /// </summary>
  private void CancelSpeechButtonClicked(object? sender, EventArgs eventArgs)
  {
    _speech.CancelAll();
    AppendLog("Speech silenced; monitoring continues.");
    UpdateControlState();
  }

  /// <summary>
  /// Runs one navigation operation.
  /// </summary>
  private void NavigateSpeech(TryNavigateSpeech navigate, string action)
  {
    AppendLog(navigate(out string text)
      ? $"{action}: {text}"
      : $"{action}: no matching enabled history entry is available.");
    UpdateControlState();
  }

  /// <summary>
  /// Lets the user select a fixed session file.
  /// </summary>
  private void BrowseButtonClicked(object? sender, EventArgs eventArgs)
  {
    AgentSource source = GetSelectedSource();
    using var dialog = new OpenFileDialog
    {
      CheckFileExists = true,
      Filter = "JSON Lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
      InitialDirectory = SessionLocator.GetBrowseInitialDirectory(
        source,
        _sessionPathTextBox.Text),
      Multiselect = false,
      Title = source == AgentSource.Auto
        ? "Select Claude or Codex session JSONL"
        : $"Select {source} session JSONL"
    };
    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }
    try
    {
      LocatedSession session = SessionLocator.FromPath(dialog.FileName, source);
      if (source == AgentSource.Auto)
      {
        _sourceComboBox.SelectedItem = session.Source;
      }
      SetSessionDisplay(session);
      _pathIsManual = true;
      _followLatestCheckBox.Checked = false;
      SaveControlsToSettings();
      AppendLog($"Selected {session.Source}: {session.DisplayName}");
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException or InvalidDataException)
    {
      AppendLog($"Unable to use selected file: {exception.Message}");
    }
  }

  /// <summary>
  /// Detects and displays the newest session asynchronously.
  /// </summary>
  private async Task<bool> DetectLatestAsync()
  {
    if (_monitor.IsRunning)
    {
      return false;
    }
    _detectLatestButton.Enabled = false;
    try
    {
      LocatedSession session = await Task.Run(() =>
        SessionLocator.FindLatest(GetSelectedSource()));
      SetSessionDisplay(session);
      _pathIsManual = false;
      SaveControlsToSettings();
      AppendLog($"Detected {session.Source}: {session.DisplayName}");
      return true;
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      AppendLog($"Session detection failed: {exception.Message}");
      return false;
    }
    finally
    {
      UpdateControlState();
    }
  }

  /// <summary>
  /// Loads indexed history into the player.
  /// </summary>
  private void MonitorHistoryLoaded(SpeechHistorySnapshot snapshot)
  {
    PostToUi(() =>
    {
      _speech.LoadHistory(snapshot.Fragments, snapshot.StartMode);
      AppendLog($"Indexed {snapshot.Fragments.Count} existing fragments.");
      UpdateControlState();
    });
  }

  /// <summary>
  /// Appends one live fragment to serialized playback.
  /// </summary>
  private void MonitorTextReady(SpeechFragment fragment)
  {
    int session = Volatile.Read(ref _monitorSession);
    PostToUi(() =>
    {
      if (!_monitor.IsRunning || session != Volatile.Read(ref _monitorSession))
      {
        return;
      }
      _speech.SpeakLive(fragment);
      AppendLog($"Queued {fragment.Category}: {fragment.Text}");
      UpdateControlState();
    });
  }

  /// <summary>
  /// Updates the accepted-node preview.
  /// </summary>
  private void MonitorMessagesChanged(IReadOnlyList<string> messages)
  {
    string preview = string.Join(
      Environment.NewLine + Environment.NewLine,
      messages.Select((message, index) => $"[{index + 1}] {message}"));
    PostToUi(() => _previewTextBox.Text = preview);
  }

  /// <summary>
  /// Saves controls to the immutable settings snapshot.
  /// </summary>
  private void SaveControlsToSettings(bool includeWindowPlacement = false)
  {
    if (_loadingSettings || _closing)
    {
      return;
    }
    UserSettings current = _settingsStore.Current;
    Rectangle bounds = WindowState == FormWindowState.Normal
      ? Bounds
      : RestoreBounds;
    var settings = current with
    {
      Source = GetSelectedSource(),
      FollowNewestSession = _followLatestCheckBox.Checked,
      ManualSessionPath = _pathIsManual ? _sessionPathTextBox.Text : null,
      Assistant = ReadVoiceProfile(ContentCategory.Assistant),
      Reasoning = ReadVoiceProfile(ContentCategory.Reasoning),
      User = ReadVoiceProfile(ContentCategory.User),
      SpokenFencedCodeTypes = FencedCodeTypeSet
        .Parse(_fenceTypesTextBox.Text)
        .NormalizedCsv,
      SpeakLastExistingEnabledMessage = _speakExistingCheckBox.Checked,
      PollIntervalMilliseconds = Decimal.ToInt32(_pollNumeric.Value),
      WindowX = includeWindowPlacement ? bounds.X : current.WindowX,
      WindowY = includeWindowPlacement ? bounds.Y : current.WindowY,
      WindowWidth = includeWindowPlacement ? bounds.Width : current.WindowWidth,
      WindowHeight = includeWindowPlacement ? bounds.Height : current.WindowHeight,
      HasWindowPlacement = includeWindowPlacement || current.HasWindowPlacement
    };
    try
    {
      _settingsStore.Update(settings);
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException)
    {
      AppendLog($"Settings save failed: {exception.Message}");
    }
  }

  /// <summary>
  /// Explicitly applies pending edits and saves all settings.
  /// </summary>
  private void SaveSettingsExplicitly()
  {
    _fenceDebounceTimer.Stop();
    ApplyFenceTypesImmediately();
    SaveControlsToSettings(includeWindowPlacement: true);
    AppendLog("Settings saved.");
  }

  /// <summary>
  /// Restores default settings after confirmation.
  /// </summary>
  private void ResetSettings()
  {
    if (MessageBox.Show(
          this,
          "Reset all Agent Panel Speaker settings?",
          Text,
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question) != DialogResult.Yes)
    {
      return;
    }
    LoadSettingsIntoControls(_settingsStore.ResetDefaults());
    AppendLog("Settings reset to defaults.");
  }

  /// <summary>
  /// Reads one profile from its table row.
  /// </summary>
  private SpeechProfileSettings ReadVoiceProfile(ContentCategory category)
  {
    VoiceRowControls row = _voiceRows[category];
    return new SpeechProfileSettings(
      row.Voice.SelectedItem?.ToString() ?? SpeechProfileSettings.NotSpoken,
      Decimal.ToInt32(row.Rate.Value),
      Decimal.ToInt32(row.Pitch.Value))
    {
      Volume = row.Volume.Value
    };
  }

  /// <summary>
  /// Handles application-local transport shortcuts.
  /// </summary>
  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (keyData == (Keys.Alt | Keys.G))
    {
      _rewindNodeButton.PerformClick();
      return true;
    }
    if (keyData == (Keys.Alt | Keys.H))
    {
      _rewindSentenceButton.PerformClick();
      return true;
    }
    if (keyData == (Keys.Alt | Keys.J))
    {
      _startButton.PerformClick();
      return true;
    }
    if (keyData == (Keys.Alt | Keys.K))
    {
      _stopButton.PerformClick();
      return true;
    }
    if (keyData == (Keys.Alt | Keys.L))
    {
      _forwardSentenceButton.PerformClick();
      return true;
    }
    if (keyData == (Keys.Alt | Keys.OemSemicolon))
    {
      _forwardNodeButton.PerformClick();
      return true;
    }
    if (keyData == (Keys.Alt | Keys.OemQuotes))
    {
      _cancelSpeechButton.PerformClick();
      return true;
    }
    return base.ProcessCmdKey(ref message, keyData);
  }

  /// <summary>
  /// Updates controls that depend on monitoring/history state.
  /// </summary>
  private void UpdateControlState()
  {
    bool running = _monitor.IsRunning;
    bool hasHistory = _speech.HasHistory;
    _sourceComboBox.Enabled = !running;
    _detectLatestButton.Enabled = !running;
    _browseButton.Enabled = !running;
    _followLatestCheckBox.Enabled = !running && !_pathIsManual;
    _pollNumeric.Enabled = !running;
    _speakExistingCheckBox.Enabled = !running;
    _startButton.Enabled = !running;
    _stopButton.Enabled = running;
    _cancelSpeechButton.Enabled = running || hasHistory;
    _rewindSentenceButton.Enabled = hasHistory;
    _forwardSentenceButton.Enabled = hasHistory;
    _rewindNodeButton.Enabled = hasHistory;
    _forwardNodeButton.Enabled = hasHistory;
  }

  /// <summary>
  /// Opens the diagnostic log in Explorer.
  /// </summary>
  private void OpenLogButtonClicked(object? sender, EventArgs eventArgs)
  {
    try
    {
      DiagnosticLog.OpenCurrentLogInExplorer();
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or
      System.ComponentModel.Win32Exception)
    {
      AppendLog($"Unable to open diagnostic log: {exception.Message}");
    }
  }

  /// <summary>
  /// Displays one resolved session.
  /// </summary>
  private void SetSessionDisplay(LocatedSession session)
  {
    _sessionTitleTextBox.Text = session.DisplayName;
    _sessionPathTextBox.Text = session.Path;
    _sessionTitleTextBox.SelectionStart = 0;
    _sessionPathTextBox.SelectionStart = 0;
  }

  /// <summary>
  /// Gets the selected source.
  /// </summary>
  private AgentSource GetSelectedSource()
  {
    return _sourceComboBox.SelectedItem is AgentSource source
      ? source
      : AgentSource.Auto;
  }

  /// <summary>
  /// Appends a timestamped activity line.
  /// </summary>
  private void AppendLog(string text)
  {
    if (_closing || IsDisposed)
    {
      return;
    }
    if (_logTextBox.TextLength != 0)
    {
      _logTextBox.AppendText(Environment.NewLine);
    }
    _logTextBox.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {text}");
    _logTextBox.SelectionStart = _logTextBox.TextLength;
    _logTextBox.ScrollToCaret();
  }

  /// <summary>
  /// Marshals an action to the UI thread.
  /// </summary>
  private void PostToUi(Action action)
  {
    if (_closing || IsDisposed)
    {
      return;
    }
    try
    {
      if (InvokeRequired)
      {
        BeginInvoke(action);
      }
      else
      {
        action();
      }
    }
    catch (InvalidOperationException) when (_closing || IsDisposed)
    {
    }
  }

  /// <summary>
  /// Logs initial layout and screens.
  /// </summary>
  private void MainFormShown(object? sender, EventArgs eventArgs)
  {
    if (_pathIsManual && File.Exists(_sessionPathTextBox.Text))
    {
      try
      {
        SetSessionDisplay(SessionLocator.FromPath(
          _sessionPathTextBox.Text,
          GetSelectedSource()));
      }
      catch (Exception exception) when (
        exception is IOException or
        UnauthorizedAccessException or
        InvalidDataException)
      {
        AppendLog($"Saved session could not be opened: {exception.Message}");
      }
    }
    DiagnosticLog.Write("ui.shown", new
    {
      highDpiMode = Application.HighDpiMode.ToString(),
      bounds = Bounds.ToString(),
      screens = Screen.AllScreens.Select(screen => screen.Bounds.ToString()).ToArray()
    });
  }

  /// <summary>
  /// Flushes pending settings and releases resources.
  /// </summary>
  private void MainFormClosing(object? sender, FormClosingEventArgs eventArgs)
  {
    _fenceDebounceTimer.Stop();
    ApplyFenceTypesImmediately();
    SaveControlsToSettings(includeWindowPlacement: true);
    _closing = true;
    Interlocked.Increment(ref _monitorSession);
    _monitor.Dispose();
    _speech.Dispose();
    _fenceDebounceTimer.Dispose();
    _toolTip.Dispose();
  }

  /// <summary>
  /// Creates full-width title and path rows.
  /// </summary>
  private TableLayoutPanel CreateSessionDetailsLayout()
  {
    var details = new TableLayoutPanel
    {
      AutoSize = true,
      ColumnCount = 2,
      RowCount = 2,
      Dock = DockStyle.Fill
    };
    details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    details.Controls.Add(MakeInlineLabel("Session:"), 0, 0);
    details.Controls.Add(_sessionTitleTextBox, 1, 0);
    details.Controls.Add(MakeInlineLabel("Path:"), 0, 1);
    details.Controls.Add(_sessionPathTextBox, 1, 1);
    return details;
  }

  /// <summary>
  /// Configures a transport button.
  /// </summary>
  private void ConfigureTransportButton(
    Button button,
    string symbol,
    string accessibleName)
  {
    button.AutoSize = false;
    button.Size = new Size(50, 38);
    button.Font = new Font("Segoe UI Symbol", 14.0f);
    button.Text = symbol;
    button.AccessibleName = accessibleName;
    _toolTip.SetToolTip(button, accessibleName);
  }

  /// <summary>
  /// Configures a standard button.
  /// </summary>
  private static void ConfigureButton(Button button, string text)
  {
    button.AutoSize = true;
    button.Text = text;
  }

  /// <summary>
  /// Configures a read-only full-width text box.
  /// </summary>
  private static void ConfigureReadOnlyTextBox(TextBox textBox)
  {
    textBox.ReadOnly = true;
    textBox.Dock = DockStyle.Fill;
  }

  /// <summary>
  /// Configures one numeric control.
  /// </summary>
  private static void ConfigureNumeric(
    NumericUpDown control,
    decimal minimum,
    decimal maximum,
    decimal value,
    int width)
  {
    control.Minimum = minimum;
    control.Maximum = maximum;
    control.Value = value;
    control.Width = width;
  }

  /// <summary>
  /// Creates a zero-to-100 volume slider with a percentage label.
  /// </summary>
  private static VolumeSliderControls CreateVolumeSlider()
  {
    var slider = new TrackBar
    {
      AutoSize = false,
      Anchor = AnchorStyles.Left,
      Height = 32,
      LargeChange = 10,
      Maximum = 100,
      Minimum = 0,
      SmallChange = 1,
      TickFrequency = 10,
      Value = 100,
      Width = 140
    };
    var valueLabel = new Label
    {
      AutoSize = true,
      Anchor = AnchorStyles.Right,
      Margin = new Padding(3, 7, 3, 0),
      Text = "100%",
      TextAlign = ContentAlignment.MiddleRight
    };
    var container = new TableLayoutPanel
    {
      AutoSize = true,
      ColumnCount = 2,
      Dock = DockStyle.Fill,
      Margin = Padding.Empty,
      RowCount = 1
    };
    container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    container.Controls.Add(slider, 0, 0);
    container.Controls.Add(valueLabel, 1, 0);
    return new VolumeSliderControls(container, slider, valueLabel);
  }

  /// <summary>
  /// Creates one numeric control.
  /// </summary>
  private static NumericUpDown CreateNumeric(
    decimal minimum,
    decimal maximum,
    decimal value,
    int width)
  {
    var control = new NumericUpDown();
    ConfigureNumeric(control, minimum, maximum, value, width);
    return control;
  }

  /// <summary>
  /// Creates an inline label.
  /// </summary>
  private static Label MakeInlineLabel(string text)
  {
    return new Label
    {
      AutoSize = true,
      Margin = new Padding(8, 7, 4, 0),
      Text = text
    };
  }

  /// <summary>
  /// Creates a section label.
  /// </summary>
  private static Label MakeSectionLabel(string text)
  {
    return new Label
    {
      AutoSize = true,
      Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
      Margin = new Padding(4),
      Text = text
    };
  }

  /// <summary>
  /// Adjusts a restored window rectangle onto an available screen.
  /// </summary>
  private static Rectangle EnsureVisible(Rectangle bounds)
  {
    return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds))
      ? bounds
      : new Rectangle(
        Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty,
        bounds.Size);
  }

  private sealed record VoiceRowControls(
    ComboBox Voice,
    NumericUpDown Rate,
    NumericUpDown Pitch,
    TrackBar Volume,
    Label VolumeValue,
    Button Test);

  private sealed record VolumeSliderControls(
    Control Container,
    TrackBar Slider,
    Label ValueLabel);
}

/// <summary>
/// Represents a replay-navigation operation.
/// </summary>
internal delegate bool TryNavigateSpeech(out string text);
