using System.Collections.Concurrent;
using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;

namespace AgentPanelSpeaker;

/// <summary>
/// Owns the Windows SAPI voice and wake-tone output on one STA worker thread.
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
  /// Starts the SAPI worker and waits for voice enumeration to finish.
  /// </summary>
  public SapiSpeechEngine()
  {
    _thread = new Thread(Run)
    {
      IsBackground = true,
      Name = "Agent Panel Speaker SAPI"
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
  /// Gets enabled SAPI voice descriptions.
  /// </summary>
  public IReadOnlyList<string> VoiceNames => _voiceNames;

  /// <summary>
  /// Starts one SAPI XML utterance with the configured wake prefix.
  /// </summary>
  public void Speak(
    string sapiXml,
    SpeechProfileSettings profile,
    AudioWakeSettings wakeSettings)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sapiXml);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(wakeSettings);
    AddCommand(new SpeakCommand(
      sapiXml,
      profile.Normalize(),
      wakeSettings.Normalize()));
  }

  /// <summary>
  /// Plays an optional isolated phone, waits, and then speaks its example.
  /// </summary>
  public void PreviewIpa(
    string? isolatedSapiXml,
    string exampleSapiXml,
    SpeechProfileSettings profile,
    AudioWakeSettings wakeSettings)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(exampleSapiXml);
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(wakeSettings);
    AddCommand(new IpaPreviewCommand(
      string.IsNullOrWhiteSpace(isolatedSapiXml)
        ? null
        : isolatedSapiXml,
      exampleSapiXml,
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
  /// Pauses the active SAPI utterance.
  /// </summary>
  public void Pause()
  {
    AddCommand(PauseCommand.Instance);
  }

  /// <summary>
  /// Resumes the active SAPI utterance.
  /// </summary>
  public void Resume()
  {
    AddCommand(ResumeCommand.Instance);
  }

  /// <summary>
  /// Stops the worker and releases COM resources.
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
  /// Creates and services the late-bound SAPI voice.
  /// </summary>
  private void Run()
  {
    object? voiceObject = null;
    object? voicesObject = null;
    try
    {
      Type voiceType = Type.GetTypeFromProgID("SAPI.SpVoice") ??
        throw new InvalidOperationException("SAPI.SpVoice is unavailable.");
      voiceObject = Activator.CreateInstance(voiceType) ??
        throw new InvalidOperationException("SAPI.SpVoice could not be created.");
      dynamic voice = voiceObject;
      voicesObject = voice.GetVoices(string.Empty, string.Empty);
      dynamic voices = voicesObject;
      var voiceIndexes = new Dictionary<string, int>(
        StringComparer.OrdinalIgnoreCase);
      var voiceNames = new List<string>();
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
            voiceNames.Add(name);
          }
        }
        finally
        {
          ReleaseComObject(tokenObject);
        }
      }
      voiceNames.Sort(StringComparer.CurrentCultureIgnoreCase);
      _voiceNames = voiceNames.ToArray();
      _initialized.Set();
      ServiceCommands(voice, voices, voiceIndexes);
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
      ReleaseComObject(voicesObject);
      ReleaseComObject(voiceObject);
    }
  }

  /// <summary>
  /// Processes commands, preview delays, and asynchronous SAPI completion.
  /// </summary>
  private void ServiceCommands(
    dynamic voice,
    dynamic voices,
    IReadOnlyDictionary<string, int> voiceIndexes)
  {
    PlaybackSequence? sequence = null;
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
            voice,
            voices,
            voiceIndexes,
            ref sequence,
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
              voice,
              voices,
              voiceIndexes,
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
            voice,
            voices,
            voiceIndexes,
            ref sequence,
            ref speaking,
            ref exiting);
          continue;
        }

        if (speaking && Convert.ToBoolean(voice.WaitUntilDone(0)))
        {
          speaking = false;
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
      }
      catch (Exception exception)
      {
        bool hadActivePlayback = sequence is not null;
        sequence = null;
        speaking = false;
        RaiseFaulted(exception);
        if (hadActivePlayback)
        {
          RaiseCompleted();
        }
      }
    }
  }

  /// <summary>
  /// Applies one command on the SAPI apartment thread.
  /// </summary>
  private void ProcessCommand(
    EngineCommand command,
    dynamic voice,
    dynamic voices,
    IReadOnlyDictionary<string, int> voiceIndexes,
    ref PlaybackSequence? sequence,
    ref bool speaking,
    ref bool exiting)
  {
    switch (command)
    {
      case SpeakCommand speak:
        CancelCurrentWithoutCompletion(voice, ref sequence, ref speaking);
        sequence = PlaybackSequence.ForSpeech(
          speak.SapiXml,
          speak.Profile,
          speak.WakeSettings);
        StartNextSegment(
          sequence,
          voice,
          voices,
          voiceIndexes,
          ref speaking);
        break;

      case IpaPreviewCommand preview:
        CancelCurrentWithoutCompletion(voice, ref sequence, ref speaking);
        sequence = PlaybackSequence.ForIpaPreview(preview);
        StartNextSegment(
          sequence,
          voice,
          voices,
          voiceIndexes,
          ref speaking);
        break;

      case WakeTestCommand wakeTest:
        CancelCurrentWithoutCompletion(voice, ref sequence, ref speaking);
        PlayWakeTone(wakeTest.WakeSettings, force: true);
        RaiseCompleted();
        break;

      case CancelCommand:
      {
        bool wasActive = sequence is not null;
        CancelCurrentWithoutCompletion(voice, ref sequence, ref speaking);
        if (wasActive)
        {
          RaiseCompleted();
        }
        break;
      }

      case PauseCommand when speaking:
        voice.Pause();
        break;

      case ResumeCommand when speaking:
        voice.Resume();
        break;

      case DisposeCommand:
        CancelCurrentWithoutCompletion(voice, ref sequence, ref speaking);
        exiting = true;
        break;
    }
  }

  /// <summary>
  /// Starts the next queued SAPI segment after applying wake policy.
  /// </summary>
  private void StartNextSegment(
    PlaybackSequence sequence,
    dynamic voice,
    dynamic voices,
    IReadOnlyDictionary<string, int> voiceIndexes,
    ref bool speaking)
  {
    if (sequence.Segments.Count == 0)
    {
      return;
    }

    SpeechSegment segment = sequence.Segments.Dequeue();
    sequence.NextSegmentUtc = null;
    ConfigureVoice(
      voice,
      voices,
      voiceIndexes,
      sequence.Profile);
    PlayWakeTone(sequence.WakeSettings, force: false);
    voice.Speak(segment.SapiXml, SpeakAsync | IsXml);
    speaking = true;
  }

  /// <summary>
  /// Selects the configured voice, rate, and volume.
  /// </summary>
  private static void ConfigureVoice(
    dynamic voice,
    dynamic voices,
    IReadOnlyDictionary<string, int> voiceIndexes,
    SpeechProfileSettings profile)
  {
    if (!voiceIndexes.TryGetValue(profile.VoiceName, out int voiceIndex))
    {
      throw new ArgumentException(
        $"Voice is not installed: {profile.VoiceName}");
    }

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
  /// Cancels any current SAPI utterance and queued preview segments.
  /// </summary>
  private void CancelCurrentWithoutCompletion(
    dynamic voice,
    ref PlaybackSequence? sequence,
    ref bool speaking)
  {
    if (speaking)
    {
      voice.Speak(string.Empty, SpeakAsync | PurgeBeforeSpeak);
      MarkAudioEnd();
    }
    speaking = false;
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
  /// Raises completion without allowing a subscriber failure to kill SAPI.
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
    string SapiXml,
    SpeechProfileSettings Profile,
    AudioWakeSettings WakeSettings) : EngineCommand;

  private sealed record IpaPreviewCommand(
    string? IsolatedSapiXml,
    string ExampleSapiXml,
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
      string sapiXml,
      SpeechProfileSettings profile,
      AudioWakeSettings wakeSettings)
    {
      return new PlaybackSequence(
        profile,
        wakeSettings,
        new[] { new SpeechSegment(sapiXml, 0) });
    }

    public static PlaybackSequence ForIpaPreview(IpaPreviewCommand command)
    {
      var segments = new List<SpeechSegment>();
      if (command.IsolatedSapiXml is not null)
      {
        segments.Add(new SpeechSegment(command.IsolatedSapiXml, 0));
        segments.Add(new SpeechSegment(
          command.ExampleSapiXml,
          command.WakeSettings.IpaExampleDelayMilliseconds));
      }
      else
      {
        segments.Add(new SpeechSegment(command.ExampleSapiXml, 0));
      }
      return new PlaybackSequence(
        command.Profile,
        command.WakeSettings,
        segments);
    }

  }

  private sealed record SpeechSegment(
    string SapiXml,
    int DelayAfterPreviousMilliseconds);
}
