using System.Reflection;
using System.Speech.Synthesis;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #94 System.Speech source-format timing.
/// </summary>
internal static class Issue94SystemSpeechSampleRateRegressionTestRunner
{
  private const string VoiceName = "Microsoft Zira Desktop";
  private const string TestText =
    "1. Nested numbered item with a table inside it";
  private const int TestRate = 6;
  private const int TestVolume = 100;
  private const int PlaybackSampleRate = 48000;
  private const double DurationToleranceRatio = 0.05;
  private const double ResampleToleranceMilliseconds = 1.0;

  private sealed record ProgressSample(
    string Text,
    int CharacterPosition,
    int CharacterCount,
    TimeSpan AudioPosition);

  private sealed record NativeReference(
    PcmWaveData Wave,
    IReadOnlyList<ProgressSample> Progress,
    string Ssml);

  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("system-speech-sample-rate/provider-default-format-is-measured",
        TestProviderDefaultFormatIsMeasured),
      ("system-speech-sample-rate/production-matches-provider-default-format",
        TestProductionMatchesProviderDefaultFormat),
      ("system-speech-sample-rate/production-preserves-provider-default-duration",
        TestProductionPreservesProviderDefaultDuration),
      ("system-speech-sample-rate/provider-default-resample-preserves-duration",
        TestProviderDefaultResamplePreservesDuration),
      ("system-speech-sample-rate/provider-progress-fits-native-pcm-timeline",
        TestProviderProgressFitsNativePcmTimeline)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #94 System.Speech sample-rate suite: {tests.Length} tests");
    foreach ((string name, Action body) in tests)
    {
      try
      {
        body();
        Console.WriteLine($"PASS  {name}");
      }
      catch (Exception exception)
      {
        ++failures;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine(
          $"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} issue #94 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #94 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestProviderDefaultFormatIsMeasured()
  {
    NativeReference reference = SynthesizeProviderDefault();
    Console.WriteLine(
      "      provider-default: " +
      $"{reference.Wave.SampleRate} Hz, " +
      $"{reference.Wave.BitsPerSample}-bit, " +
      $"{reference.Wave.Channels} channel(s), " +
      $"{reference.Wave.Samples.Length} PCM bytes, " +
      $"{reference.Wave.Duration.TotalMilliseconds:F3} ms, " +
      $"progress={reference.Progress.Count}");
    Require(reference.Wave.SampleRate > 0,
      "Provider-default WAVE output has no valid sample rate.");
    Require(reference.Wave.Samples.Length > 0,
      "Provider-default WAVE output contains no PCM samples.");
    Require(reference.Progress.Count > 0,
      "Provider-default synthesis produced no SpeakProgress events.");
  }

  private static void TestProductionMatchesProviderDefaultFormat()
  {
    NativeReference reference = SynthesizeProviderDefault();
    PcmWaveData production = SynthesizeThroughProduction();
    Console.WriteLine(
      "      production: " +
      $"{production.SampleRate} Hz, " +
      $"{production.BitsPerSample}-bit, " +
      $"{production.Channels} channel(s), " +
      $"{production.Samples.Length} PCM bytes, " +
      $"{production.Duration.TotalMilliseconds:F3} ms");
    Require(production.SampleRate == reference.Wave.SampleRate,
      "Production System.Speech requested a source sample rate different " +
      $"from Zira Desktop's provider-default rate: production=" +
      $"{production.SampleRate} Hz, default={reference.Wave.SampleRate} Hz.");
    Require(production.BitsPerSample == reference.Wave.BitsPerSample &&
            production.Channels == reference.Wave.Channels,
      "Production System.Speech source PCM format differs from the " +
      "provider-default WAVE format.");
  }

  private static void TestProductionPreservesProviderDefaultDuration()
  {
    NativeReference reference = SynthesizeProviderDefault();
    PcmWaveData production = SynthesizeThroughProduction();
    double referenceMilliseconds = reference.Wave.Duration.TotalMilliseconds;
    double productionMilliseconds = production.Duration.TotalMilliseconds;
    Require(referenceMilliseconds > 0 && productionMilliseconds > 0,
      "System.Speech produced a zero-duration comparison waveform.");
    double ratio = referenceMilliseconds / productionMilliseconds;
    double relativeDifference = Math.Abs(
      productionMilliseconds - referenceMilliseconds) /
      referenceMilliseconds;
    Console.WriteLine(
      "      duration: " +
      $"default={referenceMilliseconds:F3} ms, " +
      $"production={productionMilliseconds:F3} ms, " +
      $"default/production={ratio:F6}, " +
      $"difference={relativeDifference:P2}");
    Require(relativeDifference <= DurationToleranceRatio,
      "Production System.Speech changes Zira Desktop's natural duration by " +
      $"{relativeDifference:P2}; source-format selection must stay within " +
      $"{DurationToleranceRatio:P0} of provider-default duration.");
  }

  private static void TestProviderDefaultResamplePreservesDuration()
  {
    NativeReference reference = SynthesizeProviderDefault();
    PcmWaveData converted = reference.Wave.ConvertToMono16(PlaybackSampleRate);
    double difference = Math.Abs(
      converted.Duration.TotalMilliseconds -
      reference.Wave.Duration.TotalMilliseconds);
    Console.WriteLine(
      "      resample: " +
      $"source={reference.Wave.Duration.TotalMilliseconds:F3} ms, " +
      $"48k={converted.Duration.TotalMilliseconds:F3} ms, " +
      $"difference={difference:F6} ms");
    Require(difference <= ResampleToleranceMilliseconds,
      "48 kHz conversion changed provider-default speech duration by " +
      $"{difference:F3} ms.");
  }

  private static void TestProviderProgressFitsNativePcmTimeline()
  {
    NativeReference reference = SynthesizeProviderDefault();
    ProgressSample first = reference.Progress[0];
    ProgressSample last = reference.Progress[^1];
    Console.WriteLine(
      "      progress: " +
      $"first={first.AudioPosition.TotalMilliseconds:F3} ms ({first.Text}), " +
      $"last={last.AudioPosition.TotalMilliseconds:F3} ms ({last.Text}), " +
      $"pcm={reference.Wave.Duration.TotalMilliseconds:F3} ms");
    Require(first.AudioPosition >= TimeSpan.Zero,
      "Provider-default SpeakProgress begins before the PCM timeline.");
    Require(last.AudioPosition <= reference.Wave.Duration,
      "Provider-default SpeakProgress extends beyond the native PCM duration.");
  }

  private static NativeReference SynthesizeProviderDefault()
  {
    SpeechMarkup markup = BuildMarkup();
    using var synthesizer = CreateSynthesizer();
    string ssml = BuildSsmlDocument(
      markup.SsmlContent,
      synthesizer.Voice.Culture.Name);
    var progress = new List<ProgressSample>();
    EventHandler<SpeakProgressEventArgs> handler = (_, args) =>
      progress.Add(new ProgressSample(
        args.Text,
        args.CharacterPosition,
        args.CharacterCount,
        args.AudioPosition));

    using var stream = new MemoryStream();
    synthesizer.SpeakProgress += handler;
    try
    {
      synthesizer.SetOutputToWaveStream(stream);
      synthesizer.SpeakSsml(ssml);
    }
    finally
    {
      synthesizer.SpeakProgress -= handler;
      synthesizer.SetOutputToNull();
    }

    return new NativeReference(
      PcmWaveData.Parse(stream.ToArray()),
      progress,
      ssml);
  }

  private static PcmWaveData SynthesizeThroughProduction()
  {
    SpeechMarkup markup = BuildMarkup();
    var profile = new SpeechProfileSettings(VoiceName, TestRate, 0)
    {
      Volume = TestVolume
    };
    using var synthesizer = CreateSynthesizer();
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "RenderSystemSpeech",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("RenderSystemSpeech is missing.");
    object?[] arguments =
    {
      markup,
      profile,
      VoiceName,
      synthesizer,
      null,
      null
    };
    try
    {
      return method.Invoke(null, arguments) as PcmWaveData ??
        throw new InvalidOperationException(
          "Production System.Speech synthesis returned no waveform.");
    }
    catch (TargetInvocationException exception) when (
      exception.InnerException is not null)
    {
      throw exception.InnerException;
    }
  }

  private static SpeechMarkup BuildMarkup()
  {
    return SpeechSapiXmlBuilder.Build(
      TestText,
      0,
      Array.Empty<string>(),
      PronunciationRuleSet.Parse(string.Empty));
  }

  private static SpeechSynthesizer CreateSynthesizer()
  {
    var synthesizer = new SpeechSynthesizer();
    string[] installed = synthesizer.GetInstalledVoices()
      .Where(voice => voice.Enabled)
      .Select(voice => voice.VoiceInfo.Name)
      .ToArray();
    if (!installed.Contains(VoiceName, StringComparer.OrdinalIgnoreCase))
    {
      synthesizer.Dispose();
      throw new InvalidOperationException(
        $"Required test voice '{VoiceName}' is not installed. Available: " +
        string.Join(", ", installed));
    }
    synthesizer.SelectVoice(VoiceName);
    synthesizer.Rate = TestRate;
    synthesizer.Volume = TestVolume;
    return synthesizer;
  }

  private static string BuildSsmlDocument(string content, string culture)
  {
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "BuildSsmlDocument",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("BuildSsmlDocument is missing.");
    return method.Invoke(null, new object[] { content, culture }) as string ??
      throw new InvalidOperationException("SSML build failed.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
