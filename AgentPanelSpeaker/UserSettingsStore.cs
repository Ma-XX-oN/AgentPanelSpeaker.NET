using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPanelSpeaker;

/// <summary>
/// Loads and atomically saves immutable settings snapshots.
/// </summary>
internal sealed class UserSettingsStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
  };

  private readonly object _sync = new();
  private readonly HashSet<string> _installedVoices;
  private UserSettings _current;

  /// <summary>
  /// Initializes the store and loads the persisted snapshot when valid.
  /// </summary>
  public UserSettingsStore(IReadOnlyList<string> installedVoices)
  {
    _installedVoices = new HashSet<string>(
      installedVoices,
      StringComparer.OrdinalIgnoreCase);
    string? defaultVoice = installedVoices.FirstOrDefault();
    _current = LoadOrDefault(defaultVoice);
  }

  /// <summary>
  /// Gets the settings file path.
  /// </summary>
  public static string FilePath { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AgentPanelSpeaker",
    "settings.json");

  /// <summary>
  /// Gets the current immutable snapshot.
  /// </summary>
  public UserSettings Current
  {
    get
    {
      lock (_sync)
      {
        return _current;
      }
    }
  }

  /// <summary>
  /// Replaces and immediately saves the current snapshot.
  /// </summary>
  public void Update(UserSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    lock (_sync)
    {
      _current = Normalize(settings);
      SaveLocked();
    }
  }

  /// <summary>
  /// Saves the current snapshot again.
  /// </summary>
  public void Save()
  {
    lock (_sync)
    {
      SaveLocked();
    }
  }

  /// <summary>
  /// Replaces the current snapshot with defaults and saves it.
  /// </summary>
  public UserSettings ResetDefaults()
  {
    lock (_sync)
    {
      _current = UserSettings.CreateDefault(_installedVoices.FirstOrDefault());
      SaveLocked();
      return _current;
    }
  }

  /// <summary>
  /// Gets one current speech profile.
  /// </summary>
  public SpeechProfileSettings GetProfile(ContentCategory category)
  {
    lock (_sync)
    {
      return _current.GetProfile(category);
    }
  }

  /// <summary>
  /// Gets whether one fence type is currently enabled.
  /// </summary>
  public bool IsFenceTypeSpoken(string fenceType)
  {
    lock (_sync)
    {
      return FencedCodeTypeSet
        .Parse(_current.SpokenFencedCodeTypes)
        .Contains(fenceType);
    }
  }

  /// <summary>
  /// Gets the current normalized tokens that must be spelled out.
  /// </summary>
  public IReadOnlyList<string> GetSpelledWords()
  {
    lock (_sync)
    {
      return SpelledWordSet.Parse(_current.SpelledWords).OrderedWords;
    }
  }


  /// <summary>
  /// Gets the current normalized pronunciation rules.
  /// </summary>
  public PronunciationRuleSet GetPronunciations()
  {
    lock (_sync)
    {
      return PronunciationRuleSet.Parse(_current.Pronunciations);
    }
  }

  /// <summary>
  /// Gets the current normalized audio-wake settings.
  /// </summary>
  public AudioWakeSettings GetAudioWakeSettings()
  {
    lock (_sync)
    {
      return _current.AudioWake.Normalize();
    }
  }

  /// <summary>
  /// Loads settings or returns defaults after any read/parse failure.
  /// </summary>
  private UserSettings LoadOrDefault(string? defaultVoice)
  {
    try
    {
      if (!File.Exists(FilePath))
      {
        return UserSettings.CreateDefault(defaultVoice);
      }

      string json = File.ReadAllText(FilePath);
      UserSettings? settings = JsonSerializer.Deserialize<UserSettings>(
        json,
        JsonOptions);
      return settings is null
        ? UserSettings.CreateDefault(defaultVoice)
        : Normalize(settings);
    }
    catch (Exception exception) when (
      exception is IOException or
      UnauthorizedAccessException or
      JsonException)
    {
      DiagnosticLog.Write("settings.load_failed", new
      {
        path = FilePath,
        exception = exception.ToString()
      });
      return UserSettings.CreateDefault(defaultVoice);
    }
  }

  /// <summary>
  /// Normalizes ranges, voices, CSV content, and window dimensions.
  /// </summary>
  private UserSettings Normalize(UserSettings settings)
  {
    string spelledWords = settings.Version < 3 &&
      string.IsNullOrWhiteSpace(settings.SpelledWords)
        ? "IDE"
        : settings.SpelledWords;

    SpeechProfileSettings NormalizeProfile(SpeechProfileSettings profile)
    {
      SpeechProfileSettings normalized = profile.Normalize();
      bool hasSelectedVoice = !string.Equals(
        normalized.VoiceName,
        SpeechProfileSettings.NotSpoken,
        StringComparison.OrdinalIgnoreCase);
      if (hasSelectedVoice &&
          !_installedVoices.Contains(normalized.VoiceName))
      {
        DiagnosticLog.Write("settings.voice_missing", new
        {
          normalized.VoiceName
        });
        return normalized with
        {
          VoiceName = SpeechProfileSettings.NotSpoken
        };
      }

      return normalized;
    }

    return settings with
    {
      Version = UserSettings.CurrentVersion,
      FollowNewestSession = settings.Version >= 5 &&
        settings.FollowNewestSession,
      KeepDisplayOnWhileSpeaking = settings.Version >= 7 &&
        settings.KeepDisplayOnWhileSpeaking,
      Transcript = settings.Version >= 10
        ? (settings.Transcript ?? TranscriptSettings.Default).Normalize()
        : TranscriptSettings.Default,
      Assistant = NormalizeProfile(settings.Assistant),
      Reasoning = NormalizeProfile(settings.Reasoning),
      SubagentAssistant = NormalizeProfile(
        settings.Version < 6
          ? settings.Assistant
          : settings.SubagentAssistant),
      SubagentReasoning = NormalizeProfile(
        settings.Version < 6
          ? settings.Reasoning
          : settings.SubagentReasoning),
      User = NormalizeProfile(settings.User),
      UserContext = NormalizeProfile(
        settings.Version < 8 ? settings.User : settings.UserContext),
      SpokenFencedCodeTypes = FencedCodeTypeSet
        .Parse(settings.SpokenFencedCodeTypes)
        .NormalizedCsv,
      SpelledWords = SpelledWordSet.Parse(spelledWords).NormalizedText,
      Pronunciations = PronunciationRuleSet
        .Parse(settings.Pronunciations)
        .NormalizedText,
      AudioWake = (settings.AudioWake is null
        ? AudioWakeSettings.Default
        : settings.AudioWake).Normalize(),
      Theme = Enum.IsDefined(typeof(AppTheme), settings.Theme)
        ? settings.Theme
        : AppTheme.System,
      PollIntervalMilliseconds = Math.Clamp(
        settings.PollIntervalMilliseconds,
        50,
        2000),
      WindowWidth = Math.Max(900, settings.WindowWidth),
      WindowHeight = Math.Max(720, settings.WindowHeight)
    };
  }

  /// <summary>
  /// Writes settings through a temporary file and atomic replacement.
  /// </summary>
  private void SaveLocked()
  {
    string? directory = Path.GetDirectoryName(FilePath);
    if (string.IsNullOrWhiteSpace(directory))
    {
      throw new InvalidOperationException("Settings directory is unavailable.");
    }

    Directory.CreateDirectory(directory);
    string temporaryPath = FilePath + ".tmp";
    File.WriteAllText(
      temporaryPath,
      JsonSerializer.Serialize(_current, JsonOptions));
    File.Move(temporaryPath, FilePath, overwrite: true);
    DiagnosticLog.Write("settings.saved", new { path = FilePath });
  }
}
