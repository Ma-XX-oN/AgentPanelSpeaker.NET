from pathlib import Path
import sys


def replace_once(path: str, old: str, new: str) -> None:
  file_path = Path(path)
  text = file_path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"{path}: expected exactly one match, found {count}: {old!r}")
  file_path.write_text(text.replace(old, new), encoding="utf-8")


def apply_tests() -> None:
  path = "AgentPanelSpeaker/Issue94NativeSystemSpeechTimingRegressionTestRunner.cs"

  replace_once(
    path,
    """      ("system-speech-native-timing/provider-neutral-rate-semantics",
        TestProviderNeutralRateSemantics)
""",
    """      ("system-speech-native-timing/provider-neutral-rate-semantics",
        TestProviderNeutralRateSemantics),
      ("system-speech-native-timing/rate-match-toggle",
        TestRateMatchToggle),
      ("system-speech-native-timing/rate-match-setting-persistence",
        TestRateMatchSettingPersistence)
""")

  replace_once(
    path,
    """  private static int GetProductionSystemSpeechRate(int applicationRate)
  {
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "MapSystemSpeechRate",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("MapSystemSpeechRate is missing.");
    return method.Invoke(null, new object[] { applicationRate }) is int rate
      ? rate
      : throw new InvalidOperationException(
          "MapSystemSpeechRate returned no provider rate.");
  }
""",
    """  private static void TestRateMatchToggle()
  {
    Require(GetProductionSystemSpeechRate(6, true) == 3,
      "Matched application rate 6 must map to System.Speech rate 3.");
    Require(GetProductionSystemSpeechRate(6, false) == 6,
      "Unmatched application rate 6 must preserve System.Speech rate 6.");
    Require(GetProductionSystemSpeechRate(-10, false) == -10,
      "Unmatched minimum application rate must preserve -10.");
    Require(GetProductionSystemSpeechRate(10, false) == 10,
      "Unmatched maximum application rate must preserve +10.");
  }

  private static void TestRateMatchSettingPersistence()
  {
    UserSettings defaults = UserSettings.CreateDefault(Array.Empty<string>());
    Require(defaults.MatchDesktopAndWindowsMediaRates,
      "Rate matching must default on for existing .94.3 behavior.");

    UserSettings disabled = defaults with
    {
      MatchDesktopAndWindowsMediaRates = false
    };
    SettingsDocument document = SettingsDocument.FromRuntime(disabled);
    UserSettings roundTrip = document.ToRuntime(Array.Empty<string>());
    Require(!roundTrip.MatchDesktopAndWindowsMediaRates,
      "A disabled rate-match setting did not survive settings persistence.");

    var legacyShape = new SettingsDocument
    {
      Speech = new SpeechSettingsDocument()
    };
    Require(legacyShape.ToRuntime(Array.Empty<string>())
        .MatchDesktopAndWindowsMediaRates,
      "Settings without the new field must retain matched .94.3 behavior.");

    IReadOnlyList<SettingsChangeSet.Change> changes =
      SettingsChangeSet.GetChanges(defaults, disabled);
    Require(changes.Any(change =>
        change.Key == "Speech/MatchDesktopAndWindowsMediaRates"),
      "The rate-match option is missing from selective settings changes.");
    UserSettings selectivelyMerged = SettingsChangeSet.MergeSelected(
      defaults,
      disabled,
      new HashSet<string>(StringComparer.Ordinal)
      {
        "Speech/MatchDesktopAndWindowsMediaRates"
      });
    Require(!selectivelyMerged.MatchDesktopAndWindowsMediaRates,
      "Selective settings save did not merge the rate-match option.");

    Require(
      UiText.Get("Main.MatchDesktopAndWindowsMediaRates.Text") ==
        "Match Desktop and Windows Media rates",
      "The rate-match checkbox label is missing or incorrect.");
    string tooltip = UiText.Get(
      "Main.MatchDesktopAndWindowsMediaRates.Tooltip");
    Require(tooltip.Contains("different rate scales", StringComparison.Ordinal) &&
        tooltip.Contains("native rate range", StringComparison.Ordinal),
      "The rate-match tooltip does not explain the provider-scale tradeoff.");
  }

  private static int GetProductionSystemSpeechRate(
    int applicationRate,
    bool matchDesktopAndWindowsMediaRates = true)
  {
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "MapSystemSpeechRate",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("MapSystemSpeechRate is missing.");
    return method.Invoke(
      null,
      new object[]
      {
        applicationRate,
        matchDesktopAndWindowsMediaRates
      }) is int rate
      ? rate
      : throw new InvalidOperationException(
          "MapSystemSpeechRate returned no provider rate.");
  }
""")

  replace_once(
    path,
    """      synthesizer,
      null,
      null
    };
""",
    """      synthesizer,
      true,
      null,
      null
    };
""")

  replace_once(
    path,
    """        arguments[4] as IReadOnlyList<SpeechWordBoundary> ??
""",
    """        arguments[5] as IReadOnlyList<SpeechWordBoundary> ??
""")


def apply_production() -> None:
  replace_once(
    "AgentPanelSpeaker/UserSettings.cs",
    "  public const int CurrentVersion = 17;",
    "  public const int CurrentVersion = 18;")
  replace_once(
    "AgentPanelSpeaker/UserSettings.cs",
    """  public SpeechMasterSettings MasterSpeech { get; init; } =
    SpeechMasterSettings.Default;
""",
    """  public SpeechMasterSettings MasterSpeech { get; init; } =
    SpeechMasterSettings.Default;

  /// <summary>
  /// Gets whether Desktop/System.Speech rates are calibrated to the
  /// Windows.Media application rate scale.
  /// </summary>
  public bool MatchDesktopAndWindowsMediaRates { get; init; } = true;
""")
  replace_once(
    "AgentPanelSpeaker/UserSettings.cs",
    """      MasterSpeech = SpeechMasterSettings.Default,
      SubagentAssistant = Profile(subagentVoice, 0),
""",
    """      MasterSpeech = SpeechMasterSettings.Default,
      MatchDesktopAndWindowsMediaRates = true,
      SubagentAssistant = Profile(subagentVoice, 0),
""")

  replace_once(
    "AgentPanelSpeaker/SettingsDocument.cs",
    "  public const int CurrentSchemaVersion = 17;",
    "  public const int CurrentSchemaVersion = 18;")
  replace_once(
    "AgentPanelSpeaker/SettingsDocument.cs",
    """      KeepDisplayOnWhileSpeaking = settings.KeepDisplayOnWhileSpeaking
""",
    """      KeepDisplayOnWhileSpeaking = settings.KeepDisplayOnWhileSpeaking,
      MatchDesktopAndWindowsMediaRates =
        settings.MatchDesktopAndWindowsMediaRates
""")
  replace_once(
    "AgentPanelSpeaker/SettingsDocument.cs",
    """      KeepDisplayOnWhileSpeaking = Speech.KeepDisplayOnWhileSpeaking,
      PollIntervalMilliseconds = General.PollIntervalMilliseconds,
""",
    """      KeepDisplayOnWhileSpeaking = Speech.KeepDisplayOnWhileSpeaking,
      MatchDesktopAndWindowsMediaRates =
        Speech.MatchDesktopAndWindowsMediaRates,
      PollIntervalMilliseconds = General.PollIntervalMilliseconds,
""")
  replace_once(
    "AgentPanelSpeaker/SettingsDocument.cs",
    """  public bool KeepDisplayOnWhileSpeaking { get; init; }
}
""",
    """  public bool KeepDisplayOnWhileSpeaking { get; init; }
  public bool MatchDesktopAndWindowsMediaRates { get; init; } = true;
}
""")

  replace_once(
    "AgentPanelSpeaker/SettingsChangeSet.cs",
    """    AddScalar("Speech/KeepDisplayOn", ["Speech", "Keep display on while speaking"], saved.KeepDisplayOnWhileSpeaking, working.KeepDisplayOnWhileSpeaking);
""",
    """    AddScalar("Speech/KeepDisplayOn", ["Speech", "Keep display on while speaking"], saved.KeepDisplayOnWhileSpeaking, working.KeepDisplayOnWhileSpeaking);
    AddScalar(
      "Speech/MatchDesktopAndWindowsMediaRates",
      ["Speech", "Match Desktop and Windows Media rates"],
      saved.MatchDesktopAndWindowsMediaRates,
      working.MatchDesktopAndWindowsMediaRates);
""")
  replace_once(
    "AgentPanelSpeaker/SettingsChangeSet.cs",
    """      KeepDisplayOnWhileSpeaking = Pick("Speech/KeepDisplayOn", saved.KeepDisplayOnWhileSpeaking, working.KeepDisplayOnWhileSpeaking),
""",
    """      KeepDisplayOnWhileSpeaking = Pick("Speech/KeepDisplayOn", saved.KeepDisplayOnWhileSpeaking, working.KeepDisplayOnWhileSpeaking),
      MatchDesktopAndWindowsMediaRates = Pick(
        "Speech/MatchDesktopAndWindowsMediaRates",
        saved.MatchDesktopAndWindowsMediaRates,
        working.MatchDesktopAndWindowsMediaRates),
""")

  replace_once(
    "AgentPanelSpeaker/SapiSpeechEngine.cs",
    """  private int _windowsMediaBookmarkMode =
    (int)WindowsMediaBookmarkMode.Fallback;
""",
    """  private int _windowsMediaBookmarkMode =
    (int)WindowsMediaBookmarkMode.Fallback;
  private int _matchDesktopAndWindowsMediaRates = 1;
""")
  replace_once(
    "AgentPanelSpeaker/SapiSpeechEngine.cs",
    """  public void SetWindowsMediaBookmarkMode(WindowsMediaBookmarkMode mode)
  {
    if (!Enum.IsDefined(typeof(WindowsMediaBookmarkMode), mode))
    {
      throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }
    Volatile.Write(ref _windowsMediaBookmarkMode, (int)mode);
  }
""",
    """  public void SetWindowsMediaBookmarkMode(WindowsMediaBookmarkMode mode)
  {
    if (!Enum.IsDefined(typeof(WindowsMediaBookmarkMode), mode))
    {
      throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }
    Volatile.Write(ref _windowsMediaBookmarkMode, (int)mode);
  }

  /// <summary>
  /// Enables or disables Desktop/System.Speech rate calibration against the
  /// Windows.Media application rate scale.
  /// </summary>
  public void SetMatchDesktopAndWindowsMediaRates(bool enabled)
  {
    Volatile.Write(ref _matchDesktopAndWindowsMediaRates, enabled ? 1 : 0);
  }
""")
  replace_once(
    "AgentPanelSpeaker/SapiSpeechEngine.cs",
    """        wave = RenderSystemSpeech(
          markup,
          profile,
          backend.ProviderVoiceId,
          synthesizer,
          out boundaries,
          out trackingDegradation);
""",
    """        wave = RenderSystemSpeech(
          markup,
          profile,
          backend.ProviderVoiceId,
          synthesizer,
          Volatile.Read(ref _matchDesktopAndWindowsMediaRates) != 0,
          out boundaries,
          out trackingDegradation);
""")
  replace_once(
    "AgentPanelSpeaker/SapiSpeechEngine.cs",
    """  private static int MapSystemSpeechRate(int applicationRate)
  {
    Debug.Assert(applicationRate is >= -10 and <= 10);
    return (int)Math.Round(
      applicationRate / 2.0,
      MidpointRounding.AwayFromZero);
  }
""",
    """  private static int MapSystemSpeechRate(
    int applicationRate,
    bool matchDesktopAndWindowsMediaRates)
  {
    Debug.Assert(applicationRate is >= -10 and <= 10);
    if (!matchDesktopAndWindowsMediaRates)
    {
      return applicationRate;
    }
    return (int)Math.Round(
      applicationRate / 2.0,
      MidpointRounding.AwayFromZero);
  }
""")
  replace_once(
    "AgentPanelSpeaker/SapiSpeechEngine.cs",
    """    string providerVoiceId,
    SystemSpeechSynthesizer synthesizer,
    out IReadOnlyList<SpeechWordBoundary> boundaries,
""",
    """    string providerVoiceId,
    SystemSpeechSynthesizer synthesizer,
    bool matchDesktopAndWindowsMediaRates,
    out IReadOnlyList<SpeechWordBoundary> boundaries,
""")
  replace_once(
    "AgentPanelSpeaker/SapiSpeechEngine.cs",
    """    int providerRate = MapSystemSpeechRate(profile.Rate);
    synthesizer.Rate = providerRate;
    DiagnosticLog.Write("speech.system_speech_rate_mapped", new
    {
      applicationRate = profile.Rate,
      providerRate
    });
""",
    """    int providerRate = MapSystemSpeechRate(
      profile.Rate,
      matchDesktopAndWindowsMediaRates);
    synthesizer.Rate = providerRate;
    DiagnosticLog.Write("speech.system_speech_rate_mapped", new
    {
      applicationRate = profile.Rate,
      providerRate,
      matchDesktopAndWindowsMediaRates
    });
""")

  replace_once(
    "AgentPanelSpeaker/SpeechService.cs",
    """  public void SetWindowsMediaBookmarkMode(WindowsMediaBookmarkMode mode)
  {
    _engine.SetWindowsMediaBookmarkMode(mode);
  }
""",
    """  public void SetWindowsMediaBookmarkMode(WindowsMediaBookmarkMode mode)
  {
    _engine.SetWindowsMediaBookmarkMode(mode);
  }

  /// <summary>
  /// Enables or disables Desktop/System.Speech rate calibration.
  /// </summary>
  public void SetMatchDesktopAndWindowsMediaRates(bool enabled)
  {
    _engine.SetMatchDesktopAndWindowsMediaRates(enabled);
  }
""")

  replace_once(
    "AgentPanelSpeaker/MainForm.cs",
    "  private readonly CheckBox _keepDisplayOnCheckBox = new();\n",
    """  private readonly CheckBox _keepDisplayOnCheckBox = new();
  private readonly CheckBox _matchDesktopAndWindowsMediaRatesCheckBox = new();
""")
  replace_once(
    "AgentPanelSpeaker/MainForm.cs",
    """    _toolTip.SetToolTip(
      _keepDisplayOnCheckBox,
      "Prevents Windows from turning off the display during active speech.");
    _fenceDebounceTimer.Interval = 1000;
""",
    """    _toolTip.SetToolTip(
      _keepDisplayOnCheckBox,
      "Prevents Windows from turning off the display during active speech.");
    _matchDesktopAndWindowsMediaRatesCheckBox.AutoSize = true;
    _matchDesktopAndWindowsMediaRatesCheckBox.Margin =
      new Padding(3, 6, 3, 3);
    _matchDesktopAndWindowsMediaRatesCheckBox.Text =
      "Match Desktop and Windows Media rates";
    _toolTip.SetToolTip(
      _matchDesktopAndWindowsMediaRatesCheckBox,
      "Desktop/System.Speech and Windows Media use different rate scales. " +
      "Enable this to make their speaking rates closer; this compresses the " +
      "Desktop voice's available native rate range.");
    _fenceDebounceTimer.Interval = 1000;
""")
  replace_once(
    "AgentPanelSpeaker/MainForm.cs",
    """      _pronunciationsButton,
      _speakExistingCheckBox, _keepDisplayOnCheckBox
    });
""",
    """      _pronunciationsButton,
      _speakExistingCheckBox, _keepDisplayOnCheckBox,
      _matchDesktopAndWindowsMediaRatesCheckBox
    });
""")
  replace_once(
    "AgentPanelSpeaker/MainForm.cs",
    """      _pronunciationsButton,
      _speakExistingCheckBox,
      _keepDisplayOnCheckBox);
""",
    """      _pronunciationsButton,
      _speakExistingCheckBox,
      _keepDisplayOnCheckBox,
      _matchDesktopAndWindowsMediaRatesCheckBox);
""")
  replace_once(
    "AgentPanelSpeaker/MainForm.cs",
    """    UiText.Apply(_keepDisplayOnCheckBox, "Main.KeepDisplayOn", _toolTip);
""",
    """    UiText.Apply(_keepDisplayOnCheckBox, "Main.KeepDisplayOn", _toolTip);
    UiText.Apply(
      _matchDesktopAndWindowsMediaRatesCheckBox,
      "Main.MatchDesktopAndWindowsMediaRates",
      _toolTip);
""")
  replace_once(
    "AgentPanelSpeaker/MainForm.cs",
    """    _keepDisplayOnCheckBox.CheckedChanged += (_, _) =>
    {
      SaveControlsToSettings();
      UpdateDisplayAwakeState();
    };
""",
    """    _keepDisplayOnCheckBox.CheckedChanged += (_, _) =>
    {
      SaveControlsToSettings();
      UpdateDisplayAwakeState();
    };
    _matchDesktopAndWindowsMediaRatesCheckBox.CheckedChanged += (_, _) =>
    {
      SaveControlsToSettings();
      _speech.SetMatchDesktopAndWindowsMediaRates(
        _matchDesktopAndWindowsMediaRatesCheckBox.Checked);
    };
""")
  replace_once(
    "AgentPanelSpeaker/MainForm.cs",
    """      _keepDisplayOnCheckBox.Checked =
        settings.KeepDisplayOnWhileSpeaking;
      _fenceTypesTextBox.Text = settings.SpokenFencedCodeTypes;
""",
    """      _keepDisplayOnCheckBox.Checked =
        settings.KeepDisplayOnWhileSpeaking;
      _matchDesktopAndWindowsMediaRatesCheckBox.Checked =
        settings.MatchDesktopAndWindowsMediaRates;
      _fenceTypesTextBox.Text = settings.SpokenFencedCodeTypes;
""")
  replace_once(
    "AgentPanelSpeaker/MainForm.cs",
    """      _speech.SetWindowsMediaBookmarkMode(
        WindowsMediaBookmarkMode.Always);
""",
    """      _speech.SetWindowsMediaBookmarkMode(
        WindowsMediaBookmarkMode.Always);
      _speech.SetMatchDesktopAndWindowsMediaRates(
        settings.MatchDesktopAndWindowsMediaRates);
""")
  replace_once(
    "AgentPanelSpeaker/MainForm.cs",
    """      KeepDisplayOnWhileSpeaking = _keepDisplayOnCheckBox.Checked,
      PollIntervalMilliseconds = Decimal.ToInt32(_pollNumeric.Value),
""",
    """      KeepDisplayOnWhileSpeaking = _keepDisplayOnCheckBox.Checked,
      MatchDesktopAndWindowsMediaRates =
        _matchDesktopAndWindowsMediaRatesCheckBox.Checked,
      PollIntervalMilliseconds = Decimal.ToInt32(_pollNumeric.Value),
""")

  replace_once(
    "AgentPanelSpeaker/Resources/Strings.resx",
    """  <data name="Main.KeepDisplayOn.Name" xml:space="preserve"><value>Keep display on while speaking</value></data>
""",
    """  <data name="Main.KeepDisplayOn.Name" xml:space="preserve"><value>Keep display on while speaking</value></data>
  <data name="Main.MatchDesktopAndWindowsMediaRates.Name" xml:space="preserve"><value>Match Desktop and Windows Media rates</value></data>
  <data name="Main.MatchDesktopAndWindowsMediaRates.Description" xml:space="preserve"><value>Calibrates Desktop/System.Speech speaking rates toward the Windows Media rate scale.</value></data>
  <data name="Main.MatchDesktopAndWindowsMediaRates.Text" xml:space="preserve"><value>Match Desktop and Windows Media rates</value></data>
  <data name="Main.MatchDesktopAndWindowsMediaRates.Tooltip" xml:space="preserve"><value>Desktop/System.Speech and Windows Media use different rate scales. Enable this to make their speaking rates closer; this compresses the Desktop voice&apos;s available native rate range.</value></data>
""")


if len(sys.argv) != 2 or sys.argv[1] not in {"tests", "production"}:
  raise SystemExit("usage: apply_issue94_rate_toggle.py tests|production")

if sys.argv[1] == "tests":
  apply_tests()
else:
  apply_production()
