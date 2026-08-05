namespace AgentPanelSpeaker;

/// <summary>
/// Versioned hierarchical representation written to settings.json.
/// </summary>
internal sealed record SettingsDocument
{
  public const int CurrentSchemaVersion = 16;

  public int SchemaVersion { get; init; } = CurrentSchemaVersion;
  public SessionSettingsDocument Session { get; init; } = new();
  public SpeechSettingsDocument Speech { get; init; } = new();
  public GeneralSettingsDocument General { get; init; } = new();
  public TranscriptSettings Transcript { get; init; } = TranscriptSettings.Default;
  public AudioWakeSettings BluetoothWake { get; init; } = AudioWakeSettings.Default;
  public HotkeySettings Hotkeys { get; init; } = HotkeySettings.Default;
  public OrderedTextSettingsDocument Collections { get; init; } = new();
  public WindowSettingsDocument Window { get; init; } = new();

  public static SettingsDocument FromRuntime(UserSettings settings) => new()
  {
    Session = new SessionSettingsDocument
    {
      Source = settings.Source,
      FollowNewestSession = settings.FollowNewestSession,
      ManualSessionPath = settings.ManualSessionPath
    },
    Speech = new SpeechSettingsDocument
    {
      Master = SpeechAdjustmentSettingsDocument.FromMaster(settings.MasterSpeech),
      Assistant = SpeechRoleSettingsDocument.FromProfiles(
        settings.Assistant,
        settings.Reasoning),
      Subagent = SpeechRoleSettingsDocument.FromProfiles(
        settings.SubagentAssistant,
        settings.SubagentReasoning),
      User = SpeechRoleSettingsDocument.FromProfiles(
        settings.User,
        settings.UserContext),
      SpokenCodeBlockTypes = settings.SpokenFencedCodeTypes,
      SpeakLatestExistingMessageOnStartup =
        settings.SpeakLastExistingEnabledMessage,
      KeepDisplayOnWhileSpeaking = settings.KeepDisplayOnWhileSpeaking
    },
    General = new GeneralSettingsDocument
    {
      PollIntervalMilliseconds = settings.PollIntervalMilliseconds,
      Theme = settings.Theme
    },
    Transcript = settings.Transcript,
    BluetoothWake = settings.AudioWake,
    Hotkeys = settings.Hotkeys,
    Collections = new OrderedTextSettingsDocument
    {
      SpelledWords = settings.SpelledWords,
      Pronunciations = settings.Pronunciations
    },
    Window = new WindowSettingsDocument
    {
      X = settings.WindowX,
      Y = settings.WindowY,
      Width = settings.WindowWidth,
      Height = settings.WindowHeight,
      HasPlacement = settings.HasWindowPlacement
    }
  };

  public UserSettings ToRuntime(IReadOnlyList<string> defaultVoices)
  {
    UserSettings defaults = UserSettings.CreateDefault(defaultVoices);
    SpeechRoleSettingsDocument assistant = Speech.Assistant ?? new();
    SpeechRoleSettingsDocument subagent = Speech.Subagent ?? new();
    SpeechRoleSettingsDocument user = Speech.User ?? new();
    return defaults with
    {
      Version = UserSettings.CurrentVersion,
      MasterSpeech = Speech.Master.ToMaster(),
      Source = Session.Source,
      FollowNewestSession = Session.FollowNewestSession,
      ManualSessionPath = Session.ManualSessionPath,
      Assistant = assistant.ToMainProfile(defaults.Assistant),
      Reasoning = assistant.ToSecondaryProfile(defaults.Reasoning),
      SubagentAssistant = subagent.ToMainProfile(defaults.SubagentAssistant),
      SubagentReasoning = subagent.ToSecondaryProfile(defaults.SubagentReasoning),
      User = user.ToMainProfile(defaults.User),
      UserContext = user.ToSecondaryProfile(defaults.UserContext),
      SpokenFencedCodeTypes = Speech.SpokenCodeBlockTypes ?? string.Empty,
      SpeakLastExistingEnabledMessage =
        Speech.SpeakLatestExistingMessageOnStartup,
      KeepDisplayOnWhileSpeaking = Speech.KeepDisplayOnWhileSpeaking,
      PollIntervalMilliseconds = General.PollIntervalMilliseconds,
      Theme = General.Theme,
      Transcript = Transcript ?? TranscriptSettings.Default,
      AudioWake = BluetoothWake ?? AudioWakeSettings.Default,
      Hotkeys = Hotkeys ?? HotkeySettings.Default,
      SpelledWords = Collections.SpelledWords ?? string.Empty,
      Pronunciations = Collections.Pronunciations ?? string.Empty,
      WindowX = Window.X,
      WindowY = Window.Y,
      WindowWidth = Window.Width,
      WindowHeight = Window.Height,
      HasWindowPlacement = Window.HasPlacement
    };
  }
}

internal sealed record SessionSettingsDocument
{
  public AgentSource Source { get; init; } = AgentSource.Auto;
  public bool FollowNewestSession { get; init; } = true;
  public string? ManualSessionPath { get; init; }
}

internal sealed record SpeechSettingsDocument
{
  public SpeechAdjustmentSettingsDocument Master { get; init; } = new();
  public SpeechRoleSettingsDocument Assistant { get; init; } = new();
  public SpeechRoleSettingsDocument Subagent { get; init; } = new();
  public SpeechRoleSettingsDocument User { get; init; } = new();
  public string SpokenCodeBlockTypes { get; init; } = string.Empty;
  public bool SpeakLatestExistingMessageOnStartup { get; init; }
  public bool KeepDisplayOnWhileSpeaking { get; init; }
}

internal sealed record SpeechRoleSettingsDocument
{
  public string Voice { get; init; } = SpeechProfileSettings.NotSpoken;
  public SpeechAdjustmentSettingsDocument Main { get; init; } = new();
  public SpeechAdjustmentSettingsDocument Secondary { get; init; } = new();

  public static SpeechRoleSettingsDocument FromProfiles(
    SpeechProfileSettings main,
    SpeechProfileSettings secondary) => new()
  {
    Voice = main.VoiceName,
    Main = SpeechAdjustmentSettingsDocument.FromProfile(main),
    Secondary = SpeechAdjustmentSettingsDocument.FromProfile(secondary)
  };

  public SpeechProfileSettings ToMainProfile(SpeechProfileSettings fallback) =>
    Main.ToProfile(Voice, fallback);

  public SpeechProfileSettings ToSecondaryProfile(
    SpeechProfileSettings fallback) => Secondary.ToProfile(Voice, fallback);
}

internal sealed record SpeechAdjustmentSettingsDocument
{
  public int Rate { get; init; }
  public int Pitch { get; init; }
  public int Volume { get; init; } = 100;

  public static SpeechAdjustmentSettingsDocument FromMaster(
    SpeechMasterSettings master) => new()
  {
    Rate = master.Rate,
    Pitch = master.Pitch,
    Volume = master.Volume
  };

  public SpeechMasterSettings ToMaster() =>
    new SpeechMasterSettings(Rate, Pitch, Volume).Normalize();

  public static SpeechAdjustmentSettingsDocument FromProfile(
    SpeechProfileSettings profile) => new()
  {
    Rate = profile.Rate,
    Pitch = profile.Pitch,
    Volume = profile.Volume
  };

  public SpeechProfileSettings ToProfile(
    string? voice,
    SpeechProfileSettings fallback) => new(
      string.IsNullOrWhiteSpace(voice) ? fallback.VoiceName : voice,
      Rate,
      Pitch)
    {
      Volume = Volume
    };
}

internal sealed record GeneralSettingsDocument
{
  public int PollIntervalMilliseconds { get; init; } = 150;
  public AppTheme Theme { get; init; } = AppTheme.System;
}

internal sealed record OrderedTextSettingsDocument
{
  public string SpelledWords { get; init; } = string.Empty;
  public string Pronunciations { get; init; } = string.Empty;
}

internal sealed record WindowSettingsDocument
{
  public int X { get; init; }
  public int Y { get; init; }
  public int Width { get; init; } = 1120;
  public int Height { get; init; } = 900;
  public bool HasPlacement { get; init; }
}
