using System.Collections.Concurrent;
using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using System.Security;
using System.Speech.Synthesis;

namespace AgentPanelSpeaker;

/// <summary>
/// Serializes SAPI and System.Speech output on one STA worker thread.
/// </summary>
internal sealed class SapiSpeechEngine : IDisposable
{
  private const int SpeakAsync = 1;
  private const int PurgeBeforeSpeak = 2;
  private const int IsXml = 8;
  private const int WorkerPollMilliseconds = 40;
  private const int ToneSampleRate = 48000;

  private readonly BlockingCollection<EngineCommand> _commands = new();
  private readonly ManualResetEventSlim _initialized = new();
  private readonly Thread _thread;
  private IReadOnlyList<string> _voiceNames = Array.Empty<string>();
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
  /// Raised when the worker encounters a synthesis failure.
  /// </summary>
  public event Action<Exception>? Faulted;

  /// <summary>
  /// Gets all enabled voices exposed by either Windows speech provider.
  /// </summary>
  public IReadOnlyList<string> VoiceNames => _voiceNames;

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
      wakeSettings.Normalize()));
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
  /// Plays the wake tone and settling delay regardless of quiet duration.
  /// </summary>
  public void TestWakeTone(AudioWakeSettings wakeSettings)
  {
    ArgumentNullException.ThrowIfNull(wakeSettings);
    AddCommand(new WakeTestCommand(wakeSettings.Normalize()));
  }

  /// <summary>
  /// Cancels the active utterance or preview sequence.
  /// </summary>
  public void Cancel()
  {
    AddCommand(CancelCommand.Instance);
  }

  /// <summary>
  /// Pauses the active utterance.
  /// </summary>
  public void Pause()
  {
    AddCommand(PauseCommand.Instance);
  }

  /// <summary>
  /// Resumes the active utterance.
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

  /// <summary>
  /// Adds one command unless disposal has begun.
  /// </summary>
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
      synthesizer.SetOutputToDefaultAudioDevice();
      string[] systemVoiceNames = synthesizer
        .GetInstalledVoices()
        .Where(installed => installed.Enabled)
        .Select(installed => installed.VoiceInfo.Name.Trim())
        .Where(name => name.Length != 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

      var sapiVoiceIndexes = new Dictionary<string, int>(
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
        EnumerateSapiVoices(voicesObject, sapiVoiceIndexes);
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
        sapiVoiceIndexes.Clear();
      }

      var voiceBackends = new Dictionary<string, VoiceBackend>(
        StringComparer.OrdinalIgnoreCase);
      foreach (string name in systemVoiceNames)
      {
        voiceBackends.TryAdd(name, VoiceBackend.ForSystemSpeech());
      }
      foreach ((string name, int index) in sapiVoiceIndexes)
      {
        voiceBackends[name] = VoiceBackend.ForSapi(index);
      }

      if (voiceBackends.Count == 0)
      {
        throw new InvalidOperationException(
          "No enabled Windows speech voices were found.");
      }

      _voiceNames = voiceBackends.Keys
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();
      var completions = new ConcurrentQueue<SystemSpeechCompletion>();
      synthesizer.SpeakCompleted += (_, eventArgs) =>
        completions.Enqueue(new SystemSpeechCompletion(
          eventArgs.Prompt,
          eventArgs.Error,
          eventArgs.Cancelled));

      DiagnosticLog.Write("speech.voices_enumerated", new
      {
        systemSpeechCount = systemVoiceNames.Length,
        sapiCount = sapiVoiceIndexes.Count,
        totalCount = _voiceNames.Count
      });
      _initialized.Set();
      ServiceCommands(
        voiceObject,
        voicesObject,
        synthesizer,
        voiceBackends,
        completions);
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

  /// <summary>
  /// Adds all usable native SAPI tokens to a name-to-index map.
  /// </summary>
  private static void EnumerateSapiVoices(
    object voicesObject,
    IDictionary<string, int> voiceIndexes)
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
        if (name.Length == 0)
        {
          name = Convert.ToString(token.GetDescription(0))?.Trim() ??
            string.Empty;
        }
        if (name.Length != 0 && !voiceIndexes.ContainsKey(name))
        {
          voiceIndexes.Add(name, index);
        }
      }
      finally
      {
        ReleaseComObject(tokenObject);
      }
    }
  }

  /// <summary>
  /// Processes commands, preview delays, and asynchronous completion.
  /// </summary>
  private void ServiceCommands(
    object? voiceObject,
    object? voicesObject,
    SpeechSynthesizer synthesizer,
    IReadOnlyDictionary<string, VoiceBackend> voiceBackends,
    ConcurrentQueue<SystemSpeechCompletion> completions)
  {
    PlaybackSequence? sequence = null;
    SpeechBackend activeBackend = SpeechBackend.None;
    Prompt? activeSystemPrompt = null;
    bool speaking = false;
    bool exiting = false;

    while (!exiting)
    {
      try
      {
        if (sequence is null)
        {
          EngineCommand command = _commands.Take();
          ProcessCommand(
            command,
            voiceObject,
            voicesObject,
            synthesizer,
            voiceBackends,
            ref sequence,
            ref activeBackend,
            ref activeSystemPrompt,
            ref speaking,
            ref exiting);
          continue;
        }

        int wait = WorkerPollMilliseconds;
        if (!speaking && sequence.NextSegmentUtc is DateTime next)
        {
          TimeSpan remaining = next - DateTime.UtcNow;
          if (remaining <= TimeSpan.Zero)
          {
            StartNextSegment(
              sequence,
              voiceObject,
              voicesObject,
              synthesizer,
              voiceBackends,
              ref activeBackend,
              ref activeSystemPrompt,
              ref speaking);
            continue;
          }
          wait = Math.Max(
            1,
            Math.Min(
              WorkerPollMilliseconds,
              (int)Math.Ceiling(remaining.TotalMilliseconds)));
        }

        if (_commands.TryTake(out EngineCommand? pending, wait) &&
            pending is not null)
        {
          ProcessCommand(
            pending,
            voiceObject,
            voicesObject,
            synthesizer,
            voiceBackends,
            ref sequence,
            ref activeBackend,
            ref activeSystemPrompt,
            ref speaking,
            ref exiting);
          continue;
        }

        if (!speaking)
        {
          continue;
        }

        bool completed = activeBackend switch
        {
          SpeechBackend.Sapi => IsSapiComplete(voiceObject),
          SpeechBackend.SystemSpeech => TryTakeSystemCompletion(
            completions,
            activeSystemPrompt),
          _ => throw new InvalidOperationException(
            "Active speech has no selected backend.")
        };
        if (!completed)
        {
          continue;
        }

        speaking = false;
        activeBackend = SpeechBackend.None;
        activeSystemPrompt = null;
        MarkAudioEnd();
        if (sequence.Segments.Count == 0)
        {
          sequence = null;
          RaiseCompleted();
        }
        else
        {
          int delay = sequence.Segments.Peek().DelayAfterPreviousMilliseconds;
          sequence.NextSegmentUtc = DateTime.UtcNow.AddMilliseconds(delay);
        }
      }
      catch (Exception exception)
      {
        bool hadActivePlayback = sequence is not null;
        sequence = null;
        speaking = false;
        activeBackend = SpeechBackend.None;
        activeSystemPrompt = null;
        RaiseFaulted(exception);
        if (hadActivePlayback)
        {
          RaiseCompleted();
        }
      }
    }
  }

  /// <summary>
  /// Gets whether the native SAPI utterance has completed.
  /// </summary>
  private static bool IsSapiComplete(object? voiceObject)
  {
    if (voiceObject is null)
    {
      throw new InvalidOperationException(
        "The selected native SAPI voice provider is unavailable.");
    }
    dynamic voice = voiceObject;
    return Convert.ToBoolean(voice.WaitUntilDone(0));
  }

  /// <summary>
  /// Removes queued completion events until the active prompt is found.
  /// </summary>
  private static bool TryTakeSystemCompletion(
    ConcurrentQueue<SystemSpeechCompletion> completions,
    Prompt? activePrompt)
  {
    if (activePrompt is null)
    {
      throw new InvalidOperationException(
        "System.Speech is active without an associated prompt.");
    }

    while (completions.TryDequeue(out SystemSpeechCompletion completion))
    {
      if (!ReferenceEquals(completion.Prompt, activePrompt))
      {
        continue;
      }
      if (completion.Error is not null)
      {
        throw new InvalidOperationException(
          "System.Speech failed while speaking.",
          completion.Error);
      }
      return true;
    }
    return false;
  }

  /// <summary>
  /// Applies one command on the speech apartment thread.
  /// </summary>
  private void ProcessCommand(
    EngineCommand command,
    object? voiceObject,
    object? voicesObject,
    SpeechSynthesizer synthesizer,
    IReadOnlyDictionary<string, VoiceBackend> voiceBackends,
    ref PlaybackSequence? sequence,
    ref SpeechBackend activeBackend,
    ref Prompt? activeSystemPrompt,
    ref bool speaking,
    ref bool exiting)
  {
    switch (command)
    {
      case SpeakCommand speak:
        CancelCurrentWithoutCompletion(
          voiceObject,
          synthesizer,
          ref sequence,
          ref activeBackend,
          ref activeSystemPrompt,
          ref speaking);
        sequence = PlaybackSequence.ForSpeech(
          speak.Markup,
          speak.Profile,
          speak.WakeSettings);
        StartNextSegment(
          sequence,
          voiceObject,
          voicesObject,
          synthesizer,
          voiceBackends,
          ref activeBackend,
          ref activeSystemPrompt,
          ref speaking);
        break;

      case IpaPreviewCommand preview:
        CancelCurrentWithoutCompletion(
          voiceObject,
          synthesizer,
          ref sequence,
          ref activeBackend,
          ref activeSystemPrompt,
          ref speaking);
        sequence = PlaybackSequence.ForIpaPreview(preview);
        StartNextSegment(
          sequence,
          voiceObject,
          voicesObject,
          synthesizer,
          voiceBackends,
          ref activeBackend,
          ref activeSystemPrompt,
          ref speaking);
        break;

      case WakeTestCommand wakeTest:
        CancelCurrentWithoutCompletion(
          voiceObject,
          synthesizer,
          ref sequence,
          ref activeBackend,
          ref activeSystemPrompt,
          ref speaking);
        PlayWakeTone(wakeTest.WakeSettings, force: true);
        RaiseCompleted();
        break;

      case CancelCommand:
      {
        bool wasActive = sequence is not null;
        CancelCurrentWithoutCompletion(
          voiceObject,
          synthesizer,
          ref sequence,
          ref activeBackend,
          ref activeSystemPrompt,
          ref speaking);
        if (wasActive)
        {
          RaiseCompleted();
        }
        break;
      }

      case PauseCommand when speaking:
        PauseActive(voiceObject, synthesizer, activeBackend);
        break;

      case ResumeCommand when speaking:
        ResumeActive(voiceObject, synthesizer, activeBackend);
        break;

      case DisposeCommand:
        CancelCurrentWithoutCompletion(
          voiceObject,
          synthesizer,
          ref sequence,
          ref activeBackend,
          ref activeSystemPrompt,
          ref speaking);
        exiting = true;
        break;
    }
  }

  /// <summary>
  /// Starts the next queued segment after applying wake policy.
  /// </summary>
  private void StartNextSegment(
    PlaybackSequence sequence,
    object? voiceObject,
    object? voicesObject,
    SpeechSynthesizer synthesizer,
    IReadOnlyDictionary<string, VoiceBackend> voiceBackends,
    ref SpeechBackend activeBackend,
    ref Prompt? activeSystemPrompt,
    ref bool speaking)
  {
    if (sequence.Segments.Count == 0)
    {
      return;
    }
    if (!voiceBackends.TryGetValue(
          sequence.Profile.VoiceName,
          out VoiceBackend backend))
    {
      throw new ArgumentException(
        $"Voice is not installed: {sequence.Profile.VoiceName}");
    }

    SpeechSegment segment = sequence.Segments.Dequeue();
    sequence.NextSegmentUtc = null;
    PlayWakeTone(sequence.WakeSettings, force: false);
    switch (backend.Backend)
    {
      case SpeechBackend.Sapi:
        ConfigureSapiVoice(
          voiceObject,
          voicesObject,
          backend.SapiIndex,
          sequence.Profile);
        dynamic voice = voiceObject!;
        voice.Speak(segment.Markup.SapiXml, SpeakAsync | IsXml);
        activeBackend = SpeechBackend.Sapi;
        activeSystemPrompt = null;
        break;

      case SpeechBackend.SystemSpeech:
        synthesizer.SelectVoice(sequence.Profile.VoiceName);
        synthesizer.Rate = sequence.Profile.Rate;
        synthesizer.Volume = sequence.Profile.Volume;
        string ssml = BuildSsmlDocument(
          segment.Markup.SsmlContent,
          synthesizer.Voice.Culture.Name);
        activeSystemPrompt = synthesizer.SpeakSsmlAsync(ssml);
        activeBackend = SpeechBackend.SystemSpeech;
        break;

      default:
        throw new InvalidOperationException(
          "The selected voice has no speech backend.");
    }
    speaking = true;
  }

  /// <summary>
  /// Selects one native SAPI token and applies rate and volume.
  /// </summary>
  private static void ConfigureSapiVoice(
    object? voiceObject,
    object? voicesObject,
    int voiceIndex,
    SpeechProfileSettings profile)
  {
    if (voiceObject is null || voicesObject is null || voiceIndex < 0)
    {
      throw new InvalidOperationException(
        "The selected native SAPI voice is unavailable.");
    }

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
  /// Wraps an SSML fragment in a culture-specific speak document.
  /// </summary>
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

  /// <summary>
  /// Pauses the active provider.
  /// </summary>
  private static void PauseActive(
    object? voiceObject,
    SpeechSynthesizer synthesizer,
    SpeechBackend activeBackend)
  {
    if (activeBackend == SpeechBackend.SystemSpeech)
    {
      synthesizer.Pause();
      return;
    }
    if (activeBackend != SpeechBackend.Sapi || voiceObject is null)
    {
      throw new InvalidOperationException("No speech provider is active.");
    }
    dynamic voice = voiceObject;
    voice.Pause();
  }

  /// <summary>
  /// Resumes the active provider.
  /// </summary>
  private static void ResumeActive(
    object? voiceObject,
    SpeechSynthesizer synthesizer,
    SpeechBackend activeBackend)
  {
    if (activeBackend == SpeechBackend.SystemSpeech)
    {
      synthesizer.Resume();
      return;
    }
    if (activeBackend != SpeechBackend.Sapi || voiceObject is null)
    {
      throw new InvalidOperationException("No speech provider is active.");
    }
    dynamic voice = voiceObject;
    voice.Resume();
  }

  /// <summary>
  /// Cancels current output and queued preview segments without completion.
  /// </summary>
  private void CancelCurrentWithoutCompletion(
    object? voiceObject,
    SpeechSynthesizer synthesizer,
    ref PlaybackSequence? sequence,
    ref SpeechBackend activeBackend,
    ref Prompt? activeSystemPrompt,
    ref bool speaking)
  {
    if (speaking)
    {
      switch (activeBackend)
      {
        case SpeechBackend.Sapi when voiceObject is not null:
          dynamic voice = voiceObject;
          voice.Speak(string.Empty, SpeakAsync | PurgeBeforeSpeak);
          break;

        case SpeechBackend.SystemSpeech:
          synthesizer.SpeakAsyncCancelAll();
          break;
      }
      MarkAudioEnd();
    }
    speaking = false;
    activeBackend = SpeechBackend.None;
    activeSystemPrompt = null;
    sequence = null;
  }

  /// <summary>
  /// Emits the configured wake tone when the quiet threshold is exceeded.
  /// </summary>
  private void PlayWakeTone(AudioWakeSettings settings, bool force)
  {
    AudioWakeSettings normalized = settings.Normalize();
    if (!force && !normalized.Enabled)
    {
      return;
    }

    double quietMilliseconds = _hasAudioEndTimestamp
      ? Stopwatch.GetElapsedTime(_lastAudioEndTimestamp).TotalMilliseconds
      : double.PositiveInfinity;
    if (!force &&
        quietMilliseconds <= normalized.QuietDurationMilliseconds)
    {
      return;
    }

    try
    {
      using var stream = new MemoryStream(CreateToneWave(normalized));
      using var player = new SoundPlayer(stream);
      player.PlaySync();
      MarkAudioEnd();
      DiagnosticLog.Write("speech.wake_tone", new
      {
        normalized.FrequencyHertz,
        normalized.ToneVolume,
        normalized.PlayDurationMilliseconds,
        normalized.SettleDurationMilliseconds,
        force
      });
    }
    catch (Exception exception) when (
      exception is InvalidOperationException or IOException or
      System.ComponentModel.Win32Exception)
    {
      DiagnosticLog.Write("speech.wake_tone_failed", new
      {
        exception = exception.ToString()
      });
      RaiseFaulted(exception);
    }

    if (normalized.SettleDurationMilliseconds > 0)
    {
      Thread.Sleep(normalized.SettleDurationMilliseconds);
    }
  }

  /// <summary>
  /// Records the end of audio output using a monotonic clock.
  /// </summary>
  private void MarkAudioEnd()
  {
    _lastAudioEndTimestamp = Stopwatch.GetTimestamp();
    _hasAudioEndTimestamp = true;
  }

  /// <summary>
  /// Creates a mono 16-bit PCM sine wave with short anti-click fades.
  /// </summary>
  private static byte[] CreateToneWave(AudioWakeSettings settings)
  {
    int sampleCount = Math.Max(
      1,
      ToneSampleRate * settings.PlayDurationMilliseconds / 1000);
    const short channels = 1;
    const short bitsPerSample = 16;
    int bytesPerSample = bitsPerSample / 8;
    int dataLength = sampleCount * channels * bytesPerSample;

    using var stream = new MemoryStream(44 + dataLength);
    using var writer = new BinaryWriter(stream);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(36 + dataLength);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
    writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
    writer.Write(16);
    writer.Write((short)1);
    writer.Write(channels);
    writer.Write(ToneSampleRate);
    writer.Write(ToneSampleRate * channels * bytesPerSample);
    writer.Write((short)(channels * bytesPerSample));
    writer.Write(bitsPerSample);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
    writer.Write(dataLength);

    double amplitude = short.MaxValue * settings.ToneVolume / 100.0;
    int fadeSamples = Math.Min(sampleCount / 2, ToneSampleRate / 200);
    for (int sample = 0; sample < sampleCount; ++sample)
    {
      double fade = 1.0;
      if (fadeSamples > 0 && sample < fadeSamples)
      {
        fade = sample / (double)fadeSamples;
      }
      else if (fadeSamples > 0 && sample >= sampleCount - fadeSamples)
      {
        fade = (sampleCount - sample - 1) / (double)fadeSamples;
      }

      double angle = 2.0 * Math.PI * settings.FrequencyHertz *
        sample / ToneSampleRate;
      short value = (short)Math.Clamp(
        Math.Round(Math.Sin(angle) * amplitude * Math.Max(0.0, fade)),
        short.MinValue,
        short.MaxValue);
      writer.Write(value);
    }
    writer.Flush();
    return stream.ToArray();
  }

  /// <summary>
  /// Raises completion without allowing a subscriber failure to kill speech.
  /// </summary>
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

  /// <summary>
  /// Raises a worker failure without terminating from a subscriber exception.
  /// </summary>
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
  /// Releases one late-bound COM object when applicable.
  /// </summary>
  private static void ReleaseComObject(object? value)
  {
    if (value is not null && Marshal.IsComObject(value))
    {
      Marshal.FinalReleaseComObject(value);
    }
  }

  private abstract record EngineCommand;

  private sealed record SpeakCommand(
    SpeechMarkup Markup,
    SpeechProfileSettings Profile,
    AudioWakeSettings WakeSettings) : EngineCommand;

  private sealed record IpaPreviewCommand(
    SpeechMarkup? IsolatedMarkup,
    SpeechMarkup ExampleMarkup,
    SpeechProfileSettings Profile,
    AudioWakeSettings WakeSettings) : EngineCommand;

  private sealed record WakeTestCommand(
    AudioWakeSettings WakeSettings) : EngineCommand;

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

  private sealed class PlaybackSequence
  {
    private PlaybackSequence(
      SpeechProfileSettings profile,
      AudioWakeSettings wakeSettings,
      IEnumerable<SpeechSegment> segments)
    {
      Profile = profile;
      WakeSettings = wakeSettings;
      Segments = new Queue<SpeechSegment>(segments);
    }

    public SpeechProfileSettings Profile { get; }

    public AudioWakeSettings WakeSettings { get; }

    public Queue<SpeechSegment> Segments { get; }

    public DateTime? NextSegmentUtc { get; set; }

    public static PlaybackSequence ForSpeech(
      SpeechMarkup markup,
      SpeechProfileSettings profile,
      AudioWakeSettings wakeSettings)
    {
      return new PlaybackSequence(
        profile,
        wakeSettings,
        new[] { new SpeechSegment(markup, 0) });
    }

    public static PlaybackSequence ForIpaPreview(IpaPreviewCommand command)
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
      return new PlaybackSequence(
        command.Profile,
        command.WakeSettings,
        segments);
    }
  }

  private sealed record SpeechSegment(
    SpeechMarkup Markup,
    int DelayAfterPreviousMilliseconds);

  private readonly record struct SystemSpeechCompletion(
    Prompt Prompt,
    Exception? Error,
    bool Cancelled);

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
    None,
    Sapi,
    SystemSpeech
  }
}
