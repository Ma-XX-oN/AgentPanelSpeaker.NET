using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Provides JSONL session selection, role-specific speech settings, navigation,
/// persistence, and diagnostics.
/// </summary>
internal sealed class MainForm : Form, IMessageFilter
{
  private const int TransportButtonWidth = 50;
  private const int TransportButtonHeight = 34;
  private const int SpeechProfileWidth = 88;
  private const int EmGetFirstVisibleLine = 0x00CE;
  private const int EmGetLineCount = 0x00BA;
  private const int EmLineScroll = 0x00B6;
  private const int WmSetRedraw = 0x000B;
  private const int WmKeyDown = 0x0100;
  private const int WmLButtonDown = 0x0201;
  private const int WmNcLButtonDown = 0x00A1;
  private const int WmRButtonDown = 0x0204;
  private const int WmNcRButtonDown = 0x00A4;
  private const int WmMButtonDown = 0x0207;
  private const int WmNcMButtonDown = 0x00A7;
  private const int WmXButtonDown = 0x020B;
  private const int WmNcXButtonDown = 0x00AB;
  private const int WmSystemKeyDown = 0x0104;
  private const uint GaRoot = 2;
  private const uint GwHwndNext = 2;
  private const uint RdwInvalidate = 0x0001;
  private const uint RdwErase = 0x0004;
  private const uint RdwAllChildren = 0x0080;
  private const uint RdwUpdateNow = 0x0100;
  private const uint RdwFrame = 0x0400;
  private const int SwHide = 0;

  [DllImport("user32.dll", CharSet = CharSet.Auto)]
  private static extern IntPtr SendMessage(
    IntPtr window,
    int message,
    IntPtr wordParameter,
    IntPtr longParameter);

  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool RedrawWindow(
    IntPtr window,
    IntPtr updateRectangle,
    IntPtr updateRegion,
    uint flags);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool IsWindowVisible(IntPtr window);

  [DllImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool ShowWindow(IntPtr window, int command);

  [DllImport("user32.dll")]
  private static extern IntPtr GetAncestor(IntPtr window, uint flags);

  [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
  private static extern IntPtr GetForegroundWindowForTabDiagnostics();

  [DllImport("user32.dll", EntryPoint = "GetActiveWindow")]
  private static extern IntPtr GetActiveWindowForTabDiagnostics();

  [DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
  private static extern IntPtr WindowFromPointForTabDiagnostics(NativePoint point);

  [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
  private static extern uint GetWindowThreadProcessIdForTabDiagnostics(
    IntPtr window,
    out uint processId);

  [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "GetWindowText")]
  private static extern int GetWindowTextForTabDiagnostics(
    IntPtr window,
    StringBuilder text,
    int maximumCount);

  [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "GetClassName")]
  private static extern int GetClassNameForTabDiagnostics(
    IntPtr window,
    StringBuilder className,
    int maximumCount);

  [DllImport("user32.dll", EntryPoint = "GetTopWindow")]
  private static extern IntPtr GetTopWindowForTabDiagnostics(IntPtr window);

  [DllImport("user32.dll", EntryPoint = "GetWindow")]
  private static extern IntPtr GetWindowForTabDiagnostics(
    IntPtr window,
    uint command);

  [StructLayout(LayoutKind.Sequential)]
  private readonly struct NativePoint
  {
    public NativePoint(Point point)
    {
      X = point.X;
      Y = point.Y;
    }

    public readonly int X;
    public readonly int Y;
  }

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
  private readonly CheckBox _keepDisplayOnCheckBox = new();
  private readonly GlyphButton _playPauseButton = new();
  private readonly GlyphButton _processingTimeButton = new();
  private readonly GlyphButton _rewindSpeakerButton = new();
  private readonly GlyphButton _rewindSentenceButton = new();
  private readonly GlyphButton _forwardSentenceButton = new();
  private readonly GlyphButton _rewindNodeButton = new();
  private readonly GlyphButton _forwardNodeButton = new();
  private readonly GlyphButton _forwardSpeakerButton = new();
  private readonly GlyphButton _saveSettingsButton = new();
  private readonly GlyphButton _resetSettingsButton = new();
  private readonly GlyphButton _hotkeysButton = new();
  private readonly GlyphButton _openLogButton = new();
  private readonly Button _pronunciationsButton = new();
  private readonly GlyphButton _audioWakeButton = new();
  private readonly ComboBox _themeComboBox = new();
  private readonly Label _voiceHeaderLabel = new();
  private readonly TextBox _logTextBox = new();
  private readonly Panel _diagnosticHost = new();
  private readonly ThemedTabControl _diagnosticTabs = new();
  private readonly TabPage _transcriptTab = new("Transcript");
  private readonly TabPage _activityTab = new("Activity");
  private readonly TabPage _acceptedTextTab = new("Accepted Text");
  private readonly TranscriptView _transcriptView = new();
  private readonly TranscriptPlaybackMailbox _playbackMailbox = new(1);
  private readonly GlyphButton _transcriptSettingsButton = new();
  private readonly GlyphButton _maximizeTranscriptButton = new();
  private readonly TranscriptSettingsPopup _transcriptSettingsPopup = new();
  private readonly HoverPopupController _transcriptSettingsHoverController;
  private readonly System.Windows.Forms.Timer _transcriptSettingsSaveTimer =
    new();
  private readonly TableLayoutPanel _mainLayout = new();
  private readonly Dictionary<SpeechRole, VoiceRowControls> _voiceRows = new();
  private readonly SpeechProfileCompactControl _masterSpeechProfile =
    new("Master Speech Profile");
  private readonly VoiceDisplayField[] _voiceDisplayOrder;
  private readonly IReadOnlyList<InstalledSpeechVoice> _installedVoices;
  private readonly UserSettingsStore _settingsStore;
  private readonly DisplayAwakeController _displayAwake = new();

  private bool _pathIsManual;
  private bool _startupPresentationPending = true;
  private bool _loadingSettings;
  private bool _closing;
  private bool _playPauseTransitioning;
  private bool _voiceSettingPreviewActive;
  private bool _themeApplicationPending;
  private bool _themeTransitionQueued;
  private bool _themeTransitionActive;
  private AppTheme? _pendingThemeRequest;
  private string? _pendingThemeReason;
  private int _themeApplyGeneration;
  private int _themeApplyDepth;
  private int _lastCompletedThemeGeneration;
  private int _pendingIdleThemeGeneration;
  private bool _diagnosticsMaximized;
  private int _appliedTranscriptTrackingMilliseconds = -1;
  private string? _pendingPlayPauseTrigger;
  private readonly ThemeNativeMessageTracer _themeNativeMessageTracer = new();
  private bool _themeNativeMessageTracerStarted;
  private int _monitorSession;
  private int _historyPreviewGeneration;
  private long _pendingMonitorSeekNodeId;
  private int _pendingMonitorSeekWordIndex = -1;
  private bool _resumeAfterMonitorHistoryLoaded;
  private bool _reusePausedHistoryOnMonitorStart;
  private bool _suppressMonitorTextUntilHistoryLoaded;
  private SpeechHistorySnapshot? _selectedSessionHistory;
  private string? _selectedSessionHistoryPath;

  /// <summary>
  /// Initializes controls, settings, event handlers, and policy providers.
  /// </summary>
  public MainForm()
  {
    // Keep the native window fully transparent until the synchronous WinForms
    // control tree has been populated, themed, and laid out.  This prevents
    // Windows from presenting intermediate construction/layout paints.
    Opacity = 1.0 / 255.0;
    SetStyle(
      ControlStyles.AllPaintingInWmPaint |
      ControlStyles.OptimizedDoubleBuffer,
      true);
    SuspendLayout();
    _mainLayout.SuspendLayout();
    try
    {
      _installedVoices = _speech.GetInstalledVoices();
      _voiceDisplayOrder = BuildVoiceDisplayOrder(_installedVoices);
      _settingsStore = new UserSettingsStore(
        _installedVoices.Select(voice => voice.Name).ToArray());
      _speech.SetPolicyProviders(
        _settingsStore.GetProfile,
        _settingsStore.IsFenceTypeSpoken,
        _settingsStore.GetSpelledWords,
        _settingsStore.GetPronunciations,
        _settingsStore.GetAudioWakeSettings);
      InitializeControls();
      _transcriptSettingsHoverController = new HoverPopupController(
        _transcriptSettingsButton,
        () => new[] { _transcriptSettingsPopup },
        ShowTranscriptSettingsPopupCore,
        HideTranscriptSettingsPopupCore,
        _transcriptSettingsPopup.FocusInitialControl);
      _transcriptSettingsPopup.RegisterPopupTree(
        _transcriptSettingsHoverController);
      PopulateSources();
      PopulateVoiceRows();
      LoadSettingsIntoControls(_settingsStore.Current);
      ConnectEvents();
      UpdateControlState();
      ApplyCurrentTheme();
      _themeNativeMessageTracer.Attach(this);
      Application.AddMessageFilter(this);
      Application.Idle += ApplicationIdle;
      SystemEvents.UserPreferenceChanged += WindowsUserPreferenceChanged;
      AppendLog($"Diagnostic log: {DiagnosticLog.FilePath}");
      AppendLog($"Settings: {UserSettingsStore.FilePath}");
    }
    finally
    {
      _mainLayout.ResumeLayout(performLayout: false);
      ResumeLayout(performLayout: false);
      _mainLayout.PerformLayout();
      PerformLayout();
    }
  }

  /// <summary>
  /// Creates and arranges all Windows Forms controls.
  /// </summary>
  private void InitializeControls()
  {
    Text = "Agent Panel Speaker v212";
    AutoScaleMode = AutoScaleMode.Font;
    StartPosition = FormStartPosition.CenterScreen;
    MinimumSize = new Size(900, 720);
    Size = new Size(1120, 900);

    _instructionsLabel.AutoSize = true;
    _instructionsLabel.Dock = DockStyle.Fill;
    _instructionsLabel.Text =
      "Reads Claude/Codex JSONL directly. Ordinary tool calls and " +
      "results are " +
      "excluded; background-agent lifecycle and results are narrated.";

    _sourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    _sourceComboBox.Width = 110;
    ConfigureReadOnlyTextBox(_sessionTitleTextBox);
    ConfigureReadOnlyTextBox(_sessionPathTextBox);
    ConfigureButton(_detectLatestButton, "Detect latest");
    ConfigureButton(_browseButton, "Browse JSONL");
    ConfigureCustomTransportButton(
      _rewindSpeakerButton,
      GlyphButtonDrawing.PreviousSpeakerTurn,
      "Previous speaker turn (U or Alt+U)");
    ConfigureCustomTransportButton(
      _rewindNodeButton,
      GlyphButtonDrawing.PreviousNode,
      "Previous JSONL node (H or Alt+H)");
    ConfigureCustomTransportButton(
      _rewindSentenceButton,
      GlyphButtonDrawing.PreviousSentence,
      "Previous sentence/code line (J or Alt+J)");
    ConfigureCustomTransportButton(
      _playPauseButton,
      GlyphButtonDrawing.Play,
      "Start monitoring (K or Alt+K)");
    ConfigureCustomTransportButton(
      _forwardSentenceButton,
      GlyphButtonDrawing.NextSentence,
      "Next sentence/code line (L or Alt+L)");
    ConfigureCustomTransportButton(
      _forwardNodeButton,
      GlyphButtonDrawing.NextNode,
      "Next JSONL node (; or Alt+;)");
    ConfigureCustomTransportButton(
      _forwardSpeakerButton,
      GlyphButtonDrawing.NextSpeakerTurn,
      "Next speaker turn (O or Alt+O)");
    ConfigureCustomTransportButton(
      _processingTimeButton,
      GlyphButtonDrawing.ProcessingClock,
      "Speak AI processing time (' or Alt+')");
    ConfigureUtilityGlyphButton(
      _saveSettingsButton,
      GlyphButtonDrawing.Save,
      "Main.SaveSettings");
    ConfigureUtilityGlyphButton(
      _resetSettingsButton,
      GlyphButtonDrawing.Reset,
      "Main.ResetDefaults");
    ConfigureUtilityGlyphButton(
      _hotkeysButton,
      GlyphButtonDrawing.Keyboard,
      "Main.Hotkeys");
    ConfigureUtilityGlyphButton(
      _openLogButton,
      GlyphButtonDrawing.DiagnosticLog,
      "Main.DiagnosticLog");
    ConfigureButton(_pronunciationsButton, "Pronunciations...");
    ConfigureUtilityGlyphButton(
      _audioWakeButton,
      GlyphButtonDrawing.Bluetooth,
      "Main.BluetoothWake");
    _themeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    _themeComboBox.Width = 120;
    _themeComboBox.Items.AddRange(new object[]
    {
      AppTheme.System,
      AppTheme.Light,
      AppTheme.Dark
    });


    _followLatestCheckBox.AutoSize = true;
    _followLatestCheckBox.Margin = new Padding(3, 6, 3, 3);
    _followLatestCheckBox.Text = "Auto-follow newest session";
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
    _diagnosticHost.Dock = DockStyle.Fill;
    _diagnosticHost.MinimumSize = new Size(0, 180);
    _diagnosticTabs.Dock = DockStyle.Fill;
    _diagnosticTabs.TabPages.Add(_transcriptTab);
    _diagnosticTabs.TabPages.Add(_activityTab);
    _diagnosticTabs.TabPages.Add(_acceptedTextTab);
    _transcriptTab.Controls.Add(_transcriptView);
    _activityTab.Controls.Add(_logTextBox);
    _acceptedTextTab.Controls.Add(_previewTextBox);
    _diagnosticTabs.SelectedTab = _transcriptTab;
    ConfigureCompactGlyphButton(
      _transcriptSettingsButton,
      GlyphButtonDrawing.SettingsGear,
      "Transcript Settings");
    _toolTip.SetToolTip(
      _transcriptSettingsButton,
      "Transcript follow, highlight colour, and fade settings");
    ConfigureCompactGlyphButton(
      _maximizeTranscriptButton,
      GlyphButtonDrawing.Expand,
      "Maximize transcript tabs");
    _toolTip.SetToolTip(
      _maximizeTranscriptButton,
      "Maximize or restore the bottom tab area");
    _diagnosticHost.Controls.Add(_diagnosticTabs);
    _diagnosticHost.Controls.Add(_maximizeTranscriptButton);
    _diagnosticHost.Controls.Add(_transcriptSettingsButton);
    // The settings button is visually left of maximize even though maximize
    // is added first for z-order reasons.  Keep z-order and tab order separate.
    SetTabOrder(
      _diagnosticTabs,
      _transcriptSettingsButton,
      _maximizeTranscriptButton);
    _transcriptSettingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
    _maximizeTranscriptButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
    _transcriptSettingsSaveTimer.Interval = 250;
    _diagnosticHost.Resize += (_, _) => PositionTranscriptControls();
    UpdateDiagnosticTabTitles();
    ConfigureNumeric(_pollNumeric, 50, 2000, 150, 80);
    _fenceTypesTextBox.Width = 430;
    _speakExistingCheckBox.AutoSize = true;
    _speakExistingCheckBox.Margin = new Padding(3, 6, 3, 3);
    _speakExistingCheckBox.Text = "Speak complete latest turn on start";
    _keepDisplayOnCheckBox.AutoSize = true;
    _keepDisplayOnCheckBox.Margin = new Padding(3, 6, 3, 3);
    _keepDisplayOnCheckBox.Text = "Keep display on while speaking";
    _toolTip.SetToolTip(
      _keepDisplayOnCheckBox,
      "Prevents Windows from turning off the display during active speech.");
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
      _browseButton, MakeInlineLabel("Poll ms:"), _pollNumeric,
      _followLatestCheckBox
    });
    SetTabOrder(
      _sourceComboBox,
      _detectLatestButton,
      _browseButton,
      _pollNumeric,
      _followLatestCheckBox);

    var speechTable = new TableLayoutPanel
    {
      AutoSize = true,
      ColumnCount = 4,
      RowCount = 6,
      Dock = DockStyle.Fill,
      CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
    };
    speechTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    speechTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    speechTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
    speechTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    speechTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    AddSpeechHeader(speechTable);
    AddMasterSpeechRow(speechTable, 1);
    AddVoiceRow(speechTable, 3, SpeechRole.Agent, "AI agent");
    AddVoiceRow(speechTable, 4, SpeechRole.Subagent, "AI subagent");
    AddVoiceRow(speechTable, 5, SpeechRole.User, "User");

    var options = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = true
    };
    options.Controls.AddRange(new Control[]
    {
      MakeInlineLabel("Spoken fenced-code types:"), _fenceTypesTextBox,
      _pronunciationsButton,
      _speakExistingCheckBox, _keepDisplayOnCheckBox
    });
    SetTabOrder(
      _fenceTypesTextBox,
      _pronunciationsButton,
      _speakExistingCheckBox,
      _keepDisplayOnCheckBox);

    var transport = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      WrapContents = false
    };
    transport.Controls.AddRange(new Control[]
    {
      _rewindSpeakerButton, _rewindNodeButton, _rewindSentenceButton,
      _playPauseButton, _forwardSentenceButton,
      _forwardNodeButton, _forwardSpeakerButton, _processingTimeButton
    });
    SetTabOrder(
      _rewindSpeakerButton,
      _rewindNodeButton,
      _rewindSentenceButton,
      _playPauseButton,
      _forwardSentenceButton,
      _forwardNodeButton,
      _forwardSpeakerButton,
      _processingTimeButton);
    var utility = new FlowLayoutPanel
    {
      AutoSize = true,
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.RightToLeft,
      WrapContents = false
    };
    utility.Controls.AddRange(new Control[]
    {
      _openLogButton, _hotkeysButton, _resetSettingsButton,
      _saveSettingsButton, _audioWakeButton, _themeComboBox,
      MakeInlineLabel("Theme:")
    });
    // FlowDirection is RightToLeft so insertion order is the reverse of the
    // visual left-to-right order.  Keep the visual layout while making the
    // actual WinForms tab order match what the user sees.
    SetTabOrder(
      _themeComboBox,
      _audioWakeButton,
      _saveSettingsButton,
      _resetSettingsButton,
      _hotkeysButton,
      _openLogButton);

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
    SetTabOrder(transport, utility);

    _mainLayout.ColumnCount = 1;
    _mainLayout.RowCount = 8;
    _mainLayout.Dock = DockStyle.Fill;
    _mainLayout.Padding = new Padding(10);
    _mainLayout.ColumnStyles.Add(
      new ColumnStyle(SizeType.Percent, 100.0f));
    for (int row = 0; row < 7; row++)
    {
      _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }
    _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
    TableLayoutPanel sessionDetails = CreateSessionDetailsLayout();
    Label speechSectionLabel = MakeSectionLabel("Speech by content:");
    _mainLayout.Controls.Add(_instructionsLabel, 0, 0);
    _mainLayout.Controls.Add(sessionControls, 0, 1);
    _mainLayout.Controls.Add(sessionDetails, 0, 2);
    _mainLayout.Controls.Add(speechSectionLabel, 0, 3);
    _mainLayout.Controls.Add(speechTable, 0, 4);
    _mainLayout.Controls.Add(options, 0, 5);
    _mainLayout.Controls.Add(controls, 0, 6);
    _mainLayout.Controls.Add(_diagnosticHost, 0, 7);
    SetTabOrder(
      _instructionsLabel,
      sessionControls,
      sessionDetails,
      speechSectionLabel,
      speechTable,
      options,
      controls,
      _diagnosticHost);
    Controls.Add(_mainLayout);
    ConfigureAccessibility();
    NormalizeInlineControlHeights();
  }

  /// <summary>
  /// Keeps inline controls in a row visually aligned to the adjacent input.
  /// </summary>
  private void NormalizeInlineControlHeights()
  {
    int sourceHeight = _sourceComboBox.PreferredHeight;
    MatchButtonHeight(_detectLatestButton, sourceHeight);
    MatchButtonHeight(_browseButton, sourceHeight);
    MatchNumericHeight(_pollNumeric, sourceHeight);
    MatchButtonHeight(_pronunciationsButton, _fenceTypesTextBox.PreferredHeight);
  }

  /// <summary>
  /// Preserves a button's preferred width while matching a target control height.
  /// </summary>
  private static void MatchButtonHeight(Button button, int height)
  {
    int width = button.GetPreferredSize(Size.Empty).Width;
    button.AutoSize = false;
    button.Size = new Size(width, height);
  }

  /// <summary>
  /// Preserves a numeric input's width while matching an adjacent control.
  /// </summary>
  private static void MatchNumericHeight(NumericUpDown numeric, int height)
  {
    numeric.AutoSize = false;
    numeric.Height = height;
  }

  /// <summary>
  /// Applies translated accessibility metadata to the main interactive controls.
  /// </summary>
  private void ConfigureAccessibility()
  {
    UiText.Apply(_sourceComboBox, "Main.Source", _toolTip);
    UiText.Apply(_sessionTitleTextBox, "Main.SessionTitle", _toolTip);
    UiText.Apply(_sessionPathTextBox, "Main.SessionPath", _toolTip);
    UiText.Apply(_detectLatestButton, "Main.DetectLatest", _toolTip);
    UiText.Apply(_browseButton, "Main.BrowseJsonl", _toolTip);
    UiText.Apply(_followLatestCheckBox, "Main.FollowNewest", _toolTip);
    UiText.Apply(_pollNumeric, "Main.PollInterval", _toolTip);
    UiText.Apply(_fenceTypesTextBox, "Main.FencedCodeTypes", _toolTip);
    UiText.Apply(_speakExistingCheckBox, "Main.SpeakExisting", _toolTip);
    UiText.Apply(_keepDisplayOnCheckBox, "Main.KeepDisplayOn", _toolTip);
    UiText.Apply(_themeComboBox, "Main.Theme", _toolTip);
    UiText.Apply(_pronunciationsButton, "Main.Pronunciations", _toolTip);
    UiText.Apply(_audioWakeButton, "Main.BluetoothWake", _toolTip);
    UiText.Apply(_previewTextBox, "Main.AcceptedText", _toolTip);
    UiText.Apply(_logTextBox, "Main.ActivityLog", _toolTip);
    UiText.Apply(_diagnosticTabs, "Main.DiagnosticTabs", _toolTip);
    UiText.Apply(_transcriptView, "Main.Transcript", _toolTip);
    AccessibilityAudit.ReportMissing(this);
  }

  /// <summary>
  /// Connects all UI and worker events.
  /// </summary>
  private void ConnectEvents()
  {
    _sourceComboBox.SelectedIndexChanged += SourceSelectionChanged;
    _followLatestCheckBox.CheckedChanged += FollowLatestChanged;
    _pollNumeric.ValueChanged += (_, _) => SaveControlsToSettings();
    _speakExistingCheckBox.CheckedChanged += (_, _) =>
      SaveControlsToSettings();
    _keepDisplayOnCheckBox.CheckedChanged += (_, _) =>
    {
      SaveControlsToSettings();
      UpdateDisplayAwakeState();
    };
    _fenceTypesTextBox.TextChanged += FenceTypesTextChanged;
    _fenceDebounceTimer.Tick += FenceDebounceTimerTick;
    _detectLatestButton.Click += async (_, _) => await DetectLatestAsync();
    _browseButton.Click += BrowseButtonClicked;
    _hotkeysButton.Click += HotkeysButtonClicked;
    _playPauseButton.Click += PlayPauseButtonClicked;
    _diagnosticTabs.SelectedIndexChanged += (_, _) =>
    {
      HideTranscriptSettingsPopup(
        returnFocus: false,
        reason: "diagnostic-tab-selected-index-changed");
      UpdateDiagnosticTabTitles();
    };
    _transcriptSettingsButton.Click += (_, _) =>
      _transcriptSettingsHoverController.OpenImmediately(focusPopup: true);
    _maximizeTranscriptButton.Click += (_, _) =>
    {
      HideTranscriptSettingsPopup(
        returnFocus: false,
        reason: "maximize-transcript-click");
      SetDiagnosticsMaximized(!_diagnosticsMaximized);
    };
    _transcriptSettingsPopup.SettingsChanged += (_, _) =>
      TranscriptSettingsChanged();
    _transcriptSettingsSaveTimer.Tick += (_, _) =>
    {
      _transcriptSettingsSaveTimer.Stop();
      SaveControlsToSettings();
    };
    _transcriptSettingsPopup.TransportKeyPressed +=
      TranscriptTransportKeyPressed;
    _transcriptSettingsPopup.FocusTraversalRequested +=
      TranscriptSettingsFocusTraversalRequested;
    _transcriptSettingsPopup.DismissRequested += (_, _) =>
      HideTranscriptSettingsPopup(
        returnFocus: true,
        reason: "transcript-settings-dismiss-requested");
    _transcriptView.TransportKeyPressed += TranscriptTransportKeyPressed;
    _transcriptView.FindSeekRequested += TranscriptFindSeekRequested;
    _transcriptView.FindSeekEndRequested += TranscriptFindSeekEndRequested;
    _transcriptView.FollowSpeechChanged += TranscriptFollowSpeechChanged;
    _processingTimeButton.Click += ProcessingTimeButtonClicked;
    Deactivate += (_, _) =>
    {
      PopupFormBase.WriteActivationDiagnostics(
        "mainform-deactivate-event",
        this);
      HoverPopupController.HandleOwnerDeactivated(this);
    };
    _rewindSpeakerButton.Click += (_, _) => NavigateSpeech(
      _speech.TryRewindSpeaker,
      "Previous speaker turn");
    _rewindSentenceButton.Click += (_, _) => NavigateSpeech(
      _speech.TryRewindSentence,
      "Previous sentence/code line");
    _forwardSentenceButton.Click += (_, _) => NavigateSpeech(
      _speech.TryForwardSentence,
      "Next sentence/code line",
      "Past end of last sentence/code line.");
    _rewindNodeButton.Click += (_, _) => NavigateSpeech(
      _speech.TryRewindNode,
      "Previous JSONL node");
    _forwardNodeButton.Click += (_, _) => NavigateSpeech(
      _speech.TryForwardNode,
      "Next JSONL node",
      "Past end of last JSONL node.");
    _forwardSpeakerButton.Click += (_, _) => NavigateSpeech(
      _speech.TryForwardSpeaker,
      "Next speaker turn",
      "Past end of last speaker turn.");
    _saveSettingsButton.Click += (_, _) => ShowSettingsSelectionDialog(
      SettingsSelectionMode.Save);
    _resetSettingsButton.Click += (_, _) => ShowSettingsSelectionDialog(
      SettingsSelectionMode.Reset);
    _openLogButton.Click += OpenLogButtonClicked;
    _pronunciationsButton.Click += PronunciationsButtonClicked;
    _audioWakeButton.Click += AudioWakeButtonClicked;
    _themeComboBox.SelectedIndexChanged += ThemeSelectionChanged;
    _themeComboBox.DropDownClosed += ThemeDropDownClosed;
    ResizeEnd += (_, _) => SaveControlsToSettings(includeWindowPlacement: true);
    Shown += MainFormShown;
    FormClosing += MainFormClosing;
    _monitor.TextReady += MonitorTextReady;
    _monitor.HistoryLoaded += MonitorHistoryLoaded;
    _monitor.TurnCompleted += MonitorTurnCompleted;
    _monitor.BackgroundWorkChanged += MonitorBackgroundWorkChanged;
    _monitor.SessionChanged += MonitorSessionChanged;
    _monitor.MessagesChanged += MonitorMessagesChanged;
    _monitor.StatusChanged += status => PostToUi(() => AppendLog(status));
    _monitor.Faulted += exception => PostToUi(() =>
      AppendLog($"Monitoring failed: {exception.Message}"));
    _speech.Activity += message => PostToUi(() => AppendLog(message));
    _displayAwake.Activity += message => PostToUi(() => AppendLog(message));
    _speech.SpeakingStateChanged += speaking => PostToUi(() =>
    {
      if (!speaking)
      {
        _voiceSettingPreviewActive = false;
      }
      UpdateDisplayAwakeState();
      UpdateAllVoiceRowStates();
      UpdateControlState();
    });
    _speech.ProcessingTimeAnnouncementStateChanged += _ => PostToUi(
      UpdateControlState);
    _speech.PlaybackPositionChanged += QueuePlaybackPosition;
  }

  /// <summary>
  /// Publishes one playback position to the bounded latest-position mailbox.
  /// Only the first pending value schedules a UI-thread wake-up.
  /// </summary>
  private void QueuePlaybackPosition(TranscriptPlaybackPosition position)
  {
    if (_closing || IsDisposed)
    {
      return;
    }

    if (_playbackMailbox.Publish(position))
    {
      SchedulePlaybackMailboxDrain();
    }
  }

  /// <summary>
  /// Requests one asynchronous UI-thread mailbox drain.
  /// </summary>
  private void SchedulePlaybackMailboxDrain()
  {
    if (_closing || IsDisposed || !IsHandleCreated)
    {
      _playbackMailbox.Clear();
      return;
    }

    try
    {
      BeginInvoke((Action)DrainPlaybackMailbox);
    }
    catch (InvalidOperationException)
    {
      _playbackMailbox.Clear();
    }
  }

  /// <summary>
  /// Applies the bounded retained positions and requests one more wake-up only
  /// when a producer published while this drain was running.
  /// </summary>
  private void DrainPlaybackMailbox()
  {
    if (_closing || IsDisposed)
    {
      _playbackMailbox.Clear();
      return;
    }

    int batchCount = _playbackMailbox.GetWakeBatchCount();
    for (int index = 0; index < batchCount; ++index)
    {
      if (!_playbackMailbox.TryTake(
            out TranscriptPlaybackPosition position))
      {
        break;
      }
      if (position.FragmentText.Contains(
            "PolicyMachinery.hpp already has sections",
            StringComparison.OrdinalIgnoreCase))
      {
        DiagnosticLog.Write("transcript.mailbox_consumed", new
        {
          position.NodeId,
          position.WordIndex,
          position.Word,
          position.CharacterPosition,
          position.CharacterCount,
          position.BoundaryTimestamp,
          consumedTimestamp = Stopwatch.GetTimestamp()
        });
      }
      _transcriptView.ShowPlaybackPosition(position);
    }

    if (_playbackMailbox.CompleteWake())
    {
      SchedulePlaybackMailboxDrain();
    }
  }

  /// <summary>
  /// Adds table column headers.
  /// </summary>
  private void AddSpeechHeader(TableLayoutPanel table)
  {
    table.Controls.Add(MakeSectionLabel("Content"), 0, 0);

    _voiceHeaderLabel.AutoSize = true;
    _voiceHeaderLabel.Cursor = Cursors.Hand;
    _voiceHeaderLabel.Font = new Font(
      SystemFonts.DefaultFont,
      FontStyle.Bold);
    _voiceHeaderLabel.Margin = new Padding(4);
    _voiceHeaderLabel.Click += (_, _) => RotateVoiceDisplayOrder();
    table.Controls.Add(_voiceHeaderLabel, 1, 0);
    UpdateVoiceHeaderLabel();

    table.Controls.Add(
      MakeSpeechColumnHeader("Main", SpeechProfileWidth),
      2,
      0);
    table.Controls.Add(
      MakeSpeechColumnHeader("Context", SpeechProfileWidth),
      3,
      0);
  }


  /// <summary>
  /// Adds the global speech adjustment control shared by every role profile.
  /// </summary>
  private void AddMasterSpeechRow(TableLayoutPanel table, int row)
  {
    _masterSpeechProfile.Width = SpeechProfileWidth * 2 + 6;
    _masterSpeechProfile.TabIndex = 0;
    _masterSpeechProfile.ProfileChanged += (_, _) =>
      SaveControlsToSettings();
    _masterSpeechProfile.SetTestActions(
      new SpeechProfileTestAction("Agent Main", () =>
        PreviewVoiceSettings(SpeechRole.Agent, context: false)),
      new SpeechProfileTestAction("Agent Context", () =>
        PreviewVoiceSettings(SpeechRole.Agent, context: true)),
      new SpeechProfileTestAction("Subagent Main", () =>
        PreviewVoiceSettings(SpeechRole.Subagent, context: false)),
      new SpeechProfileTestAction("Subagent Context", () =>
        PreviewVoiceSettings(SpeechRole.Subagent, context: true)),
      new SpeechProfileTestAction("User Main", () =>
        PreviewVoiceSettings(SpeechRole.User, context: false)),
      new SpeechProfileTestAction("User Quote", () =>
        PreviewVoiceSettings(SpeechRole.User, context: true)));
    table.Controls.Add(MakeInlineLabel("Master"), 0, row);
    table.Controls.Add(MakeInlineLabel("All voices"), 1, row);
    table.Controls.Add(_masterSpeechProfile, 2, row);
    table.SetColumnSpan(_masterSpeechProfile, 2);
    _toolTip.SetToolTip(
      _masterSpeechProfile,
      "Adds rate and pitch to every profile and scales every volume. " +
      "Neutral values are rate 0, pitch 0, and volume 100.");
  }


  /// <summary>
  /// Adds one shared-voice role row with independent Main and Context
  /// profiles.
  /// </summary>
  private void AddVoiceRow(
    TableLayoutPanel table,
    int row,
    SpeechRole role,
    string label)
  {
    var voice = new VoiceComboBox
    {
      Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
      DropDownStyle = ComboBoxStyle.DropDownList,
      Dock = DockStyle.Top,
      Tag = ThemeManager.VoiceSelectorTag
    };
    voice.FormattingEnabled = true;
    voice.Format += VoiceComboBoxFormat;
    string voiceResource = role switch
    {
      SpeechRole.Agent => "Speech.Agent.Voice",
      SpeechRole.Subagent => "Speech.Subagent.Voice",
      SpeechRole.User => "Speech.User.Voice",
      _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };
    UiText.Apply(voice, voiceResource, _toolTip);
    int firstTabIndex = 1 + (_voiceRows.Count * 3);
    voice.TabIndex = firstTabIndex;

    string roleTitle = role switch
    {
      SpeechRole.Agent => "AI Agent",
      SpeechRole.Subagent => "AI Subagent",
      SpeechRole.User => "User",
      _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };
    string contextTitle = role == SpeechRole.User
      ? "User Quoted Text Speech Profile"
      : $"{roleTitle} Thoughts Speech Profile";
    var mainProfile = new SpeechProfileCompactControl(
      $"{roleTitle} Main Speech Profile")
    {
      Width = SpeechProfileWidth
    };
    mainProfile.TabIndex = firstTabIndex + 1;
    var contextProfile = new SpeechProfileCompactControl(contextTitle)
    {
      Width = SpeechProfileWidth,
      TabIndex = firstTabIndex + 2
    };
    var previewTimer = new System.Windows.Forms.Timer
    {
      Interval = 350
    };
    var controls = new VoiceRowControls(
      voice,
      mainProfile,
      contextProfile,
      previewTimer,
      $"{role} main speech is working.",
      role == SpeechRole.User
        ? "User quoted text speech is working."
        : $"{role} thoughts speech is working.");
    _voiceRows.Add(role, controls);

    table.Controls.Add(MakeInlineLabel(label), 0, row);
    table.Controls.Add(voice, 1, row);
    table.Controls.Add(mainProfile, 2, row);
    table.Controls.Add(contextProfile, 3, row);

    voice.SelectedIndexChanged += (_, _) =>
      VoiceRowChanged(role, context: false);
    mainProfile.ProfileChanged += (_, _) =>
      VoiceRowChanged(role, context: false);
    contextProfile.ProfileChanged += (_, _) =>
      VoiceRowChanged(role, context: true);
    mainProfile.TransportKeyPressed += ProfileTransportKeyPressed;
    contextProfile.TransportKeyPressed += ProfileTransportKeyPressed;
    mainProfile.FocusTraversalRequested += ProfileFocusTraversalRequested;
    contextProfile.FocusTraversalRequested += ProfileFocusTraversalRequested;
    previewTimer.Tick += (_, _) =>
      PreviewVoiceSettings(role, controls.PreviewContext);

    _toolTip.SetToolTip(
      mainProfile,
      $"{label} Main rate, pitch, and volume. Volume 0 mutes Main.");
    _toolTip.SetToolTip(
      contextProfile,
      role == SpeechRole.User
        ? "User quoted-text rate, pitch, and volume. Volume 0 mutes quoted text."
        : $"{label} Thoughts rate, pitch, and volume. Volume 0 mutes Thoughts.");
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
    RepopulateVoiceRows(preserveSelections: false);
  }

  /// <summary>
  /// Formats one installed voice using the current rotated field order.
  /// </summary>
  private void VoiceComboBoxFormat(
    object? sender,
    ListControlConvertEventArgs eventArgs)
  {
    if (eventArgs.ListItem is InstalledSpeechVoice voice)
    {
      eventArgs.Value = voice.Format(_voiceDisplayOrder);
    }
  }

  /// <summary>
  /// Rotates the displayed field order left and sorts by the new first field.
  /// </summary>
  private void RotateVoiceDisplayOrder()
  {
    VoiceDisplayField first = _voiceDisplayOrder[0];
    Array.Copy(
      _voiceDisplayOrder,
      1,
      _voiceDisplayOrder,
      0,
      _voiceDisplayOrder.Length - 1);
    _voiceDisplayOrder[^1] = first;
    UpdateVoiceHeaderLabel();
    RepopulateVoiceRows(preserveSelections: true);
  }

  /// <summary>
  /// Shows the primary voice sort field and explains the rotation action.
  /// </summary>
  private void UpdateVoiceHeaderLabel()
  {
    string firstField = GetVoiceDisplayFieldLabel(_voiceDisplayOrder[0]);
    _voiceHeaderLabel.Text = $"Voice ({firstField})";
    _voiceHeaderLabel.AccessibleName =
      $"Voice list sorted by {firstField}. Activate to rotate field order.";
    _toolTip.SetToolTip(
      _voiceHeaderLabel,
      $"Sorted by {firstField}. Click to rotate voice fields and resort.");
  }

  /// <summary>
  /// Builds the voice display order from fields that actually exist in the
  /// installed catalogue.  Natural is omitted when every installed voice has
  /// an empty Natural value.
  /// </summary>
  private static VoiceDisplayField[] BuildVoiceDisplayOrder(
    IReadOnlyList<InstalledSpeechVoice> voices)
  {
    bool hasNatural = voices.Any(voice => voice.Natural.Length != 0);
    return hasNatural
      ? new[]
      {
        VoiceDisplayField.Location,
        VoiceDisplayField.Language,
        VoiceDisplayField.VoiceName,
        VoiceDisplayField.Natural,
        VoiceDisplayField.Maker,
        VoiceDisplayField.Provider
      }
      : new[]
      {
        VoiceDisplayField.Location,
        VoiceDisplayField.Language,
        VoiceDisplayField.VoiceName,
        VoiceDisplayField.Maker,
        VoiceDisplayField.Provider
      };
  }

  /// <summary>
  /// Rebuilds all voice lists in current field order without changing profiles.
  /// </summary>
  private void RepopulateVoiceRows(bool preserveSelections)
  {
    var selectedNames = new Dictionary<SpeechRole, string>();
    if (preserveSelections)
    {
      foreach ((SpeechRole role, VoiceRowControls row) in _voiceRows)
      {
        selectedNames[role] = GetVoiceName(row.Voice.SelectedItem);
      }
    }

    InstalledSpeechVoice[] sortedVoices = _installedVoices.ToArray();
    Array.Sort(
      sortedVoices,
      (left, right) => CompareVoiceDisplay(left, right, _voiceDisplayOrder));

    bool wasLoading = _loadingSettings;
    _loadingSettings = true;
    try
    {
      foreach ((SpeechRole role, VoiceRowControls row) in _voiceRows)
      {
        row.Voice.BeginUpdate();
        try
        {
          row.Voice.Items.Clear();
          row.Voice.Items.Add(SpeechProfileSettings.NotSpoken);
          row.Voice.Items.AddRange(sortedVoices.Cast<object>().ToArray());
          if (preserveSelections)
          {
            row.Voice.SelectedItem = FindVoiceItem(
              row.Voice,
              selectedNames[role]);
          }
        }
        finally
        {
          row.Voice.EndUpdate();
        }
      }
    }
    finally
    {
      _loadingSettings = wasLoading;
    }
  }

  private static int CompareVoiceDisplay(
    InstalledSpeechVoice left,
    InstalledSpeechVoice right,
    IReadOnlyList<VoiceDisplayField> order)
  {
    foreach (VoiceDisplayField field in order)
    {
      int comparison = StringComparer.CurrentCultureIgnoreCase.Compare(
        left.GetDisplayField(field),
        right.GetDisplayField(field));
      if (comparison != 0)
      {
        return comparison;
      }
    }
    return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
  }

  private static string GetVoiceDisplayFieldLabel(VoiceDisplayField field)
  {
    return field switch
    {
      VoiceDisplayField.Location => "Location",
      VoiceDisplayField.Language => "Language",
      VoiceDisplayField.VoiceName => "Name",
      VoiceDisplayField.Natural => "Natural",
      VoiceDisplayField.Maker => "Maker",
      VoiceDisplayField.Provider => "Type",
      _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };
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
      _keepDisplayOnCheckBox.Checked =
        settings.KeepDisplayOnWhileSpeaking;
      _fenceTypesTextBox.Text = settings.SpokenFencedCodeTypes;
      _themeComboBox.SelectedItem = settings.Theme;
      _speech.SetWindowsMediaBookmarkMode(
        WindowsMediaBookmarkMode.Always);
      bool transcriptDark = ThemeManager.IsDark(settings.Theme);
      _transcriptSettingsPopup.SetSettings(
        settings.Transcript,
        transcriptDark);
      _transcriptView.ApplySettings(settings.Transcript, transcriptDark);
      _speech.SetWordBoundaryPollMilliseconds(
        settings.Transcript.HighlightUpdateMilliseconds);
      _appliedTranscriptTrackingMilliseconds =
        settings.Transcript.HighlightUpdateMilliseconds;
      _playbackMailbox.SetCapacity(
        settings.Transcript.HighlightQueueCapacity);
      _diagnosticsMaximized = settings.Transcript.Maximized;
      _masterSpeechProfile.SetProfile(
        settings.MasterSpeech.Rate,
        settings.MasterSpeech.Pitch,
        settings.MasterSpeech.Volume);
      LoadVoiceRow(
        SpeechRole.Agent,
        settings.Assistant,
        settings.Reasoning);
      LoadVoiceRow(
        SpeechRole.Subagent,
        settings.SubagentAssistant,
        settings.SubagentReasoning);
      LoadVoiceRow(SpeechRole.User, settings.User, settings.UserContext);
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
    ApplyCurrentTheme();
    SetDiagnosticsMaximized(_diagnosticsMaximized, save: false);
  }

  /// <summary>
  /// Loads one shared-voice profile row.
  /// </summary>
  private void LoadVoiceRow(
    SpeechRole role,
    SpeechProfileSettings mainProfile,
    SpeechProfileSettings contextProfile)
  {
    VoiceRowControls row = _voiceRows[role];
    row.Voice.SelectedItem = FindVoiceItem(
      row.Voice,
      mainProfile.VoiceName);
    row.MainProfile.SetProfile(
      mainProfile.Rate,
      mainProfile.Pitch,
      mainProfile.Volume);
    row.ContextProfile.SetProfile(
      contextProfile.Rate,
      contextProfile.Pitch,
      contextProfile.Volume);
    UpdateVoiceRowState(role);
    row.Voice.Invalidate();
  }

  /// <summary>
  /// Finds the dropdown item representing one stored provider voice name.
  /// </summary>
  private static object FindVoiceItem(ComboBox comboBox, string voiceName)
  {
    foreach (object item in comboBox.Items)
    {
      if (string.Equals(
            GetVoiceName(item),
            voiceName,
            StringComparison.OrdinalIgnoreCase))
      {
        return item;
      }
    }

    return SpeechProfileSettings.NotSpoken;
  }

  /// <summary>
  /// Gets the stable provider name represented by one dropdown item.
  /// </summary>
  private static string GetVoiceName(object? item)
  {
    return item switch
    {
      InstalledSpeechVoice voice => voice.Name,
      string name when name.Length != 0 => name,
      _ => SpeechProfileSettings.NotSpoken
    };
  }


  /// <summary>
  /// Ends a paused monitor before controls select a different session.
  /// </summary>
  private bool StopPausedMonitoringForReconfiguration(string trigger)
  {
    if (!_monitor.IsRunning)
    {
      return true;
    }
    if (!_speech.IsPaused)
    {
      return false;
    }

    Interlocked.Increment(ref _monitorSession);
    _monitor.Stop(trigger);
    _speech.CancelAll();
    _speech.BeginLiveSession();
    AppendLog("Paused monitoring stopped for session reconfiguration.");
    UpdateControlState();
    return true;
  }

  /// <summary>
  /// Clears stale fixed-session state after a deliberate source change.
  /// </summary>
  private void SourceSelectionChanged(object? sender, EventArgs eventArgs)
  {
    if (_loadingSettings ||
        !StopPausedMonitoringForReconfiguration("source changed"))
    {
      return;
    }
    _pathIsManual = false;
    _sessionTitleTextBox.Clear();
    _sessionPathTextBox.Clear();
    _transcriptView.ClearSession();
    _loadingSettings = true;
    _followLatestCheckBox.Checked = true;
    _loadingSettings = false;
    SaveControlsToSettings();
    UpdateControlState();
  }

  /// <summary>
  /// Pins the displayed path when auto-follow is off and releases that pin
  /// when auto-follow is enabled.
  /// </summary>
  private void FollowLatestChanged(object? sender, EventArgs eventArgs)
  {
    if (_loadingSettings)
    {
      return;
    }

    _pathIsManual = !_followLatestCheckBox.Checked &&
      !string.IsNullOrWhiteSpace(_sessionPathTextBox.Text);
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
  /// Normalizes, displays, logs, and updates the working fenced-type CSV.
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
  /// Handles a role-profile change and marks the working settings dirty.
  /// </summary>
  private void VoiceRowChanged(SpeechRole role, bool context)
  {
    if (_loadingSettings)
    {
      return;
    }
    VoiceRowControls row = _voiceRows[role];
    UpdateVoiceRowState(role);
    row.Voice.Invalidate();
    SaveControlsToSettings();
    ScheduleVoiceSettingsPreview(role, context);
  }

  /// <summary>
  /// Enables both compact profiles only when the row has a spoken voice.
  /// </summary>
  private void UpdateVoiceRowState(SpeechRole role)
  {
    VoiceRowControls row = _voiceRows[role];
    bool enabled = !string.Equals(
      GetVoiceName(row.Voice.SelectedItem),
      SpeechProfileSettings.NotSpoken,
      StringComparison.Ordinal);
    row.MainProfile.Enabled = enabled;
    row.ContextProfile.Enabled = enabled;
  }

  /// <summary>
  /// Refreshes all role rows after speech starts or stops.
  /// </summary>
  private void UpdateAllVoiceRowStates()
  {
    foreach (SpeechRole role in _voiceRows.Keys)
    {
      UpdateVoiceRowState(role);
    }
  }

  /// <summary>
  /// Debounces one Main or Context edit before speaking its test message.
  /// </summary>
  private void ScheduleVoiceSettingsPreview(
    SpeechRole role,
    bool context)
  {
    VoiceRowControls row = _voiceRows[role];
    row.PreviewTimer.Stop();
    row.PreviewContext = context;
    if (_monitor.IsRunning || _playPauseTransitioning)
    {
      return;
    }

    SpeechProfileSettings profile = ReadRoleProfile(role, context).Normalize();
    if (profile.IsSpoken)
    {
      row.PreviewTimer.Start();
    }
  }

  /// <summary>
  /// Returns the user-facing profile name for diagnostics.
  /// </summary>
  private static string GetProfileDisplayName(
    SpeechRole role,
    bool context)
  {
    if (!context)
    {
      return "Main";
    }
    return role == SpeechRole.User ? "Quote" : "Context";
  }

  /// <summary>
  /// Speaks the edited role profile unless monitoring owns speech.
  /// </summary>
  private void PreviewVoiceSettings(SpeechRole role, bool context)
  {
    VoiceRowControls row = _voiceRows[role];
    row.PreviewTimer.Stop();
    if (_monitor.IsRunning || _playPauseTransitioning ||
        (_speech.IsSpeaking && !_voiceSettingPreviewActive))
    {
      return;
    }

    try
    {
      SpeechProfileSettings profile = new SpeechMasterSettings(
        _masterSpeechProfile.Rate,
        _masterSpeechProfile.Pitch,
        _masterSpeechProfile.Volume).Apply(
          ReadRoleProfile(role, context));
      if (!profile.IsSpoken)
      {
        return;
      }

      _voiceSettingPreviewActive = true;
      AppendLog(
        $"Previewing {role} {GetProfileDisplayName(role, context)}: " +
        $"voice={profile.VoiceName}; rate={profile.Rate}; " +
        $"pitch={profile.Pitch}; volume={profile.Volume}%.");
      string message = context
        ? row.ContextPreviewMessage
        : row.MainPreviewMessage;
      _speech.SpeakUntracked(message, profile);
    }
    catch (Exception exception) when (
      exception is ArgumentException or InvalidOperationException)
    {
      _voiceSettingPreviewActive = false;
      AppendLog($"Voice preview failed: {exception.Message}");
    }
  }

  /// <summary>
  /// Starts monitoring, or toggles monitored playback between active and
  /// paused.  Reaching the current live end remains active and waits for more
  /// text until the user pauses it.
  /// </summary>
  private async void PlayPauseButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    string trigger = _pendingPlayPauseTrigger ?? "button";
    _pendingPlayPauseTrigger = null;
    DiagnosticLog.Write("transport.play_pause_requested", new
    {
      trigger,
      monitoring = _monitor.IsRunning,
      speaking = _speech.IsSpeaking,
      paused = _speech.IsPaused
    });
    if (_playPauseTransitioning)
    {
      return;
    }

    if (_monitor.IsRunning)
    {
      PauseToggleResult result = _speech.TogglePause(allowIdlePause: true);
      AppendLog(result switch
      {
        PauseToggleResult.Paused => "Playback paused.",
        PauseToggleResult.Resumed => "Playback resumed.",
        _ => "Playback pause unavailable."
      });
      UpdateControlState();
      return;
    }

    if (_voiceSettingPreviewActive)
    {
      StopVoicePreviewTimers();
      _voiceSettingPreviewActive = false;
      _speech.CancelAll();
    }

    _playPauseTransitioning = true;
    UpdateControlState();
    try
    {
      await StartMonitoringAsync();
    }
    finally
    {
      _playPauseTransitioning = false;
      UpdateControlState();
    }
  }

  /// <summary>
  /// Starts monitoring the selected or latest session.
  /// </summary>
  private async Task StartMonitoringAsync()
  {
    try
    {
      ApplyFenceTypesImmediately();
      if (string.IsNullOrWhiteSpace(_sessionPathTextBox.Text) &&
          !await DetectLatestAsync())
      {
        return;
      }
      string selectedPath = _sessionPathTextBox.Text;
      SpeechHistorySnapshot? preindexedHistory =
        _selectedSessionHistory is not null &&
        PathsReferToSameFile(_selectedSessionHistoryPath, selectedPath)
          ? _selectedSessionHistory
          : null;
      bool reusePausedHistory = preindexedHistory is not null && _speech.HasHistory;
      _reusePausedHistoryOnMonitorStart = reusePausedHistory;
      _suppressMonitorTextUntilHistoryLoaded = false;
      _resumeAfterMonitorHistoryLoaded = !reusePausedHistory;
      if (!reusePausedHistory)
      {
        _speech.BeginLiveSession();
      }
      Interlocked.Increment(ref _monitorSession);
      string? explicitPath = _pathIsManual || !_followLatestCheckBox.Checked
        ? _sessionPathTextBox.Text
        : null;
      _monitor.Start(new MonitorSettings(
        GetSelectedSource(),
        explicitPath,
        _followLatestCheckBox.Checked,
        _speakExistingCheckBox.Checked,
        TimeSpan.FromMilliseconds((double)_pollNumeric.Value),
        preindexedHistory));
      if (reusePausedHistory)
      {
        PauseToggleResult result = _speech.TogglePause(allowIdlePause: true);
        AppendLog(result == PauseToggleResult.Resumed
          ? "Monitoring started; playback resumed from indexed history."
          : "Monitoring started, but indexed playback could not resume.");
      }
      else
      {
        AppendLog("Monitoring started; existing history is being indexed.");
      }
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException or
      InvalidDataException or InvalidOperationException or ArgumentException)
    {
      AppendLog($"Unable to start: {exception.Message}");
    }
  }

  /// <summary>
  /// Queues the selected AI turn's processing duration in the User voice.
  /// </summary>
  private void ProcessingTimeButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    if (_speech.TryQueueProcessingTimeAnnouncement(
          out string announcement,
          out string unavailableReason))
    {
      AppendLog($"Processing-time announcement queued: {announcement}");
    }
    else
    {
      AppendLog(
        $"Processing-time announcement unavailable: {unavailableReason}.");
    }
    UpdateControlState();
  }

  /// <summary>
  /// Runs one navigation operation.
  /// </summary>
  private void NavigateSpeech(
    TryNavigateSpeech navigate,
    string action,
    string? endMessage = null)
  {
    AppendLog(navigate(out string text)
      ? $"{action}: {text}"
      : endMessage ??
        $"{action}: no matching enabled history entry is available.");
    UpdateControlState();
  }

  /// <summary>
  /// Opens the configurable keyboard-shortcut editor.
  /// </summary>
  private void HotkeysButtonClicked(object? sender, EventArgs eventArgs)
  {
    UserSettings current = _settingsStore.Current;
    using var dialog = new HotkeySettingsDialog(current.Hotkeys, GetSelectedTheme());
    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }
    _settingsStore.Update(current with { Hotkeys = dialog.Settings });
    UpdateSettingsSaveState();
    AppendLog("Hotkeys updated.");
  }

  /// <summary>
  /// Opens the spelling and pronunciation-rule editor.
  /// </summary>
  private void PronunciationsButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    SaveControlsToSettings();
    UserSettings current = _settingsStore.Current;
    using var dialog = new PronunciationDialog(
      current.SpelledWords,
      current.Pronunciations,
      _speech,
      GetIpaPreviewProfile,
      _settingsStore.GetAudioWakeSettings,
      AppendLog,
      GetSelectedTheme());
    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    SpelledWordSet spelled = SpelledWordSet.Parse(dialog.SpelledWordsText);
    PronunciationRuleSet pronunciations = PronunciationRuleSet.Parse(
      dialog.PronunciationsText);
    try
    {
      _settingsStore.Update(_settingsStore.Current with
      {
        SpelledWords = spelled.NormalizedText,
        Pronunciations = pronunciations.NormalizedText
      });
      UpdateSettingsSaveState();
      AppendLog(
        $"Pronunciations updated: {spelled.OrderedWords.Count} spelled; " +
        $"{pronunciations.Rules.Count} IPA rules.");
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException)
    {
      AppendLog($"Settings save failed: {exception.Message}");
    }
  }

  /// <summary>
  /// Opens the Bluetooth wake-tone settings and test dialog.
  /// </summary>
  private void AudioWakeButtonClicked(object? sender, EventArgs eventArgs)
  {
    SaveControlsToSettings();
    using var dialog = new AudioWakeSettingsDialog(
      _settingsStore.Current.AudioWake,
      _speech,
      GetAudioWakeTestProfiles(),
      GetSelectedTheme());
    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    try
    {
      AudioWakeSettings settings = dialog.CurrentSettings.Normalize();
      _settingsStore.Update(_settingsStore.Current with
      {
        AudioWake = settings
      });
      UpdateSettingsSaveState();
      AppendLog(
        settings.Enabled
          ? $"Bluetooth wake enabled: {settings.FrequencyHertz} Hz; " +
            $"quiet={settings.QuietDurationMilliseconds} ms."
          : "Bluetooth wake disabled.");
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException)
    {
      AppendLog($"Settings save failed: {exception.Message}");
    }
  }

  /// <summary>
  /// Captures each content profile for the wake-plus-phrase test.
  /// </summary>
  private IReadOnlyList<AudioWakeTestProfile> GetAudioWakeTestProfiles()
  {
    return new[]
    {
      new AudioWakeTestProfile(
        "AI agent main",
        ReadVoiceProfile(ContentCategory.Assistant)),
      new AudioWakeTestProfile(
        "AI agent context",
        ReadVoiceProfile(ContentCategory.Reasoning)),
      new AudioWakeTestProfile(
        "AI subagent main",
        ReadVoiceProfile(ContentCategory.SubagentAssistant)),
      new AudioWakeTestProfile(
        "AI subagent context",
        ReadVoiceProfile(ContentCategory.SubagentReasoning)),
      new AudioWakeTestProfile(
        "User main",
        ReadVoiceProfile(ContentCategory.User)),
      new AudioWakeTestProfile(
        "User quoted text",
        ReadVoiceProfile(ContentCategory.UserContext))
    };
  }

  /// <summary>
  /// Chooses the first currently spoken profile for IPA previews.
  /// </summary>
  private SpeechProfileSettings GetIpaPreviewProfile()
  {
    foreach (ContentCategory category in new[]
    {
      ContentCategory.Assistant,
      ContentCategory.Reasoning,
      ContentCategory.SubagentAssistant,
      ContentCategory.SubagentReasoning,
      ContentCategory.User,
      ContentCategory.UserContext
    })
    {
      SpeechProfileSettings profile = ReadVoiceProfile(category).Normalize();
      if (profile.IsSpoken)
      {
        return profile;
      }
    }
    return ReadVoiceProfile(ContentCategory.Assistant).Normalize();
  }

  /// <summary>
  /// Lets the user select a fixed session file.
  /// </summary>
  private void BrowseButtonClicked(object? sender, EventArgs eventArgs)
  {
    if (!StopPausedMonitoringForReconfiguration("browse session"))
    {
      return;
    }
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
    if (!StopPausedMonitoringForReconfiguration("detect latest session"))
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
    int session = Volatile.Read(ref _monitorSession);
    PostToUi(() =>
    {
      if (!_monitor.IsRunning || session != Volatile.Read(ref _monitorSession))
      {
        return;
      }
      _suppressMonitorTextUntilHistoryLoaded = false;
      if (_reusePausedHistoryOnMonitorStart)
      {
        DiagnosticLog.Write("monitor.unexpected_duplicate_history", new
        {
          fragmentCount = snapshot.Fragments.Count
        });
        return;
      }

      _speech.LoadHistory(
        snapshot.Fragments,
        snapshot.Completions,
        snapshot.BackgroundWorkEvents,
        snapshot.StartMode);

      if (_pendingMonitorSeekNodeId > 0 &&
          _pendingMonitorSeekWordIndex >= 0)
      {
        if (_speech.TrySeekToTranscriptWord(
              _pendingMonitorSeekNodeId,
              _pendingMonitorSeekWordIndex,
              out string seekText))
        {
          AppendLog($"Restored Find speech position: {seekText}");
        }
        else
        {
          AppendLog("Unable to restore the Find speech position after monitoring started.");
        }
        _pendingMonitorSeekNodeId = 0;
        _pendingMonitorSeekWordIndex = -1;
      }

      if (_resumeAfterMonitorHistoryLoaded)
      {
        _resumeAfterMonitorHistoryLoaded = false;
        PauseToggleResult result = _speech.TogglePause(allowIdlePause: true);
        AppendLog(result == PauseToggleResult.Resumed
          ? "Playback started after history indexing."
          : "Playback could not start after history indexing.");
      }

      AppendLog($"Indexed {snapshot.Fragments.Count} existing fragments.");
      UpdateControlState();
    });
  }

  /// <summary>
  /// Adds a source-provided terminal marker to processing-time history.
  /// </summary>
  private void MonitorTurnCompleted(TurnCompletion completion)
  {
    int session = Volatile.Read(ref _monitorSession);
    PostToUi(() =>
    {
      if (!_monitor.IsRunning || session != Volatile.Read(ref _monitorSession))
      {
        return;
      }
      _speech.RegisterTurnCompletion(completion);
      UpdateControlState();
    });
  }

  /// <summary>
  /// Adds one background-work lifecycle update to clock timing state.
  /// </summary>
  private void MonitorBackgroundWorkChanged(BackgroundWorkEvent workEvent)
  {
    int session = Volatile.Read(ref _monitorSession);
    PostToUi(() =>
    {
      if (!_monitor.IsRunning || session != Volatile.Read(ref _monitorSession))
      {
        return;
      }
      _speech.RegisterBackgroundWorkEvent(workEvent);
      UpdateControlState();
    });
  }

  /// <summary>
  /// Compares two session paths using Windows file-name semantics.
  /// </summary>
  private static bool PathsReferToSameFile(string? left, string? right)
  {
    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
    {
      return false;
    }

    try
    {
      return string.Equals(
        Path.GetFullPath(left),
        Path.GetFullPath(right),
        StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception exception) when (
      exception is ArgumentException or NotSupportedException or
      PathTooLongException)
    {
      return false;
    }
  }

  /// <summary>
  /// Preserves indexed playback when monitoring confirms the same JSONL, or
  /// resets speech state when monitoring switches to a different session.
  /// </summary>
  private void MonitorSessionChanged(LocatedSession session)
  {
    int generation = Interlocked.Increment(ref _monitorSession);
    PostToUi(() =>
    {
      if (!_monitor.IsRunning || generation != Volatile.Read(ref _monitorSession))
      {
        return;
      }
      bool preserveIndexedHistory =
        _selectedSessionHistory is not null &&
        PathsReferToSameFile(_selectedSessionHistoryPath, session.Path) &&
        PathsReferToSameFile(_sessionPathTextBox.Text, session.Path);
      if (preserveIndexedHistory)
      {
        _reusePausedHistoryOnMonitorStart = false;
        DiagnosticLog.Write("monitor.session_history_preserved", new
        {
          session.Path
        });
      }
      else
      {
        _speech.BeginLiveSession();
      }
      SetSessionDisplay(session);
      AppendLog($"Active session: {session.DisplayName}");
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
      if (_suppressMonitorTextUntilHistoryLoaded)
      {
        DiagnosticLog.Write("monitor.history_fragment_suppressed", new
        {
          fragment.NodeId,
          fragment.Category,
          fragment.Text
        });
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
    int session = Volatile.Read(ref _monitorSession);
    string preview = string.Join(
      Environment.NewLine + Environment.NewLine,
      messages.Select((message, index) => $"[{index + 1}] {message}"));
    PostToUi(() =>
    {
      if (_monitor.IsRunning && session == Volatile.Read(ref _monitorSession))
      {
        UpdateAcceptedTextPreview(preview);
      }
    });
  }

  private void PositionTranscriptControls()
  {
    int right = _diagnosticHost.ClientSize.Width - 14;
    _maximizeTranscriptButton.Location = new Point(
      Math.Max(0, right - _maximizeTranscriptButton.Width),
      3);
    _transcriptSettingsButton.Location = new Point(
      Math.Max(0,
        _maximizeTranscriptButton.Left - _transcriptSettingsButton.Width - 3),
      3);
    _transcriptSettingsButton.BringToFront();
    _maximizeTranscriptButton.BringToFront();

    if (!IsHandleCreated ||
        !_diagnosticHost.IsHandleCreated ||
        !_transcriptSettingsButton.IsHandleCreated)
    {
      return;
    }

    Point settingsButtonScreen = _transcriptSettingsButton.PointToScreen(
      Point.Empty);
    Rectangle workArea =
      Screen.FromControl(_transcriptSettingsButton).WorkingArea;
    int x = Math.Clamp(
      _diagnosticHost.PointToScreen(
        new Point(right - _transcriptSettingsPopup.Width, 0)).X,
      workArea.Left,
      Math.Max(workArea.Left, workArea.Right - _transcriptSettingsPopup.Width));
    int preferredY = settingsButtonScreen.Y +
      _transcriptSettingsButton.Height + 2;
    int y = preferredY + _transcriptSettingsPopup.Height <= workArea.Bottom
      ? preferredY
      : settingsButtonScreen.Y - _transcriptSettingsPopup.Height - 2;
    _transcriptSettingsPopup.Location = new Point(
      x,
      Math.Clamp(
        y,
        workArea.Top,
        Math.Max(
          workArea.Top,
          workArea.Bottom - _transcriptSettingsPopup.Height)));
  }

  private void ToggleTranscriptSettingsPopup(bool focusPopup)
  {
    if (_transcriptSettingsHoverController.IsOpen)
    {
      LogTranscriptSettingsCloseRequest(
        returnFocus: false,
        reason: "toggle-transcript-settings-close");
      _transcriptSettingsHoverController.Close(returnFocus: false);
      return;
    }
    _transcriptSettingsHoverController.OpenImmediately(focusPopup);
  }

  private void ShowTranscriptSettingsPopup(bool focusPopup)
  {
    _transcriptSettingsHoverController.OpenImmediately(focusPopup);
  }

  private void HideTranscriptSettingsPopup(
    bool returnFocus,
    string reason)
  {
    LogTranscriptSettingsCloseRequest(returnFocus, reason);
    _transcriptSettingsHoverController.Close(returnFocus);
  }

  private void LogTranscriptSettingsCloseRequest(
    bool returnFocus,
    string reason)
  {
    DiagnosticLog.Write("popup.root_close_request", new
    {
      reason,
      returnFocus,
      controllerOpen = _transcriptSettingsHoverController.IsOpen,
      settingsVisible = _transcriptSettingsPopup.Visible,
      settingsContainsFocus = _transcriptSettingsPopup.ContainsFocus,
      activeForm = Form.ActiveForm?.GetType().FullName,
      mainContainsFocus = ContainsFocus
    });
  }

  private void ShowTranscriptSettingsPopupCore(bool focusPopup)
  {
    PositionTranscriptControls();
    _transcriptSettingsPopup.PrepareForDisplay();
    _transcriptSettingsPopup.ShowAboveOwner(this);
  }

  private void HideTranscriptSettingsPopupCore(bool returnFocus)
  {
    _transcriptSettingsPopup.PrepareForHide();
    _transcriptSettingsPopup.Hide();
    if (returnFocus && _transcriptSettingsButton.CanFocus)
    {
      _transcriptSettingsButton.Focus();
    }
  }

  private void TranscriptSettingsFocusTraversalRequested(
    object? sender,
    FocusTraversalRequestedEventArgs eventArgs)
  {
    HideTranscriptSettingsPopup(
      returnFocus: false,
      reason: eventArgs.Forward
        ? "focus-traversal-requested-forward"
        : "focus-traversal-requested-backward");
    Control target = eventArgs.Forward
      ? _maximizeTranscriptButton
      : _diagnosticTabs;
    target.Focus();
  }

  private void TranscriptFollowSpeechChanged(bool enabled)
  {
    if (_transcriptSettingsPopup.Settings.FollowSpeech == enabled)
    {
      return;
    }
    _transcriptSettingsPopup.SetSettings(
      _transcriptSettingsPopup.Settings with { FollowSpeech = enabled },
      ThemeManager.IsDark(GetSelectedTheme()));
    TranscriptSettingsChanged();
    AppendLog($"Transcript follow mode {(enabled ? "enabled" : "disabled")}.");
  }

  private void TranscriptSettingsChanged()
  {
    bool dark = ThemeManager.IsDark(GetSelectedTheme());
    TranscriptSettings settings = _transcriptSettingsPopup.Settings;
    _transcriptView.ApplySettings(settings, dark);
    _playbackMailbox.SetCapacity(settings.HighlightQueueCapacity);
    if (_appliedTranscriptTrackingMilliseconds !=
        settings.HighlightUpdateMilliseconds)
    {
      _speech.SetWordBoundaryPollMilliseconds(
        settings.HighlightUpdateMilliseconds);
      _appliedTranscriptTrackingMilliseconds =
        settings.HighlightUpdateMilliseconds;
    }
    _transcriptSettingsSaveTimer.Stop();
    _transcriptSettingsSaveTimer.Start();
  }

  /// <summary>
  /// Moves the paused speech marker to the voiced word selected by Find.
  /// </summary>
  private void TranscriptFindSeekRequested(
    object? sender,
    FindSeekRequestedEventArgs eventArgs)
  {
    if (_speech.TrySeekToTranscriptWord(
          eventArgs.NodeId,
          eventArgs.NodeWordIndex,
          out string text))
    {
      _pendingMonitorSeekNodeId = eventArgs.NodeId;
      _pendingMonitorSeekWordIndex = eventArgs.NodeWordIndex;
      AppendLog($"Find moved speech marker: {text}");
    }
    else
    {
      AppendLog("Find match is not in voiced speech history.");
    }
    UpdateControlState();
  }

  /// <summary>
  /// Moves Find and paused speech navigation to the blank transcript-end
  /// position when no later voiced result exists.
  /// </summary>
  private void TranscriptFindSeekEndRequested(object? sender, EventArgs eventArgs)
  {
    _speech.MoveToPausedLiveEnd();
    AppendLog("Find reached the end of voiced transcript content.");
    UpdateControlState();
  }

  private void TranscriptTransportKeyPressed(
    object? sender,
    TransportKeyPressedEventArgs eventArgs)
  {
    Keys keyCode = eventArgs.KeyCode & Keys.KeyCode;
    string shortcut = (eventArgs.KeyCode & Keys.Modifiers) == Keys.Alt
      ? $"Alt+{FormatTransportKey(keyCode)}"
      : FormatTransportKey(keyCode);
    _ = ActivateTransportShortcut(keyCode, shortcut);
  }

  private void SetDiagnosticsMaximized(bool maximized, bool save = true)
  {
    _diagnosticsMaximized = maximized;
    for (int row = 0; row < 7; row++)
    {
      foreach (Control control in _mainLayout.Controls.Cast<Control>()
                 .Where(control => _mainLayout.GetRow(control) == row))
      {
        control.Visible = !maximized;
      }
    }
    _mainLayout.Padding = maximized ? Padding.Empty : new Padding(10);
    _maximizeTranscriptButton.Drawing = maximized
      ? GlyphButtonDrawing.Restore
      : GlyphButtonDrawing.Expand;
    _maximizeTranscriptButton.AccessibleName = maximized
      ? "Restore transcript tabs"
      : "Maximize transcript tabs";
    PositionTranscriptControls();
    if (save && !_loadingSettings)
    {
      SaveControlsToSettings();
    }
  }

  /// <summary>
  /// Uses the longer accepted-text title only while that tab is selected.
  /// </summary>
  private void UpdateDiagnosticTabTitles()
  {
    _transcriptTab.Text = "Transcript";
    _activityTab.Text = "Activity";
    _acceptedTextTab.Text = ReferenceEquals(
      _diagnosticTabs.SelectedTab,
      _acceptedTextTab)
        ? "Recent Accepted JSONL"
        : "Accepted Text";
  }

  /// <summary>
  /// Replaces the accepted-text preview without forcing a manually scrolled
  /// view back to the top.  A view already following the end stays at the end.
  /// </summary>
  private void UpdateAcceptedTextPreview(string preview)
  {
    bool followNewest = IsFollowingTextEnd(_previewTextBox);
    int firstVisibleLine = GetFirstVisibleLine(_previewTextBox);
    int selectionStart = _previewTextBox.SelectionStart;
    int selectionLength = _previewTextBox.SelectionLength;

    _previewTextBox.Text = preview;
    if (followNewest)
    {
      _previewTextBox.SelectionStart = _previewTextBox.TextLength;
      _previewTextBox.SelectionLength = 0;
      _previewTextBox.ScrollToCaret();
      return;
    }

    _previewTextBox.SelectionStart = Math.Min(
      selectionStart,
      _previewTextBox.TextLength);
    _previewTextBox.SelectionLength = Math.Min(
      selectionLength,
      _previewTextBox.TextLength - _previewTextBox.SelectionStart);
    ScrollTextBoxToLine(_previewTextBox, firstVisibleLine);
  }

  /// <summary>
  /// Returns whether a multiline edit control is currently showing its final
  /// display line.
  /// </summary>
  private static bool IsFollowingTextEnd(TextBox textBox)
  {
    if (!textBox.IsHandleCreated || textBox.TextLength == 0)
    {
      return true;
    }

    int firstVisible = GetFirstVisibleLine(textBox);
    int displayLines = Math.Max(
      1,
      textBox.ClientSize.Height / Math.Max(1, textBox.Font.Height));
    int lineCount = SendMessage(
      textBox.Handle,
      EmGetLineCount,
      IntPtr.Zero,
      IntPtr.Zero).ToInt32();
    return firstVisible + displayLines >= Math.Max(0, lineCount - 1);
  }

  /// <summary>
  /// Gets the first visible display line of a multiline edit control.
  /// </summary>
  private static int GetFirstVisibleLine(TextBox textBox)
  {
    return !textBox.IsHandleCreated
      ? 0
      : SendMessage(
          textBox.Handle,
          EmGetFirstVisibleLine,
          IntPtr.Zero,
          IntPtr.Zero).ToInt32();
  }

  /// <summary>
  /// Restores one multiline edit control to a requested visible line.
  /// </summary>
  private static void ScrollTextBoxToLine(TextBox textBox, int line)
  {
    if (!textBox.IsHandleCreated)
    {
      return;
    }

    int current = GetFirstVisibleLine(textBox);
    int delta = Math.Max(0, line) - current;
    if (delta != 0)
    {
      _ = SendMessage(
        textBox.Handle,
        EmLineScroll,
        IntPtr.Zero,
        new IntPtr(delta));
    }
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
      MasterSpeech = new SpeechMasterSettings(
        _masterSpeechProfile.Rate,
        _masterSpeechProfile.Pitch,
        _masterSpeechProfile.Volume),
      Assistant = ReadVoiceProfile(ContentCategory.Assistant),
      Reasoning = ReadVoiceProfile(ContentCategory.Reasoning),
      SubagentAssistant = ReadVoiceProfile(
        ContentCategory.SubagentAssistant),
      SubagentReasoning = ReadVoiceProfile(
        ContentCategory.SubagentReasoning),
      User = ReadVoiceProfile(ContentCategory.User),
      UserContext = ReadVoiceProfile(ContentCategory.UserContext),
      SpokenFencedCodeTypes = FencedCodeTypeSet
        .Parse(_fenceTypesTextBox.Text)
        .NormalizedCsv,
      SpeakLastExistingEnabledMessage = _speakExistingCheckBox.Checked,
      KeepDisplayOnWhileSpeaking = _keepDisplayOnCheckBox.Checked,
      PollIntervalMilliseconds = Decimal.ToInt32(_pollNumeric.Value),
      Theme = GetSelectedTheme(),
      Transcript = _transcriptSettingsPopup.Settings with
      {
        Maximized = _diagnosticsMaximized
      },
      WindowX = includeWindowPlacement ? bounds.X : current.WindowX,
      WindowY = includeWindowPlacement ? bounds.Y : current.WindowY,
      WindowWidth = includeWindowPlacement ? bounds.Width : current.WindowWidth,
      WindowHeight = includeWindowPlacement ? bounds.Height : current.WindowHeight,
      HasWindowPlacement = includeWindowPlacement || current.HasWindowPlacement
    };
    try
    {
      _settingsStore.Update(settings);
      UpdateSettingsSaveState();
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException)
    {
      AppendLog($"Settings save failed: {exception.Message}");
    }
  }

  /// <summary>
  /// Opens the shared selector for saving changes or resetting defaults.
  /// </summary>
  private void ShowSettingsSelectionDialog(SettingsSelectionMode mode)
  {
    _transcriptSettingsSaveTimer.Stop();
    _fenceDebounceTimer.Stop();
    ApplyFenceTypesImmediately();
    SaveControlsToSettings(includeWindowPlacement: true);

    UserSettings comparison = mode == SettingsSelectionMode.Save
      ? _settingsStore.Saved
      : _settingsStore.Defaults;
    IReadOnlyList<SettingsChangeSet.Change> changes =
      SettingsChangeSet.GetChanges(comparison, _settingsStore.Current);
    if (changes.Count == 0)
    {
      UpdateSettingsSaveState();
      return;
    }

    using var dialog = new SettingsSelectionDialog(
      changes,
      GetSelectedTheme(),
      mode);
    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    if (mode == SettingsSelectionMode.Save)
    {
      CommitSelectedSettings(dialog.SelectedKeys, discardUnselected: false);
      return;
    }

    UserSettings working = _settingsStore.Current;
    UserSettings reset = SettingsChangeSet.MergeSelected(
      working,
      _settingsStore.Defaults,
      dialog.SelectedKeys);
    _settingsStore.Update(reset);
    LoadSettingsIntoControls(_settingsStore.Current);
    UpdateSettingsSaveState();
    AppendLog($"Reset {dialog.SelectedKeys.Count} setting(s) to defaults.");
  }

  /// <summary>
  /// Reads one profile from its table row.
  /// </summary>
  private SpeechProfileSettings ReadVoiceProfile(ContentCategory category)
  {
    return ReadRoleProfile(
      GetSpeechRole(category),
      IsContextCategory(category));
  }

  /// <summary>
  /// Reads one Main or Context profile from a shared voice row.
  /// </summary>
  private SpeechProfileSettings ReadRoleProfile(
    SpeechRole role,
    bool context)
  {
    VoiceRowControls row = _voiceRows[role];
    SpeechProfileCompactControl profile = context
      ? row.ContextProfile
      : row.MainProfile;
    return new SpeechProfileSettings(
      GetVoiceName(row.Voice.SelectedItem),
      profile.Rate,
      profile.Pitch)
    {
      Volume = profile.Volume
    };
  }

  /// <summary>
  /// Maps one fragment category to its shared voice row.
  /// </summary>
  private static SpeechRole GetSpeechRole(ContentCategory category)
  {
    return category switch
    {
      ContentCategory.Assistant or ContentCategory.Reasoning =>
        SpeechRole.Agent,
      ContentCategory.SubagentAssistant or
        ContentCategory.SubagentReasoning => SpeechRole.Subagent,
      ContentCategory.User or ContentCategory.UserContext => SpeechRole.User,
      _ => throw new ArgumentOutOfRangeException(
        nameof(category),
        category,
        null)
    };
  }

  /// <summary>
  /// Returns whether one category uses the Context profile.
  /// </summary>
  private static bool IsContextCategory(ContentCategory category)
  {
    return category is ContentCategory.Reasoning or
      ContentCategory.SubagentReasoning or ContentCategory.UserContext;
  }

  /// <summary>
  /// Routes a transport key raised while a profile editor owns focus.
  /// </summary>
  private void ProfileTransportKeyPressed(
    object? sender,
    TransportKeyPressedEventArgs eventArgs)
  {
    string shortcut = FormatTransportKey(eventArgs.KeyCode);
    _ = ActivateTransportShortcut(eventArgs.KeyCode, shortcut);
  }

  /// <summary>
  /// Continues the speech matrix's explicit row-wise tab order.
  /// </summary>
  private void ProfileFocusTraversalRequested(
    object? sender,
    FocusTraversalRequestedEventArgs eventArgs)
  {
    if (sender is not SpeechProfileCompactControl profile ||
        !TryLocateProfileControl(profile, out SpeechRole role, out bool context))
    {
      return;
    }

    Control target = GetProfileTraversalTarget(
      role,
      context,
      eventArgs.Forward);
    BeginInvoke(new Action(() =>
    {
      profile.CompleteFocusTraversal();
      if (IsDisposed || target.IsDisposed || !target.CanFocus)
      {
        return;
      }

      target.Focus();
    }));
  }

  private bool TryLocateProfileControl(
    SpeechProfileCompactControl profile,
    out SpeechRole role,
    out bool context)
  {
    foreach ((SpeechRole candidateRole, VoiceRowControls row) in _voiceRows)
    {
      if (ReferenceEquals(profile, row.MainProfile))
      {
        role = candidateRole;
        context = false;
        return true;
      }
      if (ReferenceEquals(profile, row.ContextProfile))
      {
        role = candidateRole;
        context = true;
        return true;
      }
    }

    role = default;
    context = false;
    return false;
  }

  private Control GetProfileTraversalTarget(
    SpeechRole role,
    bool context,
    bool forward)
  {
    VoiceRowControls row = _voiceRows[role];
    if (!forward)
    {
      return context ? row.MainProfile : row.Voice;
    }
    if (!context)
    {
      return row.ContextProfile;
    }

    return role switch
    {
      SpeechRole.Agent => _voiceRows[SpeechRole.Subagent].Voice,
      SpeechRole.Subagent => _voiceRows[SpeechRole.User].Voice,
      _ => _fenceTypesTextBox
    };
  }

  private SpeechProfileCompactControl? GetOpenProfileControl()
  {
    return GetProfileControls().FirstOrDefault(
      profile => profile.IsEditorVisible);
  }

  private IEnumerable<SpeechProfileCompactControl> GetProfileControls()
  {
    yield return _masterSpeechProfile;
    foreach (VoiceRowControls row in _voiceRows.Values)
    {
      yield return row.MainProfile;
      yield return row.ContextProfile;
    }
  }

  /// <summary>
  /// Handles transport hotkeys before focused child windows consume them.
  /// </summary>
  public bool PreFilterMessage(ref Message message)
  {
    if (message.Msg is WmLButtonDown or WmRButtonDown or
        WmMButtonDown or WmXButtonDown or
        WmNcLButtonDown or WmNcRButtonDown or
        WmNcMButtonDown or WmNcXButtonDown)
    {
      HoverPopupController.HandleGlobalPointerDown(
        Control.FromChildHandle(message.HWnd));
      return false;
    }

    if (GetAncestor(message.HWnd, GaRoot) != Handle)
    {
      return false;
    }

    if (message.Msg is not (WmKeyDown or WmSystemKeyDown))
    {
      return false;
    }

    Keys keyCode = (Keys)(int)message.WParam & Keys.KeyCode;
    Keys modifiers = Control.ModifierKeys & Keys.Modifiers;
    if (keyCode == Keys.F && modifiers == Keys.Control)
    {
      _transcriptView.OpenFind();
      return true;
    }
    bool hasAltOnly = modifiers == Keys.Alt;
    bool hasNoModifiers = modifiers == Keys.None;
    if (!hasAltOnly && !hasNoModifiers)
    {
      return false;
    }
    if (hasNoModifiers && IsTransportShortcutBlockedByFocusedControl())
    {
      return false;
    }

    string shortcut = hasAltOnly
      ? $"Alt+{FormatTransportKey(keyCode)}"
      : FormatTransportKey(keyCode);
    return ActivateTransportShortcut(keyCode, shortcut);
  }

  /// <summary>
  /// Handles profile-editor dismissal and application transport shortcuts.
  /// </summary>
  protected override bool ProcessCmdKey(ref Message message, Keys keyData)
  {
    if (keyData == (Keys.Control | Keys.F))
    {
      _transcriptView.OpenFind();
      return true;
    }
    if (HoverPopupController.HandleGlobalDismissKey(keyData))
    {
      return true;
    }

    Keys modifiers = keyData & Keys.Modifiers;
    Keys keyCode = keyData & Keys.KeyCode;
    bool logTabTraversal = keyCode == Keys.Tab &&
      (modifiers == Keys.None || modifiers == Keys.Shift);
    MainTabSnapshot? tabBefore = logTabTraversal
      ? CaptureMainTabSnapshot()
      : null;
    if (tabBefore is not null)
    {
      DiagnosticLog.Write("main.tab_before", new
      {
        direction = modifiers == Keys.Shift ? "backward" : "forward",
        keyData = keyData.ToString(),
        state = tabBefore,
        tabStops = DescribeMainTabStops()
      });
    }

    bool hasAltOnly = modifiers == Keys.Alt;
    bool hasNoModifiers = modifiers == Keys.None;
    if (!hasAltOnly && !hasNoModifiers)
    {
      bool handled = base.ProcessCmdKey(ref message, keyData);
      CompleteMainTabDiagnostics(keyData, tabBefore, handled);
      return handled;
    }
    if (hasNoModifiers && IsTransportShortcutBlockedByFocusedControl())
    {
      bool handled = base.ProcessCmdKey(ref message, keyData);
      CompleteMainTabDiagnostics(keyData, tabBefore, handled);
      return handled;
    }

    string shortcut = hasAltOnly
      ? $"Alt+{FormatTransportKey(keyCode)}"
      : FormatTransportKey(keyCode);
    bool result = ActivateTransportShortcut(keyCode, shortcut) ||
      base.ProcessCmdKey(ref message, keyData);
    CompleteMainTabDiagnostics(keyData, tabBefore, result);
    return result;
  }

  private sealed record MainTabSnapshot(
    object? FocusedControl,
    object? ActiveControl,
    bool MainFormContainsFocus,
    string? ManagedActiveForm,
    string Foreground,
    string ActiveWindow,
    int MainFormZOrderRank,
    Point CursorPosition,
    string CursorMonitor,
    Rectangle CursorMonitorBounds,
    Rectangle CursorMonitorWorkingArea,
    object WindowUnderCursor);

  private void CompleteMainTabDiagnostics(
    Keys keyData,
    MainTabSnapshot? before,
    bool handled)
  {
    if (before is null)
    {
      return;
    }

    string direction = (keyData & Keys.Shift) != 0
      ? "backward"
      : "forward";
    MainTabSnapshot immediate = CaptureMainTabSnapshot();
    DiagnosticLog.Write("main.tab_after", new
    {
      direction,
      keyData = keyData.ToString(),
      handled,
      before,
      after = immediate,
      cursorMoved = before.CursorPosition != immediate.CursorPosition,
      foregroundChanged = before.Foreground != immediate.Foreground,
      focusLeftMainForm = !immediate.MainFormContainsFocus
    });

    if (!immediate.MainFormContainsFocus ||
        immediate.FocusedControl is null)
    {
      DiagnosticLog.Write("main.tab_boundary_observed", new
      {
        direction,
        handled,
        before,
        after = immediate,
        tabStops = DescribeMainTabStops()
      });
    }

    if (!IsHandleCreated || IsDisposed)
    {
      return;
    }

    try
    {
      BeginInvoke((Action)(() =>
      {
        MainTabSnapshot settled = CaptureMainTabSnapshot();
        DiagnosticLog.Write("main.tab_settled", new
        {
          direction,
          keyData = keyData.ToString(),
          handled,
          before,
          immediate,
          settled,
          cursorMovedFromBefore =
            before.CursorPosition != settled.CursorPosition,
          cursorMovedAfterImmediate =
            immediate.CursorPosition != settled.CursorPosition,
          foregroundChangedFromBefore = before.Foreground != settled.Foreground,
          foregroundChangedAfterImmediate =
            immediate.Foreground != settled.Foreground,
          focusLeftMainForm = !settled.MainFormContainsFocus
        });
      }));
    }
    catch (InvalidOperationException exception)
    {
      DiagnosticLog.WriteException(
        "main.tab_settled_logging_failed",
        exception,
        direction,
        isTerminating: false);
    }
  }

  private MainTabSnapshot CaptureMainTabSnapshot()
  {
    Control? focused = FindFocusedDescendant(this);
    Control? active = GetDeepestActiveControl(this);
    Point cursor = Cursor.Position;
    Screen screen = Screen.FromPoint(cursor);
    IntPtr foreground = GetForegroundWindowForTabDiagnostics();
    IntPtr activeWindow = GetActiveWindowForTabDiagnostics();
    IntPtr underCursor = GetAncestor(
      WindowFromPointForTabDiagnostics(new NativePoint(cursor)),
      GaRoot);

    return new MainTabSnapshot(
      DescribeControlForTabDiagnostics(focused),
      DescribeControlForTabDiagnostics(active),
      ContainsFocus,
      Form.ActiveForm?.GetType().FullName,
      DescribeNativeWindowForTabDiagnostics(foreground),
      DescribeNativeWindowForTabDiagnostics(activeWindow),
      GetTopLevelZOrderRankForTabDiagnostics(
        IsHandleCreated ? Handle : IntPtr.Zero),
      cursor,
      screen.DeviceName,
      screen.Bounds,
      screen.WorkingArea,
      DescribeNativeWindowObjectForTabDiagnostics(underCursor));
  }

  private object[] DescribeMainTabStops()
  {
    var controls = new List<object>();
    CollectTabStops(this, GetType().Name, controls);
    return controls.ToArray();
  }

  private static void CollectTabStops(
    Control parent,
    string parentPath,
    List<object> destination)
  {
    Control[] children = parent.Controls.Cast<Control>()
      .OrderBy(control => control.TabIndex)
      .ThenBy(control => control.Name, StringComparer.Ordinal)
      .ToArray();
    foreach (Control child in children)
    {
      string segment = string.IsNullOrWhiteSpace(child.Name)
        ? child.GetType().Name
        : child.Name;
      string path = $"{parentPath}/{segment}";
      if (child.TabStop && child.Visible && child.Enabled && child.CanSelect)
      {
        destination.Add(new
        {
          path,
          type = child.GetType().FullName,
          child.Name,
          text = TruncateTabDiagnosticText(child.Text),
          child.TabIndex,
          child.TabStop,
          child.Enabled,
          child.Visible,
          child.CanSelect,
          child.Focused,
          child.ContainsFocus,
          bounds = GetScreenBoundsForTabDiagnostics(child)
        });
      }
      if (child.HasChildren && child.Visible && child.Enabled)
      {
        CollectTabStops(child, path, destination);
      }
    }
  }

  private static Control? FindFocusedDescendant(Control parent)
  {
    if (parent.Focused)
    {
      return parent;
    }
    foreach (Control child in parent.Controls)
    {
      if (!child.ContainsFocus && !child.Focused)
      {
        continue;
      }
      return FindFocusedDescendant(child) ?? child;
    }
    return null;
  }

  private static Control? GetDeepestActiveControl(ContainerControl container)
  {
    Control? current = container.ActiveControl;
    while (current is ContainerControl nested && nested.ActiveControl is not null)
    {
      current = nested.ActiveControl;
    }
    return current;
  }

  private static object? DescribeControlForTabDiagnostics(Control? control)
  {
    if (control is null)
    {
      return null;
    }
    return new
    {
      type = control.GetType().FullName,
      control.Name,
      text = TruncateTabDiagnosticText(control.Text),
      control.TabIndex,
      control.TabStop,
      control.Enabled,
      control.Visible,
      control.CanSelect,
      control.Focused,
      control.ContainsFocus,
      bounds = GetScreenBoundsForTabDiagnostics(control),
      path = GetControlPathForTabDiagnostics(control)
    };
  }

  private static string GetControlPathForTabDiagnostics(Control control)
  {
    var parts = new Stack<string>();
    for (Control? current = control; current is not null; current = current.Parent)
    {
      parts.Push(string.IsNullOrWhiteSpace(current.Name)
        ? current.GetType().Name
        : current.Name);
    }
    return string.Join("/", parts);
  }

  private static Rectangle GetScreenBoundsForTabDiagnostics(Control control)
  {
    try
    {
      return control.RectangleToScreen(control.ClientRectangle);
    }
    catch (InvalidOperationException)
    {
      return Rectangle.Empty;
    }
  }

  private static string TruncateTabDiagnosticText(string? text)
  {
    const int MaximumLength = 80;
    if (string.IsNullOrEmpty(text) || text.Length <= MaximumLength)
    {
      return text ?? string.Empty;
    }
    return text[..MaximumLength];
  }

  private static string DescribeNativeWindowForTabDiagnostics(IntPtr window)
  {
    object description = DescribeNativeWindowObjectForTabDiagnostics(window);
    return JsonSerializer.Serialize(description);
  }

  private static object DescribeNativeWindowObjectForTabDiagnostics(IntPtr window)
  {
    if (window == IntPtr.Zero)
    {
      return new { hwnd = "0x0" };
    }
    _ = GetWindowThreadProcessIdForTabDiagnostics(window, out uint processId);
    var text = new StringBuilder(256);
    _ = GetWindowTextForTabDiagnostics(window, text, text.Capacity);
    var className = new StringBuilder(256);
    _ = GetClassNameForTabDiagnostics(window, className, className.Capacity);
    return new
    {
      hwnd = $"0x{window.ToInt64():X}",
      processId,
      belongsToCurrentProcess = processId == (uint)Environment.ProcessId,
      className = className.ToString(),
      text = text.ToString()
    };
  }

  private static int GetTopLevelZOrderRankForTabDiagnostics(IntPtr target)
  {
    if (target == IntPtr.Zero)
    {
      return -1;
    }
    IntPtr current = GetTopWindowForTabDiagnostics(IntPtr.Zero);
    for (int rank = 0; current != IntPtr.Zero && rank < 512; rank++)
    {
      if (current == target)
      {
        return rank;
      }
      current = GetWindowForTabDiagnostics(current, GwHwndNext);
    }
    return -1;
  }

  /// <summary>
  /// Activates one transport command from the form or profile editor.
  /// </summary>
  private bool ActivateTransportShortcut(Keys keyCode, string shortcut)
  {
    if (keyCode == Keys.Oemplus)
    {
      _transcriptSettingsPopup.SetSettings(
        _transcriptSettingsPopup.Settings with
        {
          FollowSpeech = !_transcriptSettingsPopup.Settings.FollowSpeech
        },
        ThemeManager.IsDark(GetSelectedTheme()));
      TranscriptSettingsChanged();
      AppendLog($"Transcript follow mode {(_transcriptSettingsPopup.Settings.FollowSpeech ? "enabled" : "disabled")}.");
      return true;
    }

    HotkeyAction action = _settingsStore.Current.Hotkeys.GetAction(keyCode);
    if (action == HotkeyAction.None)
    {
      return false;
    }

    if (action == HotkeyAction.ToggleTranscriptSize)
    {
      SetDiagnosticsMaximized(!_diagnosticsMaximized);
      return true;
    }
    Button? button = action switch
    {
      HotkeyAction.PreviousSpeaker => _rewindSpeakerButton,
      HotkeyAction.PreviousNode => _rewindNodeButton,
      HotkeyAction.PreviousSentence => _rewindSentenceButton,
      HotkeyAction.PlayPause => _playPauseButton,
      HotkeyAction.NextSentence => _forwardSentenceButton,
      HotkeyAction.NextNode => _forwardNodeButton,
      HotkeyAction.NextSpeaker => _forwardSpeakerButton,
      HotkeyAction.ProcessingTime => _processingTimeButton,
      _ => null
    };
    if (button is null)
    {
      return false;
    }
    if (!button.Enabled)
    {
      return true;
    }
    if (button.CanFocus)
    {
      button.Focus();
    }

    switch (action)
    {
      case HotkeyAction.PreviousSpeaker:
        NavigateSpeech(_speech.TryRewindSpeaker, "Previous speaker turn");
        break;
      case HotkeyAction.PreviousNode:
        NavigateSpeech(_speech.TryRewindNode, "Previous JSONL node");
        break;
      case HotkeyAction.PreviousSentence:
        NavigateSpeech(_speech.TryRewindSentence, "Previous sentence/code line");
        break;
      case HotkeyAction.PlayPause:
        _pendingPlayPauseTrigger = $"keyboard:{shortcut}";
        PlayPauseButtonClicked(button, EventArgs.Empty);
        break;
      case HotkeyAction.NextSentence:
        NavigateSpeech(_speech.TryForwardSentence, "Next sentence/code line", "Past end of last sentence/code line.");
        break;
      case HotkeyAction.NextNode:
        NavigateSpeech(_speech.TryForwardNode, "Next JSONL node", "Past end of last JSONL node.");
        break;
      case HotkeyAction.NextSpeaker:
        NavigateSpeech(_speech.TryForwardSpeaker, "Next speaker turn", "Past end of last speaker turn.");
        break;
      case HotkeyAction.ProcessingTime:
        ProcessingTimeButtonClicked(button, EventArgs.Empty);
        break;
    }
    return true;
  }

  /// <summary>
  /// Formats punctuation transport keys for logs.
  /// </summary>
  private static string FormatTransportKey(Keys keyCode)
  {
    return keyCode switch
    {
      Keys.OemSemicolon => ";",
      Keys.OemQuotes => "'",
      Keys.Oemplus => "=",
      _ => keyCode.ToString()
    };
  }

  /// <summary>
  /// Returns whether a text-entry control should retain one bare shortcut.
  /// </summary>
  private bool IsTransportShortcutBlockedByFocusedControl()
  {
    Control? focused = this;
    while (focused is ContainerControl container &&
           container.ActiveControl is Control active)
    {
      focused = active;
    }

    var ancestry = new List<Control>();
    for (Control? control = focused; control is not null;
         control = control.Parent)
    {
      ancestry.Add(control);
    }

    if (ancestry.Any(control => control is UpDownBase))
    {
      return false;
    }

    foreach (Control control in ancestry)
    {
      if (control is TextBoxBase textBox && !textBox.ReadOnly)
      {
        return true;
      }
      if (control is ComboBox comboBox &&
          comboBox.DropDownStyle != ComboBoxStyle.DropDownList)
      {
        return true;
      }
    }
    return false;
  }

  /// <summary>
  /// Cancels all pending automatic voice-setting previews.
  /// </summary>
  private void StopVoicePreviewTimers()
  {
    foreach (VoiceRowControls row in _voiceRows.Values)
    {
      row.PreviewTimer.Stop();
    }
  }

  /// <summary>
  /// Keeps the display awake only while enabled speech is actively playing.
  /// </summary>
  private void UpdateDisplayAwakeState()
  {
    bool shouldKeepAwake = _keepDisplayOnCheckBox.Checked &&
      _speech.IsSpeaking &&
      !_speech.IsPaused;
    _displayAwake.SetActive(shouldKeepAwake);
  }

  /// <summary>
  /// Updates controls that depend on monitoring/history state.
  /// </summary>
  private void UpdateControlState()
  {
    bool running = _monitor.IsRunning;
    bool paused = _speech.IsPaused;
    bool configurationLocked = running && !paused;
    bool playbackOwned = (running || _speech.IsSpeaking) && !paused;
    bool hasHistory = _speech.HasHistory;
    if (playbackOwned && !_voiceSettingPreviewActive)
    {
      StopVoicePreviewTimers();
    }
    _sourceComboBox.Enabled = !configurationLocked;
    _detectLatestButton.Enabled = !configurationLocked;
    _browseButton.Enabled = !configurationLocked;
    _followLatestCheckBox.AutoCheck = !configurationLocked;
    _followLatestCheckBox.Enabled = !configurationLocked;
    _pollNumeric.Enabled = !configurationLocked;
    _speakExistingCheckBox.Enabled = !configurationLocked;
    _playPauseButton.Enabled = !_playPauseTransitioning;
    _pronunciationsButton.Enabled = true;
    UpdatePlayPauseButton(running);
    _processingTimeButton.Enabled =
      _speech.CanRequestProcessingTimeAnnouncement &&
      !_speech.IsProcessingTimeAnnouncementPending;
    _rewindSpeakerButton.Enabled = hasHistory;
    _rewindSentenceButton.Enabled = hasHistory;
    _forwardSentenceButton.Enabled = hasHistory;
    _rewindNodeButton.Enabled = hasHistory;
    _forwardNodeButton.Enabled = hasHistory;
    _forwardSpeakerButton.Enabled = hasHistory;
    UpdateSettingsSaveState();
  }

  /// <summary>
  /// Shows Play while stopped or paused, and Pause while monitored playback is
  /// active or waiting at the current live end.
  /// </summary>
  private void UpdatePlayPauseButton(bool running)
  {
    bool showPause = running && !_speech.IsPaused;
    _playPauseButton.Drawing = showPause
      ? GlyphButtonDrawing.Pause
      : GlyphButtonDrawing.Play;
    string description = showPause
      ? "Pause monitored playback (K or Alt+K)"
      : running
        ? "Resume monitored playback (K or Alt+K)"
        : "Start monitoring (K or Alt+K)";
    _playPauseButton.AccessibleName = description;
    _toolTip.SetToolTip(_playPauseButton, description);
  }

  /// <summary>
  /// Applies and saves a deliberate theme selection.
  /// </summary>
  private void ThemeSelectionChanged(object? sender, EventArgs eventArgs)
  {
    DiagnosticLog.Write("theme.selection_changed", new
    {
      loadingSettings = _loadingSettings,
      droppedDown = _themeComboBox.DroppedDown,
      selectedTheme = GetSelectedTheme().ToString(),
      pending = _themeApplicationPending,
      applyDepth = _themeApplyDepth
    });

    if (_loadingSettings)
    {
      return;
    }

    SaveControlsToSettings();
    if (_themeComboBox.DroppedDown)
    {
      _themeApplicationPending = true;
      DiagnosticLog.Write("theme.application_deferred", new
      {
        reason = "combo-dropdown-open",
        selectedTheme = GetSelectedTheme().ToString(),
        applyDepth = _themeApplyDepth
      });
      return;
    }

    QueueThemeApplication(GetSelectedTheme(), "selection-changed");
  }

  /// <summary>
  /// Applies a theme selection after the native ComboBox dropdown has closed.
  /// </summary>
  private void ThemeDropDownClosed(object? sender, EventArgs eventArgs)
  {
    DiagnosticLog.Write("theme.dropdown_closed", new
    {
      pending = _themeApplicationPending,
      isDisposed = IsDisposed,
      disposing = Disposing,
      selectedTheme = GetSelectedTheme().ToString(),
      applyDepth = _themeApplyDepth
    });

    if (!_themeApplicationPending || IsDisposed || Disposing)
    {
      return;
    }

    _themeApplicationPending = false;
    DiagnosticLog.Write("theme.begininvoke_queued", new
    {
      selectedTheme = GetSelectedTheme().ToString(),
      applyDepth = _themeApplyDepth
    });
    BeginInvoke(new Action(() =>
    {
      DiagnosticLog.Write("theme.begininvoke_executing", new
      {
        isDisposed = IsDisposed,
        disposing = Disposing,
        selectedTheme = GetSelectedTheme().ToString(),
        applyDepth = _themeApplyDepth
      });
      if (!IsDisposed && !Disposing)
      {
        QueueThemeApplication(GetSelectedTheme(), "dropdown-closed");
      }
    }));
  }

  /// <summary>
  /// Reapplies System theme when Windows changes its app-theme preference.
  /// </summary>
  private void WindowsUserPreferenceChanged(
    object? sender,
    UserPreferenceChangedEventArgs eventArgs)
  {
    PostToUi(() =>
    {
      if (GetSelectedTheme() == AppTheme.System)
      {
        QueueThemeApplication(AppTheme.System, "windows-preference-changed");
      }
    });
  }

  /// <summary>
  /// Queues a serialized theme transition after the current WinForms/native
  /// message callback has unwound.  Repeated requests are coalesced to the
  /// latest requested theme.
  /// </summary>
  private void QueueThemeApplication(AppTheme theme, string reason)
  {
    _pendingThemeRequest = theme;
    _pendingThemeReason = reason;

    DiagnosticLog.Write("theme.transition_requested", new
    {
      theme = theme.ToString(),
      reason,
      queued = _themeTransitionQueued,
      active = _themeTransitionActive,
      applyDepth = _themeApplyDepth
    });

    if (_themeTransitionQueued || _themeTransitionActive || IsDisposed || Disposing)
    {
      return;
    }

    _themeTransitionQueued = true;
    BeginInvoke(new Action(ProcessQueuedThemeTransition));
  }

  /// <summary>
  /// Executes the latest queued theme request as one redraw-suppressed
  /// transaction, then synchronously repaints the resulting HWND hierarchy.
  /// </summary>
  private async void ProcessQueuedThemeTransition()
  {
    _themeTransitionQueued = false;
    if (IsDisposed || Disposing || _themeTransitionActive ||
        _pendingThemeRequest is not AppTheme requestedTheme)
    {
      return;
    }

    string reason = _pendingThemeReason ?? "unknown";
    _pendingThemeRequest = null;
    _pendingThemeReason = null;
    _themeTransitionActive = true;

    bool redrawAvailable = IsHandleCreated;
    IntPtr redrawHandle = redrawAvailable ? Handle : IntPtr.Zero;
    Control? focusedBefore = FindFocusedDescendant(this);
    bool mainContainedFocusBefore = ContainsFocus;
    double opacityBefore = Opacity;
    const double TransitionOpacity = 1.0 / 255.0;
    bool opacityLowered = false;
    ThemeTransitionSnapshotForm? transitionSnapshot = null;
    Bitmap? webViewSnapshot = null;
    Rectangle webViewSnapshotScreenBounds = Rectangle.Empty;
    var redrawControls = new List<Control>();
    var suppressedRedrawHandles = new Dictionary<Control, IntPtr>();
    EventHandler recreatedHandleSuppressor = (sender, eventArgs) =>
    {
      if (!_themeTransitionActive ||
          sender is not Control control ||
          !suppressedRedrawHandles.ContainsKey(control) ||
          control.IsDisposed ||
          control.Disposing ||
          !control.IsHandleCreated)
      {
        return;
      }

      IntPtr handle = control.Handle;
      if (!control.Visible || !IsWindowVisible(handle))
      {
        suppressedRedrawHandles.Remove(control);
        DiagnosticLog.Write("theme.child_redraw_recreate_not_suppressed", new
        {
          theme = requestedTheme.ToString(),
          controlType = control.GetType().FullName,
          control.Name,
          handle = handle.ToInt64(),
          managedVisible = control.Visible,
          nativeVisible = IsWindowVisible(handle)
        });
        return;
      }

      _ = SendMessage(handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
      suppressedRedrawHandles[control] = handle;
      DiagnosticLog.Write("theme.child_redraw_suppressed_after_recreate", new
      {
        theme = requestedTheme.ToString(),
        controlType = control.GetType().FullName,
        control.Name,
        handle = handle.ToInt64()
      });
    };

    if (Visible && IsHandleCreated)
    {
      try
      {
        webViewSnapshot = await _transcriptView.CapturePreviewBitmapAsync();
        if (webViewSnapshot is not null)
        {
          webViewSnapshotScreenBounds = _transcriptView.GetWebViewScreenBounds();
          DiagnosticLog.Write("theme.transition_webview_snapshot", new
          {
            theme = requestedTheme.ToString(),
            phase = "captured",
            bounds = webViewSnapshotScreenBounds.ToString(),
            width = webViewSnapshot.Width,
            height = webViewSnapshot.Height
          });
        }
      }
      catch (Exception exception)
      {
        webViewSnapshot?.Dispose();
        webViewSnapshot = null;
        DiagnosticLog.Write("theme.transition_webview_snapshot", new
        {
          theme = requestedTheme.ToString(),
          phase = "failed",
          exception = exception.ToString()
        });
      }
    }

    if (redrawAvailable)
    {
      CollectRedrawControls(this, redrawControls);
      foreach (Control control in redrawControls)
      {
        if (!control.IsHandleCreated ||
            !control.Visible ||
            !IsWindowVisible(control.Handle))
        {
          continue;
        }

        control.HandleCreated += recreatedHandleSuppressor;
        IntPtr handle = control.Handle;
        suppressedRedrawHandles[control] = handle;
        _ = SendMessage(
          handle,
          WmSetRedraw,
          IntPtr.Zero,
          IntPtr.Zero);
      }
    }

    try
    {
      if (Visible && IsHandleCreated)
      {
        Bitmap? snapshotBitmap = null;
        try
        {
          snapshotBitmap = new Bitmap(Width, Height);
          using (Graphics snapshotGraphics = Graphics.FromImage(snapshotBitmap))
          {
            snapshotGraphics.CopyFromScreen(
              Left,
              Top,
              0,
              0,
              snapshotBitmap.Size,
              CopyPixelOperation.SourceCopy);

            if (webViewSnapshot is not null &&
                !webViewSnapshotScreenBounds.IsEmpty)
            {
              var destination = new Rectangle(
                webViewSnapshotScreenBounds.Left - Left,
                webViewSnapshotScreenBounds.Top - Top,
                webViewSnapshotScreenBounds.Width,
                webViewSnapshotScreenBounds.Height);
              snapshotGraphics.DrawImage(webViewSnapshot, destination);
            }
          }

          transitionSnapshot = new ThemeTransitionSnapshotForm(
            snapshotBitmap,
            Bounds);
          snapshotBitmap = null; // Ownership transferred to the snapshot form.
          transitionSnapshot.Show(this);
          transitionSnapshot.Update();
          webViewSnapshot?.Dispose();
          webViewSnapshot = null;

          DiagnosticLog.Write("theme.transition_snapshot", new
          {
            theme = requestedTheme.ToString(),
            phase = "shown",
            bounds = Bounds.ToString(),
            snapshotHandle = transitionSnapshot.Handle.ToInt64()
          });
        }
        catch (Exception exception)
        {
          snapshotBitmap?.Dispose();
          webViewSnapshot?.Dispose();
          webViewSnapshot = null;
          transitionSnapshot?.Dispose();
          transitionSnapshot = null;
          DiagnosticLog.Write("theme.transition_snapshot", new
          {
            theme = requestedTheme.ToString(),
            phase = "failed",
            exception = exception.ToString()
          });
        }
      }

      if (Visible && opacityBefore > TransitionOpacity)
      {
        Opacity = TransitionOpacity;
        opacityLowered = true;
      }

      DiagnosticLog.Write("theme.transition_begin", new
      {
        theme = requestedTheme.ToString(),
        reason,
        redrawSuppressed = redrawAvailable,
        redrawScope = "children-only",
        childCount = redrawControls.Count,
        handle = redrawHandle.ToInt64(),
        focusedBefore = DescribeControlForTabDiagnostics(focusedBefore),
        mainContainedFocusBefore,
        opacityBefore,
        transitionOpacity = Opacity,
        opacityLowered
      });

      ApplyCurrentTheme(requestedTheme);
    }
    finally
    {
      foreach (Control control in redrawControls)
      {
        if (!control.IsDisposed)
        {
          control.HandleCreated -= recreatedHandleSuppressor;
        }
      }

      if (redrawAvailable && IsHandleCreated && Handle == redrawHandle)
      {
        int redrawReenabledCount = 0;
        int hiddenAfterReenableCount = 0;
        foreach (KeyValuePair<Control, IntPtr> entry in
                 suppressedRedrawHandles.ToArray())
        {
          Control control = entry.Key;
          IntPtr suppressedHandle = entry.Value;
          if (control.IsDisposed ||
              control.Disposing ||
              !control.IsHandleCreated ||
              control.Handle != suppressedHandle)
          {
            continue;
          }

          _ = SendMessage(
            suppressedHandle,
            WmSetRedraw,
            new IntPtr(1),
            IntPtr.Zero);
          redrawReenabledCount++;

          if (!control.Visible)
          {
            _ = ShowWindow(suppressedHandle, SwHide);
            hiddenAfterReenableCount++;
          }
        }

        bool redrawSucceeded = RedrawWindow(
          redrawHandle,
          IntPtr.Zero,
          IntPtr.Zero,
          RdwInvalidate | RdwErase | RdwFrame |
          RdwAllChildren | RdwUpdateNow);
        DiagnosticLog.Write("theme.transition_redraw", new
        {
          theme = requestedTheme.ToString(),
          handle = redrawHandle.ToInt64(),
          redrawScope = "visible-children-only",
          childCount = redrawControls.Count,
          suppressedCount = suppressedRedrawHandles.Count,
          redrawReenabledCount,
          hiddenAfterReenableCount,
          redrawSucceeded,
          win32Error = redrawSucceeded ? 0 : Marshal.GetLastWin32Error()
        });
      }

      if (opacityLowered && !IsDisposed && !Disposing)
      {
        Opacity = opacityBefore;
        DiagnosticLog.Write("theme.transition_opacity_restored", new
        {
          theme = requestedTheme.ToString(),
          opacity = Opacity
        });
      }

      webViewSnapshot?.Dispose();
      webViewSnapshot = null;

      if (transitionSnapshot is not null)
      {
        long snapshotHandle = transitionSnapshot.IsHandleCreated
          ? transitionSnapshot.Handle.ToInt64()
          : 0;
        transitionSnapshot.Close();
        transitionSnapshot.Dispose();
        transitionSnapshot = null;
        DiagnosticLog.Write("theme.transition_snapshot", new
        {
          theme = requestedTheme.ToString(),
          phase = "removed",
          snapshotHandle
        });
      }

      if (mainContainedFocusBefore &&
          ReferenceEquals(Form.ActiveForm, this) &&
          focusedBefore is not null &&
          !focusedBefore.IsDisposed &&
          focusedBefore.Enabled &&
          focusedBefore.Visible &&
          focusedBefore.CanSelect &&
          !focusedBefore.ContainsFocus)
      {
        bool restored = focusedBefore.Focus();
        DiagnosticLog.Write("theme.transition_focus_restore", new
        {
          theme = requestedTheme.ToString(),
          restored,
          focusedBefore = DescribeControlForTabDiagnostics(focusedBefore),
          focusedAfter = DescribeControlForTabDiagnostics(
            FindFocusedDescendant(this))
        });
      }

      _themeTransitionActive = false;
      DiagnosticLog.Write("theme.transition_end", new
      {
        theme = requestedTheme.ToString(),
        reason,
        pendingTheme = _pendingThemeRequest?.ToString(),
        isDisposed = IsDisposed,
        disposing = Disposing
      });
    }

    if (_pendingThemeRequest is not null && !IsDisposed && !Disposing)
    {
      QueueThemeApplication(
        _pendingThemeRequest.Value,
        _pendingThemeReason ?? "coalesced");
    }
  }

  /// <summary>
  /// Collects existing managed child controls without forcing HWND creation.
  /// The MainForm itself is intentionally excluded so redraw suppression does
  /// not alter top-level activation/visibility semantics.
  /// </summary>
  private static void CollectRedrawControls(
    Control parent,
    List<Control> destination)
  {
    foreach (Control child in parent.Controls)
    {
      destination.Add(child);
      if (child.HasChildren)
      {
        CollectRedrawControls(child, destination);
      }
    }
  }

  /// <summary>
  /// Gets the selected theme with System as a safe default.
  /// </summary>
  private AppTheme GetSelectedTheme()
  {
    return _themeComboBox.SelectedItem is AppTheme theme
      ? theme
      : AppTheme.System;
  }

  /// <summary>
  /// Applies the currently selected effective theme.
  /// </summary>
  private void ApplyCurrentTheme(AppTheme? requestedTheme = null)
  {
    int generation = ++_themeApplyGeneration;
    int entryDepth = _themeApplyDepth;
    _themeApplyDepth++;
    AppTheme theme = requestedTheme ?? GetSelectedTheme();
    bool dark = ThemeManager.IsDark(theme);
    DiagnosticLog.Write("theme.apply_begin", new
    {
      generation,
      entryDepth,
      theme = theme.ToString(),
      dark,
      pending = _themeApplicationPending,
      isHandleCreated = IsHandleCreated,
      handle = IsHandleCreated ? Handle.ToInt64() : 0,
      isDisposed = IsDisposed,
      disposing = Disposing
    });
    if (entryDepth > 0)
    {
      DiagnosticLog.Write("theme.apply_reentered", new
      {
        generation,
        entryDepth,
        theme = theme.ToString(),
        dark
      });
    }

    ThemeManager.SetDiagnosticGeneration(generation);
    try
    {
      LogThemeStage(generation, "main-form", "begin");
      ThemeManager.Apply(this, theme);
      LogThemeStage(generation, "main-form", "end");

      LogThemeStage(generation, "tooltip", "begin");
      ThemeManager.ApplyToolTip(_toolTip, dark);
      LogThemeStage(generation, "tooltip", "end");

      int profileIndex = 0;
      foreach (SpeechProfileCompactControl profile in GetProfileControls())
      {
        string stage = $"profile-{profileIndex}";
        LogThemeStage(generation, stage, "begin", profile);
        profile.ApplyTheme(dark);
        LogThemeStage(generation, stage, "end", profile);
        profileIndex++;
      }

      LogThemeStage(generation, "transcript-settings-popup", "begin",
        _transcriptSettingsPopup);
      _transcriptSettingsPopup.ApplyTheme(dark);
      LogThemeStage(generation, "transcript-settings-popup", "end",
        _transcriptSettingsPopup);

      LogThemeStage(generation, "transcript-view", "begin", _transcriptView);
      _transcriptView.ApplyTheme(dark);
      LogThemeStage(generation, "transcript-view", "end", _transcriptView);

      LogThemeStage(generation, "transport-heights", "begin");
      SynchronizeTransportButtonHeights();
      LogThemeStage(generation, "transport-heights", "end");

      LogThemeStage(generation, "position-transcript-controls", "begin");
      PositionTranscriptControls();
      LogThemeStage(generation, "position-transcript-controls", "end");
    }
    finally
    {
      ThemeManager.SetDiagnosticGeneration(generation);
      _themeApplyDepth--;
      DiagnosticLog.Write("theme.apply_end", new
      {
        generation,
        remainingDepth = _themeApplyDepth,
        theme = theme.ToString(),
        dark,
        isHandleCreated = IsHandleCreated,
        handle = IsHandleCreated ? Handle.ToInt64() : 0,
        isDisposed = IsDisposed,
        disposing = Disposing
      });
      _lastCompletedThemeGeneration = generation;
      ThemeManager.StartNativeMessageTrace(generation);
      QueuePostThemeDiagnostics(generation, theme, dark);
    }
  }

  /// <summary>
  /// Queues checkpoints that run only after control returns to the WinForms
  /// message loop following a theme application.
  /// </summary>
  private void QueuePostThemeDiagnostics(
    int generation,
    AppTheme theme,
    bool dark)
  {
    _pendingIdleThemeGeneration = generation;
    DiagnosticLog.Write("theme.post_apply_queued", new
    {
      generation,
      theme = theme.ToString(),
      dark,
      isHandleCreated = IsHandleCreated,
      handle = IsHandleCreated ? Handle.ToInt64() : 0
    });

    if (IsDisposed || Disposing || !IsHandleCreated)
    {
      DiagnosticLog.Write("theme.post_apply_queue_skipped", new
      {
        generation,
        isDisposed = IsDisposed,
        disposing = Disposing,
        isHandleCreated = IsHandleCreated
      });
      return;
    }

    BeginInvoke((Action)(() =>
    {
      DiagnosticLog.Write("theme.post_apply_begininvoke_1", new
      {
        generation,
        latestGeneration = _lastCompletedThemeGeneration,
        isDisposed = IsDisposed,
        disposing = Disposing,
        isHandleCreated = IsHandleCreated,
        handle = IsHandleCreated ? Handle.ToInt64() : 0,
        containsFocus = ContainsFocus,
        activeForm = Form.ActiveForm?.Text
      });
      if (IsDisposed || Disposing || !IsHandleCreated)
      {
        return;
      }

      BeginInvoke((Action)(() =>
      {
        DiagnosticLog.Write("theme.post_apply_begininvoke_2", new
        {
          generation,
          latestGeneration = _lastCompletedThemeGeneration,
          isDisposed = IsDisposed,
          disposing = Disposing,
          isHandleCreated = IsHandleCreated,
          handle = IsHandleCreated ? Handle.ToInt64() : 0,
          containsFocus = ContainsFocus,
          activeForm = Form.ActiveForm?.Text
        });
      }));
    }));
  }

  /// <summary>
  /// Records the first application-idle checkpoint after the latest theme
  /// generation has returned to the message loop.
  /// </summary>
  private void ApplicationIdle(object? sender, EventArgs eventArgs)
  {
    if (!_themeNativeMessageTracerStarted)
    {
      _themeNativeMessageTracerStarted = true;
      _themeNativeMessageTracer.Start();
    }

    int generation = _pendingIdleThemeGeneration;
    if (generation <= 0)
    {
      return;
    }

    _pendingIdleThemeGeneration = 0;
    DiagnosticLog.Write("theme.post_apply_idle", new
    {
      generation,
      latestGeneration = _lastCompletedThemeGeneration,
      isDisposed = IsDisposed,
      disposing = Disposing,
      isHandleCreated = IsHandleCreated,
      handle = IsHandleCreated ? Handle.ToInt64() : 0,
      containsFocus = ContainsFocus,
      activeForm = Form.ActiveForm?.Text
    });
  }

  private static void LogThemeStage(
    int generation,
    string stage,
    string phase,
    Control? control = null)
  {
    DiagnosticLog.Write("theme.apply_stage", new
    {
      generation,
      stage,
      phase,
      controlType = control?.GetType().FullName,
      controlName = control?.Name,
      controlText = control?.Text,
      isDisposed = control?.IsDisposed,
      disposing = control?.Disposing,
      isHandleCreated = control?.IsHandleCreated,
      handle = control is { IsHandleCreated: true } ? control.Handle.ToInt64() : 0
    });
  }

  /// <summary>
  /// Keeps every glyph transport button at one standard height.
  /// </summary>
  private void SynchronizeTransportButtonHeights()
  {
    int height = TransportButtonHeight;
    GlyphButton[] buttons =
    {
      _rewindSpeakerButton,
      _rewindNodeButton,
      _rewindSentenceButton,
      _playPauseButton,
      _forwardSentenceButton,
      _forwardNodeButton,
      _forwardSpeakerButton,
      _processingTimeButton
    };
    foreach (GlyphButton button in buttons)
    {
      button.Height = height;
    }
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
  /// Indexes the selected transcript immediately and publishes its paused
  /// playback marker before the user presses Play.
  /// </summary>
  private async Task LoadPausedHistoryPreviewAsync(LocatedSession session)
  {
    int generation = Interlocked.Increment(ref _historyPreviewGeneration);
    string expectedPath = session.Path;
    bool startAtLatestTurn = _speakExistingCheckBox.Checked;
    try
    {
      SpeechHistorySnapshot snapshot = await Task.Run(() =>
        _monitor.LoadHistoryPreview(session, startAtLatestTurn));
      if (_closing || IsDisposed || generation != Volatile.Read(
            ref _historyPreviewGeneration) || _monitor.IsRunning ||
          !string.Equals(
            expectedPath,
            _sessionPathTextBox.Text,
            StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      _selectedSessionHistory = snapshot;
      _selectedSessionHistoryPath = expectedPath;
      _speech.LoadHistory(
        snapshot.Fragments,
        snapshot.Completions,
        snapshot.BackgroundWorkEvents,
        snapshot.StartMode);
      AppendLog(
        $"Indexed {snapshot.Fragments.Count} existing fragments for paused navigation.");
      UpdateControlState();
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException or
      InvalidDataException or InvalidOperationException or ArgumentException)
    {
      if (generation == Volatile.Read(ref _historyPreviewGeneration))
      {
        AppendLog($"Unable to index paused history: {exception.Message}");
      }
    }
  }

  /// <summary>
  /// Displays one resolved session.
  /// </summary>
  private void SetSessionDisplay(
    LocatedSession session,
    bool restoredFromSettings = false)
  {
    if (!PathsReferToSameFile(_selectedSessionHistoryPath, session.Path))
    {
      _selectedSessionHistory = null;
      _selectedSessionHistoryPath = null;
    }
    _sessionTitleTextBox.Text = session.DisplayName;
    _sessionPathTextBox.Text = session.Path;
    _transcriptView.SelectSession(
      session.Path,
      session.Source,
      session.DisplayName,
      restoredFromSettings);
    if (!_monitor.IsRunning)
    {
      _ = LoadPausedHistoryPreviewAsync(session);
    }
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
    ApplyCurrentTheme();
    if (_pathIsManual && File.Exists(_sessionPathTextBox.Text))
    {
      try
      {
        SetSessionDisplay(
          SessionLocator.FromPath(
            _sessionPathTextBox.Text,
            GetSelectedSource()),
          restoredFromSettings: true);
      }
      catch (Exception exception) when (
        exception is IOException or
        UnauthorizedAccessException or
        InvalidDataException)
      {
        AppendLog($"Saved session could not be opened: {exception.Message}");
      }
    }

    // Shown fires after the native form has been created.  Defer presentation
    // by one message turn so all synchronous Shown work and its resulting
    // layout have completed while the form is still transparent.
    BeginInvoke((Action)PresentInitializedWindow);
  }

  /// <summary>
  /// Presents the fully populated ordinary WinForms UI in one completed frame.
  /// </summary>
  private void PresentInitializedWindow()
  {
    if (!_startupPresentationPending || _closing || IsDisposed)
    {
      return;
    }

    // Keep the form technically visible at 1/255 opacity while forcing the
    // first complete layout and native-child paint.  Restore full opacity on
    // the following UI turn so those controls have already painted once.
    _mainLayout.PerformLayout();
    PerformLayout();
    Invalidate(invalidateChildren: true);
    Update();
    BeginInvoke((Action)FinishStartupPresentation);
  }

  /// <summary>
  /// Restores full opacity after the initial low-opacity paint has completed.
  /// </summary>
  private void FinishStartupPresentation()
  {
    if (!_startupPresentationPending || _closing || IsDisposed)
    {
      return;
    }

    _startupPresentationPending = false;
    Opacity = 1.0;
    Invalidate(invalidateChildren: true);
    Update();

    DiagnosticLog.Write("ui.shown", new
    {
      highDpiMode = Application.HighDpiMode.ToString(),
      bounds = Bounds.ToString(),
      screens = Screen.AllScreens.Select(screen => screen.Bounds.ToString()).ToArray()
    });
  }

  /// <summary>
  /// Gets all settings that differ from the persisted snapshot.
  /// </summary>
  private IReadOnlyList<SettingsChangeSet.Change> GetSettingsChanges()
  {
    return SettingsChangeSet.GetChanges(
      _settingsStore.Saved,
      _settingsStore.Current);
  }

  /// <summary>
  /// Enables save controls only while unsaved settings exist.
  /// </summary>
  private void UpdateSettingsSaveState()
  {
    IReadOnlyList<SettingsChangeSet.Change> changes = GetSettingsChanges();
    bool dirty = changes.Count > 0;
    _saveSettingsButton.Enabled = dirty;
    bool defaultsDiffer = SettingsChangeSet.GetChanges(
      _settingsStore.Defaults,
      _settingsStore.Current).Count > 0;
    _resetSettingsButton.Enabled = defaultsDiffer;
    DiagnosticLog.Write("settings.dirty_state", new
    {
      dirty,
      changedKeys = changes.Select(change => change.Key).ToArray()
    });
  }

  /// <summary>
  /// Commits selected changes and optionally discards every unselected change.
  /// </summary>
  private bool CommitSelectedSettings(
    IReadOnlySet<string> selectedKeys,
    bool discardUnselected)
  {
    try
    {
      UserSettings saved = _settingsStore.Saved;
      UserSettings working = _settingsStore.Current;
      UserSettings merged = SettingsChangeSet.MergeSelected(
        saved,
        working,
        selectedKeys);
      _settingsStore.Commit(merged);
      if (!discardUnselected)
      {
        _settingsStore.Update(working);
      }
      UpdateSettingsSaveState();
      AppendLog($"Saved {selectedKeys.Count} changed setting(s).");
      return true;
    }
    catch (Exception exception) when (
      exception is IOException or UnauthorizedAccessException)
    {
      AppendLog($"Settings save failed: {exception.Message}");
      return false;
    }
  }

  /// <summary>
  /// Flushes pending settings and releases resources.
  /// </summary>
  private void MainFormClosing(object? sender, FormClosingEventArgs eventArgs)
  {
    bool externalTermination = Program.ExternalTerminationRequested;
    bool immediateTermination = externalTermination ||
      eventArgs.CloseReason == CloseReason.TaskManagerClosing ||
      eventArgs.CloseReason == CloseReason.WindowsShutDown;
    _transcriptSettingsSaveTimer.Stop();
    _fenceDebounceTimer.Stop();

    IReadOnlyList<SettingsChangeSet.Change> changes = [];
    if (!immediateTermination)
    {
      ApplyFenceTypesImmediately();
      SaveControlsToSettings(includeWindowPlacement: true);
      changes = GetSettingsChanges();
    }
    DiagnosticLog.Write("settings.closing", new
    {
      eventArgs.CloseReason,
      externalTermination,
      immediateTermination,
      promptBypassed = immediateTermination,
      changedKeys = changes.Select(change => change.Key).ToArray()
    });
    if (!immediateTermination && changes.Count > 0)
    {
      using var dialog = new SaveChangedSettingsDialog(
        changes,
        GetSelectedTheme(),
        closing: true);
      if (dialog.ShowDialog(this) != DialogResult.OK)
      {
        eventArgs.Cancel = true;
        return;
      }
      if (!CommitSelectedSettings(
            dialog.SelectedKeys,
            discardUnselected: true))
      {
        eventArgs.Cancel = true;
        return;
      }
    }

    Application.RemoveMessageFilter(this);
    Application.Idle -= ApplicationIdle;
    SystemEvents.UserPreferenceChanged -= WindowsUserPreferenceChanged;
    _closing = true;
    _playbackMailbox.Clear();
    Interlocked.Increment(ref _monitorSession);
    _monitor.Dispose();
    _displayAwake.Dispose();
    _speech.Dispose();
    _fenceDebounceTimer.Dispose();
    _themeNativeMessageTracer.Dispose();
    _transcriptSettingsHoverController.Dispose();
    _transcriptSettingsSaveTimer.Dispose();
    foreach (VoiceRowControls row in _voiceRows.Values)
    {
      row.PreviewTimer.Dispose();
    }
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
    SetTabOrder(_sessionTitleTextBox, _sessionPathTextBox);
    return details;
  }

  /// <summary>
  /// Configures a transport button.
  /// </summary>
  private void ConfigureTransportButton(
    GlyphButton button,
    string symbol,
    string accessibleName)
  {
    button.AutoSize = false;
    button.Size = new Size(TransportButtonWidth, TransportButtonHeight);
    button.Font = new Font("Segoe UI Symbol", 11.0f);
    button.Drawing = GlyphButtonDrawing.Text;
    button.Glyph = symbol;
    button.UseInkBounds = true;
    button.Text = string.Empty;
    button.AccessibleName = accessibleName;
    _toolTip.SetToolTip(button, accessibleName);
  }

  /// <summary>
  /// Configures a custom vector transport button.
  /// </summary>
  private void ConfigureCustomTransportButton(
    GlyphButton button,
    GlyphButtonDrawing drawing,
    string accessibleName)
  {
    button.AutoSize = false;
    button.Size = new Size(TransportButtonWidth, TransportButtonHeight);
    button.Drawing = drawing;
    button.Glyph = string.Empty;
    button.UseInkBounds = false;
    button.Text = string.Empty;
    button.AccessibleName = accessibleName;
    _toolTip.SetToolTip(button, accessibleName);
  }

  /// <summary>
  /// Configures one compact vector button used beside the transcript tabs.
  /// </summary>
  private void ConfigureCompactGlyphButton(
    GlyphButton button,
    GlyphButtonDrawing drawing,
    string accessibleName)
  {
    button.AutoSize = false;
    button.Size = new Size(24, 21);
    button.Drawing = drawing;
    button.Glyph = string.Empty;
    button.UseInkBounds = false;
    button.Text = string.Empty;
    button.AccessibleName = accessibleName;
  }

  /// <summary>
  /// Configures an icon-only utility button through the translation layer.
  /// </summary>
  private void ConfigureUtilityGlyphButton(
    GlyphButton button,
    GlyphButtonDrawing drawing,
    string resourcePrefix)
  {
    button.AutoSize = false;
    button.Size = new Size(38, TransportButtonHeight);
    button.Drawing = drawing;
    button.Glyph = string.Empty;
    button.UseInkBounds = false;
    button.Text = string.Empty;
    UiText.Apply(button, resourcePrefix, _toolTip);
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
  /// Assigns deterministic WinForms tab indices in the supplied logical
  /// order.  This keeps normal SelectNextControl traversal aligned with the
  /// visual control order without introducing a separate traversal policy.
  /// </summary>
  private static void SetTabOrder(params Control[] controls)
  {
    for (int index = 0; index < controls.Length; index++)
    {
      controls[index].TabIndex = index;
    }
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
    control.TextAlign = HorizontalAlignment.Right;
    control.Value = value;
    control.Width = width;
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
  /// Logs MainForm handle creation so a rapid theme switch can be correlated
  /// with native handle lifetime.
  /// </summary>
  protected override void OnHandleCreated(EventArgs eventArgs)
  {
    base.OnHandleCreated(eventArgs);
    DiagnosticLog.Write("theme.main_handle_created", new
    {
      generation = _lastCompletedThemeGeneration,
      handle = Handle.ToInt64(),
      recreatingHandle = RecreatingHandle,
      isDisposed = IsDisposed,
      disposing = Disposing
    });
  }

  /// <summary>
  /// Logs MainForm handle destruction so a rapid theme switch can be
  /// correlated with native handle lifetime.
  /// </summary>
  protected override void OnHandleDestroyed(EventArgs eventArgs)
  {
    DiagnosticLog.Write("theme.main_handle_destroyed", new
    {
      generation = _lastCompletedThemeGeneration,
      handle = IsHandleCreated ? Handle.ToInt64() : 0,
      recreatingHandle = RecreatingHandle,
      isDisposed = IsDisposed,
      disposing = Disposing
    });
    base.OnHandleDestroyed(eventArgs);
  }

  /// <summary>
  /// Logs MainForm paint entry/exit after a theme generation has completed.
  /// </summary>
  protected override void OnPaint(PaintEventArgs eventArgs)
  {
    int generation = _lastCompletedThemeGeneration;
    if (generation > 0)
    {
      DiagnosticLog.Write("theme.main_paint", new
      {
        generation,
        phase = "begin",
        clip = eventArgs.ClipRectangle,
        isHandleCreated = IsHandleCreated,
        handle = IsHandleCreated ? Handle.ToInt64() : 0
      });
    }

    base.OnPaint(eventArgs);

    if (generation > 0)
    {
      DiagnosticLog.Write("theme.main_paint", new
      {
        generation,
        phase = "end",
        clip = eventArgs.ClipRectangle,
        isHandleCreated = IsHandleCreated,
        handle = IsHandleCreated ? Handle.ToInt64() : 0
      });
    }
  }

  /// <summary>
  /// Logs native messages relevant to theme settling and painting.
  /// </summary>
  protected override void WndProc(ref Message message)
  {
    const int WmPaint = 0x000F;
    const int WmEraseBkgnd = 0x0014;
    const int WmSysColorChange = 0x0015;
    const int WmSettingChange = 0x001A;
    const int WmNcPaint = 0x0085;
    const int WmThemeChanged = 0x031A;

    int nativeTraceGeneration = ThemeManager.GetNativeMessageTraceGeneration();
    bool trace = nativeTraceGeneration > 0 || message.Msg is
      WmPaint or
      WmEraseBkgnd or
      WmSysColorChange or
      WmSettingChange or
      WmNcPaint or
      WmThemeChanged;
    int generation = nativeTraceGeneration > 0
      ? nativeTraceGeneration
      : _lastCompletedThemeGeneration;
    if (trace && generation > 0)
    {
      DiagnosticLog.Write("theme.main_wndproc", new
      {
        generation,
        phase = "begin",
        message = $"0x{message.Msg:X4}",
        wParam = message.WParam.ToInt64(),
        lParam = message.LParam.ToInt64(),
        isHandleCreated = IsHandleCreated,
        handle = IsHandleCreated ? Handle.ToInt64() : 0
      });
    }

    base.WndProc(ref message);

    if (trace && generation > 0)
    {
      DiagnosticLog.Write("theme.main_wndproc", new
      {
        generation,
        phase = "end",
        message = $"0x{message.Msg:X4}",
        result = message.Result.ToInt64(),
        isHandleCreated = IsHandleCreated,
        handle = IsHandleCreated ? Handle.ToInt64() : 0
      });
    }
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
  /// Creates a compact two-line speech-table column heading.
  /// </summary>
  private static Label MakeSpeechColumnHeader(string text, int width)
  {
    return new Label
    {
      AutoSize = false,
      Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
      Height = 24,
      Margin = new Padding(3, 2, 3, 2),
      Text = text,
      TextAlign = ContentAlignment.MiddleCenter,
      Width = width
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

  /// <summary>
  /// Redraws owner-drawn voice fields after a table-layout resize settles.
  /// </summary>
  private sealed class VoiceComboBox : ComboBox
  {
    private bool _refreshQueued;

    public VoiceComboBox()
    {
      SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
      base.OnHandleCreated(eventArgs);
      DropDownWidth = Math.Max(1, Width);
    }

    protected override void OnResize(EventArgs eventArgs)
    {
      base.OnResize(eventArgs);
      DropDownWidth = Math.Max(1, Width);
      Invalidate();
      QueueSettledRefresh();
    }

    private void QueueSettledRefresh()
    {
      if (_refreshQueued || !IsHandleCreated || IsDisposed)
      {
        return;
      }

      _refreshQueued = true;
      BeginInvoke((Action)(() =>
      {
        _refreshQueued = false;
        if (IsDisposed || !IsHandleCreated)
        {
          return;
        }

        DropDownWidth = Math.Max(1, Width);
        Refresh();
      }));
    }
  }

  private sealed class VoiceRowControls
  {
    public VoiceRowControls(
      ComboBox voice,
      SpeechProfileCompactControl mainProfile,
      SpeechProfileCompactControl contextProfile,
      System.Windows.Forms.Timer previewTimer,
      string mainPreviewMessage,
      string contextPreviewMessage)
    {
      Voice = voice;
      MainProfile = mainProfile;
      ContextProfile = contextProfile;
      PreviewTimer = previewTimer;
      MainPreviewMessage = mainPreviewMessage;
      ContextPreviewMessage = contextPreviewMessage;
    }

    public ComboBox Voice { get; }

    public SpeechProfileCompactControl MainProfile { get; }

    public SpeechProfileCompactControl ContextProfile { get; }

    public System.Windows.Forms.Timer PreviewTimer { get; }

    public string MainPreviewMessage { get; }

    public string ContextPreviewMessage { get; }

    public bool PreviewContext { get; set; }
  }

}

/// <summary>
/// Represents a replay-navigation operation.
/// </summary>
internal delegate bool TryNavigateSpeech(out string text);
