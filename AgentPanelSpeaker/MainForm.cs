using System.Windows.Automation;

namespace AgentPanelSpeaker;

/// <summary>
/// Provides transcript-container selection, monitoring, and speech controls.
/// </summary>
internal sealed class MainForm : Form
{
  private readonly TranscriptReader _reader = new();
  private readonly TranscriptMonitor _monitor = new();
  private readonly SpeechService _speech = new();

  private readonly Label _instructionsLabel = new();
  private readonly Button _captureButton = new();
  private readonly Button _refreshPreviewButton = new();
  private readonly Label _targetLabel = new();
  private readonly TextBox _previewTextBox = new();
  private readonly ComboBox _voiceComboBox = new();
  private readonly NumericUpDown _rateNumeric = new();
  private readonly NumericUpDown _idleNumeric = new();
  private readonly NumericUpDown _pollNumeric = new();
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

  private TranscriptTarget? _target;
  private int _monitorSession;
  private bool _closing;

  /// <summary>
  /// Initializes the application window and event connections.
  /// </summary>
  public MainForm()
  {
    InitializeControls();
    PopulateVoices();

    _captureButton.Click += CaptureButtonClicked;
    _refreshPreviewButton.Click += RefreshPreviewButtonClicked;
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
    _monitor.TailChanged += MonitorTailChanged;
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
    Text = "Agent Panel Speaker v16";
    AutoScaleDimensions = new SizeF(96.0f, 96.0f);
    AutoScaleMode = AutoScaleMode.Dpi;
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(760, 620);
    Size = new Size(920, 760);

    _instructionsLabel.AutoSize = true;
    _instructionsLabel.MaximumSize = new Size(850, 0);
    _instructionsLabel.Text =
      "1. Select under pointer, then move the pointer over normal Claude or " +
      "Codex narration.  2. The program walks upward through the " +
      "accessibility tree and retains the smallest transcript-like " +
      "container.  3. Confirm the preview and start monitoring.";

    ConfigureButton(_captureButton, "Select under pointer (3 s)");
    ConfigureButton(_refreshPreviewButton, "Refresh preview");
    ConfigureButton(_startButton, "Start");
    ConfigureButton(_stopButton, "Stop");
    ConfigureButton(_cancelSpeechButton, "Cancel speech");
    ConfigureButton(_rewindSentenceButton, "Rewind sentence");
    ConfigureButton(_forwardSentenceButton, "Forward sentence");
    ConfigureButton(_rewindNodeButton, "Rewind node");
    ConfigureButton(_forwardNodeButton, "Forward node");
    ConfigureButton(_testVoiceButton, "Test voice");
    ConfigureButton(_openLogButton, "Open diagnostic log");

    _targetLabel.AutoSize = true;
    _targetLabel.Text = "Target: none";

    _previewTextBox.Multiline = true;
    _previewTextBox.ReadOnly = true;
    _previewTextBox.ScrollBars = ScrollBars.Vertical;
    _previewTextBox.Dock = DockStyle.Fill;
    _previewTextBox.Font = new Font(FontFamily.GenericMonospace, 9.0f);

    _voiceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    _voiceComboBox.Width = 250;

    ConfigureNumeric(_rateNumeric, -10, 10, 0, 50);
    ConfigureNumeric(_idleNumeric, 100, 5000, 1000, 80);
    ConfigureNumeric(_pollNumeric, 100, 2000, 150, 80);

    _speakExistingCheckBox.AutoSize = true;
    _speakExistingCheckBox.Text = "Speak text already visible on start";

    _logTextBox.Multiline = true;
    _logTextBox.ReadOnly = true;
    _logTextBox.ScrollBars = ScrollBars.Vertical;
    _logTextBox.Dock = DockStyle.Fill;
    _logTextBox.Font = new Font(FontFamily.GenericMonospace, 9.0f);

    var targetButtons = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = true
    };
    targetButtons.Controls.AddRange(new Control[]
    {
      _captureButton,
      _refreshPreviewButton
    });

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
      MakeInlineLabel("Idle ms:"),
      _idleNumeric,
      MakeInlineLabel("Poll ms:"),
      _pollNumeric,
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
    layout.Controls.Add(targetButtons, 0, 1);
    layout.Controls.Add(_targetLabel, 0, 2);
    layout.Controls.Add(MakeSectionLabel("Detected transcript tail:"), 0, 3);
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
  /// <param name="button">Button to configure.</param>
  /// <param name="text">Button text.</param>
  private static void ConfigureButton(Button button, string text)
  {
    button.AutoSize = true;
    button.Text = text;
  }

  /// <summary>
  /// Applies common numeric control settings.
  /// </summary>
  /// <param name="control">Control to configure.</param>
  /// <param name="minimum">Minimum value.</param>
  /// <param name="maximum">Maximum value.</param>
  /// <param name="value">Initial value.</param>
  /// <param name="width">Control width.</param>
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
  /// Creates a label suitable for a settings row.
  /// </summary>
  /// <param name="text">Label text.</param>
  /// <returns>The configured label.</returns>
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
  /// <param name="text">Heading text.</param>
  /// <returns>The configured heading.</returns>
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
  /// Loads all enabled Windows speech voices.
  /// </summary>
  private void PopulateVoices()
  {
    try
    {
      IReadOnlyList<string> voices = _speech.GetInstalledVoiceNames();
      foreach (string voice in voices)
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
  /// Waits three seconds and selects the transcript container beneath the
  /// pointer.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private async void CaptureButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    await CaptureTargetAsync();
  }

  /// <summary>
  /// Refreshes the transcript preview.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private async void RefreshPreviewButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    await RefreshTargetDisplayAsync();
  }

  /// <summary>
  /// Starts monitoring the selected transcript container.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private void StartButtonClicked(object? sender, EventArgs eventArgs)
  {
    TranscriptTarget? target = _target;
    if (target is null)
    {
      MessageBox.Show(
        this,
        "Select a transcript region first.",
        Text,
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
      return;
    }

    try
    {
      ConfigureSpeech();
      _speech.BeginLiveSession();
      DiagnosticLog.Write("speech.session_started");
      Interlocked.Increment(ref _monitorSession);
      _monitor.Start(
        target,
        TimeSpan.FromMilliseconds((double)_pollNumeric.Value),
        TimeSpan.FromMilliseconds((double)_idleNumeric.Value),
        _speakExistingCheckBox.Checked);
      AppendLog("Monitoring started.");
      UpdateControlState();
    }
    catch (Exception exception) when (
      exception is ArgumentException or
      InvalidOperationException or
      ElementNotAvailableException)
    {
      AppendLog($"Unable to start: {exception.Message}");
    }
  }

  /// <summary>
  /// Stops transcript monitoring.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
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
  /// Cancels current and queued speech.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private void CancelSpeechButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    _speech.CancelAll();
    DiagnosticLog.Write("speech.cancel_all");
    AppendLog("Speech cancelled.");
  }

  /// <summary>
  /// Replays the preceding spoken sentence.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private void RewindSentenceButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    if (_speech.TryRewindSentence(out string text))
    {
      DiagnosticLog.Write("speech.rewind_sentence", new { text });
      AppendLog($"Rewind sentence and continue: {text}");
    }
    else
    {
      AppendLog("No earlier sentence is available.");
    }
  }

  /// <summary>
  /// Moves forward one spoken sentence and continues from there.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private void ForwardSentenceButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    if (_speech.TryForwardSentence(out string text))
    {
      DiagnosticLog.Write("speech.forward_sentence", new { text });
      AppendLog($"Forward sentence and continue: {text}");
    }
    else
    {
      AppendLog("No later sentence is available.");
    }
  }

  /// <summary>
  /// Replays the preceding spoken accessibility node.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private void RewindNodeButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    if (_speech.TryRewindNode(out string text))
    {
      DiagnosticLog.Write("speech.rewind_node", new { text });
      AppendLog($"Rewind node and continue: {text}");
    }
    else
    {
      AppendLog("No earlier node is available.");
    }
  }

  /// <summary>
  /// Moves forward one spoken accessibility node and continues from there.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private void ForwardNodeButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    if (_speech.TryForwardNode(out string text))
    {
      DiagnosticLog.Write("speech.forward_node", new { text });
      AppendLog($"Forward node and continue: {text}");
    }
    else
    {
      AppendLog("No later node is available.");
    }
  }

  /// <summary>
  /// Speaks a short test phrase with the selected settings.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
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
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
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
  /// Records a DPI transition after WinForms performs automatic scaling.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">DPI-change arguments.</param>
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
  /// Records the completed move or resize of the main window.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private void MainFormResizeEnded(object? sender, EventArgs eventArgs)
  {
    WriteLayoutDiagnostic("ui.resize_end");
  }

  /// <summary>
  /// Records the initial monitor layout after the form is displayed.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
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
  /// Stops workers and releases speech resources during window closure.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Form-closing arguments.</param>
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
  /// Updates the live preview when the monitored tail changes.
  /// </summary>
  /// <param name="tail">Latest transcript tail.</param>
  private void MonitorTailChanged(IReadOnlyList<string> tail)
  {
    string preview = FormatPreview(tail);
    PostToUi(() => _previewTextBox.Text = preview);
  }

  /// <summary>
  /// Marshals a speech fragment from the monitor thread to the UI thread.
  /// </summary>
  /// <param name="fragment">Text and node ready for speech.</param>
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
  /// Marshals monitor status from the worker thread to the UI thread.
  /// </summary>
  /// <param name="status">Monitor status text.</param>
  private void MonitorStatusChanged(string status)
  {
    PostToUi(() =>
    {
      AppendLog(status);
      UpdateControlState();
    });
  }

  /// <summary>
  /// Marshals a monitoring failure to the UI thread.
  /// </summary>
  /// <param name="exception">Monitoring exception.</param>
  private void MonitorFaulted(Exception exception)
  {
    PostToUi(() =>
    {
      AppendLog($"Monitoring failed: {exception.Message}");
      UpdateControlState();
    });
  }

  /// <summary>
  /// Hides this window, waits for the pointer to be placed over normal agent
  /// narration, and automatically selects the transcript ancestor.
  /// </summary>
  private async Task CaptureTargetAsync()
  {
    if (_monitor.IsRunning)
    {
      return;
    }

    AppendLog(
      "Move the pointer over normal Claude/Codex narration. Capturing in " +
      "3 seconds.");
    SetSelectionButtonsEnabled(false);
    Enabled = false;
    Hide();

    TranscriptTarget? target = null;
    Exception? failure = null;
    System.Drawing.Point point = System.Drawing.Point.Empty;
    try
    {
      await Task.Delay(TimeSpan.FromSeconds(3));
      if (!NativeMethods.GetPhysicalCursorPos(out var nativePoint))
      {
        throw new InvalidOperationException(
          "The physical pointer position could not be read.");
      }

      point = nativePoint.ToDrawingPoint();
      target = await Task.Run(() =>
        TranscriptTarget.CreateFromPoint(point));
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      System.Runtime.InteropServices.COMException or
      ArgumentException or
      InvalidOperationException)
    {
      failure = exception;
    }
    finally
    {
      Show();
      Activate();
      Enabled = true;
      SetSelectionButtonsEnabled(true);
    }

    if (failure is not null)
    {
      AppendLog($"Selection failed: {failure.Message}");
      return;
    }

    if (target is null)
    {
      AppendLog("Selection failed: no transcript target was returned.");
      return;
    }

    _target = target;
    DiagnosticLog.Write("target.selected", new
    {
      point = $"{point.X},{point.Y}",
      description = target.Describe(),
      target.SelectionReason
    });
    await RefreshTargetDisplayAsync();
  }

  /// <summary>
  /// Reads the selected container and updates its description and tail
  /// preview.
  /// </summary>
  private async Task RefreshTargetDisplayAsync()
  {
    TranscriptTarget? target = _target;
    if (target is null)
    {
      _targetLabel.Text = "Target: none";
      _previewTextBox.Clear();
      UpdateControlState();
      return;
    }

    SetSelectionButtonsEnabled(false);

    try
    {
      _targetLabel.Text = $"Target: {target.Describe()}";
      IReadOnlyList<string> tail = await Task.Run(() =>
        _reader.ReadTail(target));
      _previewTextBox.Text = FormatPreview(tail);
      DiagnosticLog.Write("preview.read", new
      {
        target = target.Describe(),
        source = _reader.LastReadSource,
        details = _reader.LastReadDetails,
        tail
      });

      if (tail.Count == 0)
      {
        AppendLog(
          "No transcript text was found inside the selected container.");
      }
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      InvalidOperationException)
    {
      _previewTextBox.Clear();
      AppendLog($"Preview failed: {exception.Message}");
    }
    finally
    {
      SetSelectionButtonsEnabled(true);
      UpdateControlState();
    }
  }

  /// <summary>
  /// Formats detected tail paragraphs for the preview box.
  /// </summary>
  /// <param name="tail">Detected paragraphs.</param>
  /// <returns>Formatted preview text.</returns>
  private static string FormatPreview(IReadOnlyList<string> tail)
  {
    if (tail.Count == 0)
    {
      return "(No text found.)";
    }

    return string.Join(
      Environment.NewLine + Environment.NewLine,
      tail.Select((paragraph, index) =>
        $"[{index + 1}] {paragraph}"));
  }


  /// <summary>
  /// Records the form and major child-control geometry.
  /// </summary>
  /// <param name="eventName">Diagnostic event identifier.</param>
  private void WriteLayoutDiagnostic(string eventName)
  {
    DiagnosticLog.Write(eventName, new
    {
      dpi = DeviceDpi,
      formBounds = RectangleToString(Bounds),
      clientSize = $"{ClientSize.Width}x{ClientSize.Height}",
      screen = Screen.FromControl(this).DeviceName,
      font = Font.ToString(),
      instructions = RectangleToString(_instructionsLabel.Bounds),
      target = RectangleToString(_targetLabel.Bounds),
      preview = RectangleToString(_previewTextBox.Bounds),
      voice = RectangleToString(_voiceComboBox.Bounds),
      activity = RectangleToString(_logTextBox.Bounds)
    });
  }

  /// <summary>
  /// Formats a rectangle for compact diagnostic output.
  /// </summary>
  /// <param name="rectangle">Rectangle to format.</param>
  /// <returns>Left, top, width, and height.</returns>
  private static string RectangleToString(Rectangle rectangle)
  {
    return $"{rectangle.Left},{rectangle.Top} " +
      $"{rectangle.Width}x{rectangle.Height}";
  }

  /// <summary>
  /// Applies the selected voice and speech rate.
  /// </summary>
  private void ConfigureSpeech()
  {
    string voice = _voiceComboBox.SelectedItem as string ?? string.Empty;
    _speech.Configure(voice, Decimal.ToInt32(_rateNumeric.Value));
  }

  /// <summary>
  /// Appends a timestamped activity line while limiting retained log size.
  /// </summary>
  /// <param name="message">Activity text.</param>
  private void AppendLog(string message)
  {
    const int maximumCharacters = 100_000;

    if (_logTextBox.TextLength > maximumCharacters)
    {
      _logTextBox.Text = _logTextBox.Text[^50_000..];
    }

    _logTextBox.AppendText(
      $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
  }

  /// <summary>
  /// Posts an action to the UI thread unless the form is closing.
  /// </summary>
  /// <param name="action">UI action.</param>
  private void PostToUi(Action action)
  {
    if (_closing || IsDisposed || !IsHandleCreated)
    {
      return;
    }

    BeginInvoke(action);
  }

  /// <summary>
  /// Enables or disables target-selection controls during preview work.
  /// </summary>
  /// <param name="enabled">Desired enabled state.</param>
  private void SetSelectionButtonsEnabled(bool enabled)
  {
    _captureButton.Enabled = enabled;
    _refreshPreviewButton.Enabled = enabled;
  }

  /// <summary>
  /// Updates controls according to target and monitor state.
  /// </summary>
  private void UpdateControlState()
  {
    bool running = _monitor.IsRunning;
    bool hasTarget = _target is not null;

    _captureButton.Enabled = !running;
    _refreshPreviewButton.Enabled = !running && hasTarget;
    _startButton.Enabled = !running && hasTarget;
    _stopButton.Enabled = running;
    _voiceComboBox.Enabled = !running;
    _rateNumeric.Enabled = !running;
    _idleNumeric.Enabled = !running;
    _pollNumeric.Enabled = !running;
    _speakExistingCheckBox.Enabled = !running;
    _rewindSentenceButton.Enabled = _speech.HasHistory;
    _forwardSentenceButton.Enabled = _speech.HasHistory;
    _rewindNodeButton.Enabled = _speech.HasHistory;
    _forwardNodeButton.Enabled = _speech.HasHistory;
  }
}
