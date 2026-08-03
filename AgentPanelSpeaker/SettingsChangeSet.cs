namespace AgentPanelSpeaker;

/// <summary>
/// Describes and selectively merges differences between saved and working settings.
/// </summary>
internal static class SettingsChangeSet
{
  internal sealed record Change(string Key, string DisplayName);

  public static IReadOnlyList<Change> GetChanges(
    UserSettings saved,
    UserSettings working)
  {
    var changes = new List<Change>();
    Add(nameof(UserSettings.Source), "Session source", saved.Source, working.Source);
    Add(nameof(UserSettings.FollowNewestSession), "Auto-follow newest session", saved.FollowNewestSession, working.FollowNewestSession);
    Add(nameof(UserSettings.ManualSessionPath), "Selected session path", saved.ManualSessionPath, working.ManualSessionPath);
    Add(nameof(UserSettings.Assistant), "Assistant voice settings", saved.Assistant, working.Assistant);
    Add(nameof(UserSettings.Reasoning), "Thoughts voice settings", saved.Reasoning, working.Reasoning);
    Add(nameof(UserSettings.SubagentAssistant), "Subagent assistant voice settings", saved.SubagentAssistant, working.SubagentAssistant);
    Add(nameof(UserSettings.SubagentReasoning), "Subagent thoughts voice settings", saved.SubagentReasoning, working.SubagentReasoning);
    Add(nameof(UserSettings.User), "User voice settings", saved.User, working.User);
    Add(nameof(UserSettings.UserContext), "User-context voice settings", saved.UserContext, working.UserContext);
    Add(nameof(UserSettings.SpokenFencedCodeTypes), "Spoken fenced-code types", saved.SpokenFencedCodeTypes, working.SpokenFencedCodeTypes);
    Add(nameof(UserSettings.SpeakLastExistingEnabledMessage), "Speak existing text on startup", saved.SpeakLastExistingEnabledMessage, working.SpeakLastExistingEnabledMessage);
    Add(nameof(UserSettings.KeepDisplayOnWhileSpeaking), "Keep display on while speaking", saved.KeepDisplayOnWhileSpeaking, working.KeepDisplayOnWhileSpeaking);
    Add(nameof(UserSettings.PollIntervalMilliseconds), "Polling interval", saved.PollIntervalMilliseconds, working.PollIntervalMilliseconds);
    Add(nameof(UserSettings.Theme), "Theme", saved.Theme, working.Theme);
    Add(nameof(UserSettings.WindowsMediaBookmarks), "Windows.Media highlight timing", saved.WindowsMediaBookmarks, working.WindowsMediaBookmarks);
    Add(nameof(UserSettings.Transcript), "Transcript settings", saved.Transcript, working.Transcript);
    Add(nameof(UserSettings.SpelledWords), "Spelled words", saved.SpelledWords, working.SpelledWords);
    Add(nameof(UserSettings.Pronunciations), "Pronunciation rules", saved.Pronunciations, working.Pronunciations);
    Add(nameof(UserSettings.AudioWake), "Bluetooth wake settings", saved.AudioWake, working.AudioWake);
    Add(nameof(UserSettings.Hotkeys), "Hotkeys", saved.Hotkeys, working.Hotkeys);
    if (saved.WindowX != working.WindowX ||
        saved.WindowY != working.WindowY ||
        saved.WindowWidth != working.WindowWidth ||
        saved.WindowHeight != working.WindowHeight ||
        saved.HasWindowPlacement != working.HasWindowPlacement)
    {
      changes.Add(new Change("WindowPlacement", "Window size and position"));
    }
    return changes;

    void Add<T>(string key, string displayName, T oldValue, T newValue)
    {
      if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
      {
        changes.Add(new Change(key, displayName));
      }
    }
  }

  public static UserSettings MergeSelected(
    UserSettings saved,
    UserSettings working,
    IReadOnlySet<string> selected)
  {
    bool Has(string key) => selected.Contains(key);
    UserSettings merged = saved with
    {
      Source = Has(nameof(UserSettings.Source)) ? working.Source : saved.Source,
      FollowNewestSession = Has(nameof(UserSettings.FollowNewestSession)) ? working.FollowNewestSession : saved.FollowNewestSession,
      ManualSessionPath = Has(nameof(UserSettings.ManualSessionPath)) ? working.ManualSessionPath : saved.ManualSessionPath,
      Assistant = Has(nameof(UserSettings.Assistant)) ? working.Assistant : saved.Assistant,
      Reasoning = Has(nameof(UserSettings.Reasoning)) ? working.Reasoning : saved.Reasoning,
      SubagentAssistant = Has(nameof(UserSettings.SubagentAssistant)) ? working.SubagentAssistant : saved.SubagentAssistant,
      SubagentReasoning = Has(nameof(UserSettings.SubagentReasoning)) ? working.SubagentReasoning : saved.SubagentReasoning,
      User = Has(nameof(UserSettings.User)) ? working.User : saved.User,
      UserContext = Has(nameof(UserSettings.UserContext)) ? working.UserContext : saved.UserContext,
      SpokenFencedCodeTypes = Has(nameof(UserSettings.SpokenFencedCodeTypes)) ? working.SpokenFencedCodeTypes : saved.SpokenFencedCodeTypes,
      SpeakLastExistingEnabledMessage = Has(nameof(UserSettings.SpeakLastExistingEnabledMessage)) ? working.SpeakLastExistingEnabledMessage : saved.SpeakLastExistingEnabledMessage,
      KeepDisplayOnWhileSpeaking = Has(nameof(UserSettings.KeepDisplayOnWhileSpeaking)) ? working.KeepDisplayOnWhileSpeaking : saved.KeepDisplayOnWhileSpeaking,
      PollIntervalMilliseconds = Has(nameof(UserSettings.PollIntervalMilliseconds)) ? working.PollIntervalMilliseconds : saved.PollIntervalMilliseconds,
      Theme = Has(nameof(UserSettings.Theme)) ? working.Theme : saved.Theme,
      WindowsMediaBookmarks = Has(nameof(UserSettings.WindowsMediaBookmarks)) ? working.WindowsMediaBookmarks : saved.WindowsMediaBookmarks,
      Transcript = Has(nameof(UserSettings.Transcript)) ? working.Transcript : saved.Transcript,
      SpelledWords = Has(nameof(UserSettings.SpelledWords)) ? working.SpelledWords : saved.SpelledWords,
      Pronunciations = Has(nameof(UserSettings.Pronunciations)) ? working.Pronunciations : saved.Pronunciations,
      AudioWake = Has(nameof(UserSettings.AudioWake)) ? working.AudioWake : saved.AudioWake,
      Hotkeys = Has(nameof(UserSettings.Hotkeys)) ? working.Hotkeys : saved.Hotkeys
    };
    if (Has("WindowPlacement"))
    {
      merged = merged with
      {
        WindowX = working.WindowX,
        WindowY = working.WindowY,
        WindowWidth = working.WindowWidth,
        WindowHeight = working.WindowHeight,
        HasWindowPlacement = working.HasWindowPlacement
      };
    }
    return merged;
  }
}
