using System.Security.Cryptography;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Security;
using SystemSpeechSynthesizer = System.Speech.Synthesis.SpeechSynthesizer;

namespace AgentPanelSpeaker;

/// <summary>
/// User-selected settings for one isolated System.Speech provider experiment.
/// </summary>
internal sealed record SystemSpeechMatrixOptions(
  InstalledSpeechVoice Voice,
  int Rate,
  int Volume);

/// <summary>
/// One immutable SSML body in the controlled System.Speech diagnostic matrix.
/// </summary>
internal sealed record SystemSpeechMatrixCase(string Id, string Body);

/// <summary>
/// Runs a fixed SSML matrix directly against System.Speech without touching
/// SpeechService history, cursor state, or transcript navigation.
/// </summary>
internal static class SystemSpeechMatrixDiagnosticRunner
{
  private const int SystemSpeechSampleRate = 16000;
  private const int OutputSampleRate = 48000;
  private const int InterCaseDelayMilliseconds = 500;

  private static readonly SystemSpeechMatrixCase[] Cases =
  {
    new("plain-hyphenated", "AI-transcript.py"),
    new(
      "characters-hyphenated",
      "<say-as interpret-as=\"characters\">AI</say-as>-transcript.py"),
    new(
      "characters-space",
      "<say-as interpret-as=\"characters\">AI</say-as> transcript.py"),
    new(
      "characters-break-hyphenated",
      "<say-as interpret-as=\"characters\">AI</say-as>" +
        "<break time=\"100ms\"/>-transcript.py"),
    new(
      "characters-sub-alias",
      "<say-as interpret-as=\"characters\">AI</say-as>" +
        "<sub alias=\"transcript\">-transcript</sub>.py")
  };

  /// <summary>
  /// Runs the matrix on a worker thread so synthesis/playback never blocks UI.
  /// </summary>
  public static Task RunAsync(SystemSpeechMatrixOptions options)
  {
    ArgumentNullException.ThrowIfNull(options);
    if (options.Voice.Provider != SpeechVoiceProvider.SystemSpeech)
    {
      throw new ArgumentException(
        "The SSML matrix can only run against a System.Speech voice.",
        nameof(options));
    }
    return Task.Run(() => Run(options));
  }

  private static void Run(SystemSpeechMatrixOptions options)
  {
    string runId = Guid.NewGuid().ToString("N");
    int rate = Math.Clamp(options.Rate, -10, 10);
    int volume = Math.Clamp(options.Volume, 0, 100);
    using var synthesizer = new SystemSpeechSynthesizer();
    synthesizer.SelectVoice(options.Voice.ProviderVoiceId);
    synthesizer.Rate = rate;
    synthesizer.Volume = volume;
    string culture = synthesizer.Voice.Culture.Name;

    DiagnosticLog.Write("speech.system_speech_matrix_started", new
    {
      runId,
      voice = options.Voice.ProviderVoiceId,
      provider = "System.Speech",
      culture,
      rate,
      volume,
      caseCount = Cases.Length,
      caseIds = Cases.Select(item => item.Id).ToArray()
    });

    try
    {
      for (int index = 0; index < Cases.Length; ++index)
      {
        RunCase(
          synthesizer,
          options.Voice.ProviderVoiceId,
          culture,
          runId,
          index,
          Cases[index]);
        if (index + 1 < Cases.Length)
        {
          Thread.Sleep(InterCaseDelayMilliseconds);
        }
      }
      DiagnosticLog.Write("speech.system_speech_matrix_completed", new
      {
        runId,
        voice = options.Voice.ProviderVoiceId,
        caseCount = Cases.Length
      });
    }
    catch (Exception exception)
    {
      DiagnosticLog.WriteException(
        "speech.system_speech_matrix_failed",
        exception,
        source: $"System.Speech matrix {runId}",
        isTerminating: false);
      throw;
    }
  }

  private static void RunCase(
    SystemSpeechSynthesizer synthesizer,
    string voice,
    string culture,
    string runId,
    int index,
    SystemSpeechMatrixCase matrixCase)
  {
    string ssml = BuildSsmlDocument(matrixCase.Body, culture);
    using var stream = new MemoryStream();
    EventHandler<SpeakProgressEventArgs> progressHandler = (_, args) =>
      DiagnosticLog.Write("speech.system_speech_matrix_progress", new
      {
        runId,
        caseIndex = index + 1,
        caseId = matrixCase.Id,
        voice,
        args.Text,
        args.CharacterPosition,
        args.CharacterCount,
        audioPositionMilliseconds = args.AudioPosition.TotalMilliseconds
      });

    synthesizer.SpeakProgress += progressHandler;
    try
    {
      var outputFormat = new SpeechAudioFormatInfo(
        SystemSpeechSampleRate,
        AudioBitsPerSample.Sixteen,
        AudioChannel.Mono);
      synthesizer.SetOutputToAudioStream(stream, outputFormat);
      DiagnosticLog.Write("speech.system_speech_matrix_case_submitted", new
      {
        runId,
        caseIndex = index + 1,
        caseCount = Cases.Length,
        caseId = matrixCase.Id,
        voice,
        culture,
        ssml,
        characterLength = ssml.Length,
        utf8ByteLength = Encoding.UTF8.GetByteCount(ssml),
        sha256 = ComputeUtf8Sha256(ssml)
      });
      synthesizer.SpeakSsml(ssml);
    }
    finally
    {
      synthesizer.SpeakProgress -= progressHandler;
      synthesizer.SetOutputToNull();
    }

    PcmWaveData sourceWave = PcmWaveData.FromPcmSamples(
      channels: 1,
      sampleRate: SystemSpeechSampleRate,
      bitsPerSample: 16,
      samples: stream.ToArray());
    int sampleFrames = sourceWave.Samples.Length / sourceWave.BlockAlign;
    DiagnosticLog.Write("speech.system_speech_matrix_case_synthesized", new
    {
      runId,
      caseIndex = index + 1,
      caseId = matrixCase.Id,
      voice,
      sourceSampleRate = sourceWave.SampleRate,
      sourceBitsPerSample = sourceWave.BitsPerSample,
      sourceChannels = sourceWave.Channels,
      sourcePcmBytes = sourceWave.Samples.Length,
      sampleFrames,
      durationMilliseconds = sourceWave.Duration.TotalMilliseconds
    });

    PcmWaveData playbackWave = sourceWave.ConvertToMono16(OutputSampleRate);
    DiagnosticLog.Write("speech.system_speech_matrix_case_playback_started", new
    {
      runId,
      caseIndex = index + 1,
      caseId = matrixCase.Id,
      voice,
      playbackSampleRate = playbackWave.SampleRate,
      playbackPcmBytes = playbackWave.Samples.Length,
      durationMilliseconds = playbackWave.Duration.TotalMilliseconds
    });
    using (var player = new WaveOutPlayer(playbackWave))
    {
      while (!player.IsComplete)
      {
        Thread.Sleep(10);
      }
    }
    DiagnosticLog.Write("speech.system_speech_matrix_case_completed", new
    {
      runId,
      caseIndex = index + 1,
      caseId = matrixCase.Id,
      voice
    });
  }

  private static string BuildSsmlDocument(string body, string culture)
  {
    string language = SecurityElement.Escape(culture) ?? "en-US";
    return
      $"<speak version=\"1.0\" " +
      $"xmlns=\"http://www.w3.org/2001/10/synthesis\" " +
      $"xml:lang=\"{language}\">" +
      "<prosody pitch=\"0%\">" + body + "</prosody></speak>";
  }

  private static string ComputeUtf8Sha256(string value)
  {
    return Convert.ToHexString(
      SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
  }
}
