using System.Collections.Concurrent;
using System.Speech.AudioFormat;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using InstalledVoice = System.Speech.Synthesis.InstalledVoice;
using SystemSpeechSynthesizer = System.Speech.Synthesis.SpeechSynthesizer;
using VoiceInfo = System.Speech.Synthesis.VoiceInfo;
using WinRtSpeechSynthesizer = Windows.Media.SpeechSynthesis.SpeechSynthesizer;

namespace AgentPanelSpeaker;

/// <summary>
/// Renders speech and wake audio into one PCM buffer on one STA worker.
/// </summary>
internal sealed class SapiSpeechEngine : IDisposable
{
  private const int IsXml = 8;
  private const int SpFileModeCreateForWrite = 3;
  private const int DefaultWorkerPollMilliseconds = 10;
  private const int OutputSampleRate = 48000;
  private const int SystemSpeechSampleRate = 16000;

  private readonly BlockingCollection<EngineCommand> _commands = new();
  private readonly ManualResetEventSlim _initialized = new();
  private readonly Thread _thread;
  private IReadOnlyList<InstalledSpeechVoice> _voices =
    Array.Empty<InstalledSpeechVoice>();
  private Exception? _initializationException;
  private int _wordBoundaryPollMilliseconds = DefaultWorkerPollMilliseconds;
  private int _windowsMediaBookmarkMode =
    (int)WindowsMediaBookmarkMode.Fallback;
  private long _lastAudioEndTimestamp;
  private bool _hasAudioEndTimestamp;
  private bool _disposed;

  /// <summary>
  /// Starts the speech worker and waits for voice enumeration to finish.
  /// </summary>
  public SapiSpeechEngine()
  {
    _thread = new Thread(Run)
    {
      IsBackground = true,
      Name = "Agent Panel Speaker speech"
    };
    _thread.SetApartmentState(ApartmentState.STA);
    _thread.Start();
    _initialized.Wait();
    if (_initializationException is not null)
    {
      throw new InvalidOperationException(
        "Windows speech synthesis could not be initialized.",
        _initializationException);
    }
  }

  /// <summary>
  /// Raised after the active utterance, sequence, or wake test completes.
  /// </summary>
  public event Action? Completed;

  /// <summary>
  /// Raised when the worker encounters a synthesis or playback failure.
  /// </summary>
  public event Action<Exception>? Faulted;

  /// <summary>
  /// Raised when the worker recovers from unsupported preview markup.
  /// </summary>
  public event Action<string>? Notice;

  /// <summary>
  /// Raised as playback reaches one synthesized word boundary.
  /// </summary>
  public event Action<SpeechWordBoundary>? WordBoundary;

  /// <summary>
  /// Gets all enabled voices exposed by the available Windows speech providers.
  /// </summary>
  public IReadOnlyList<InstalledSpeechVoice> Voices => _voices;

  /// <summary>
  /// Sets how frequently the playback worker checks for crossed word
  /// boundaries.  Smaller values improve transcript-marker responsiveness.
  /// </summary>
  public void SetWordBoundaryPollMilliseconds(int milliseconds)
  {
    int bounded = Math.Clamp(
      (int)Math.Round(milliseconds / 5.0) * 5,
      5,
      40);
    Volatile.Write(ref _wordBoundaryPollMilliseconds, bounded);
    try
    {
      _thread.Priority = bounded <= 10
        ? ThreadPriority.AboveNormal
        : ThreadPriority.Normal;
    }
    catch (Exception exception) when (
      exception is ThreadStateException or SecurityException)
    {
      DiagnosticLog.Write("speech.word_tracking_priority_failed", new
      {
        milliseconds = bounded,
        exception = exception.Message
      });
    }
  }

  /// <summary>
  /// Sets when Windows.Media voices use explicit bookmark timing.
  /// </summary>
  public void SetWindowsMediaBookmarkMode(WindowsMediaBookmarkMode mode)
  {
    if (!Enum.IsDefined(typeof(WindowsMediaBookmarkMode), mode))
    {
      throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }
    Volatile.Write(ref _windowsMediaBookmarkMode, (int)mode);
  }

  /// <summary>
  /// Starts one marked-up utterance with the configured wake prefix.
  /// </summary>
  public void Speak(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    AudioWakeSettings wakeSettings)
  {
    ArgumentNullException.ThrowIfNull(markup);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(wakeSettings);
    AddCommand(new SpeakCommand(
      markup,
      profile.Normalize(),
      wakeSettings.Normalize(),
      ForceWake: false));
  }

  /// <summary>
  /// Previews ordinary text and forces the enabled Bluetooth wake prefix.
  /// </summary>
  public void PreviewText(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    AudioWakeSettings wakeSettings)
  {
    ArgumentNullException.ThrowIfNull(markup);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(wakeSettings);
    AudioWakeSettings normalizedWake = wakeSettings.Normalize();
    AddCommand(new SpeakCommand(
      markup,
      profile.Normalize(),
      normalizedWake,
      ForceWake: normalizedWake.Enabled));
  }

  /// <summary>
  /// Plays an optional isolated phone, waits, and then speaks its example.
  /// </summary>
  public void PreviewIpa(
    SpeechMarkup? isolatedMarkup,
    SpeechMarkup exampleMarkup,
    SpeechMarkup? exampleFallbackMarkup,
    SpeechProfileSettings profile,
    AudioWakeSettings wakeSettings)
  {
    ArgumentNullException.ThrowIfNull(exampleMarkup);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(wakeSettings);
    AddCommand(new IpaPreviewCommand(
      isolatedMarkup,
      exampleMarkup,
      exampleFallbackMarkup,
      profile.Normalize(),
      wakeSettings.Normalize()));
  }

  /// <summary>
  /// Plays the wake tone and settling silence regardless of quiet duration.
  /// </summary>
  public void TestWakeTone(AudioWakeSettings wakeSettings)
  {
    ArgumentNullException.ThrowIfNull(wakeSettings);
    AddCommand(new WakeTestCommand(wakeSettings.Normalize()));
  }

  /// <summary>
  /// Plays a forced wake prefix and phrase as one contiguous PCM stream.
  /// </summary>
  public void TestWakePhrase(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    AudioWakeSettings wakeSettings)
  {
    ArgumentNullException.ThrowIfNull(markup);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(wakeSettings);
    AddCommand(new SpeakCommand(
      markup,
      profile.Normalize(),
      wakeSettings.Normalize(),
      ForceWake: true));
  }

  /// <summary>
  /// Cancels the active utterance or preview sequence.
  /// </summary>
  public void Cancel()
  {
    AddCommand(CancelCommand.Instance);
  }

  /// <summary>
  /// Pauses the active contiguous output stream.
  /// </summary>
  public void Pause()
  {
    AddCommand(PauseCommand.Instance);
  }

  /// <summary>
  /// Resumes the active contiguous output stream.
  /// </summary>
  public void Resume()
  {
    AddCommand(ResumeCommand.Instance);
  }

  /// <summary>
  /// Stops the worker and releases speech resources.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    try
    {
      _commands.Add(DisposeCommand.Instance);
    }
    catch (InvalidOperationException)
    {
    }
    _thread.Join();
    _commands.Dispose();
    _initialized.Dispose();
  }

  private void AddCommand(EngineCommand command)
  {
    if (_disposed)
    {
      throw new ObjectDisposedException(nameof(SapiSpeechEngine));
    }
    _commands.Add(command);
  }

  /// <summary>
  /// Creates the available speech providers and services the command queue.
  /// </summary>
  private void Run()
  {
    object? voiceObject = null;
    object? voicesObject = null;
    SystemSpeechSynthesizer? synthesizer = null;
    WinRtSpeechSynthesizer? windowsMediaSynthesizer = null;
    try
    {
      synthesizer = new SystemSpeechSynthesizer();
      synthesizer.SetOutputToNull();

      var systemVoices = EnumerateSystemSpeechVoices(synthesizer);
      var sapiVoices = new Dictionary<string, SapiVoice>(
        StringComparer.OrdinalIgnoreCase);
      IReadOnlyList<WindowsMediaVoice> windowsMediaVoices =
        Array.Empty<WindowsMediaVoice>();
      try
      {
        windowsMediaSynthesizer = new WinRtSpeechSynthesizer();
        windowsMediaVoices = EnumerateWindowsMediaVoices();
      }
      catch (Exception exception)
      {
        DiagnosticLog.Write("speech.windows_media_unavailable", new
        {
          exception = exception.ToString()
        });
        windowsMediaSynthesizer?.Dispose();
        windowsMediaSynthesizer = null;
      }

      try
      {
        Type voiceType = Type.GetTypeFromProgID("SAPI.SpVoice") ??
          throw new InvalidOperationException("SAPI.SpVoice is unavailable.");
        voiceObject = Activator.CreateInstance(voiceType) ??
          throw new InvalidOperationException(
            "SAPI.SpVoice could not be created.");
        dynamic voice = voiceObject;
        voicesObject = voice.GetVoices(string.Empty, string.Empty);
        EnumerateSapiVoices(voicesObject, sapiVoices);
      }
      catch (Exception exception)
      {
        DiagnosticLog.Write("speech.sapi_unavailable", new
        {
          exception = exception.ToString()
        });
        ReleaseComObject(voicesObject);
        ReleaseComObject(voiceObject);
        voicesObject = null;
        voiceObject = null;
        sapiVoices.Clear();
      }

      var registrations = new Dictionary<string, VoiceRegistration>(
        StringComparer.OrdinalIgnoreCase);
      foreach ((string name, string displayName) in systemVoices)
      {
        InstalledSpeechVoice voice = InstalledSpeechVoice.CreateLegacy(
          name,
          name,
          SpeechVoiceProvider.SystemSpeech,
          displayName);
        AddVoiceRegistration(
          registrations,
          voice,
          VoiceBackend.ForSystemSpeech(name));
      }

      foreach ((string name, SapiVoice sapiVoice) in sapiVoices)
      {
        InstalledSpeechVoice voice = InstalledSpeechVoice.CreateLegacy(
          name,
          name,
          SpeechVoiceProvider.Sapi,
          sapiVoice.DisplayName);
        AddVoiceRegistration(
          registrations,
          voice,
          VoiceBackend.ForSapi(sapiVoice.Index, name));
      }

      foreach (WindowsMediaVoice mediaVoice in windowsMediaVoices)
      {
        InstalledSpeechVoice voice =
          InstalledSpeechVoice.CreateWindowsMedia(
            $"winrt:{mediaVoice.Id}",
            mediaVoice.Id,
            mediaVoice.DisplayName,
            mediaVoice.Description,
            mediaVoice.Language);
        AddVoiceRegistration(
          registrations,
          voice,
          VoiceBackend.ForWindowsMedia(mediaVoice.Id));
      }

      if (registrations.Count == 0)
      {
        throw new InvalidOperationException(
          "No enabled Windows speech voices were found.");
      }

      _voices = registrations.Values
        .Select(registration => registration.Voice)
        .OrderBy(
          voiceInfo => voiceInfo.Location,
          StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(
          voiceInfo => voiceInfo.Language,
          StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(
          voiceInfo => voiceInfo.VoiceName,
          StringComparer.CurrentCultureIgnoreCase)
        .ToArray();
      IReadOnlyDictionary<string, VoiceBackend> voiceBackends =
        registrations.Values.ToDictionary(
          registration => registration.Voice.Name,
          registration => registration.Backend,
          StringComparer.OrdinalIgnoreCase);

      DiagnosticLog.Write("speech.voices_enumerated", new
      {
        systemSpeechCount = systemVoices.Count,
        sapiCount = sapiVoices.Count,
        windowsMediaCount = windowsMediaVoices.Count,
        totalCount = _voices.Count,
        voices = _voices.Select(voice => new
        {
          voice.Name,
          provider = voice.Provider.ToString(),
          voice.ProviderVoiceId,
          display = voice.ToString()
        }).ToArray()
      });
      _initialized.Set();
      ServiceCommands(
        voiceObject,
        voicesObject,
        synthesizer,
        windowsMediaSynthesizer,
        voiceBackends);
    }
    catch (Exception exception)
    {
      if (!_initialized.IsSet)
      {
        _initializationException = exception;
        _initialized.Set();
      }
      else
      {
        RaiseFaulted(exception);
      }
    }
    finally
    {
      _initialized.Set();
      windowsMediaSynthesizer?.Dispose();
      synthesizer?.Dispose();
      ReleaseComObject(voicesObject);
      ReleaseComObject(voiceObject);
    }
  }

  private static Dictionary<string, string> EnumerateSystemSpeechVoices(
    SystemSpeechSynthesizer synthesizer)
  {
    var voices = new Dictionary<string, string>(
      StringComparer.OrdinalIgnoreCase);
    foreach (InstalledVoice installed in synthesizer.GetInstalledVoices())
    {
      if (!installed.Enabled)
      {
        continue;
      }

      VoiceInfo info = installed.VoiceInfo;
      string name = info.Name.Trim();
      if (name.Length == 0)
      {
        continue;
      }

      string displayName = BuildVoiceDisplayName(
        name,
        info.Description,
        info.Culture.EnglishName);
      AddPreferredDisplayName(voices, name, displayName);
    }
    return voices;
  }

  /// <summary>
  /// Adds all usable native SAPI tokens to a name-to-index map.
  /// </summary>
  private static void EnumerateSapiVoices(
    object voicesObject,
    IDictionary<string, SapiVoice> voicesByName)
  {
    dynamic voices = voicesObject;
    int count = Convert.ToInt32(voices.Count);
    for (int index = 0; index < count; ++index)
    {
      object tokenObject = voices.Item(index);
      try
      {
        dynamic token = tokenObject;
        string name;
        try
        {
          name = Convert.ToString(token.GetAttribute("Name"))?.Trim() ??
            string.Empty;
        }
        catch (COMException)
        {
          name = string.Empty;
        }
        string description = Convert.ToString(
          token.GetDescription(0))?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
          name = description;
        }
        if (name.Length != 0 && !voicesByName.ContainsKey(name))
        {
          voicesByName.Add(
            name,
            new SapiVoice(
              index,
              BuildVoiceDisplayName(name, description, string.Empty)));
        }
      }
      finally
      {
        ReleaseComObject(tokenObject);
      }
    }
  }

  /// <summary>
  /// Reads every Microsoft-signed voice exposed by the modern WinRT catalogue.
  /// </summary>
  private static IReadOnlyList<WindowsMediaVoice> EnumerateWindowsMediaVoices()
  {
    return WinRtSpeechSynthesizer.AllVoices
      .Select(voice => new WindowsMediaVoice(
        voice.Id?.Trim() ?? string.Empty,
        voice.DisplayName?.Trim() ?? string.Empty,
        voice.Description?.Trim() ?? string.Empty,
        voice.Language?.Trim() ?? string.Empty))
      .Where(voice =>
        voice.Id.Length != 0 && voice.DisplayName.Length != 0)
      .ToArray();
  }

  /// <summary>
  /// Adds one provider voice, merging duplicates while retaining the backend
  /// with the highest feature priority and the richest display metadata.
  /// </summary>
  private static void AddVoiceRegistration(
    IDictionary<string, VoiceRegistration> registrations,
    InstalledSpeechVoice voice,
    VoiceBackend backend)
  {
    string catalogueKey = voice.GetCatalogueKey();
    string? existingKey = registrations.ContainsKey(catalogueKey)
      ? catalogueKey
      : registrations
        .FirstOrDefault(pair => string.Equals(
          pair.Value.Voice.Name,
          voice.Name,
          StringComparison.OrdinalIgnoreCase))
        .Key;

    if (string.IsNullOrEmpty(existingKey))
    {
      registrations.Add(catalogueKey, new VoiceRegistration(voice, backend));
      return;
    }

    VoiceRegistration existing = registrations[existingKey];
    bool useNewBackend = GetProviderPriority(voice.Provider) >
      GetProviderPriority(existing.Voice.Provider);
    InstalledSpeechVoice selectedVoice = useNewBackend
      ? (voice with { Name = existing.Voice.Name })
        .MergeDisplayMetadata(existing.Voice)
      : existing.Voice.MergeDisplayMetadata(voice);
    VoiceBackend selectedBackend = useNewBackend
      ? backend
      : existing.Backend;
    registrations[existingKey] = new VoiceRegistration(
      selectedVoice,
      selectedBackend);
  }

  private static int GetProviderPriority(SpeechVoiceProvider provider)
  {
    return provider switch
    {
      SpeechVoiceProvider.WindowsMedia => 3,
      SpeechVoiceProvider.SystemSpeech => 2,
      SpeechVoiceProvider.Sapi => 1,
      _ => 0
    };
  }

  private static string BuildVoiceDisplayName(
    string name,
    string? description,
    string? cultureName)
  {
    string normalizedDescription = description?.Trim() ?? string.Empty;
    if (normalizedDescription.Length != 0 &&
        !string.Equals(
          normalizedDescription,
          name,
          StringComparison.OrdinalIgnoreCase))
    {
      return normalizedDescription;
    }

    string normalizedCulture = cultureName?.Trim() ?? string.Empty;
    return normalizedCulture.Length == 0
      ? name
      : $"{name} - {normalizedCulture}";
  }

  private static void AddPreferredDisplayName(
    IDictionary<string, string> displayNames,
    string name,
    string candidate)
  {
    if (!displayNames.TryGetValue(name, out string? existing) ||
        candidate.Length > existing.Length)
    {
      displayNames[name] = candidate;
    }
  }

  /// <summary>
  /// Processes commands and completion for the active WinMM buffer.
  /// </summary>
  private void ServiceCommands(
    object? voiceObject,
    object? voicesObject,
    SystemSpeechSynthesizer synthesizer,
    WinRtSpeechSynthesizer? windowsMediaSynthesizer,
    IReadOnlyDictionary<string, VoiceBackend> voiceBackends)
  {
    WaveOutPlayer? player = null;
    IReadOnlyList<SpeechWordBoundary> wordBoundaries =
      Array.Empty<SpeechWordBoundary>();
    int nextWordBoundary = 0;
    bool exiting = false;

    while (!exiting)
    {
      EngineCommand? command = null;
      try
      {
        int pollMilliseconds = Volatile.Read(
          ref _wordBoundaryPollMilliseconds);
        if (_commands.TryTake(
              out command,
              pollMilliseconds) &&
            command is not null)
        {
          ProcessCommand(
            command,
            voiceObject,
            voicesObject,
            synthesizer,
            windowsMediaSynthesizer,
            voiceBackends,
            ref player,
            ref wordBoundaries,
            ref nextWordBoundary,
            ref exiting);
        }

        if (player is not null)
        {
          TimeSpan position = player.Position;
          while (nextWordBoundary < wordBoundaries.Count &&
                 wordBoundaries[nextWordBoundary].AudioPosition <= position)
          {
            RaiseWordBoundary(wordBoundaries[nextWordBoundary]);
            nextWordBoundary++;
          }
          if (player.IsComplete)
          {
            player.Dispose();
            player = null;
            wordBoundaries = Array.Empty<SpeechWordBoundary>();
            nextWordBoundary = 0;
            MarkAudioEnd();
            RaiseCompleted();
          }
        }
      }
      catch (Exception exception)
      {
        bool shouldComplete = player is not null || command is PlaybackCommand;
        CancelPlayer(ref player);
        if (command is DisposeCommand)
        {
          exiting = true;
        }
        RaiseFaulted(exception);
        if (shouldComplete)
        {
          RaiseCompleted();
        }
      }
    }

    CancelPlayer(ref player);
  }

  private void ProcessCommand(
    EngineCommand command,
    object? voiceObject,
    object? voicesObject,
    SystemSpeechSynthesizer synthesizer,
    WinRtSpeechSynthesizer? windowsMediaSynthesizer,
    IReadOnlyDictionary<string, VoiceBackend> voiceBackends,
    ref WaveOutPlayer? player,
    ref IReadOnlyList<SpeechWordBoundary> wordBoundaries,
    ref int nextWordBoundary,
    ref bool exiting)
  {
    switch (command)
    {
      case SpeakCommand speak:
        CancelPlayer(ref player);
        SpeechPlaybackBuffer speechBuffer = StartSpeechPlayback(
          PlaybackRequest.ForSpeech(speak),
          voiceObject,
          voicesObject,
          synthesizer,
          windowsMediaSynthesizer,
          voiceBackends);
        player = new WaveOutPlayer(speechBuffer.Wave);
        wordBoundaries = speechBuffer.WordBoundaries;
        nextWordBoundary = 0;
        break;

      case IpaPreviewCommand preview:
        CancelPlayer(ref player);
        SpeechPlaybackBuffer previewBuffer = StartSpeechPlayback(
          PlaybackRequest.ForIpaPreview(preview),
          voiceObject,
          voicesObject,
          synthesizer,
          windowsMediaSynthesizer,
          voiceBackends);
        player = new WaveOutPlayer(previewBuffer.Wave);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        nextWordBoundary = 0;
        break;

      case WakeTestCommand wakeTest:
        CancelPlayer(ref player);
        player = StartWakeToneTest(wakeTest.WakeSettings);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        nextWordBoundary = 0;
        break;

      case CancelCommand:
      {
        bool wasActive = player is not null;
        CancelPlayer(ref player);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        nextWordBoundary = 0;
        if (wasActive)
        {
          RaiseCompleted();
        }
        break;
      }

      case PauseCommand when player is not null:
        player.Pause();
        break;

      case ResumeCommand when player is not null:
        player.Resume();
        break;

      case DisposeCommand:
        CancelPlayer(ref player);
        exiting = true;
        break;
    }
  }

  /// <summary>
  /// Renders every segment, prefixes wake audio, and starts one PCM buffer.
  /// </summary>
  private SpeechPlaybackBuffer StartSpeechPlayback(
    PlaybackRequest request,
    object? voiceObject,
    object? voicesObject,
    SystemSpeechSynthesizer synthesizer,
    WinRtSpeechSynthesizer? windowsMediaSynthesizer,
    IReadOnlyDictionary<string, VoiceBackend> voiceBackends)
  {
    if (!voiceBackends.TryGetValue(
          request.Profile.VoiceName,
          out VoiceBackend backend))
    {
      throw new ArgumentException(
        $"Voice is not installed: {request.Profile.VoiceName}");
    }

    var parts = new List<PcmWaveData>();
    var boundaries = new List<SpeechWordBoundary>();
    PcmWaveData? outputFormat = null;
    TimeSpan speechOffset = TimeSpan.Zero;
    foreach (SpeechSegment segment in request.Segments)
    {
      RenderedSpeechSegment? rendered = RenderSpeechSegment(
        segment,
        request.Profile,
        backend,
        voiceObject,
        voicesObject,
        synthesizer,
        windowsMediaSynthesizer);
      if (rendered is null)
      {
        continue;
      }

      if (outputFormat is not null &&
          segment.DelayAfterPreviousMilliseconds > 0)
      {
        PcmWaveData delay = outputFormat.CreateSilence(
          segment.DelayAfterPreviousMilliseconds);
        parts.Add(delay);
        speechOffset += delay.Duration;
      }

      PcmWaveData converted = rendered.Wave.ConvertToMono16(OutputSampleRate);
      outputFormat ??= converted;
      foreach (SpeechWordBoundary boundary in rendered.WordBoundaries)
      {
        boundaries.Add(boundary with
        {
          AudioPosition = speechOffset + boundary.AudioPosition
        });
      }
      parts.Add(converted);
      speechOffset += converted.Duration;
    }

    if (outputFormat is null)
    {
      RaiseNotice(
        "The selected voice rejected both the isolated IPA sound and its " +
        "carrier; no IPA audio was available.");
      outputFormat = PcmWaveData.CreateDefaultFormat();
      parts.Add(outputFormat.CreateSilence(1));
    }

    PcmWaveData speech = PcmWaveData.Concatenate(parts);
    bool wakeApplied = ShouldApplyWake(
      request.WakeSettings,
      request.ForceWake);
    PcmWaveData playback = wakeApplied
      ? PrefixWakeAudio(speech, request.WakeSettings)
      : speech;

    DiagnosticLog.Write("speech.playback_buffer", new
    {
      request.Profile.VoiceName,
      request.Profile.Rate,
      request.Profile.Pitch,
      request.Profile.Volume,
      wakeApplied,
      request.ForceWake,
      request.WakeSettings.FrequencyHertz,
      request.WakeSettings.PlayDurationMilliseconds,
      request.WakeSettings.SettleDurationMilliseconds,
      sampleRate = playback.SampleRate,
      sampleBytes = playback.Samples.Length
    });
    TimeSpan wakeOffset = wakeApplied
      ? TimeSpan.FromMilliseconds(
          request.WakeSettings.PlayDurationMilliseconds +
          request.WakeSettings.SettleDurationMilliseconds)
      : TimeSpan.Zero;
    return new SpeechPlaybackBuffer(
      playback,
      boundaries.Select(boundary => boundary with
      {
        AudioPosition = wakeOffset + boundary.AudioPosition
      }).ToArray());
  }

  private WaveOutPlayer StartWakeToneTest(AudioWakeSettings settings)
  {
    PcmWaveData format = PcmWaveData.CreateDefaultFormat();
    PcmWaveData playback = PcmWaveData.Concatenate(new[]
    {
      format.CreateTone(
        settings.FrequencyHertz,
        settings.ToneVolume,
        settings.PlayDurationMilliseconds),
      format.CreateSilence(settings.SettleDurationMilliseconds)
    });
    DiagnosticLog.Write("speech.wake_tone_test", new
    {
      settings.FrequencyHertz,
      settings.ToneVolume,
      settings.PlayDurationMilliseconds,
      settings.SettleDurationMilliseconds
    });
    return new WaveOutPlayer(playback);
  }

  private static PcmWaveData PrefixWakeAudio(
    PcmWaveData speech,
    AudioWakeSettings settings)
  {
    return PcmWaveData.Concatenate(new[]
    {
      speech.CreateTone(
        settings.FrequencyHertz,
        settings.ToneVolume,
        settings.PlayDurationMilliseconds),
      speech.CreateSilence(settings.SettleDurationMilliseconds),
      speech
    });
  }

  private bool ShouldApplyWake(AudioWakeSettings settings, bool force)
  {
    if (force)
    {
      return true;
    }
    if (!settings.Enabled)
    {
      return false;
    }

    double quietMilliseconds = _hasAudioEndTimestamp
      ? Stopwatch.GetElapsedTime(_lastAudioEndTimestamp).TotalMilliseconds
      : double.PositiveInfinity;
    return quietMilliseconds > settings.QuietDurationMilliseconds;
  }

  private RenderedSpeechSegment RenderSpeech(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    VoiceBackend backend,
    object? voiceObject,
    object? voicesObject,
    SystemSpeechSynthesizer synthesizer,
    WinRtSpeechSynthesizer? windowsMediaSynthesizer)
  {
    IReadOnlyList<SpeechWordBoundary> boundaries;
    PcmWaveData wave;
    switch (backend.Backend)
    {
      case SpeechBackend.Sapi:
        wave = RenderSapiSpeech(
          markup,
          profile,
          backend.SapiIndex,
          voiceObject,
          voicesObject);
        boundaries = CreateApproximateBoundaries(markup.PlainText, wave.Duration);
        break;

      case SpeechBackend.SystemSpeech:
        wave = RenderSystemSpeech(
          markup,
          profile,
          backend.ProviderVoiceId,
          synthesizer,
          out boundaries);
        break;

      case SpeechBackend.WindowsMedia:
        wave = RenderWindowsMediaSpeech(
          markup,
          profile,
          backend.ProviderVoiceId,
          windowsMediaSynthesizer ?? throw new InvalidOperationException(
            "The Windows.Media speech backend is unavailable."),
          (WindowsMediaBookmarkMode)Volatile.Read(
            ref _windowsMediaBookmarkMode),
          out boundaries);
        break;

      default:
        throw new InvalidOperationException(
          "The selected voice has no speech backend.");
    }
    return new RenderedSpeechSegment(wave, boundaries);
  }

  /// <summary>
  /// Renders one preview segment and applies its explicit unsupported-IPA
  /// recovery policy.
  /// </summary>
  private RenderedSpeechSegment? RenderSpeechSegment(
    SpeechSegment segment,
    SpeechProfileSettings profile,
    VoiceBackend backend,
    object? voiceObject,
    object? voicesObject,
    SystemSpeechSynthesizer synthesizer,
    WinRtSpeechSynthesizer? windowsMediaSynthesizer)
  {
    try
    {
      return RenderSpeech(
        segment.Markup,
        profile,
        backend,
        voiceObject,
        voicesObject,
        synthesizer,
        windowsMediaSynthesizer);
    }
    catch (Exception primaryException) when (
      (segment.SkipWhenRejected || segment.FallbackMarkup is not null) &&
      IsPreviewMarkupRejection(primaryException))
    {
      SpeechMarkup? fallbackMarkup = segment.FallbackMarkup;
      DiagnosticLog.Write("speech.preview_markup_rejected", new
      {
        profile.VoiceName,
        backend = backend.Backend.ToString(),
        segment.Label,
        exception = primaryException.ToString(),
        fallback = fallbackMarkup is not null
      });

      if (fallbackMarkup is null)
      {
        RaiseNotice(segment.Label == "isolated IPA"
          ? "The selected voice cannot synthesize this isolated IPA sound; " +
            "playing the example instead."
          : "The selected voice cannot synthesize this IPA carrier; " +
            "skipping it.");
        return null;
      }

      try
      {
        RenderedSpeechSegment fallback = RenderSpeech(
          fallbackMarkup,
          profile,
          backend,
          voiceObject,
          voicesObject,
          synthesizer,
          windowsMediaSynthesizer);
        RaiseNotice(
          "The selected voice rejected this IPA example; " +
          "using its ordinary word pronunciation.");
        return fallback;
      }
      catch (Exception fallbackException)
      {
        throw new AggregateException(
          "The selected voice rejected both the IPA preview and its " +
          "ordinary-pronunciation fallback.",
          primaryException,
          fallbackException);
      }
    }
  }

  /// <summary>
  /// Identifies provider failures caused by rejected pronunciation markup.
  /// </summary>
  private static bool IsPreviewMarkupRejection(Exception exception)
  {
    return exception is FormatException or COMException or ArgumentException;
  }

  /// <summary>
  /// Renders native SAPI XML into a temporary WAVE file.
  /// </summary>
  private static PcmWaveData RenderSapiSpeech(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    int voiceIndex,
    object? voiceObject,
    object? voicesObject)
  {
    if (voiceObject is null || voicesObject is null || voiceIndex < 0)
    {
      throw new InvalidOperationException(
        "The selected native SAPI voice is unavailable.");
    }

    Type fileStreamType = Type.GetTypeFromProgID("SAPI.SpFileStream") ??
      throw new InvalidOperationException("SAPI.SpFileStream is unavailable.");
    object fileStreamObject = Activator.CreateInstance(fileStreamType) ??
      throw new InvalidOperationException(
        "SAPI.SpFileStream could not be created.");
    string path = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-{Guid.NewGuid():N}.wav");
    bool opened = false;
    try
    {
      dynamic fileStream = fileStreamObject;
      fileStream.Open(path, SpFileModeCreateForWrite, false);
      opened = true;
      dynamic voice = voiceObject;
      voice.AudioOutputStream = fileStreamObject;
      ConfigureSapiVoice(voiceObject, voicesObject, voiceIndex, profile);
      voice.Speak(markup.SapiXml, IsXml);
      fileStream.Close();
      opened = false;
      return PcmWaveData.Parse(File.ReadAllBytes(path));
    }
    finally
    {
      if (opened)
      {
        try
        {
          dynamic fileStream = fileStreamObject;
          fileStream.Close();
        }
        catch (COMException)
        {
        }
      }
      ReleaseComObject(fileStreamObject);
      TryDeleteFile(path);
    }
  }

  /// <summary>
  /// Renders System.Speech SSML into an in-memory WAVE file.
  /// </summary>
  private static PcmWaveData RenderSystemSpeech(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    string providerVoiceId,
    SystemSpeechSynthesizer synthesizer,
    out IReadOnlyList<SpeechWordBoundary> boundaries)
  {
    using var stream = new MemoryStream();
    var collected = new List<SpeechWordBoundary>();
    MatchCollection sourceTokens = SpeechTokenization.Matches(markup.PlainText);
    int? synthesisCharacterOffset = null;
    EventHandler<System.Speech.Synthesis.SpeakProgressEventArgs> handler =
      (_, eventArgs) =>
      {
        int firstSourceTokenStart = sourceTokens.Count == 0
          ? 0
          : sourceTokens[0].Index;
        synthesisCharacterOffset ??=
          eventArgs.CharacterPosition - firstSourceTokenStart;
        int sourcePosition = Math.Clamp(
          eventArgs.CharacterPosition - synthesisCharacterOffset.Value,
          0,
          markup.PlainText.Length);
        int sourceCount = Math.Clamp(
          eventArgs.CharacterCount,
          0,
          markup.PlainText.Length - sourcePosition);
        int tokenIndex = FindTokenIndexForSourceRange(
          sourceTokens,
          sourcePosition,
          sourceCount);
        DiagnosticLog.Write("sapi.speak_progress", new
        {
          provider = "System.Speech",
          voice = providerVoiceId,
          markup.PlainText,
          eventArgs.Text,
          eventArgs.CharacterPosition,
          eventArgs.CharacterCount,
          eventArgs.AudioPosition,
          synthesisCharacterOffset,
          sourcePosition,
          sourceCount,
          tokenIndex,
          sourceToken = tokenIndex >= 0 && tokenIndex < sourceTokens.Count
            ? sourceTokens[tokenIndex].Value
            : string.Empty
        });
        collected.Add(new SpeechWordBoundary(
          eventArgs.AudioPosition,
          tokenIndex,
          sourcePosition,
          sourceCount,
          eventArgs.Text,
          Exact: true));
      };
    synthesizer.SelectVoice(providerVoiceId);
    synthesizer.Rate = profile.Rate;
    synthesizer.Volume = profile.Volume;
    string ssml = BuildSsmlDocument(
      markup.SsmlContent,
      synthesizer.Voice.Culture.Name);
    synthesizer.SpeakProgress += handler;
    try
    {
      var outputFormat = new SpeechAudioFormatInfo(
        SystemSpeechSampleRate,
        AudioBitsPerSample.Sixteen,
        AudioChannel.Mono);
      synthesizer.SetOutputToAudioStream(stream, outputFormat);
      synthesizer.SpeakSsml(ssml);
    }
    finally
    {
      synthesizer.SpeakProgress -= handler;
      synthesizer.SetOutputToNull();
    }
    PcmWaveData wave = PcmWaveData.FromPcmSamples(
      channels: 1,
      sampleRate: SystemSpeechSampleRate,
      bitsPerSample: 16,
      samples: stream.ToArray());
    boundaries = collected.Count == 0
      ? CreateApproximateBoundaries(markup.PlainText, wave.Duration)
      : collected;
    return wave;
  }


  /// <summary>
  /// Maps a System.Speech source range to the intersecting display token.
  /// </summary>
  private static int FindTokenIndexForSourceRange(
    MatchCollection tokens,
    int characterPosition,
    int characterCount)
  {
    if (tokens.Count == 0)
    {
      return 0;
    }

    int rangeEnd = checked(
      characterPosition + Math.Max(1, characterCount));
    for (int index = 0; index < tokens.Count; ++index)
    {
      Match token = tokens[index];
      int tokenEnd = token.Index + token.Length;
      if (token.Index < rangeEnd && tokenEnd > characterPosition)
      {
        return index;
      }
    }

    for (int index = 0; index < tokens.Count; ++index)
    {
      if (tokens[index].Index >= characterPosition)
      {
        return index;
      }
    }

    return tokens.Count - 1;
  }

  /// <summary>
  /// Renders a modern Windows voice into its returned audio/WAVE stream.
  /// </summary>
  private static PcmWaveData RenderWindowsMediaSpeech(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    string providerVoiceId,
    WinRtSpeechSynthesizer synthesizer,
    WindowsMediaBookmarkMode bookmarkMode,
    out IReadOnlyList<SpeechWordBoundary> boundaries)
  {
    VoiceInformation voice = WinRtSpeechSynthesizer.AllVoices
      .FirstOrDefault(candidate => string.Equals(
        candidate.Id,
        providerVoiceId,
        StringComparison.OrdinalIgnoreCase)) ??
      throw new InvalidOperationException(
        "The selected Windows.Media voice is unavailable.");

    synthesizer.Voice = voice;
    synthesizer.Options.IncludeWordBoundaryMetadata = true;
    synthesizer.Options.IncludeSentenceBoundaryMetadata = true;
    synthesizer.Options.SpeakingRate = Math.Pow(2.0, profile.Rate / 10.0);
    synthesizer.Options.AudioPitch = 1.0;
    synthesizer.Options.AudioVolume = profile.Volume / 100.0;
    bool requestBookmarks = bookmarkMode != WindowsMediaBookmarkMode.Off;
    string ssml = requestBookmarks && TryBuildBookmarkedSsml(
        markup,
        voice.Language,
        out string bookmarkedSsml)
      ? bookmarkedSsml
      : BuildSsmlDocument(markup.SsmlContent, voice.Language);

    using SpeechSynthesisStream stream = synthesizer
      .SynthesizeSsmlToStreamAsync(ssml)
      .AsTask()
      .GetAwaiter()
      .GetResult();
    int byteCount = checked((int)stream.Size);
    uint size = checked((uint)byteCount);
    using IInputStream input = stream.GetInputStreamAt(0);
    using var reader = new DataReader(input);
    uint loaded = reader.LoadAsync(size)
      .AsTask()
      .GetAwaiter()
      .GetResult();
    if (loaded != size)
    {
      throw new InvalidDataException(
        $"The Windows.Media speech stream ended after {loaded} of " +
        $"{size} bytes.");
    }

    var bytes = new byte[byteCount];
    reader.ReadBytes(bytes);
    PcmWaveData wave = PcmWaveData.Parse(bytes);
    IReadOnlyList<SpeechWordBoundary> wordBoundaries =
      CreateWindowsMediaBoundaries(
        markup.PlainText,
        stream,
        wave.Duration,
        voice.DisplayName);
    int speakableTokenCount = SpeechTokenization.Matches(markup.PlainText)
      .Cast<Match>()
      .Count(token => token.Value.Any(char.IsLetterOrDigit));
    int exactSpeakableBoundaryCount = wordBoundaries
      .Where(boundary => boundary.Exact &&
        boundary.Text.Any(char.IsLetterOrDigit))
      .Select(boundary => boundary.WordIndex)
      .Distinct()
      .Count();
    bool wordTimingReliable = speakableTokenCount != 0 &&
      exactSpeakableBoundaryCount >= Math.Ceiling(speakableTokenCount * 0.8);
    if (requestBookmarks &&
        (bookmarkMode == WindowsMediaBookmarkMode.Always ||
         !wordTimingReliable) &&
        TryCreateWindowsMediaBookmarkBoundaries(
          markup.PlainText,
          stream,
          out IReadOnlyList<SpeechWordBoundary> bookmarkBoundaries))
    {
      boundaries = bookmarkBoundaries;
      DiagnosticLog.Write("speech.windows_media_bookmarks_used", new
      {
        voice = voice.DisplayName,
        mode = bookmarkMode.ToString(),
        boundaryCount = boundaries.Count
      });
    }
    else
    {
      boundaries = wordBoundaries;
    }
    return wave;
  }

  /// <summary>
  /// Converts Windows.Media SpeechWord cues into exact display-token ranges.
  /// Windows.Media reports cue positions in the complete SSML input coordinate
  /// space, so the first cue is used to remove the document/tag prefix before
  /// ranges are compared with <paramref name="text"/>.
  /// </summary>
  private static IReadOnlyList<SpeechWordBoundary>
    CreateWindowsMediaBoundaries(
      string text,
      SpeechSynthesisStream stream,
      TimeSpan duration,
      string voiceName)
  {
    MatchCollection tokens = SpeechTokenization.Matches(text);
    TimedMetadataTrack? wordTrack = stream.TimedMetadataTracks
      .FirstOrDefault(track => string.Equals(
        track.Label,
        "SpeechWord",
        StringComparison.OrdinalIgnoreCase));
    if (wordTrack is null)
    {
      DiagnosticLog.Write("speech.windows_media_boundaries_unavailable", new
      {
        voiceName,
        reason = "No SpeechWord timed-metadata track was returned."
      });
      return CreateApproximateBoundaries(text, duration);
    }

    SpeechCue[] cues = wordTrack.Cues
      .OfType<SpeechCue>()
      .OrderBy(cue => cue.StartTime)
      .ToArray();
    if (cues.Length == 0 || tokens.Count == 0)
    {
      DiagnosticLog.Write("speech.windows_media_boundaries_unavailable", new
      {
        voiceName,
        reason = "The SpeechWord track or source token list was empty.",
        cueCount = cues.Length,
        tokenCount = tokens.Count
      });
      return CreateApproximateBoundaries(text, duration);
    }

    int firstCueTokenIndex = FindMatchingTokenByText(
      tokens,
      cues[0].Text,
      0);
    if (firstCueTokenIndex < 0)
    {
      firstCueTokenIndex = 0;
    }
    int firstCueStart = cues[0].StartPositionInInput ?? 0;
    int inputPositionOffset =
      firstCueStart - tokens[firstCueTokenIndex].Index;

    var result = new List<SpeechWordBoundary>(cues.Length);
    var usedTokenIndexes = new HashSet<int>();
    int nextSearchTokenIndex = 0;
    int rejectedCueCount = 0;
    foreach (SpeechCue cue in cues)
    {
      int rawStart = cue.StartPositionInInput ?? -1;
      int rawEndInclusive = cue.EndPositionInInput ?? rawStart;
      int adjustedStart = rawStart < 0
        ? -1
        : rawStart - inputPositionOffset;
      int adjustedEndInclusive = rawEndInclusive < 0
        ? adjustedStart
        : rawEndInclusive - inputPositionOffset;
      int tokenIndex = FindTokenForInputRange(
        tokens,
        adjustedStart,
        adjustedEndInclusive);

      string cueText = cue.Text ?? string.Empty;
      if (tokenIndex < nextSearchTokenIndex ||
          tokenIndex < 0 ||
          !CueTextMatchesToken(cueText, tokens[tokenIndex].Value))
      {
        int textMatchedIndex = FindMatchingTokenByText(
          tokens,
          cueText,
          nextSearchTokenIndex);
        if (textMatchedIndex >= 0)
        {
          tokenIndex = textMatchedIndex;
        }
      }

      if (tokenIndex < 0 || tokenIndex >= tokens.Count)
      {
        ++rejectedCueCount;
        continue;
      }

      Match token = tokens[tokenIndex];
      if (usedTokenIndexes.Add(tokenIndex))
      {
        result.Add(new SpeechWordBoundary(
          cue.StartTime,
          tokenIndex,
          token.Index,
          token.Length,
          token.Value,
          Exact: true));
      }
      nextSearchTokenIndex = Math.Max(nextSearchTokenIndex, tokenIndex + 1);
    }

    int usableCueCount = cues.Count(cue =>
      !string.IsNullOrWhiteSpace(cue.Text));
    double coverage = usableCueCount == 0
      ? 0.0
      : (double)result.Count / usableCueCount;
    if (result.Count == 0 || coverage < 0.80)
    {
      DiagnosticLog.Write("speech.windows_media_boundaries_unavailable", new
      {
        voiceName,
        reason = "Too few SpeechWord cues mapped safely to source tokens.",
        cueCount = cues.Length,
        exactCueCount = result.Count,
        rejectedCueCount,
        tokenCount = tokens.Count,
        inputPositionOffset,
        coverage
      });
      return CreateApproximateBoundaries(text, duration);
    }

    DiagnosticLog.Write("speech.windows_media_boundaries", new
    {
      voiceName,
      exactCueCount = result.Count,
      cueCount = cues.Length,
      rejectedCueCount,
      tokenCount = tokens.Count,
      inputPositionOffset,
      coverage
    });
    return result;
  }

  /// <summary>
  /// Finds a monotonically following token whose visible text matches a cue.
  /// </summary>
  private static int FindMatchingTokenByText(
    MatchCollection tokens,
    string? cueText,
    int startIndex)
  {
    string normalizedCue = NormalizeCueText(cueText);
    if (normalizedCue.Length == 0)
    {
      return -1;
    }

    for (int index = Math.Max(0, startIndex); index < tokens.Count; ++index)
    {
      if (string.Equals(
        normalizedCue,
        NormalizeCueText(tokens[index].Value),
        StringComparison.OrdinalIgnoreCase))
      {
        return index;
      }
    }
    return -1;
  }

  /// <summary>
  /// Returns whether a word cue and display token describe the same text.
  /// </summary>
  private static bool CueTextMatchesToken(string? cueText, string tokenText)
  {
    string normalizedCue = NormalizeCueText(cueText);
    return normalizedCue.Length != 0 && string.Equals(
      normalizedCue,
      NormalizeCueText(tokenText),
      StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Removes punctuation differences that do not affect spoken word identity.
  /// </summary>
  private static string NormalizeCueText(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return string.Empty;
    }

    return new string(text
      .Where(character => char.IsLetterOrDigit(character) ||
        character is '_' or '\'' or '’' or '-')
      .ToArray());
  }

  private static int FindTokenForInputRange(
    MatchCollection tokens,
    int start,
    int endInclusive)
  {
    if (start < 0)
    {
      return -1;
    }

    int endExclusive = Math.Max(start + 1, endInclusive + 1);
    for (int index = 0; index < tokens.Count; ++index)
    {
      Match token = tokens[index];
      int tokenEnd = token.Index + token.Length;
      if (token.Index < endExclusive && tokenEnd > start)
      {
        return index;
      }
    }
    return -1;
  }

  private static IReadOnlyList<SpeechWordBoundary>
    CreateApproximateBoundaries(string text, TimeSpan duration)
  {
    MatchCollection matches = SpeechTokenization.Matches(text);
    if (matches.Count == 0)
    {
      return Array.Empty<SpeechWordBoundary>();
    }
    double totalWeight = matches.Cast<Match>()
      .Sum(match => Math.Max(1, match.Length));
    double elapsedWeight = 0.0;
    var result = new List<SpeechWordBoundary>(matches.Count);
    for (int index = 0; index < matches.Count; index++)
    {
      Match match = matches[index];
      result.Add(new SpeechWordBoundary(
        TimeSpan.FromTicks(checked((long)Math.Round(
          duration.Ticks * elapsedWeight / totalWeight))),
        index,
        match.Index,
        match.Length,
        match.Value,
        Exact: false));
      elapsedWeight += Math.Max(1, match.Length);
    }
    return result;
  }

  private static void ConfigureSapiVoice(
    object voiceObject,
    object voicesObject,
    int voiceIndex,
    SpeechProfileSettings profile)
  {
    dynamic voice = voiceObject;
    dynamic voices = voicesObject;
    object tokenObject = voices.Item(voiceIndex);
    try
    {
      voice.Voice = tokenObject;
    }
    finally
    {
      ReleaseComObject(tokenObject);
    }
    voice.Rate = profile.Rate;
    voice.Volume = profile.Volume;
  }

  /// <summary>
  /// Inserts one named SSML mark before each token after the first.  An
  /// attached period remains the same display token but is rendered as the
  /// spoken word "dot" so that token-level marks do not make it silent.
  /// </summary>
  private static bool IsWordCharacter(char value)
  {
    return char.IsLetterOrDigit(value) || value == '_';
  }

  /// <summary>
  /// Returns whether one complete display token is a leading decimal.
  /// </summary>
  private static bool IsLeadingDecimal(string value)
  {
    if (value.Length < 2 || value[0] != '.')
    {
      return false;
    }
    for (int index = 1; index < value.Length; ++index)
    {
      if (!char.IsDigit(value[index]))
      {
        return false;
      }
    }
    return true;
  }

  /// <summary>
  /// Returns synthesis text for one display token without changing its display
  /// range.  Leading decimals are spoken as "point" plus their digits.
  /// </summary>
  private static string GetBookmarkedSynthesisText(
    MatchCollection tokens,
    int index)
  {
    Match token = tokens[index];
    if (IsLeadingDecimal(token.Value))
    {
      return "point " + token.Value[1..];
    }

    bool attachedPeriod = token.Value == "." &&
      index + 1 < tokens.Count &&
      token.Index + token.Length == tokens[index + 1].Index &&
      IsWordCharacter(tokens[index + 1].Value[0]);
    return attachedPeriod ? "dot" : token.Value;
  }

  private static bool TryBuildBookmarkedSsml(
    SpeechMarkup markup,
    string cultureName,
    out string ssml)
  {
    try
    {
      XDocument document = XDocument.Parse(
        BuildSsmlDocument(markup.SsmlContent, cultureName),
        LoadOptions.PreserveWhitespace);
      XNamespace ns = document.Root?.Name.Namespace ??
        "http://www.w3.org/2001/10/synthesis";
      List<XText> textNodes = document
        .DescendantNodes()
        .OfType<XText>()
        .ToList();
      string visibleText = string.Concat(textNodes.Select(node => node.Value));
      MatchCollection tokens = SpeechTokenization.Matches(markup.PlainText);
      var placements = new List<(
        int Position,
        int Length,
        int TokenIndex,
        string SynthesisText)>();
      int searchPosition = 0;
      for (int index = 0; index < tokens.Count; ++index)
      {
        Match token = tokens[index];
        int position = visibleText.IndexOf(
          token.Value,
          searchPosition,
          StringComparison.Ordinal);
        if (position < 0)
        {
          position = visibleText.IndexOf(
            token.Value,
            searchPosition,
            StringComparison.OrdinalIgnoreCase);
        }
        if (position < 0)
        {
          DiagnosticLog.Write("speech.windows_media_bookmark_build_failed", new
          {
            reason = "A display token was not found in the generated SSML text.",
            tokenIndex = index,
            token = token.Value
          });
          ssml = string.Empty;
          return false;
        }

        placements.Add((
          position,
          token.Length,
          index,
          GetBookmarkedSynthesisText(tokens, index)));
        searchPosition = position + token.Length;
      }

      int[] nodeStarts = new int[textNodes.Count];
      int running = 0;
      for (int index = 0; index < textNodes.Count; ++index)
      {
        nodeStarts[index] = running;
        running += textNodes[index].Value.Length;
      }

      var nodePlacements = new Dictionary<int, List<(
        int Local,
        int Length,
        int TokenIndex,
        string SynthesisText)>>();
      foreach ((
          int position,
          int length,
          int tokenIndex,
          string synthesisText) in placements)
      {
        int nodeIndex = -1;
        for (int candidate = textNodes.Count - 1; candidate >= 0; --candidate)
        {
          int nodeEnd = nodeStarts[candidate] + textNodes[candidate].Value.Length;
          if (nodeStarts[candidate] <= position && position + length <= nodeEnd)
          {
            nodeIndex = candidate;
            break;
          }
        }
        if (nodeIndex < 0)
        {
          ssml = string.Empty;
          return false;
        }
        if (!nodePlacements.TryGetValue(nodeIndex, out var list))
        {
          list = new List<(
            int Local,
            int Length,
            int TokenIndex,
            string SynthesisText)>();
          nodePlacements.Add(nodeIndex, list);
        }
        list.Add((
          position - nodeStarts[nodeIndex],
          length,
          tokenIndex,
          synthesisText));
      }

      foreach ((
          int nodeIndex,
          List<(
            int Local,
            int Length,
            int TokenIndex,
            string SynthesisText)> list) in
          nodePlacements.OrderByDescending(pair => pair.Key))
      {
        XText node = textNodes[nodeIndex];
        var replacement = new List<object>();
        int consumed = 0;
        foreach ((
            int local,
            int length,
            int tokenIndex,
            string synthesisText) in list.OrderBy(value => value.Local))
        {
          replacement.Add(new XText(node.Value[consumed..local]));
          if (tokenIndex != 0)
          {
            replacement.Add(new XElement(
              ns + "mark",
              new XAttribute("name", $"aps_{tokenIndex}")));
          }
          replacement.Add(new XText(synthesisText));
          consumed = local + length;
        }
        replacement.Add(new XText(node.Value[consumed..]));
        node.ReplaceWith(replacement);
      }

      ssml = document.ToString(SaveOptions.DisableFormatting);
      return true;
    }
    catch (Exception exception) when (
      exception is System.Xml.XmlException or InvalidOperationException)
    {
      DiagnosticLog.Write("speech.windows_media_bookmark_build_failed", new
      {
        exception = exception.ToString()
      });
      ssml = string.Empty;
      return false;
    }
  }

  /// <summary>
  /// Converts SpeechBookmark cues to display-token boundaries and removes
  /// earlier tokens that share the next token's exact timestamp.
  /// </summary>
  private static bool TryCreateWindowsMediaBookmarkBoundaries(
    string text,
    SpeechSynthesisStream stream,
    out IReadOnlyList<SpeechWordBoundary> boundaries)
  {
    MatchCollection tokens = SpeechTokenization.Matches(text);
    TimedMetadataTrack? track = stream.TimedMetadataTracks
      .FirstOrDefault(candidate => string.Equals(
        candidate.Label,
        "SpeechBookmark",
        StringComparison.OrdinalIgnoreCase));
    if (track is null || tokens.Count == 0)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      return false;
    }

    var raw = new List<SpeechWordBoundary>();
    Match firstToken = tokens[0];
    raw.Add(new SpeechWordBoundary(
      TimeSpan.Zero,
      0,
      firstToken.Index,
      firstToken.Length,
      firstToken.Value,
      Exact: true));
    foreach (SpeechCue cue in track.Cues.OfType<SpeechCue>())
    {
      string identity = string.IsNullOrWhiteSpace(cue.Text)
        ? cue.Id ?? string.Empty
        : cue.Text;
      Match match = Regex.Match(identity, @"aps_(\d+)$");
      if (!match.Success ||
          !int.TryParse(match.Groups[1].Value, out int tokenIndex) ||
          tokenIndex < 0 || tokenIndex >= tokens.Count)
      {
        continue;
      }
      Match token = tokens[tokenIndex];
      raw.Add(new SpeechWordBoundary(
        cue.StartTime,
        tokenIndex,
        token.Index,
        token.Length,
        token.Value,
        Exact: true));
    }
    raw.Sort(static (left, right) =>
    {
      int timeComparison =
        left.AudioPosition.CompareTo(right.AudioPosition);
      return timeComparison != 0
        ? timeComparison
        : left.WordIndex.CompareTo(right.WordIndex);
    });
    if (raw.Count == 0)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      return false;
    }

    var compacted = new List<SpeechWordBoundary>(raw.Count);
    for (int index = 0; index < raw.Count; ++index)
    {
      bool collapsed = index + 1 < raw.Count &&
        raw[index].AudioPosition == raw[index + 1].AudioPosition;
      if (!collapsed)
      {
        compacted.Add(raw[index]);
      }
    }
    boundaries = compacted;
    return true;
  }

  private static string BuildSsmlDocument(
    string content,
    string cultureName)
  {
    string language = SecurityElement.Escape(cultureName) ?? "en-US";
    return
      $"<speak version=\"1.0\" " +
      $"xmlns=\"http://www.w3.org/2001/10/synthesis\" " +
      $"xml:lang=\"{language}\">{content}</speak>";
  }

  private void CancelPlayer(ref WaveOutPlayer? player)
  {
    if (player is null)
    {
      return;
    }

    try
    {
      player.Stop();
    }
    finally
    {
      player.Dispose();
      player = null;
      MarkAudioEnd();
    }
  }

  private void MarkAudioEnd()
  {
    _lastAudioEndTimestamp = Stopwatch.GetTimestamp();
    _hasAudioEndTimestamp = true;
  }

  private static void TryDeleteFile(string path)
  {
    try
    {
      File.Delete(path);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private void RaiseWordBoundary(SpeechWordBoundary boundary)
  {
    try
    {
      WordBoundary?.Invoke(boundary);
    }
    catch (Exception exception)
    {
      DiagnosticLog.Write("speech.word_boundary_handler_failed", new
      {
        exception = exception.ToString()
      });
    }
  }

  private void RaiseCompleted()
  {
    try
    {
      Completed?.Invoke();
    }
    catch (Exception exception)
    {
      DiagnosticLog.Write("speech.completed_handler_failed", new
      {
        exception = exception.ToString()
      });
    }
  }

  private void RaiseFaulted(Exception exception)
  {
    try
    {
      Faulted?.Invoke(exception);
    }
    catch (Exception handlerException)
    {
      DiagnosticLog.Write("speech.fault_handler_failed", new
      {
        exception = handlerException.ToString()
      });
    }
  }

  /// <summary>
  /// Reports a recovered preview problem without failing playback.
  /// </summary>
  private void RaiseNotice(string message)
  {
    try
    {
      Notice?.Invoke(message);
    }
    catch (Exception handlerException)
    {
      DiagnosticLog.Write("speech.notice_handler_failed", new
      {
        exception = handlerException.ToString()
      });
    }
  }

  private static void ReleaseComObject(object? value)
  {
    if (value is not null && Marshal.IsComObject(value))
    {
      Marshal.FinalReleaseComObject(value);
    }
  }

  private sealed record RenderedSpeechSegment(
    PcmWaveData Wave,
    IReadOnlyList<SpeechWordBoundary> WordBoundaries);

  private abstract record EngineCommand;

  private abstract record PlaybackCommand : EngineCommand;

  private sealed record SpeakCommand(
    SpeechMarkup Markup,
    SpeechProfileSettings Profile,
    AudioWakeSettings WakeSettings,
    bool ForceWake) : PlaybackCommand;

  private sealed record IpaPreviewCommand(
    SpeechMarkup? IsolatedMarkup,
    SpeechMarkup ExampleMarkup,
    SpeechMarkup? ExampleFallbackMarkup,
    SpeechProfileSettings Profile,
    AudioWakeSettings WakeSettings) : PlaybackCommand;

  private sealed record WakeTestCommand(
    AudioWakeSettings WakeSettings) : PlaybackCommand;

  private sealed record CancelCommand : EngineCommand
  {
    public static CancelCommand Instance { get; } = new();
  }

  private sealed record PauseCommand : EngineCommand
  {
    public static PauseCommand Instance { get; } = new();
  }

  private sealed record ResumeCommand : EngineCommand
  {
    public static ResumeCommand Instance { get; } = new();
  }

  private sealed record DisposeCommand : EngineCommand
  {
    public static DisposeCommand Instance { get; } = new();
  }

  private sealed class PlaybackRequest
  {
    private PlaybackRequest(
      SpeechProfileSettings profile,
      AudioWakeSettings wakeSettings,
      bool forceWake,
      IEnumerable<SpeechSegment> segments)
    {
      Profile = profile;
      WakeSettings = wakeSettings;
      ForceWake = forceWake;
      Segments = segments.ToArray();
    }

    public SpeechProfileSettings Profile { get; }

    public AudioWakeSettings WakeSettings { get; }

    public bool ForceWake { get; }

    public IReadOnlyList<SpeechSegment> Segments { get; }

    public static PlaybackRequest ForSpeech(SpeakCommand command)
    {
      return new PlaybackRequest(
        command.Profile,
        command.WakeSettings,
        command.ForceWake,
        new[]
        {
          new SpeechSegment(
            command.Markup,
            FallbackMarkup: null,
            SkipWhenRejected: false,
            DelayAfterPreviousMilliseconds: 0,
            Label: "speech")
        });
    }

    public static PlaybackRequest ForIpaPreview(IpaPreviewCommand command)
    {
      var segments = new List<SpeechSegment>();
      if (command.IsolatedMarkup is not null)
      {
        segments.Add(new SpeechSegment(
          command.IsolatedMarkup,
          FallbackMarkup: null,
          SkipWhenRejected: true,
          DelayAfterPreviousMilliseconds: 0,
          Label: "isolated IPA"));
        segments.Add(new SpeechSegment(
          command.ExampleMarkup,
          FallbackMarkup: command.ExampleFallbackMarkup,
          SkipWhenRejected: command.ExampleFallbackMarkup is null,
          DelayAfterPreviousMilliseconds:
            command.WakeSettings.IpaExampleDelayMilliseconds,
          Label: "IPA example"));
      }
      else
      {
        segments.Add(new SpeechSegment(
          command.ExampleMarkup,
          FallbackMarkup: command.ExampleFallbackMarkup,
          SkipWhenRejected: command.ExampleFallbackMarkup is null,
          DelayAfterPreviousMilliseconds: 0,
          Label: "IPA example"));
      }
      return new PlaybackRequest(
        command.Profile,
        command.WakeSettings,
        forceWake: command.WakeSettings.Enabled,
        segments: segments);
    }
  }

  private sealed record SpeechSegment(
    SpeechMarkup Markup,
    SpeechMarkup? FallbackMarkup,
    bool SkipWhenRejected,
    int DelayAfterPreviousMilliseconds,
    string Label);

  private readonly record struct SapiVoice(
    int Index,
    string DisplayName);

  private readonly record struct WindowsMediaVoice(
    string Id,
    string DisplayName,
    string Description,
    string Language);

  private sealed record VoiceRegistration(
    InstalledSpeechVoice Voice,
    VoiceBackend Backend);

  private readonly record struct VoiceBackend(
    SpeechBackend Backend,
    int SapiIndex,
    string ProviderVoiceId)
  {
    public static VoiceBackend ForSapi(int index, string providerVoiceId)
    {
      return new VoiceBackend(
        SpeechBackend.Sapi,
        index,
        providerVoiceId);
    }

    public static VoiceBackend ForSystemSpeech(string providerVoiceId)
    {
      return new VoiceBackend(
        SpeechBackend.SystemSpeech,
        -1,
        providerVoiceId);
    }

    public static VoiceBackend ForWindowsMedia(string providerVoiceId)
    {
      return new VoiceBackend(
        SpeechBackend.WindowsMedia,
        -1,
        providerVoiceId);
    }
  }

  private enum SpeechBackend
  {
    Sapi,
    SystemSpeech,
    WindowsMedia
  }
}
