namespace AgentPanelSpeaker;

/// <summary>
/// Persists all user-configurable application behavior.
/// </summary>
internal sealed record UserSettings(
  int Version,
  AgentSource Source,
  bool FollowNewestSession,
  string? ManualSessionPath,
  SpeechProfileSettings Assistant,
  SpeechProfileSettings Reasoning,
  SpeechProfileSettings User,
  string SpokenFencedCodeTypes,
  bool SpeakLastExistingEnabledMessage,
  int PollIntervalMilliseconds,
  int WindowX,
  int WindowY,
  int WindowWidth,
  int WindowHeight,
  bool HasWindowPlacement)
{
  public const int CurrentVersion = 14;

  /// <summary>
  /// Gets the main speech profile used for background-agent results.
  /// </summary>
  public SpeechProfileSettings SubagentAssistant { get; init; } =
    new(SpeechProfileSettings.NotSpoken, 0, 0) { Volume = 100 };

  /// <summary>
  /// Gets the context speech profile used for subagent reasoning.
  /// </summary>
  public SpeechProfileSettings SubagentReasoning { get; init; } =
    new(SpeechProfileSettings.NotSpoken, 0, 0) { Volume = 100 };

  /// <summary>
  /// Gets the User context profile used for explicit Markdown blockquotes.
  /// </summary>
  public SpeechProfileSettings UserContext { get; init; } =
    new(SpeechProfileSettings.NotSpoken, 0, 0) { Volume = 100 };

  /// <summary>
  /// Gets the one-token-per-line list that should be spelled out.
  /// </summary>
  public string SpelledWords { get; init; } = string.Empty;

  /// <summary>
  /// Gets case-sensitive and /i spoken-text or IPA pronunciation rules.
  /// </summary>
  public string Pronunciations { get; init; } = string.Empty;

  /// <summary>
  /// Gets the optional Bluetooth wake-tone settings.
  /// </summary>
  public AudioWakeSettings AudioWake { get; init; } =
    AudioWakeSettings.Default;

  /// <summary>
  /// Gets the requested light, dark, or Windows-following theme.
  /// </summary>
  public AppTheme Theme { get; init; } = AppTheme.System;

  /// <summary>
  /// Gets when Windows.Media voices use bookmark/compaction timing.
  /// </summary>
  public WindowsMediaBookmarkMode WindowsMediaBookmarks { get; init; } =
    WindowsMediaBookmarkMode.Always;

  public HotkeySettings Hotkeys { get; init; } = HotkeySettings.Default;

  /// <summary>
  /// Creates defaults using the first installed voice for assistant output.
  /// </summary>
  public static UserSettings CreateDefault(string? defaultVoice)
  {
    string voice = string.IsNullOrWhiteSpace(defaultVoice)
      ? SpeechProfileSettings.NotSpoken
      : defaultVoice;
    var userProfile = new SpeechProfileSettings(
      SpeechProfileSettings.NotSpoken,
      0,
      0)
    {
      Volume = 100
    };
    return new UserSettings(
      CurrentVersion,
      AgentSource.Auto,
      FollowNewestSession: true,
      ManualSessionPath: null,
      new SpeechProfileSettings(voice, 0, 0) { Volume = 100 },
      new SpeechProfileSettings(voice, 0, 0) { Volume = 100 },
      userProfile,
      SpokenFencedCodeTypes: string.Empty,
      SpeakLastExistingEnabledMessage: false,
      PollIntervalMilliseconds: 150,
      WindowX: 0,
      WindowY: 0,
      WindowWidth: 1120,
      WindowHeight: 900,
      HasWindowPlacement: false)
    {
      SpelledWords = "IDE",
      Pronunciations = string.Empty,
      AudioWake = AudioWakeSettings.Default,
      Theme = AppTheme.System,
      WindowsMediaBookmarks = WindowsMediaBookmarkMode.Always,
      Hotkeys = HotkeySettings.Default,
      SubagentAssistant = new SpeechProfileSettings(voice, 0, 0)
      {
        Volume = 100
      },
      SubagentReasoning = new SpeechProfileSettings(voice, 0, 0)
      {
        Volume = 100
      },
      UserContext = userProfile,
      KeepDisplayOnWhileSpeaking = false,
      Transcript = TranscriptSettings.Default
    };
  }


  /// <summary>
  /// Gets rendered-transcript presentation and follow settings.
  /// </summary>
  public TranscriptSettings Transcript { get; init; } =
    TranscriptSettings.Default;

  /// <summary>
  /// Gets whether active speech should keep the Windows display awake.
  /// </summary>
  public bool KeepDisplayOnWhileSpeaking { get; init; }

  /// <summary>
  /// Gets one category's profile.
  /// </summary>
  public SpeechProfileSettings GetProfile(ContentCategory category)
  {
    return category switch
    {
      ContentCategory.Assistant => Assistant,
      ContentCategory.Reasoning => Reasoning,
      ContentCategory.SubagentAssistant => SubagentAssistant,
      ContentCategory.SubagentReasoning => SubagentReasoning,
      ContentCategory.User => User,
      ContentCategory.UserContext => UserContext,
      _ => throw new ArgumentOutOfRangeException(
        nameof(category),
        category,
        null)
    };
  }
}
