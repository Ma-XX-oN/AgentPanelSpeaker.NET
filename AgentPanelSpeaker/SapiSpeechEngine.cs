using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
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
  private const int SystemSpeechTimingSampleRate = 16000;

  private readonly BlockingCollection<EngineCommand> _commands = new();
  private readonly ManualResetEventSlim _initialized = new();
  private readonly Thread _thread;
  private IReadOnlyList<InstalledSpeechVoice> _voices =
    Array.Empty<InstalledSpeechVoice>();
  private Exception? _initializationException;
  private int _wordBoundaryPollMilliseconds = DefaultWorkerPollMilliseconds;
  private int _windowsMediaBookmarkMode =
    (int)WindowsMediaBookmarkMode.Fallback;
  private int _matchDesktopAndWindowsMediaRates = 1;
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
  /// Raised when audio can be synthesized but exact word ownership is not
  /// available. The caller must highlight the complete speech fragment.
  /// </summary>
  public event Action<SpeechTrackingDegradation>? WordTrackingUnavailable;

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
  /// Enables or disables Desktop/System.Speech rate calibration against the
  /// Windows.Media application rate scale.
  /// </summary>
  public void SetMatchDesktopAndWindowsMediaRates(bool enabled)
  {
    Volatile.Write(ref _matchDesktopAndWindowsMediaRates, enabled ? 1 : 0);
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
    SpeechTrackingDegradation? pendingTrackingDegradation = null;
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
            ref pendingTrackingDegradation,
            ref nextWordBoundary,
            ref exiting);
        }

        if (player is not null)
        {
          if (command is null && pendingTrackingDegradation is not null)
          {
            RaiseWordTrackingUnavailable(pendingTrackingDegradation);
            pendingTrackingDegradation = null;
          }
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
            pendingTrackingDegradation = null;
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
        pendingTrackingDegradation = null;
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
    ref SpeechTrackingDegradation? pendingTrackingDegradation,
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
        pendingTrackingDegradation = speechBuffer.TrackingDegradation;
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
        pendingTrackingDegradation = null;
        nextWordBoundary = 0;
        break;

      case WakeTestCommand wakeTest:
        CancelPlayer(ref player);
        player = StartWakeToneTest(wakeTest.WakeSettings);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        pendingTrackingDegradation = null;
        nextWordBoundary = 0;
        break;

      case CancelCommand:
      {
        bool wasActive = player is not null;
        CancelPlayer(ref player);
        wordBoundaries = Array.Empty<SpeechWordBoundary>();
        pendingTrackingDegradation = null;
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
        pendingTrackingDegradation = null;
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
    SpeechTrackingDegradation? trackingDegradation = null;
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
      trackingDegradation ??= rendered.TrackingDegradation;
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
      }).ToArray(),
      trackingDegradation);
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
    SpeechTrackingDegradation? trackingDegradation = null;
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
        boundaries = Array.Empty<SpeechWordBoundary>();
        trackingDegradation = new SpeechTrackingDegradation(
          "Sapi",
          profile.VoiceName,
          "native_sapi_has_no_word_timing");
        break;

      case SpeechBackend.SystemSpeech:
        wave = RenderSystemSpeech(
          markup,
          profile,
          backend.ProviderVoiceId,
          synthesizer,
          Volatile.Read(ref _matchDesktopAndWindowsMediaRates) != 0,
          out boundaries,
          out trackingDegradation);
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
          out boundaries,
          out trackingDegradation);
        break;

      default:
        throw new InvalidOperationException(
          "The selected voice has no speech backend.");
    }
    return new RenderedSpeechSegment(
      wave,
      boundaries,
      trackingDegradation);
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
  /// Maps the provider-neutral application rate onto System.Speech's
  /// coarser integer rate scale.  Issue #94 measurements show that
  /// one System.Speech step is approximately two application-rate
  /// steps over the supported UI range.
  /// </summary>
  private static int MapSystemSpeechRate(
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

  /// <summary>
  /// Renders System.Speech SSML into an in-memory WAVE file.
  /// </summary>
  private static PcmWaveData RenderSystemSpeech(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    string providerVoiceId,
    SystemSpeechSynthesizer synthesizer,
    bool matchDesktopAndWindowsMediaRates,
    out IReadOnlyList<SpeechWordBoundary> boundaries,
    out SpeechTrackingDegradation? trackingDegradation)
  {
    using var stream = new MemoryStream();
    var collected = new List<SpeechWordBoundary>();
    bool mappingFailed = false;

    synthesizer.SelectVoice(providerVoiceId);
    int providerRate = MapSystemSpeechRate(
      profile.Rate,
      matchDesktopAndWindowsMediaRates);
    synthesizer.Rate = providerRate;
    DiagnosticLog.Write("speech.system_speech_rate_mapped", new
    {
      applicationRate = profile.Rate,
      providerRate,
      matchDesktopAndWindowsMediaRates
    });
    synthesizer.Volume = profile.Volume;
    string ssml = BuildSsmlDocument(
      markup.SsmlContent,
      synthesizer.Voice.Culture.Name);
    DiagnosticLog.Write("speech.system_speech_markup_ssml_content", new
    {
      provider = "System.Speech",
      voice = providerVoiceId,
      culture = synthesizer.Voice.Culture.Name,
      characterLength = markup.SsmlContent.Length,
      utf8ByteLength = Encoding.UTF8.GetByteCount(markup.SsmlContent),
      sha256 = ComputeUtf8Sha256(markup.SsmlContent),
      ssmlContent = markup.SsmlContent
    });
    DiagnosticLog.Write("speech.system_speech_ssml_document", new
    {
      provider = "System.Speech",
      voice = providerVoiceId,
      culture = synthesizer.Voice.Culture.Name,
      characterLength = ssml.Length,
      utf8ByteLength = Encoding.UTF8.GetByteCount(ssml),
      sha256 = ComputeUtf8Sha256(ssml),
      ssml
    });

    EventHandler<System.Speech.Synthesis.SpeakProgressEventArgs> handler =
      (_, eventArgs) =>
      {
        if (!TryMapSystemSpeechProgress(
              markup,
              ssml,
              eventArgs.CharacterPosition,
              eventArgs.CharacterCount,
              eventArgs.Text,
              eventArgs.AudioPosition,
              out SpeechWordBoundary boundary))
        {
          mappingFailed = true;
          DiagnosticLog.Write("sapi.speak_progress_unmapped", new
          {
            provider = "System.Speech",
            voice = providerVoiceId,
            eventArgs.Text,
            eventArgs.CharacterPosition,
            eventArgs.CharacterCount,
            eventArgs.AudioPosition
          });
          return;
        }

        DiagnosticLog.Write("sapi.speak_progress", new
        {
          provider = "System.Speech",
          voice = providerVoiceId,
          markup.PlainText,
          eventArgs.Text,
          eventArgs.CharacterPosition,
          eventArgs.CharacterCount,
          eventArgs.AudioPosition,
          sourcePosition = boundary.CharacterPosition,
          sourceCount = boundary.CharacterCount,
          wordIndex = boundary.WordIndex,
          wordCount = boundary.WordCount
        });
        collected.Add(boundary);
      };

    synthesizer.SpeakProgress += handler;
    try
    {
      synthesizer.SetOutputToWaveStream(stream);
      DiagnosticLog.Write("speech.system_speech_ssml_submitted", new
      {
        provider = "System.Speech",
        voice = providerVoiceId,
        culture = synthesizer.Voice.Culture.Name,
        characterLength = ssml.Length,
        utf8ByteLength = Encoding.UTF8.GetByteCount(ssml),
        sha256 = ComputeUtf8Sha256(ssml),
        ssml
      });
      synthesizer.SpeakSsml(ssml);
    }
    finally
    {
      synthesizer.SpeakProgress -= handler;
      synthesizer.SetOutputToNull();
    }

    PcmWaveData wave = PcmWaveData.Parse(stream.ToArray());
    double timingScale = SystemSpeechTimingSampleRate /
      (double)wave.SampleRate;
    if (timingScale != 1.0)
    {
      collected = collected
        .Select(boundary => boundary with
        {
          AudioPosition = TimeSpan.FromTicks(checked((long)Math.Round(
            boundary.AudioPosition.Ticks * timingScale)))
        })
        .ToList();
    }
    DiagnosticLog.Write("speech.system_speech_timing_normalized", new
    {
      provider = "System.Speech",
      voice = providerVoiceId,
      providerTimingSampleRate = SystemSpeechTimingSampleRate,
      pcmSampleRate = wave.SampleRate,
      timingScale,
      boundaryCount = collected.Count
    });

    int expectedWordCount = GetSystemSpeechWords(markup).Count;
    if (expectedWordCount == 0)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      trackingDegradation = null;
    }
    else if (collected.Count == 0)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      trackingDegradation = new SpeechTrackingDegradation(
        "SystemSpeech",
        providerVoiceId,
        "system_speech_returned_no_progress_events");
    }
    else if (mappingFailed)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      trackingDegradation = new SpeechTrackingDegradation(
        "SystemSpeech",
        providerVoiceId,
        "system_speech_word_mapping_failed");
    }
    else
    {
      boundaries = collected;
      trackingDegradation = null;
    }
    return wave;
  }

  /// <summary>
  /// Returns a stable fingerprint for the exact UTF-8 diagnostic payload.
  /// </summary>
  private static string ComputeUtf8Sha256(string value)
  {
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  /// <summary>
  /// Translates one provider-native System.Speech progress range through the
  /// exact SSML provenance created with the speech markup.
  /// </summary>
  private static bool TryMapSystemSpeechProgress(
    SpeechMarkup markup,
    string ssml,
    int characterPosition,
    int characterCount,
    string spokenText,
    TimeSpan audioPosition,
    out SpeechWordBoundary boundary)
  {
    boundary = null!;
    if (markup.SsmlProvenance is not { Count: > 0 } provenance)
    {
      return false;
    }

    int contentStart = ssml.IndexOf(
      markup.SsmlContent,
      StringComparison.Ordinal);
    if (contentStart < 0)
    {
      return false;
    }

    int eventStart = characterPosition - contentStart;
    int eventLength = Math.Max(1, characterCount);
    int eventEnd;
    try
    {
      eventEnd = checked(eventStart + eventLength);
    }
    catch (OverflowException)
    {
      return false;
    }
    if (eventEnd <= 0 || eventStart >= markup.SsmlContent.Length)
    {
      return false;
    }

    SpeechMarkupProvenanceSpan[] owners = provenance
      .Where(span =>
        span.SsmlCharacterStart < eventEnd &&
        span.SsmlCharacterStart + span.SsmlCharacterLength > eventStart)
      .ToArray();
    if (owners.Length == 0)
    {
      return false;
    }

    IReadOnlyList<SpeechMarkupWord> words = GetSystemSpeechWords(markup);
    SpeechMarkupWord[] ownedWords = words
      .Where(word => owners.Any(owner =>
      {
        int ownerEnd = owner.SourceCharacterStart + owner.SourceCharacterLength;
        int wordEnd = word.CharacterStart + word.CharacterLength;
        return owner.SourceCharacterStart < wordEnd &&
          ownerEnd > word.CharacterStart;
      }))
      .GroupBy(word => word.WordIndex)
      .Select(group => group.First())
      .OrderBy(word => word.WordIndex)
      .ToArray();
    if (ownedWords.Length == 0)
    {
      return false;
    }

    for (int index = 1; index < ownedWords.Length; ++index)
    {
      if (ownedWords[index].WordIndex != ownedWords[index - 1].WordIndex + 1)
      {
        return false;
      }
    }

    SpeechMarkupWord first = ownedWords[0];
    SpeechMarkupWord last = ownedWords[^1];
    int sourceEnd = checked(last.CharacterStart + last.CharacterLength);
    boundary = new SpeechWordBoundary(
      audioPosition,
      first.WordIndex,
      first.CharacterStart,
      sourceEnd - first.CharacterStart,
      spokenText,
      Exact: true,
      WordCount: last.WordIndex - first.WordIndex + 1);
    return true;
  }

  /// <summary>
  /// Returns exact attached fragment words, or a plain-text token inventory for
  /// untracked preview speech that has no canonical transcript attachment.
  /// </summary>
  private static IReadOnlyList<SpeechMarkupWord> GetSystemSpeechWords(
    SpeechMarkup markup)
  {
    if (markup.Words is { Count: > 0 } exactWords)
    {
      return exactWords;
    }

    MatchCollection matches = SpeechTokenization.Matches(markup.PlainText);
    return matches
      .Cast<Match>()
      .Select((match, index) => new SpeechMarkupWord(
        index,
        match.Value,
        match.Index,
        match.Length))
      .ToArray();
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
    out IReadOnlyList<SpeechWordBoundary> boundaries,
    out SpeechTrackingDegradation? trackingDegradation)
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
    string bookmarkedSsml = string.Empty;
    bool bookmarkedSsmlBuilt = requestBookmarks && TryBuildBookmarkedSsml(
      markup,
      voice.Language,
      out bookmarkedSsml);
    string degradationReason = requestBookmarks
      ? bookmarkedSsmlBuilt
        ? string.Empty
        : "windows_media_bookmark_build_failed"
      : "windows_media_bookmarks_disabled";
    string ssml = bookmarkedSsmlBuilt
      ? bookmarkedSsml
      : BuildSsmlDocument(markup.SsmlContent, voice.Language);
    bool retriedWithoutBookmarks = false;

    SpeechSynthesisStream stream;
    try
    {
      stream = synthesizer
        .SynthesizeSsmlToStreamAsync(ssml)
        .AsTask()
        .GetAwaiter()
        .GetResult();
    }
    catch (Exception exception) when (
      exception is FormatException or COMException or ArgumentException)
    {
      DiagnosticLog.Write("speech.windows_media_ssml_rejected", new
      {
        voice = voice.DisplayName,
        voice.Language,
        bookmarkMode = bookmarkMode.ToString(),
        bookmarkedSsmlBuilt,
        ssml,
        hresult = exception.HResult,
        exception = exception.ToString()
      });

      if (!bookmarkedSsmlBuilt)
      {
        throw;
      }

      string fallbackSsml = BuildSsmlDocument(
        markup.SsmlContent,
        voice.Language);
      DiagnosticLog.Write("speech.windows_media_ssml_retry_without_bookmarks", new
      {
        voice = voice.DisplayName,
        voice.Language,
        bookmarkMode = bookmarkMode.ToString(),
        fallbackSsml
      });
      try
      {
        stream = synthesizer
          .SynthesizeSsmlToStreamAsync(fallbackSsml)
          .AsTask()
          .GetAwaiter()
          .GetResult();
        retriedWithoutBookmarks = true;
        degradationReason = "windows_media_bookmark_ssml_rejected";
      }
      catch (Exception fallbackException)
      {
        DiagnosticLog.Write("speech.windows_media_ssml_retry_failed", new
        {
          voice = voice.DisplayName,
          voice.Language,
          fallbackSsml,
          hresult = fallbackException.HResult,
          exception = fallbackException.ToString()
        });
        throw;
      }
    }

    using (stream)
    {
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
      string bookmarkFailureReason = string.Empty;
    if (bookmarkedSsmlBuilt && !retriedWithoutBookmarks &&
          TryCreateWindowsMediaBookmarkBoundaries(
            markup,
            stream,
            out IReadOnlyList<SpeechWordBoundary> bookmarkBoundaries,
            out bookmarkFailureReason))
      {
        boundaries = bookmarkBoundaries;
        trackingDegradation = null;
        DiagnosticLog.Write("speech.windows_media_bookmarks_used", new
        {
          voice = voice.DisplayName,
          mode = bookmarkMode.ToString(),
          boundaryCount = boundaries.Count
        });
      }
      else
      {
        boundaries = Array.Empty<SpeechWordBoundary>();
        if (degradationReason.Length == 0)
        {
          degradationReason = bookmarkFailureReason.Length == 0
            ? "windows_media_bookmark_metadata_unavailable"
            : bookmarkFailureReason;
        }
        trackingDegradation = new SpeechTrackingDegradation(
          "WindowsMedia",
          voice.DisplayName,
          degradationReason);
        DiagnosticLog.Write("speech.word_tracking_unavailable", new
        {
          backend = "WindowsMedia",
          voice = voice.DisplayName,
          reason = degradationReason,
          highlightMode = "fragment"
        });
      }
      return wave;
    }
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
      IReadOnlyList<SpeechMarkupWord> words = GetMarkupWords(markup);
      for (int wordIndex = words.Count - 1; wordIndex >= 0; --wordIndex)
      {
        SpeechMarkupWord word = words[wordIndex];
        List<XText> textNodes = document
          .DescendantNodes()
          .OfType<XText>()
          .ToList();
        int[] nodeStarts = new int[textNodes.Count];
        int running = 0;
        for (int index = 0; index < textNodes.Count; ++index)
        {
          nodeStarts[index] = running;
          running += textNodes[index].Value.Length;
        }
        string visibleText = string.Concat(textNodes.Select(node => node.Value));
        int position = word.CharacterStart;
        if (position < 0 || position + word.CharacterLength > visibleText.Length ||
            !string.Equals(
              visibleText.Substring(position, word.CharacterLength),
              word.Text,
              StringComparison.Ordinal))
        {
          DiagnosticLog.Write("speech.windows_media_bookmark_build_failed", new
          {
            reason = "Canonical word range was not preserved in generated SSML text.",
            wordIndex,
            word.Text,
            word.CharacterStart,
            word.CharacterLength
          });
          ssml = string.Empty;
          return false;
        }

        int nodeIndex = -1;
        for (int candidate = 0; candidate < textNodes.Count; ++candidate)
        {
          int nodeEnd = nodeStarts[candidate] + textNodes[candidate].Value.Length;
          if (nodeStarts[candidate] <= position && position < nodeEnd)
          {
            nodeIndex = candidate;
            break;
          }
        }
        if (nodeIndex < 0)
        {
          DiagnosticLog.Write("speech.windows_media_bookmark_build_failed", new
          {
            reason = "Canonical word start did not fall inside an SSML text node.",
            wordIndex,
            word.Text,
            word.CharacterStart
          });
          ssml = string.Empty;
          return false;
        }

        XText node = textNodes[nodeIndex];
        int local = position - nodeStarts[nodeIndex];
        int available = node.Value.Length - local;
        bool wholeWordInNode = available >= word.CharacterLength &&
          string.Equals(
            node.Value.Substring(local, word.CharacterLength),
            word.Text,
            StringComparison.Ordinal);
        string synthesisText = GetOwnedBookmarkedSynthesisText(markup, words, wordIndex);
        string prefix = node.Value[..local];
        string suffix;
        string spoken;
        if (wholeWordInNode &&
            !string.Equals(synthesisText, word.Text, StringComparison.Ordinal))
        {
          suffix = node.Value[(local + word.CharacterLength)..];
          spoken = synthesisText;
        }
        else
        {
          suffix = node.Value[local..];
          spoken = string.Empty;
        }

        var mark = new XElement(
          ns + "mark",
          new XAttribute("name", $"aps_{word.WordIndex}"));
        XElement? sayAs = node
          .Ancestors()
          .FirstOrDefault(element => string.Equals(
            element.Name.LocalName,
            "say-as",
            StringComparison.OrdinalIgnoreCase));
        bool markOutsideSayAs = sayAs is not null;
        if (markOutsideSayAs)
        {
          sayAs!.AddBeforeSelf(mark);
        }

        var replacement = new List<object>();
        if (prefix.Length != 0)
        {
          replacement.Add(new XText(prefix));
        }
        if (!markOutsideSayAs)
        {
          replacement.Add(mark);
        }
        if (spoken.Length != 0)
        {
          replacement.Add(new XText(spoken));
        }
        if (suffix.Length != 0)
        {
          replacement.Add(new XText(suffix));
        }
        node.ReplaceWith(replacement);
      }

      ssml = document.ToString(SaveOptions.DisableFormatting);
      return true;
    }
    catch (Exception exception) when (
      exception is System.Xml.XmlException or InvalidOperationException or
      ArgumentOutOfRangeException)
    {
      DiagnosticLog.Write("speech.windows_media_bookmark_build_failed", new
      {
        exception = exception.ToString()
      });
      ssml = string.Empty;
      return false;
    }
  }

  private static IReadOnlyList<SpeechMarkupWord> GetMarkupWords(
    SpeechMarkup markup)
  {
    if (markup.Words is { Count: > 0 } exactWords)
    {
      return exactWords;
    }
    MatchCollection tokens = SpeechTokenization.Matches(markup.PlainText);
    return tokens
      .Cast<Match>()
      .Select((token, index) => new SpeechMarkupWord(
        index,
        token.Value,
        token.Index,
        token.Length))
      .ToArray();
  }

  private static string GetOwnedBookmarkedSynthesisText(
    SpeechMarkup markup,
    IReadOnlyList<SpeechMarkupWord> words,
    int index)
  {
    SpeechMarkupWord word = words[index];
    if (IsLeadingDecimal(word.Text))
    {
      return "point " + word.Text[1..];
    }
    bool attachedPeriod = word.Text == "." &&
      index + 1 < words.Count &&
      word.CharacterStart + word.CharacterLength ==
        words[index + 1].CharacterStart &&
      words[index + 1].Text.Length != 0 &&
      IsWordCharacter(words[index + 1].Text[0]);
    return attachedPeriod ? "dot" : word.Text;
  }

  /// <summary>
  /// Converts SpeechBookmark cues to display-token boundaries and removes
  /// earlier tokens that share the next token's exact timestamp.
  /// </summary>
  private static bool TryCreateWindowsMediaBookmarkBoundaries(
    SpeechMarkup markup,
    SpeechSynthesisStream stream,
    out IReadOnlyList<SpeechWordBoundary> boundaries,
    out string failureReason)
  {
    IReadOnlyList<SpeechMarkupWord> words = GetMarkupWords(markup);
    TimedMetadataTrack? track = stream.TimedMetadataTracks
      .FirstOrDefault(candidate => string.Equals(
        candidate.Label,
        "SpeechBookmark",
        StringComparison.OrdinalIgnoreCase));
    if (track is null)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      failureReason = "windows_media_missing_speechbookmark_track";
      return false;
    }
    if (words.Count == 0)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      failureReason = string.Empty;
      return true;
    }

    var raw = new List<SpeechWordBoundary>();
    foreach (SpeechCue cue in track.Cues.OfType<SpeechCue>())
    {
      string identity = string.IsNullOrWhiteSpace(cue.Text)
        ? cue.Id ?? string.Empty
        : cue.Text;
      Match match = Regex.Match(identity, @"aps_(\d+)$");
      if (!match.Success ||
          !int.TryParse(match.Groups[1].Value, out int ownerIndex))
      {
        continue;
      }
      SpeechMarkupWord? word = words.FirstOrDefault(candidate =>
        candidate.WordIndex == ownerIndex);
      if (word is null)
      {
        boundaries = Array.Empty<SpeechWordBoundary>();
        failureReason = "windows_media_bookmark_unknown_word_owner";
        return false;
      }
      raw.Add(new SpeechWordBoundary(
        cue.StartTime,
        word.WordIndex,
        word.CharacterStart,
        word.CharacterLength,
        word.Text,
        Exact: true));
    }

    int[] expected = words.Select(word => word.WordIndex).Order().ToArray();
    int[] observed = raw.Select(boundary => boundary.WordIndex)
      .Distinct()
      .Order()
      .ToArray();
    if (!observed.SequenceEqual(expected))
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      failureReason = "windows_media_incomplete_speechbookmark_mapping";
      return false;
    }

    raw.Sort(static (left, right) =>
    {
      int timeComparison =
        left.AudioPosition.CompareTo(right.AudioPosition);
      return timeComparison != 0
        ? timeComparison
        : left.WordIndex.CompareTo(right.WordIndex);
    });
    var grouped = new List<SpeechWordBoundary>();
    for (int index = 0; index < raw.Count;)
    {
      int end = index + 1;
      while (end < raw.Count &&
             raw[end].AudioPosition == raw[index].AudioPosition)
      {
        ++end;
      }
      SpeechWordBoundary[] sameTime = raw.GetRange(index, end - index)
        .GroupBy(boundary => boundary.WordIndex)
        .Select(group => group.First())
        .OrderBy(boundary => boundary.WordIndex)
        .ToArray();
      for (int item = 1; item < sameTime.Length; ++item)
      {
        if (sameTime[item].WordIndex != sameTime[item - 1].WordIndex + 1)
        {
          boundaries = Array.Empty<SpeechWordBoundary>();
          failureReason = "windows_media_noncontiguous_shared_sound_ownership";
          return false;
        }
      }
      SpeechWordBoundary firstBoundary = sameTime[0];
      SpeechWordBoundary lastBoundary = sameTime[^1];
      int rangeEnd = checked(
        lastBoundary.CharacterPosition + lastBoundary.CharacterCount);
      grouped.Add(firstBoundary with
      {
        CharacterCount = rangeEnd - firstBoundary.CharacterPosition,
        Text = markup.PlainText.Substring(
          firstBoundary.CharacterPosition,
          rangeEnd - firstBoundary.CharacterPosition),
        WordCount = sameTime.Length
      });
      index = end;
    }

    boundaries = grouped;
    failureReason = string.Empty;
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

  private void RaiseWordTrackingUnavailable(
    SpeechTrackingDegradation degradation)
  {
    try
    {
      WordTrackingUnavailable?.Invoke(degradation);
    }
    catch (Exception exception)
    {
      DiagnosticLog.Write("speech.word_tracking_handler_failed", new
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
    IReadOnlyList<SpeechWordBoundary> WordBoundaries,
    SpeechTrackingDegradation? TrackingDegradation);

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
