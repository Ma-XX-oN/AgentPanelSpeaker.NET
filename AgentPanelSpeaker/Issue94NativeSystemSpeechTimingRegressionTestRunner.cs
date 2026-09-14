using System.Reflection;
using System.Speech.Synthesis;

namespace AgentPanelSpeaker;

/// <summary>
/// Regressions for native System.Speech PCM with normalized provider timing.
/// </summary>
internal static class Issue94NativeSystemSpeechTimingRegressionTestRunner
{
  private const string VoiceName = "Microsoft Zira Desktop";
  private const string TestText =
    "The quick brown fox crosses the quiet field before sunrise, walks past " +
    "the old bridge beside the river, waits for the morning train, returns " +
    "along the narrow road through the village, and finally we finish.";
  private const int TestRate = 6;
  private const int TestVolume = 100;
  private const int ProviderTimingSampleRate = 16000;
  private const int PlaybackSampleRate = 48000;
  private const double TimingToleranceMilliseconds = 35.0;
  private const double DurationToleranceMilliseconds = 1.0;

  private sealed record NativeReference(
    PcmWaveData Wave,
    IReadOnlyList<TimeSpan> Progress);

  private sealed record ProductionReference(
    PcmWaveData Wave,
    IReadOnlyList<SpeechWordBoundary> Boundaries);

  public static int Run()
  {
    try
    {
      TestNativeOutputAndNormalizedTiming();
      Console.WriteLine(
        "PASS  system-speech-native-timing/native-output-and-normalized-timing");
      Console.WriteLine();
      Console.WriteLine("PASS: 1/1 issue #94 native timing tests passed.");
      return 0;
    }
    catch (Exception exception)
    {
      Console.WriteLine(
        "FAIL  system-speech-native-timing/native-output-and-normalized-timing");
      Console.WriteLine(
        $"      {exception.GetType().Name}: {exception.Message}");
      Console.WriteLine();
      Console.WriteLine("FAIL: 1/1 issue #94 native timing tests failed.");
      return 1;
    }
  }

  private static void TestNativeOutputAndNormalizedTiming()
  {
    NativeReference native = SynthesizeProviderDefault();
    ProductionReference production = SynthesizeProduction();

    Require(native.Progress.Count > 0,
      "Provider-default synthesis produced no SpeakProgress events.");
    Require(production.Boundaries.Count > 0,
      "Production synthesis produced no exact word boundaries.");
    Require(production.Wave.SampleRate == native.Wave.SampleRate,
      "Production System.Speech is not using the provider-default PCM rate: " +
      $"production={production.Wave.SampleRate} Hz, " +
      $"provider-default={native.Wave.SampleRate} Hz.");

    double timingScale = ProviderTimingSampleRate /
      (double)native.Wave.SampleRate;
    double expectedLastMilliseconds =
      native.Progress[^1].TotalMilliseconds * timingScale;
    double actualLastMilliseconds =
      production.Boundaries[^1].AudioPosition.TotalMilliseconds;
    double timingDifference = Math.Abs(
      actualLastMilliseconds - expectedLastMilliseconds);

    PcmWaveData converted = production.Wave.ConvertToMono16(PlaybackSampleRate);
    double durationDifference = Math.Abs(
      converted.Duration.TotalMilliseconds -
      production.Wave.Duration.TotalMilliseconds);

    Console.WriteLine(
      "      native-timing: " +
      $"native={native.Wave.SampleRate} Hz, " +
      $"production={production.Wave.SampleRate} Hz, " +
      $"scale={timingScale:F9}, " +
      $"raw-last={native.Progress[^1].TotalMilliseconds:F3} ms, " +
      $"expected-last={expectedLastMilliseconds:F3} ms, " +
      $"production-last={actualLastMilliseconds:F3} ms, " +
      $"timing-difference={timingDifference:F3} ms, " +
      $"source-duration={production.Wave.Duration.TotalMilliseconds:F3} ms, " +
      $"48k-duration={converted.Duration.TotalMilliseconds:F3} ms");

    Require(timingDifference <= TimingToleranceMilliseconds,
      "Production System.Speech boundaries are not normalized from the " +
      $"provider timing clock onto the native PCM timeline; difference=" +
      $"{timingDifference:F3} ms.");
    Require(production.Boundaries[^1].AudioPosition <= production.Wave.Duration,
      "The final normalized System.Speech boundary extends beyond the PCM.");
    Require(durationDifference <= DurationToleranceMilliseconds,
      "48 kHz playback conversion changed native System.Speech duration by " +
      $"{durationDifference:F3} ms.");
  }

  private static NativeReference SynthesizeProviderDefault()
  {
    SpeechMarkup markup = BuildMarkup();
    using var synthesizer = CreateSynthesizer();
    string ssml = BuildSsmlDocument(
      markup.SsmlContent,
      synthesizer.Voice.Culture.Name);
    var progress = new List<TimeSpan>();
    EventHandler<SpeakProgressEventArgs> handler = (_, args) =>
      progress.Add(args.AudioPosition);

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
      progress);
  }

  private static ProductionReference SynthesizeProduction()
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
      PcmWaveData wave = method.Invoke(null, arguments) as PcmWaveData ??
        throw new InvalidOperationException(
          "Production System.Speech synthesis returned no waveform.");
      IReadOnlyList<SpeechWordBoundary> boundaries =
        arguments[4] as IReadOnlyList<SpeechWordBoundary> ??
        throw new InvalidOperationException(
          "Production System.Speech synthesis returned no boundary list.");
      return new ProductionReference(wave, boundaries);
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
