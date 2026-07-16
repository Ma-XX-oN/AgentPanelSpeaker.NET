using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Speech.Synthesis;

namespace AgentPanelSpeaker;

/// <summary>
/// Renders speech and wake audio into one PCM buffer on one STA worker.
/// </summary>
internal sealed class SapiSpeechEngine : IDisposable
{
  private const int IsXml = 8;
  private const int SpFileModeCreateForWrite = 3;
  private const int WorkerPollMilliseconds = 40;
  private const int OutputSampleRate = 48000;

  private readonly BlockingCollection<EngineCommand> _commands = new();
  private readonly ManualResetEventSlim _initialized = new();
  private readonly Thread _thread;
  private IReadOnlyList<InstalledSpeechVoice> _voices =
    Array.Empty<InstalledSpeechVoice>();
  private Exception? _initializationException;
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
  /// Gets all enabled voices exposed by either Windows speech provider.
  /// </summary>
  public IReadOnlyList<InstalledSpeechVoice> Voices => _voices;

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
  /// Plays an optional isolated phone, waits, and then speaks its example.
  /// </summary>
  public void PreviewIpa(
    SpeechMarkup? isolatedMarkup,
    SpeechMarkup exampleMarkup,
    SpeechProfileSettings profile,
    AudioWakeSettings wakeSettings)
  {
    ArgumentNullException.ThrowIfNull(exampleMarkup);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(wakeSettings);
    AddCommand(new IpaPreviewCommand(
      isolatedMarkup,
      exampleMarkup,
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
  /// Creates both speech providers and services the serialized command queue.
  /// </summary>
  private void Run()
  {
    object? voiceObject = null;
    object? voicesObject = null;
    SpeechSynthesizer? synthesizer = null;
    try
    {
      synthesizer = new SpeechSynthesizer();
      synthesizer.SetOutputToNull();
      var systemVoices = EnumerateSystemSpeechVoices(synthesizer);
      var sapiVoices = new Dictionary<string, SapiVoice>(
        StringComparer.OrdinalIgnoreCase);

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

      var voiceBackends = new Dictionary<string, VoiceBackend>(
        StringComparer.OrdinalIgnoreCase);
      var displayNames = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase);
      foreach ((string name, string displayName) in systemVoices)
      {
        voiceBackends.TryAdd(name, VoiceBackend.ForSystemSpeech());
        AddPreferredDisplayName(displayNames, name, displayName);
      }
      foreach ((string name, SapiVoice sapiVoice) in sapiVoices)
      {
        voiceBackends[name] = VoiceBackend.ForSapi(sapiVoice.Index);
        AddPreferredDisplayName(
          displayNames,
          name,
          sapiVoice.DisplayName);
      }

      if (voiceBackends.Count == 0)
      {
        throw new InvalidOperationException(
          "No enabled Windows speech voices were found.");
      }

      _voices = voiceBackends.Keys
        .Select(name => new InstalledSpeechVoice(name, displayNames[name]))
        .OrderBy(
          voiceInfo => voiceInfo.DisplayName,
          StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

      DiagnosticLog.Write("speech.voices_enumerated", new
      {
        systemSpeechCount = systemVoices.Count,
        sapiCount = sapiVoices.Count,
        totalCount = _voices.Count
      });
      _initialized.Set();
      ServiceCommands(
        voiceObject,
        voicesObject,
        synthesizer,
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
      synthesizer?.Dispose();
      ReleaseComObject(voicesObject);
      ReleaseComObject(voiceObject);
    }
  }

  private static Dictionary<string, string> EnumerateSystemSpeechVoices(
    SpeechSynthesizer synthesizer)
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
    SpeechSynthesizer synthesizer,
    IReadOnlyDictionary<string, VoiceBackend> voiceBackends)
  {
    WaveOutPlayer? player = null;
    bool exiting = false;

    while (!exiting)
    {
      EngineCommand? command = null;
      try
      {
        if (_commands.TryTake(
              out command,
              WorkerPollMilliseconds) &&
            command is not null)
        {
          ProcessCommand(
            command,
            voiceObject,
            voicesObject,
            synthesizer,
            voiceBackends,
            ref player,
            ref exiting);
        }

        if (player is not null && player.IsComplete)
        {
          player.Dispose();
          player = null;
          MarkAudioEnd();
          RaiseCompleted();
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
    SpeechSynthesizer synthesizer,
    IReadOnlyDictionary<string, VoiceBackend> voiceBackends,
    ref WaveOutPlayer? player,
    ref bool exiting)
  {
    switch (command)
    {
      case SpeakCommand speak:
        CancelPlayer(ref player);
        player = StartSpeechPlayback(
          PlaybackRequest.ForSpeech(speak),
          voiceObject,
          voicesObject,
          synthesizer,
          voiceBackends);
        break;

      case IpaPreviewCommand preview:
        CancelPlayer(ref player);
        player = StartSpeechPlayback(
          PlaybackRequest.ForIpaPreview(preview),
          voiceObject,
          voicesObject,
          synthesizer,
          voiceBackends);
        break;

      case WakeTestCommand wakeTest:
        CancelPlayer(ref player);
        player = StartWakeToneTest(wakeTest.WakeSettings);
        break;

      case CancelCommand:
      {
        bool wasActive = player is not null;
        CancelPlayer(ref player);
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
  private WaveOutPlayer StartSpeechPlayback(
    PlaybackRequest request,
    object? voiceObject,
    object? voicesObject,
    SpeechSynthesizer synthesizer,
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
    PcmWaveData? outputFormat = null;
    foreach (SpeechSegment segment in request.Segments)
    {
      if (outputFormat is not null &&
          segment.DelayAfterPreviousMilliseconds > 0)
      {
        parts.Add(outputFormat.CreateSilence(
          segment.DelayAfterPreviousMilliseconds));
      }

      PcmWaveData rendered = RenderSpeech(
          segment.Markup,
          request.Profile,
          backend,
          voiceObject,
          voicesObject,
          synthesizer)
        .ConvertToMono16(OutputSampleRate);
      outputFormat ??= rendered;
      parts.Add(rendered);
    }

    if (outputFormat is null)
    {
      throw new InvalidOperationException(
        "A speech playback request contains no speech segments.");
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
    return new WaveOutPlayer(playback);
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

  private static PcmWaveData RenderSpeech(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    VoiceBackend backend,
    object? voiceObject,
    object? voicesObject,
    SpeechSynthesizer synthesizer)
  {
    return backend.Backend switch
    {
      SpeechBackend.Sapi => RenderSapiSpeech(
        markup,
        profile,
        backend.SapiIndex,
        voiceObject,
        voicesObject),
      SpeechBackend.SystemSpeech => RenderSystemSpeech(
        markup,
        profile,
        synthesizer),
      _ => throw new InvalidOperationException(
        "The selected voice has no speech backend.")
    };
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
    SpeechSynthesizer synthesizer)
  {
    using var stream = new MemoryStream();
    synthesizer.SelectVoice(profile.VoiceName);
    synthesizer.Rate = profile.Rate;
    synthesizer.Volume = profile.Volume;
    string ssml = BuildSsmlDocument(
      markup.SsmlContent,
      synthesizer.Voice.Culture.Name);
    try
    {
      synthesizer.SetOutputToWaveStream(stream);
      synthesizer.SpeakSsml(ssml);
    }
    finally
    {
      synthesizer.SetOutputToNull();
    }
    return PcmWaveData.Parse(stream.ToArray());
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

  private static void ReleaseComObject(object? value)
  {
    if (value is not null && Marshal.IsComObject(value))
    {
      Marshal.FinalReleaseComObject(value);
    }
  }

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
        new[] { new SpeechSegment(command.Markup, 0) });
    }

    public static PlaybackRequest ForIpaPreview(IpaPreviewCommand command)
    {
      var segments = new List<SpeechSegment>();
      if (command.IsolatedMarkup is not null)
      {
        segments.Add(new SpeechSegment(command.IsolatedMarkup, 0));
        segments.Add(new SpeechSegment(
          command.ExampleMarkup,
          command.WakeSettings.IpaExampleDelayMilliseconds));
      }
      else
      {
        segments.Add(new SpeechSegment(command.ExampleMarkup, 0));
      }
      return new PlaybackRequest(
        command.Profile,
        command.WakeSettings,
        forceWake: false,
        segments);
    }
  }

  private sealed record SpeechSegment(
    SpeechMarkup Markup,
    int DelayAfterPreviousMilliseconds);

  private readonly record struct SapiVoice(
    int Index,
    string DisplayName);

  private readonly record struct VoiceBackend(
    SpeechBackend Backend,
    int SapiIndex)
  {
    public static VoiceBackend ForSapi(int index)
    {
      return new VoiceBackend(SpeechBackend.Sapi, index);
    }

    public static VoiceBackend ForSystemSpeech()
    {
      return new VoiceBackend(SpeechBackend.SystemSpeech, -1);
    }
  }

  private enum SpeechBackend
  {
    Sapi,
    SystemSpeech
  }
}
