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
  public const int CurrentVersion = 2;

  /// <summary>
  /// Creates defaults using the first installed voice for assistant output.
  /// </summary>
  public static UserSettings CreateDefault(string? defaultVoice)
  {
    string voice = string.IsNullOrWhiteSpace(defaultVoice)
      ? SpeechProfileSettings.NotSpoken
      : defaultVoice;
    return new UserSettings(
      CurrentVersion,
      AgentSource.Auto,
      FollowNewestSession: true,
      ManualSessionPath: null,
      new SpeechProfileSettings(voice, 0, 0) { Volume = 100 },
      new SpeechProfileSettings(voice, 0, 0) { Volume = 100 },
      new SpeechProfileSettings(
        SpeechProfileSettings.NotSpoken,
        0,
        0)
      {
        Volume = 100
      },
      SpokenFencedCodeTypes: string.Empty,
      SpeakLastExistingEnabledMessage: false,
      PollIntervalMilliseconds: 150,
      WindowX: 0,
      WindowY: 0,
      WindowWidth: 1120,
      WindowHeight: 900,
      HasWindowPlacement: false);
  }

  /// <summary>
  /// Gets one category's profile.
  /// </summary>
  public SpeechProfileSettings GetProfile(ContentCategory category)
  {
    return category switch
    {
      ContentCategory.Assistant => Assistant,
      ContentCategory.Reasoning => Reasoning,
      ContentCategory.User => User,
      _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };
  }
}
