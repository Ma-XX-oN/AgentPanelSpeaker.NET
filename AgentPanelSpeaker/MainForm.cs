using System.Windows.Automation;

namespace AgentPanelSpeaker;

/// <summary>
/// Provides target selection, transcript monitoring, and speech controls.
/// </summary>
internal sealed class MainForm : Form
{
  private readonly TranscriptReader _reader = new();
  private readonly TranscriptMonitor _monitor = new();
  private readonly SpeechService _speech = new();
  private readonly List<AutomationElement> _targetHistory = new();

  private readonly Label _instructionsLabel = new();
  private readonly Button _captureButton = new();
  private readonly Button _parentButton = new();
  private readonly Button _childButton = new();
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
  private readonly Button _testVoiceButton = new();
  private readonly TextBox _logTextBox = new();

  private int _targetHistoryIndex = -1;
  private bool _closing;

  /// <summary>
  /// Initializes the application window and event connections.
  /// </summary>
  public MainForm()
  {
    InitializeControls();
    PopulateVoices();

    _captureButton.Click += CaptureButtonClicked;
    _parentButton.Click += ParentButtonClicked;
    _childButton.Click += ChildButtonClicked;
    _refreshPreviewButton.Click += RefreshPreviewButtonClicked;
    _startButton.Click += StartButtonClicked;
    _stopButton.Click += StopButtonClicked;
    _cancelSpeechButton.Click += CancelSpeechButtonClicked;
    _testVoiceButton.Click += TestVoiceButtonClicked;
    FormClosing += MainFormClosing;

    _monitor.TextReady += MonitorTextReady;
    _monitor.TailChanged += MonitorTailChanged;
    _monitor.StatusChanged += MonitorStatusChanged;
    _monitor.Faulted += MonitorFaulted;

    UpdateControlState();
  }

  /// <summary>
  /// Creates and arranges all Windows Forms controls.
  /// </summary>
  private void InitializeControls()
  {
    Text = "Agent Panel Speaker";
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(760, 620);
    Size = new Size(920, 760);

    _instructionsLabel.AutoSize = true;
    _instructionsLabel.MaximumSize = new Size(850, 0);
    _instructionsLabel.Text =
      "1. Select under pointer, then move the mouse over transcript text.  " +
      "2. Use Parent until the preview shows the bottom of the visible " +
      "transcript rather than one text line.  3. Start monitoring; the " +
      "preview then updates live.";

    ConfigureButton(_captureButton, "Select under pointer (3 s)");
    ConfigureButton(_parentButton, "Parent");
    ConfigureButton(_childButton, "Back to child");
    ConfigureButton(_refreshPreviewButton, "Refresh preview");
    ConfigureButton(_startButton, "Start");
    ConfigureButton(_stopButton, "Stop");
    ConfigureButton(_cancelSpeechButton, "Cancel speech");
    ConfigureButton(_testVoiceButton, "Test voice");

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
    ConfigureNumeric(_pollNumeric, 50, 2000, 200, 80);

    _speakExistingCheckBox.AutoSize = true;
    _speakExistingCheckBox.Text = "Speak current paragraph on start";

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
      _parentButton,
      _childButton,
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
      _testVoiceButton
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
  /// Begins delayed pointer-based target selection.
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
  /// Moves the selected target to its raw UI Automation parent.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private async void ParentButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    AutomationElement? current = GetCurrentTarget();
    if (current is null)
    {
      return;
    }

    try
    {
      AutomationElement? parent = TreeWalker.RawViewWalker.GetParent(current);
      if (parent is null)
      {
        AppendLog("The selected element has no accessible parent.");
        return;
      }

      if (_targetHistoryIndex + 1 < _targetHistory.Count)
      {
        _targetHistory.RemoveRange(
          _targetHistoryIndex + 1,
          _targetHistory.Count - _targetHistoryIndex - 1);
      }

      _targetHistory.Add(parent);
      _targetHistoryIndex = _targetHistory.Count - 1;
      await RefreshTargetDisplayAsync();
    }
    catch (ElementNotAvailableException exception)
    {
      AppendLog($"Target is no longer available: {exception.Message}");
    }
  }

  /// <summary>
  /// Returns to the previous child in target-selection history.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private async void ChildButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    if (_targetHistoryIndex <= 0)
    {
      return;
    }

    --_targetHistoryIndex;
    await RefreshTargetDisplayAsync();
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
  /// Starts monitoring the selected transcript target.
  /// </summary>
  /// <param name="sender">Unused event sender.</param>
  /// <param name="eventArgs">Unused event arguments.</param>
  private void StartButtonClicked(object? sender, EventArgs eventArgs)
  {
    AutomationElement? target = GetCurrentTarget();
    if (target is null)
    {
      MessageBox.Show(
        this,
        "Select a transcript target first.",
        Text,
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
      return;
    }

    try
    {
      ConfigureSpeech();
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
    _monitor.Stop();
    AppendLog("Monitoring stopped.");
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
    AppendLog("Speech cancelled.");
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
      _speech.Speak("Agent panel speech is working.");
    }
    catch (Exception exception) when (
      exception is ArgumentException or
      InvalidOperationException)
    {
      AppendLog($"Voice test failed: {exception.Message}");
    }
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
    _monitor.Dispose();
    _speech.Dispose();
  }

  /// <summary>
  /// Updates the live preview when the monitored visible tail changes.
  /// </summary>
  /// <param name="tail">The latest visible transcript tail.</param>
  private void MonitorTailChanged(IReadOnlyList<string> tail)
  {
    string preview = FormatPreview(tail);
    PostToUi(() => _previewTextBox.Text = preview);
  }

  /// <summary>
  /// Marshals a speech fragment from the monitor thread to the UI thread.
  /// </summary>
  /// <param name="text">Text ready for speech.</param>
  private void MonitorTextReady(string text)
  {
    PostToUi(() =>
    {
      try
      {
        ConfigureSpeech();
        _speech.Speak(text);
        AppendLog($"Speak: {text}");
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
  /// Marshals a monitor failure from the worker thread to the UI thread.
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
  /// Hides the application, waits three seconds, and captures the UI element
  /// under the mouse pointer.
  /// </summary>
  private async Task CaptureTargetAsync()
  {
    if (_monitor.IsRunning)
    {
      return;
    }

    AppendLog("Move the pointer over transcript text.  Capturing in 3 seconds.");
    Enabled = false;
    Hide();

    AutomationElement? element = null;
    Exception? failure = null;

    try
    {
      await Task.Delay(TimeSpan.FromSeconds(3));
      Point cursor = Cursor.Position;
      element = AutomationElement.FromPoint(
        new System.Windows.Point(cursor.X, cursor.Y));
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      InvalidOperationException)
    {
      failure = exception;
    }
    finally
    {
      Show();
      Activate();
      Enabled = true;
    }

    if (failure is not null)
    {
      AppendLog($"Selection failed: {failure.Message}");
      return;
    }

    if (element is null)
    {
      AppendLog("No UI Automation element was found under the pointer.");
      return;
    }

    _targetHistory.Clear();
    _targetHistory.Add(element);
    _targetHistoryIndex = 0;
    await RefreshTargetDisplayAsync();
  }

  /// <summary>
  /// Reads the selected target and updates its description and tail preview.
  /// </summary>
  private async Task RefreshTargetDisplayAsync()
  {
    AutomationElement? target = GetCurrentTarget();
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
      _targetLabel.Text = DescribeTarget(target);
      IReadOnlyList<string> tail = await Task.Run(() =>
        _reader.ReadTail(target));
      _previewTextBox.Text = FormatPreview(tail);

      if (tail.Count == 0)
      {
        AppendLog(
          "No transcript text was found.  Try Parent and refresh again.");
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
  /// Returns the current target-history element.
  /// </summary>
  /// <returns>The selected target or null.</returns>
  private AutomationElement? GetCurrentTarget()
  {
    return _targetHistoryIndex >= 0 &&
      _targetHistoryIndex < _targetHistory.Count
        ? _targetHistory[_targetHistoryIndex]
        : null;
  }

  /// <summary>
  /// Builds a readable UI Automation target description.
  /// </summary>
  /// <param name="target">Target element.</param>
  /// <returns>A target description.</returns>
  private static string DescribeTarget(AutomationElement target)
  {
    try
    {
      AutomationElement.AutomationElementInformation current = target.Current;
      string name = string.IsNullOrWhiteSpace(current.Name)
        ? "(unnamed)"
        : current.Name;
      string controlType = current.ControlType.ProgrammaticName;
      return $"Target: {controlType}; {name}; PID {current.ProcessId}";
    }
    catch (ElementNotAvailableException)
    {
      return "Target: no longer available";
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
    _parentButton.Enabled = enabled;
    _childButton.Enabled = enabled;
    _refreshPreviewButton.Enabled = enabled;
  }

  /// <summary>
  /// Updates controls according to target and monitor state.
  /// </summary>
  private void UpdateControlState()
  {
    bool running = _monitor.IsRunning;
    bool hasTarget = GetCurrentTarget() is not null;

    _captureButton.Enabled = !running;
    _parentButton.Enabled = !running && hasTarget;
    _childButton.Enabled = !running && _targetHistoryIndex > 0;
    _refreshPreviewButton.Enabled = !running && hasTarget;
    _startButton.Enabled = !running && hasTarget;
    _stopButton.Enabled = running;
    _voiceComboBox.Enabled = !running;
    _rateNumeric.Enabled = !running;
    _idleNumeric.Enabled = !running;
    _pollNumeric.Enabled = !running;
    _speakExistingCheckBox.Enabled = !running;
  }
}
