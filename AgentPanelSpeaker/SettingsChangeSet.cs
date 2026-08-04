namespace AgentPanelSpeaker;

/// <summary>
/// Describes and selectively merges differences between saved and working settings.
/// </summary>
internal static class SettingsChangeSet
{
  internal sealed record Change(
    string Key,
    IReadOnlyList<string> Path,
    int Order = 0)
  {
    public string DisplayName => Path.Count == 0 ? Key : Path[^1];
  }

  public static IReadOnlyList<Change> GetChanges(
    UserSettings saved,
    UserSettings working)
  {
    var changes = new List<Change>();

    AddScalar("Session/Source", ["Session", "Source"], saved.Source, working.Source);
    AddScalar("Session/FollowNewest", ["Session", "Auto-follow newest session"], saved.FollowNewestSession, working.FollowNewestSession);
    AddScalar("Session/Path", ["Session", "Selected session path"], saved.ManualSessionPath, working.ManualSessionPath);

    AddProfile("Speech/Assistant", ["Speech", "Assistant"], saved.Assistant, working.Assistant);
    AddProfile("Speech/Thoughts", ["Speech", "Thoughts"], saved.Reasoning, working.Reasoning);
    AddProfile("Speech/Subagent/Assistant", ["Speech", "Subagent", "Assistant"], saved.SubagentAssistant, working.SubagentAssistant);
    AddProfile("Speech/Subagent/Thoughts", ["Speech", "Subagent", "Thoughts"], saved.SubagentReasoning, working.SubagentReasoning);
    AddProfile("Speech/User", ["Speech", "User"], saved.User, working.User);
    AddProfile("Speech/User/Context", ["Speech", "User", "Context"], saved.UserContext, working.UserContext);

    AddScalar("Speech/FencedCodeTypes", ["Speech", "Spoken fenced-code types"], saved.SpokenFencedCodeTypes, working.SpokenFencedCodeTypes);
    AddScalar("Speech/SpeakExisting", ["Speech", "Speak existing text on startup"], saved.SpeakLastExistingEnabledMessage, working.SpeakLastExistingEnabledMessage);
    AddScalar("Speech/KeepDisplayOn", ["Speech", "Keep display on while speaking"], saved.KeepDisplayOnWhileSpeaking, working.KeepDisplayOnWhileSpeaking);
    AddScalar("Speech/WindowsMediaBookmarks", ["Speech", "Windows.Media highlight timing"], saved.WindowsMediaBookmarks, working.WindowsMediaBookmarks);

    AddScalar("General/PollInterval", ["General", "Polling interval"], saved.PollIntervalMilliseconds, working.PollIntervalMilliseconds);
    AddScalar("General/Theme", ["General", "Theme"], saved.Theme, working.Theme);

    AddTranscript(saved.Transcript, working.Transcript);
    AddAudioWake(saved.AudioWake, working.AudioWake);
    AddHotkeys(saved.Hotkeys, working.Hotkeys);
    AddOrderedTextCollection("SpelledWords", "Spelled words", saved.SpelledWords, working.SpelledWords);
    AddPronunciations(saved.Pronunciations, working.Pronunciations);

    if (saved.WindowX != working.WindowX ||
        saved.WindowY != working.WindowY ||
        saved.WindowWidth != working.WindowWidth ||
        saved.WindowHeight != working.WindowHeight ||
        saved.HasWindowPlacement != working.HasWindowPlacement)
    {
      changes.Add(new Change(
        "Window/Placement",
        ["Window", "Size and position"]));
    }

    return changes;

    void AddScalar<T>(
      string key,
      IReadOnlyList<string> path,
      T oldValue,
      T newValue)
    {
      if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
      {
        changes.Add(new Change(key, path));
      }
    }

    void AddProfile(
      string keyPrefix,
      IReadOnlyList<string> pathPrefix,
      SpeechProfileSettings oldValue,
      SpeechProfileSettings newValue)
    {
      AddScalar($"{keyPrefix}/Voice", [.. pathPrefix, "Voice"], oldValue.VoiceName, newValue.VoiceName);
      AddScalar($"{keyPrefix}/Rate", [.. pathPrefix, "Rate"], oldValue.Rate, newValue.Rate);
      AddScalar($"{keyPrefix}/Pitch", [.. pathPrefix, "Pitch"], oldValue.Pitch, newValue.Pitch);
      AddScalar($"{keyPrefix}/Volume", [.. pathPrefix, "Volume"], oldValue.Volume, newValue.Volume);
    }

    void AddTranscript(TranscriptSettings oldValue, TranscriptSettings newValue)
    {
      AddScalar("Transcript/FollowSpeech", ["Transcript", "Follow speech"], oldValue.FollowSpeech, newValue.FollowSpeech);
      AddScalar("Transcript/LightHighlight", ["Transcript", "Light highlight colour"], oldValue.LightHighlightArgb, newValue.LightHighlightArgb);
      AddScalar("Transcript/DarkHighlight", ["Transcript", "Dark highlight colour"], oldValue.DarkHighlightArgb, newValue.DarkHighlightArgb);
      AddScalar("Transcript/Fade", ["Transcript", "Highlight fade"], oldValue.FadeMilliseconds, newValue.FadeMilliseconds);
      AddScalar("Transcript/UpdateInterval", ["Transcript", "Highlight update interval"], oldValue.HighlightUpdateMilliseconds, newValue.HighlightUpdateMilliseconds);
      AddScalar("Transcript/QueueCapacity", ["Transcript", "Highlight queue capacity"], oldValue.HighlightQueueCapacity, newValue.HighlightQueueCapacity);
      AddScalar("Transcript/Maximized", ["Transcript", "Maximized"], oldValue.Maximized, newValue.Maximized);
    }

    void AddAudioWake(AudioWakeSettings oldValue, AudioWakeSettings newValue)
    {
      AddScalar("BluetoothWake/Enabled", ["Bluetooth wake", "Enabled"], oldValue.Enabled, newValue.Enabled);
      AddScalar("BluetoothWake/QuietDuration", ["Bluetooth wake", "Quiet duration"], oldValue.QuietDurationMilliseconds, newValue.QuietDurationMilliseconds);
      AddScalar("BluetoothWake/Frequency", ["Bluetooth wake", "Frequency"], oldValue.FrequencyHertz, newValue.FrequencyHertz);
      AddScalar("BluetoothWake/ToneVolume", ["Bluetooth wake", "Tone volume"], oldValue.ToneVolume, newValue.ToneVolume);
      AddScalar("BluetoothWake/PlayDuration", ["Bluetooth wake", "Play duration"], oldValue.PlayDurationMilliseconds, newValue.PlayDurationMilliseconds);
      AddScalar("BluetoothWake/SettleDuration", ["Bluetooth wake", "Settle duration"], oldValue.SettleDurationMilliseconds, newValue.SettleDurationMilliseconds);
      AddScalar("BluetoothWake/IpaExampleDelay", ["Bluetooth wake", "IPA example delay"], oldValue.IpaExampleDelayMilliseconds, newValue.IpaExampleDelayMilliseconds);
    }

    void AddHotkeys(HotkeySettings oldValue, HotkeySettings newValue)
    {
      AddScalar("Hotkeys/PreviousSpeaker", ["Hotkeys", "Previous speaker"], oldValue.PreviousSpeaker, newValue.PreviousSpeaker);
      AddScalar("Hotkeys/PreviousNode", ["Hotkeys", "Previous node"], oldValue.PreviousNode, newValue.PreviousNode);
      AddScalar("Hotkeys/PreviousSentence", ["Hotkeys", "Previous sentence"], oldValue.PreviousSentence, newValue.PreviousSentence);
      AddScalar("Hotkeys/PlayPause", ["Hotkeys", "Play or pause"], oldValue.PlayPause, newValue.PlayPause);
      AddScalar("Hotkeys/NextSentence", ["Hotkeys", "Next sentence"], oldValue.NextSentence, newValue.NextSentence);
      AddScalar("Hotkeys/NextNode", ["Hotkeys", "Next node"], oldValue.NextNode, newValue.NextNode);
      AddScalar("Hotkeys/NextSpeaker", ["Hotkeys", "Next speaker"], oldValue.NextSpeaker, newValue.NextSpeaker);
      AddScalar("Hotkeys/ProcessingTime", ["Hotkeys", "Processing time"], oldValue.ProcessingTime, newValue.ProcessingTime);
      AddScalar("Hotkeys/ToggleTranscriptSize", ["Hotkeys", "Toggle transcript size"], oldValue.ToggleTranscriptSize, newValue.ToggleTranscriptSize);
    }

    void AddOrderedTextCollection(
      string keyPrefix,
      string heading,
      string oldText,
      string newText)
    {
      string[] oldItems = SplitLines(oldText);
      string[] newItems = SplitLines(newText);
      var oldSet = oldItems.ToHashSet(StringComparer.Ordinal);
      var newSet = newItems.ToHashSet(StringComparer.Ordinal);
      int order = 0;
      foreach (string item in newItems.Where(item => !oldSet.Contains(item)))
      {
        changes.Add(new Change($"{keyPrefix}/Added/{Escape(item)}", [heading, $"Added - {item}"], order++));
      }
      order = 0;
      foreach (string item in oldItems.Where(item => !newSet.Contains(item)))
      {
        changes.Add(new Change($"{keyPrefix}/Removed/{Escape(item)}", [heading, $"Removed - {item}"], 200000 + order++));
      }
    }

    void AddPronunciations(string oldText, string newText)
    {
      PronunciationRuleSet oldRules = PronunciationRuleSet.Parse(oldText);
      PronunciationRuleSet newRules = PronunciationRuleSet.Parse(newText);
      var oldByKey = oldRules.Rules.ToDictionary(RuleIdentity, StringComparer.Ordinal);
      var newByKey = newRules.Rules.ToDictionary(RuleIdentity, StringComparer.Ordinal);

      int order = 0;
      foreach (PronunciationRule rule in newRules.Rules)
      {
        string identity = RuleIdentity(rule);
        if (!oldByKey.ContainsKey(identity))
        {
          changes.Add(new Change($"Pronunciations/Added/{Escape(identity)}", ["Pronunciations", $"Added - {rule.Token}"], order++));
        }
      }
      order = 0;
      foreach (PronunciationRule rule in newRules.Rules)
      {
        string identity = RuleIdentity(rule);
        if (oldByKey.TryGetValue(identity, out PronunciationRule? oldRule) &&
            !EquivalentRule(oldRule, rule))
        {
          changes.Add(new Change($"Pronunciations/Modified/{Escape(identity)}", ["Pronunciations", $"Modified - {rule.Token}"], 100000 + order++));
        }
      }
      order = 0;
      foreach (PronunciationRule rule in oldRules.Rules)
      {
        string identity = RuleIdentity(rule);
        if (!newByKey.ContainsKey(identity))
        {
          changes.Add(new Change($"Pronunciations/Removed/{Escape(identity)}", ["Pronunciations", $"Removed - {rule.Token}"], 200000 + order++));
        }
      }
    }
  }

  public static UserSettings MergeSelected(
    UserSettings saved,
    UserSettings working,
    IReadOnlySet<string> selected)
  {
    bool Has(string key) => selected.Contains(key);
    T Pick<T>(string key, T oldValue, T newValue) => Has(key) ? newValue : oldValue;

    SpeechProfileSettings MergeProfile(
      string prefix,
      SpeechProfileSettings oldValue,
      SpeechProfileSettings newValue) => oldValue with
    {
      VoiceName = Pick($"{prefix}/Voice", oldValue.VoiceName, newValue.VoiceName),
      Rate = Pick($"{prefix}/Rate", oldValue.Rate, newValue.Rate),
      Pitch = Pick($"{prefix}/Pitch", oldValue.Pitch, newValue.Pitch),
      Volume = Pick($"{prefix}/Volume", oldValue.Volume, newValue.Volume)
    };

    var merged = saved with
    {
      Source = Pick("Session/Source", saved.Source, working.Source),
      FollowNewestSession = Pick("Session/FollowNewest", saved.FollowNewestSession, working.FollowNewestSession),
      ManualSessionPath = Pick("Session/Path", saved.ManualSessionPath, working.ManualSessionPath),
      Assistant = MergeProfile("Speech/Assistant", saved.Assistant, working.Assistant),
      Reasoning = MergeProfile("Speech/Thoughts", saved.Reasoning, working.Reasoning),
      SubagentAssistant = MergeProfile("Speech/Subagent/Assistant", saved.SubagentAssistant, working.SubagentAssistant),
      SubagentReasoning = MergeProfile("Speech/Subagent/Thoughts", saved.SubagentReasoning, working.SubagentReasoning),
      User = MergeProfile("Speech/User", saved.User, working.User),
      UserContext = MergeProfile("Speech/User/Context", saved.UserContext, working.UserContext),
      SpokenFencedCodeTypes = Pick("Speech/FencedCodeTypes", saved.SpokenFencedCodeTypes, working.SpokenFencedCodeTypes),
      SpeakLastExistingEnabledMessage = Pick("Speech/SpeakExisting", saved.SpeakLastExistingEnabledMessage, working.SpeakLastExistingEnabledMessage),
      KeepDisplayOnWhileSpeaking = Pick("Speech/KeepDisplayOn", saved.KeepDisplayOnWhileSpeaking, working.KeepDisplayOnWhileSpeaking),
      WindowsMediaBookmarks = Pick("Speech/WindowsMediaBookmarks", saved.WindowsMediaBookmarks, working.WindowsMediaBookmarks),
      PollIntervalMilliseconds = Pick("General/PollInterval", saved.PollIntervalMilliseconds, working.PollIntervalMilliseconds),
      Theme = Pick("General/Theme", saved.Theme, working.Theme),
      Transcript = saved.Transcript with
      {
        FollowSpeech = Pick("Transcript/FollowSpeech", saved.Transcript.FollowSpeech, working.Transcript.FollowSpeech),
        LightHighlightArgb = Pick("Transcript/LightHighlight", saved.Transcript.LightHighlightArgb, working.Transcript.LightHighlightArgb),
        DarkHighlightArgb = Pick("Transcript/DarkHighlight", saved.Transcript.DarkHighlightArgb, working.Transcript.DarkHighlightArgb),
        FadeMilliseconds = Pick("Transcript/Fade", saved.Transcript.FadeMilliseconds, working.Transcript.FadeMilliseconds),
        HighlightUpdateMilliseconds = Pick("Transcript/UpdateInterval", saved.Transcript.HighlightUpdateMilliseconds, working.Transcript.HighlightUpdateMilliseconds),
        HighlightQueueCapacity = Pick("Transcript/QueueCapacity", saved.Transcript.HighlightQueueCapacity, working.Transcript.HighlightQueueCapacity),
        Maximized = Pick("Transcript/Maximized", saved.Transcript.Maximized, working.Transcript.Maximized)
      },
      AudioWake = saved.AudioWake with
      {
        Enabled = Pick("BluetoothWake/Enabled", saved.AudioWake.Enabled, working.AudioWake.Enabled),
        QuietDurationMilliseconds = Pick("BluetoothWake/QuietDuration", saved.AudioWake.QuietDurationMilliseconds, working.AudioWake.QuietDurationMilliseconds),
        FrequencyHertz = Pick("BluetoothWake/Frequency", saved.AudioWake.FrequencyHertz, working.AudioWake.FrequencyHertz),
        ToneVolume = Pick("BluetoothWake/ToneVolume", saved.AudioWake.ToneVolume, working.AudioWake.ToneVolume),
        PlayDurationMilliseconds = Pick("BluetoothWake/PlayDuration", saved.AudioWake.PlayDurationMilliseconds, working.AudioWake.PlayDurationMilliseconds),
        SettleDurationMilliseconds = Pick("BluetoothWake/SettleDuration", saved.AudioWake.SettleDurationMilliseconds, working.AudioWake.SettleDurationMilliseconds),
        IpaExampleDelayMilliseconds = Pick("BluetoothWake/IpaExampleDelay", saved.AudioWake.IpaExampleDelayMilliseconds, working.AudioWake.IpaExampleDelayMilliseconds)
      },
      Hotkeys = saved.Hotkeys with
      {
        PreviousSpeaker = Pick("Hotkeys/PreviousSpeaker", saved.Hotkeys.PreviousSpeaker, working.Hotkeys.PreviousSpeaker),
        PreviousNode = Pick("Hotkeys/PreviousNode", saved.Hotkeys.PreviousNode, working.Hotkeys.PreviousNode),
        PreviousSentence = Pick("Hotkeys/PreviousSentence", saved.Hotkeys.PreviousSentence, working.Hotkeys.PreviousSentence),
        PlayPause = Pick("Hotkeys/PlayPause", saved.Hotkeys.PlayPause, working.Hotkeys.PlayPause),
        NextSentence = Pick("Hotkeys/NextSentence", saved.Hotkeys.NextSentence, working.Hotkeys.NextSentence),
        NextNode = Pick("Hotkeys/NextNode", saved.Hotkeys.NextNode, working.Hotkeys.NextNode),
        NextSpeaker = Pick("Hotkeys/NextSpeaker", saved.Hotkeys.NextSpeaker, working.Hotkeys.NextSpeaker),
        ProcessingTime = Pick("Hotkeys/ProcessingTime", saved.Hotkeys.ProcessingTime, working.Hotkeys.ProcessingTime),
        ToggleTranscriptSize = Pick("Hotkeys/ToggleTranscriptSize", saved.Hotkeys.ToggleTranscriptSize, working.Hotkeys.ToggleTranscriptSize)
      },
      SpelledWords = MergeOrderedTextCollection("SpelledWords", saved.SpelledWords, working.SpelledWords, selected),
      Pronunciations = MergePronunciations(saved.Pronunciations, working.Pronunciations, selected)
    };

    if (Has("Window/Placement"))
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

  private static string MergeOrderedTextCollection(
    string prefix,
    string oldText,
    string newText,
    IReadOnlySet<string> selected)
  {
    var result = SplitLines(oldText).ToList();
    string[] working = SplitLines(newText);
    foreach (string item in working)
    {
      if (!result.Contains(item, StringComparer.Ordinal) &&
          selected.Contains($"{prefix}/Added/{Escape(item)}"))
      {
        result.Add(item);
      }
    }
    result.RemoveAll(item =>
      !working.Contains(item, StringComparer.Ordinal) &&
      selected.Contains($"{prefix}/Removed/{Escape(item)}"));
    return string.Join(Environment.NewLine, result);
  }

  private static string MergePronunciations(
    string oldText,
    string newText,
    IReadOnlySet<string> selected)
  {
    var result = PronunciationRuleSet.Parse(oldText).Rules.ToList();
    IReadOnlyList<PronunciationRule> working = PronunciationRuleSet.Parse(newText).Rules;
    var workingByKey = working.ToDictionary(RuleIdentity, StringComparer.Ordinal);

    foreach (PronunciationRule rule in working)
    {
      string identity = RuleIdentity(rule);
      int existing = result.FindIndex(item => RuleIdentity(item) == identity);
      if (existing < 0 && selected.Contains($"Pronunciations/Added/{Escape(identity)}"))
      {
        result.Add(rule);
      }
      else if (existing >= 0 && !EquivalentRule(result[existing], rule) &&
               selected.Contains($"Pronunciations/Modified/{Escape(identity)}"))
      {
        result[existing] = rule;
      }
    }

    result.RemoveAll(rule =>
    {
      string identity = RuleIdentity(rule);
      return !workingByKey.ContainsKey(identity) &&
        selected.Contains($"Pronunciations/Removed/{Escape(identity)}");
    });

    return string.Join(
      Environment.NewLine,
      result.Select(rule =>
        $"{rule.Token}{(rule.IgnoreCase ? "/i" : string.Empty)}=" +
        $"{(rule.Kind == PronunciationRuleKind.Ipa ? "ipa:" : string.Empty)}" +
        rule.Value));
  }

  private static string[] SplitLines(string text) => (text ?? string.Empty)
    .Replace("\r\n", "\n")
    .Replace('\r', '\n')
    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

  private static string RuleIdentity(PronunciationRule rule) =>
    (rule.IgnoreCase ? "i:" : "e:") +
    (rule.IgnoreCase ? rule.Token.ToUpperInvariant() : rule.Token);

  private static bool EquivalentRule(PronunciationRule left, PronunciationRule right) =>
    left.Token == right.Token &&
    left.Value == right.Value &&
    left.Kind == right.Kind &&
    left.IgnoreCase == right.IgnoreCase;

  private static string Escape(string value) => Uri.EscapeDataString(value);
}
