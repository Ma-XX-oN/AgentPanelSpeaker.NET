namespace AgentPanelSpeaker;

/// <summary>
/// Provides JSONL session selection, monitoring, and speech controls.
/// </summary>
internal sealed class MainForm : Form
{
  private readonly JsonlSessionMonitor _monitor = new();
  private readonly SpeechService _speech = new();

  private readonly Label _instructionsLabel = new();
  private readonly ComboBox _sourceComboBox = new();
  private readonly TextBox _sessionPathTextBox = new();
  private readonly Button _detectLatestButton = new();
  private readonly Button _browseButton = new();
  private readonly CheckBox _followLatestCheckBox = new();
  private readonly TextBox _previewTextBox = new();
  private readonly ComboBox _voiceComboBox = new();
  private readonly NumericUpDown _rateNumeric = new();
  private readonly NumericUpDown _pollNumeric = new();
  private readonly CheckBox _speakMessagesCheckBox = new();
  private readonly CheckBox _speakReasoningCheckBox = new();
  private readonly CheckBox _skipFencedCodeCheckBox = new();
  private readonly CheckBox _speakExistingCheckBox = new();
  private readonly Button _startButton = new();
  private readonly Button _stopButton = new();
  private readonly Button _cancelSpeechButton = new();
  private readonly Button _rewindSentenceButton = new();
  private readonly Button _forwardSentenceButton = new();
  private readonly Button _rewindNodeButton = new();
  private readonly Button _forwardNodeButton = new();
  private readonly Button _testVoiceButton = new();
  private readonly Button _openLogButton = new();
  private readonly TextBox _logTextBox = new();

  private bool _pathIsManual;
  private int _monitorSession;
  private bool _closing;

  /// <summary>
  /// Initializes the application window and event connections.
  /// </summary>
  public MainForm()
  {
    InitializeControls();
    PopulateSources();
    PopulateVoices();

    _sourceComboBox.SelectedIndexChanged += SourceSelectionChanged;
    _detectLatestButton.Click += DetectLatestButtonClicked;
    _browseButton.Click += BrowseButtonClicked;
    _startButton.Click += StartButtonClicked;
    _stopButton.Click += StopButtonClicked;
    _cancelSpeechButton.Click += CancelSpeechButtonClicked;
    _rewindSentenceButton.Click += RewindSentenceButtonClicked;
    _forwardSentenceButton.Click += ForwardSentenceButtonClicked;
    _rewindNodeButton.Click += RewindNodeButtonClicked;
    _forwardNodeButton.Click += ForwardNodeButtonClicked;
    _testVoiceButton.Click += TestVoiceButtonClicked;
    _openLogButton.Click += OpenLogButtonClicked;
    DpiChanged += MainFormDpiChanged;
    ResizeEnd += MainFormResizeEnded;
    Shown += MainFormShown;
    FormClosing += MainFormClosing;

    _monitor.TextReady += MonitorTextReady;
    _monitor.MessagesChanged += MonitorMessagesChanged;
    _monitor.StatusChanged += MonitorStatusChanged;
    _monitor.Faulted += MonitorFaulted;

    UpdateControlState();
    AppendLog($"Diagnostic log: {DiagnosticLog.FilePath}");
  }

  /// <summary>
  /// Creates and arranges all Windows Forms controls.
  /// </summary>
  private void InitializeControls()
  {
    Text = "Agent Panel Speaker v18";
    AutoScaleDimensions = new SizeF(96.0f, 96.0f);
    AutoScaleMode = AutoScaleMode.Dpi;
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(820, 650);
    Size = new Size(980, 800);

    _instructionsLabel.AutoSize = true;
    _instructionsLabel.MaximumSize = new Size(920, 0);
    _instructionsLabel.Text =
      "Reads Claude/Codex session JSONL directly. It speaks assistant text " +
      "and optional reasoning while skipping tool calls, command output, " +
      "diffs, and tool results.";

    _sourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    _sourceComboBox.Width = 110;

    _sessionPathTextBox.ReadOnly = true;
    _sessionPathTextBox.Width = 560;

    ConfigureButton(_detectLatestButton, "Detect latest");
    ConfigureButton(_browseButton, "Browse JSONL");
    ConfigureButton(_startButton, "Start");
    ConfigureButton(_stopButton, "Stop");
    ConfigureButton(_cancelSpeechButton, "Cancel speech");
    ConfigureButton(_rewindSentenceButton, "Rewind sentence");
    ConfigureButton(_forwardSentenceButton, "Forward sentence");
    ConfigureButton(_rewindNodeButton, "Rewind node");
    ConfigureButton(_forwardNodeButton, "Forward node");
    ConfigureButton(_testVoiceButton, "Test voice");
    ConfigureButton(_openLogButton, "Open diagnostic log");

    _followLatestCheckBox.AutoSize = true;
    _followLatestCheckBox.Checked = true;
    _followLatestCheckBox.Text = "Follow newest session";

    _previewTextBox.Multiline = true;
    _previewTextBox.ReadOnly = true;
    _previewTextBox.ScrollBars = ScrollBars.Vertical;
    _previewTextBox.Dock = DockStyle.Fill;
    _previewTextBox.Font = new Font(FontFamily.GenericMonospace, 9.0f);

    _voiceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    _voiceComboBox.Width = 240;

    ConfigureNumeric(_rateNumeric, -10, 10, 0, 55);
    ConfigureNumeric(_pollNumeric, 50, 2000, 150, 80);

    ConfigureCheckBox(
      _speakMessagesCheckBox,
      "Speak assistant messages",
      isChecked: true);
    ConfigureCheckBox(
      _speakReasoningCheckBox,
      "Speak reasoning/thinking",
      isChecked: true);
    ConfigureCheckBox(
      _skipFencedCodeCheckBox,
      "Skip fenced code",
      isChecked: true);
    ConfigureCheckBox(
      _speakExistingCheckBox,
      "Speak last existing assistant message on start",
      isChecked: false);

    _logTextBox.Multiline = true;
    _logTextBox.ReadOnly = true;
    _logTextBox.ScrollBars = ScrollBars.Vertical;
    _logTextBox.Dock = DockStyle.Fill;
    _logTextBox.Font = new Font(FontFamily.GenericMonospace, 9.0f);

    var sessionControls = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = true
    };
    sessionControls.Controls.AddRange(new Control[]
    {
      MakeInlineLabel("Source:"),
      _sourceComboBox,
      _detectLatestButton,
      _browseButton,
      _followLatestCheckBox
    });

    var pathControls = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    pathControls.Controls.Add(MakeInlineLabel("Session:"));
    pathControls.Controls.Add(_sessionPathTextBox);

    var settings = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = true
    };
    settings.Controls.AddRange(new Control[]
    {
      MakeInlineLabel("Voice:"),
      _voiceComboBox,
      MakeInlineLabel("Rate:"),
      _rateNumeric,
      MakeInlineLabel("Poll ms:"),
      _pollNumeric,
      _speakMessagesCheckBox,
      _speakReasoningCheckBox,
      _skipFencedCodeCheckBox,
      _speakExistingCheckBox
    });

    var runButtons = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = true
    };
    runButtons.Controls.AddRange(new Control[]
    {
      _startButton,
      _stopButton,
      _cancelSpeechButton,
      _rewindSentenceButton,
      _forwardSentenceButton,
      _rewindNodeButton,
      _forwardNodeButton,
      _testVoiceButton,
      _openLogButton
    });

    var layout = new TableLayoutPanel
    {
      ColumnCount = 1,
      RowCount = 10,
      Dock = DockStyle.Fill,
      Padding = new Padding(10)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48.0f));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52.0f));

    layout.Controls.Add(_instructionsLabel, 0, 0);
    layout.Controls.Add(sessionControls, 0, 1);
    layout.Controls.Add(pathControls, 0, 2);
    layout.Controls.Add(MakeSectionLabel("Recent accepted assistant text:"), 0, 3);
    layout.Controls.Add(_previewTextBox, 0, 4);
    layout.Controls.Add(settings, 0, 5);
    layout.Controls.Add(runButtons, 0, 6);
    layout.Controls.Add(MakeSectionLabel("Activity:"), 0, 8);
    layout.Controls.Add(_logTextBox, 0, 9);

    Controls.Add(layout);
  }

  /// <summary>
  /// Applies common button sizing.
  /// </summary>
  private static void ConfigureButton(Button button, string text)
  {
    button.AutoSize = true;
    button.Text = text;
  }

  /// <summary>
  /// Applies common numeric control settings.
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
  /// Applies common checkbox settings.
  /// </summary>
  private static void ConfigureCheckBox(
    CheckBox checkBox,
    string text,
    bool isChecked)
  {
    checkBox.AutoSize = true;
    checkBox.Checked = isChecked;
    checkBox.Text = text;
  }

  /// <summary>
  /// Creates a label suitable for a settings row.
  /// </summary>
  private static Label MakeInlineLabel(string text)
  {
    return new Label
    {
      AutoSize = true,
      Margin = new Padding(8, 7, 0, 0),
      Text = text
    };
  }

  /// <summary>
  /// Creates a section heading label.
  /// </summary>
  private static Label MakeSectionLabel(string text)
  {
    return new Label
    {
      AutoSize = true,
      Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
      Margin = new Padding(0, 8, 0, 3),
      Text = text
    };
  }

  /// <summary>
  /// Loads source choices.
  /// </summary>
  private void PopulateSources()
  {
    _sourceComboBox.Items.AddRange(new object[]
    {
      AgentSource.Auto,
      AgentSource.Codex,
      AgentSource.Claude
    });
    _sourceComboBox.SelectedItem = AgentSource.Auto;
  }

  /// <summary>
  /// Loads all enabled Windows speech voices.
  /// </summary>
  private void PopulateVoices()
  {
    try
    {
      foreach (string voice in _speech.GetInstalledVoiceNames())
      {
        _voiceComboBox.Items.Add(voice);
      }

      if (_voiceComboBox.Items.Count != 0)
      {
        _voiceComboBox.SelectedIndex = 0;
      }
      else
      {
        AppendLog("No enabled System.Speech voices were found.");
      }
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or
      PlatformNotSupportedException)
    {
      AppendLog($"Speech initialization failed: {exception.Message}");
    }
  }

  /// <summary>
  /// Clears a stale auto-detected path after the source changes.
  /// </summary>
  private void SourceSelectionChanged(object? sender, EventArgs eventArgs)
  {
    if (!_pathIsManual)
    {
      _sessionPathTextBox.Clear();
    }
  }

  /// <summary>
  /// Finds and displays the newest session for the selected source.
  /// </summary>
  private async void DetectLatestButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    await DetectLatestAsync();
  }

  /// <summary>
  /// Lets the user select a fixed JSONL session file.
  /// </summary>
  private void BrowseButtonClicked(object? sender, EventArgs eventArgs)
  {
    using var dialog = new OpenFileDialog
    {
      CheckFileExists = true,
      Filter = "JSON Lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
      Multiselect = false,
      Title = "Select Claude or Codex session JSONL"
    };

    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    try
    {
      AgentSource source = GetSelectedSource();
      LocatedSession session = SessionLocator.FromPath(dialog.FileName, source);
      _sessionPathTextBox.Text = session.Path;
      _pathIsManual = true;
      _followLatestCheckBox.Checked = false;
      if (source == AgentSource.Auto)
      {
        _sourceComboBox.SelectedItem = session.Source;
      }
      DiagnosticLog.Write("session.browsed", session);
      AppendLog($"Selected {session.Source}: {session.Path}");
    }
    catch (Exception exception) when (
      exception is IOException or
      UnauthorizedAccessException or
      InvalidDataException)
    {
      AppendLog($"Unable to use selected file: {exception.Message}");
    }
  }

  /// <summary>
  /// Starts monitoring the selected or latest JSONL session.
  /// </summary>
  private async void StartButtonClicked(object? sender, EventArgs eventArgs)
  {
    if (!_speakMessagesCheckBox.Checked && !_speakReasoningCheckBox.Checked)
    {
      MessageBox.Show(
        this,
        "Enable assistant messages, reasoning/thinking, or both.",
        Text,
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
      return;
    }

    try
    {
      if (string.IsNullOrWhiteSpace(_sessionPathTextBox.Text))
      {
        bool found = await DetectLatestAsync();
        if (!found)
        {
          return;
        }
      }

      ConfigureSpeech();
      _speech.BeginLiveSession();
      Interlocked.Increment(ref _monitorSession);

      string? explicitPath = _pathIsManual || !_followLatestCheckBox.Checked
        ? _sessionPathTextBox.Text
        : null;
      var settings = new MonitorSettings(
        GetSelectedSource(),
        explicitPath,
        _followLatestCheckBox.Checked,
        _speakExistingCheckBox.Checked,
        TimeSpan.FromMilliseconds((double)_pollNumeric.Value),
        new ExtractionOptions(
          _speakMessagesCheckBox.Checked,
          _speakReasoningCheckBox.Checked,
          _skipFencedCodeCheckBox.Checked));

      _monitor.Start(settings);
      DiagnosticLog.Write("speech.session_started");
      AppendLog("Monitoring started.");
      UpdateControlState();
    }
    catch (Exception exception) when (
      exception is IOException or
      UnauthorizedAccessException or
      InvalidDataException or
      InvalidOperationException or
      ArgumentException)
    {
      AppendLog($"Unable to start: {exception.Message}");
    }
  }

  /// <summary>
  /// Stops transcript monitoring and speech immediately.
  /// </summary>
  private void StopButtonClicked(object? sender, EventArgs eventArgs)
  {
    Interlocked.Increment(ref _monitorSession);
    _speech.CancelAll();
    DiagnosticLog.Write("speech.cancel_all", new { source = "stop" });
    _monitor.Stop();
    AppendLog("Monitoring and speech stopped.");
    UpdateControlState();
  }

  /// <summary>
  /// Cancels current and queued speech without stopping file monitoring.
  /// </summary>
  private void CancelSpeechButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    _speech.CancelAll();
    DiagnosticLog.Write("speech.cancel_all");
    AppendLog("Speech cancelled.");
  }

  /// <summary>
  /// Replays the preceding spoken sentence and continues.
  /// </summary>
  private void RewindSentenceButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    NavigateSpeech(
      _speech.TryRewindSentence,
      "Rewind sentence and continue");
  }

  /// <summary>
  /// Moves forward one spoken sentence and continues.
  /// </summary>
  private void ForwardSentenceButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    NavigateSpeech(
      _speech.TryForwardSentence,
      "Forward sentence and continue");
  }

  /// <summary>
  /// Replays the preceding JSONL assistant node and continues.
  /// </summary>
  private void RewindNodeButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    NavigateSpeech(
      _speech.TryRewindNode,
      "Rewind node and continue");
  }

  /// <summary>
  /// Moves forward one JSONL assistant node and continues.
  /// </summary>
  private void ForwardNodeButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    NavigateSpeech(
      _speech.TryForwardNode,
      "Forward node and continue");
  }

  /// <summary>
  /// Runs one replay-navigation operation.
  /// </summary>
  private void NavigateSpeech(
    TryNavigateSpeech navigate,
    string action)
  {
    if (navigate(out string text))
    {
      DiagnosticLog.Write("speech.navigation", new { action, text });
      AppendLog($"{action}: {text}");
    }
    else
    {
      AppendLog($"{action}: no matching history entry is available.");
    }
  }

  /// <summary>
  /// Speaks a short test phrase with the selected settings.
  /// </summary>
  private void TestVoiceButtonClicked(object? sender, EventArgs eventArgs)
  {
    try
    {
      ConfigureSpeech();
      _speech.SpeakUntracked("Agent panel speech is working.");
      DiagnosticLog.Write("speech.test");
    }
    catch (Exception exception) when (
      exception is ArgumentException or
      InvalidOperationException)
    {
      AppendLog($"Voice test failed: {exception.Message}");
    }
  }

  /// <summary>
  /// Opens the current structured diagnostic log in File Explorer.
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
  /// Updates the recent accepted-node preview.
  /// </summary>
  private void MonitorMessagesChanged(IReadOnlyList<string> messages)
  {
    string preview = string.Join(
      Environment.NewLine + Environment.NewLine,
      messages.Select((message, index) => $"[{index + 1}] {message}"));
    PostToUi(() => _previewTextBox.Text = preview);
  }

  /// <summary>
  /// Queues one monitor fragment for serialized speech.
  /// </summary>
  private void MonitorTextReady(SpeechFragment fragment)
  {
    int session = Volatile.Read(ref _monitorSession);
    PostToUi(() =>
    {
      if (!_monitor.IsRunning ||
          session != Volatile.Read(ref _monitorSession))
      {
        DiagnosticLog.Write("speech.skipped_after_stop", new
        {
          fragment.NodeId,
          text = fragment.Text,
          session,
          currentSession = Volatile.Read(ref _monitorSession)
        });
        return;
      }

      try
      {
        ConfigureSpeech();
        _speech.SpeakLive(fragment);
        DiagnosticLog.Write("speech.queued", new
        {
          fragment.NodeId,
          text = fragment.Text
        });
        AppendLog($"Speak: {fragment.Text}");
        UpdateControlState();
      }
      catch (Exception exception) when (
        exception is ArgumentException or
        InvalidOperationException)
      {
        AppendLog($"Speech failed: {exception.Message}");
      }
    });
  }

  /// <summary>
  /// Marshals monitor status to the UI thread.
  /// </summary>
  private void MonitorStatusChanged(string status)
  {
    PostToUi(() =>
    {
      AppendLog(status);
      UpdateControlState();
    });
  }

  /// <summary>
  /// Marshals a monitor failure to the UI thread.
  /// </summary>
  private void MonitorFaulted(Exception exception)
  {
    PostToUi(() =>
    {
      AppendLog($"Monitoring failed: {exception.Message}");
      UpdateControlState();
    });
  }

  /// <summary>
  /// Finds the latest session without blocking the UI thread.
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
      AgentSource source = GetSelectedSource();
      LocatedSession session = await Task.Run(() =>
        SessionLocator.FindLatest(source));
      _sessionPathTextBox.Text = session.Path;
      _pathIsManual = false;
      DiagnosticLog.Write("session.detected", session);
      AppendLog($"Detected {session.Source}: {session.Path}");
      return true;
    }
    catch (Exception exception) when (
      exception is IOException or
      UnauthorizedAccessException or
      InvalidOperationException)
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
  /// Gets the selected source enum.
  /// </summary>
  private AgentSource GetSelectedSource()
  {
    return _sourceComboBox.SelectedItem is AgentSource source
      ? source
      : AgentSource.Auto;
  }

  /// <summary>
  /// Applies the selected voice and rate.
  /// </summary>
  private void ConfigureSpeech()
  {
    string voice = _voiceComboBox.SelectedItem?.ToString() ?? string.Empty;
    _speech.Configure(voice, Decimal.ToInt32(_rateNumeric.Value));
  }

  /// <summary>
  /// Updates enabled control states from monitoring state.
  /// </summary>
  private void UpdateControlState()
  {
    bool running = _monitor.IsRunning;
    _sourceComboBox.Enabled = !running;
    _detectLatestButton.Enabled = !running;
    _browseButton.Enabled = !running;
    _followLatestCheckBox.Enabled = !running && !_pathIsManual;
    _speakMessagesCheckBox.Enabled = !running;
    _speakReasoningCheckBox.Enabled = !running;
    _skipFencedCodeCheckBox.Enabled = !running;
    _speakExistingCheckBox.Enabled = !running;
    _pollNumeric.Enabled = !running;
    _startButton.Enabled = !running;
    _stopButton.Enabled = running;
    _rewindSentenceButton.Enabled = _speech.HasHistory;
    _forwardSentenceButton.Enabled = _speech.HasHistory;
    _rewindNodeButton.Enabled = _speech.HasHistory;
    _forwardNodeButton.Enabled = _speech.HasHistory;
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

    string line = $"{DateTime.Now:HH:mm:ss.fff}  {text}";
    if (_logTextBox.TextLength != 0)
    {
      _logTextBox.AppendText(Environment.NewLine);
    }

    _logTextBox.AppendText(line);
    _logTextBox.SelectionStart = _logTextBox.TextLength;
    _logTextBox.ScrollToCaret();
  }

  /// <summary>
  /// Marshals an action to the UI thread when needed.
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
      // The form closed between the state check and BeginInvoke.
    }
  }

  /// <summary>
  /// Records a DPI transition after WinForms automatic scaling.
  /// </summary>
  private void MainFormDpiChanged(
    object? sender,
    DpiChangedEventArgs eventArgs)
  {
    DiagnosticLog.Write("ui.dpi_changed", new
    {
      oldDpi = eventArgs.DeviceDpiOld,
      newDpi = eventArgs.DeviceDpiNew,
      currentBounds = RectangleToString(Bounds),
      suggestedBounds = RectangleToString(eventArgs.SuggestedRectangle),
      screen = Screen.FromControl(this).DeviceName
    });

    BeginInvoke((Action)(() =>
    {
      PerformLayout();
      WriteLayoutDiagnostic("ui.layout_after_dpi");
    }));
  }

  /// <summary>
  /// Records a completed move or resize.
  /// </summary>
  private void MainFormResizeEnded(object? sender, EventArgs eventArgs)
  {
    WriteLayoutDiagnostic("ui.resize_end");
  }

  /// <summary>
  /// Records initial monitor layout.
  /// </summary>
  private void MainFormShown(object? sender, EventArgs eventArgs)
  {
    DiagnosticLog.Write("ui.screens", new
    {
      screens = Screen.AllScreens.Select(screen => new
      {
        screen.DeviceName,
        bounds = RectangleToString(screen.Bounds),
        workingArea = RectangleToString(screen.WorkingArea),
        screen.Primary
      }).ToArray()
    });
    WriteLayoutDiagnostic("ui.shown");
  }

  /// <summary>
  /// Stops workers and releases speech resources during closure.
  /// </summary>
  private void MainFormClosing(
    object? sender,
    FormClosingEventArgs eventArgs)
  {
    _closing = true;
    DiagnosticLog.Write("app.closing");
    _monitor.Dispose();
    _speech.Dispose();
  }

  /// <summary>
  /// Records current form and key-control layout.
  /// </summary>
  private void WriteLayoutDiagnostic(string eventName)
  {
    DiagnosticLog.Write(eventName, new
    {
      deviceDpi = DeviceDpi,
      bounds = RectangleToString(Bounds),
      clientSize = $"{ClientSize.Width}x{ClientSize.Height}",
      font = $"{Font.Name} {Font.SizeInPoints:0.##}",
      previewBounds = RectangleToString(_previewTextBox.Bounds),
      activityBounds = RectangleToString(_logTextBox.Bounds),
      screen = Screen.FromControl(this).DeviceName
    });
  }

  /// <summary>
  /// Formats one rectangle for diagnostics.
  /// </summary>
  private static string RectangleToString(Rectangle rectangle)
  {
    return $"{rectangle.X},{rectangle.Y} " +
      $"{rectangle.Width}x{rectangle.Height}";
  }

  /// <summary>
  /// Delegate shape used by speech navigation operations.
  /// </summary>
  private delegate bool TryNavigateSpeech(out string text);
}
