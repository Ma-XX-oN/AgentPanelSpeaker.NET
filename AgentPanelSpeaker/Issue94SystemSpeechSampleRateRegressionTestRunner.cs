using System.Reflection;
using System.Speech.Synthesis;
using VoiceInformation = Windows.Media.SpeechSynthesis.VoiceInformation;
using WinRtSpeechSynthesizer = Windows.Media.SpeechSynthesis.SpeechSynthesizer;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions and measurements for issue #94 cross-provider rate timing.
/// </summary>
internal static class Issue94SystemSpeechSampleRateRegressionTestRunner
{
  private const string SystemSpeechVoiceName = "Microsoft Zira Desktop";
  private const string WindowsMediaVoiceName = "Microsoft Zira";
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
      ("system-speech-sample-rate/explicit-16k-preserves-default-duration",
        TestExplicit16kPreservesProviderDefaultDuration),
      ("system-speech-sample-rate/provider-default-resample-preserves-duration",
        TestProviderDefaultResamplePreservesDuration),
      ("system-speech-sample-rate/provider-progress-fits-native-pcm-timeline",
        TestProviderProgressFitsNativePcmTimeline),
      ("system-speech-sample-rate/provider-rate-semantics-are-measured",
        TestProviderRateSemanticsAreMeasured)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #94 provider timing suite: {tests.Length} tests");
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
    NativeReference reference = SynthesizeProviderDefault(TestRate);
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

  private static void TestExplicit16kPreservesProviderDefaultDuration()
  {
    NativeReference reference = SynthesizeProviderDefault(TestRate);
    PcmWaveData production = SynthesizeSystemSpeechThroughProduction(TestRate);
    double referenceMilliseconds = reference.Wave.Duration.TotalMilliseconds;
    double productionMilliseconds = production.Duration.TotalMilliseconds;
    Require(referenceMilliseconds > 0 && productionMilliseconds > 0,
      "System.Speech produced a zero-duration comparison waveform.");
    double ratio = referenceMilliseconds / productionMilliseconds;
    double relativeDifference = Math.Abs(
      productionMilliseconds - referenceMilliseconds) /
      referenceMilliseconds;
    Console.WriteLine(
      "      source-format: " +
      $"default={reference.Wave.SampleRate} Hz/{referenceMilliseconds:F3} ms, " +
      $"production={production.SampleRate} Hz/{productionMilliseconds:F3} ms, " +
      $"default/production={ratio:F6}, " +
      $"difference={relativeDifference:P2}");
    Require(relativeDifference <= DurationToleranceRatio,
      "Explicit System.Speech output format changes Zira Desktop's natural " +
      $"duration by {relativeDifference:P2}; expected no more than " +
      $"{DurationToleranceRatio:P0}.");
  }

  private static void TestProviderDefaultResamplePreservesDuration()
  {
    NativeReference reference = SynthesizeProviderDefault(TestRate);
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
    NativeReference reference = SynthesizeProviderDefault(TestRate);
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

  private static void TestProviderRateSemanticsAreMeasured()
  {
    PcmWaveData systemRateZero = SynthesizeSystemSpeechThroughProduction(0);
    PcmWaveData systemRateSix = SynthesizeSystemSpeechThroughProduction(TestRate);
    PcmWaveData mediaRateZero = SynthesizeWindowsMediaThroughProduction(0);
    PcmWaveData mediaRateSix = SynthesizeWindowsMediaThroughProduction(TestRate);

    double systemScale = DivideDurations(systemRateZero, systemRateSix);
    double mediaScale = DivideDurations(mediaRateZero, mediaRateSix);
    double rateZeroRatio = DivideDurations(systemRateZero, mediaRateZero);
    double rateSixRatio = DivideDurations(systemRateSix, mediaRateSix);
    Console.WriteLine(
      "      provider-rates: " +
      $"SystemSpeech r0={systemRateZero.Duration.TotalMilliseconds:F3} ms, " +
      $"r6={systemRateSix.Duration.TotalMilliseconds:F3} ms, " +
      $"speedup={systemScale:F6}; " +
      $"WindowsMedia r0={mediaRateZero.Duration.TotalMilliseconds:F3} ms, " +
      $"r6={mediaRateSix.Duration.TotalMilliseconds:F3} ms, " +
      $"speedup={mediaScale:F6}; " +
      $"system/media duration r0={rateZeroRatio:F6}, " +
      $"r6={rateSixRatio:F6}");

    Require(systemRateZero.Duration > TimeSpan.Zero &&
            systemRateSix.Duration > TimeSpan.Zero &&
            mediaRateZero.Duration > TimeSpan.Zero &&
            mediaRateSix.Duration > TimeSpan.Zero,
      "One provider returned a zero-duration rate comparison waveform.");
  }

  private static double DivideDurations(PcmWaveData numerator, PcmWaveData denominator)
  {
    double denominatorMilliseconds = denominator.Duration.TotalMilliseconds;
    Require(denominatorMilliseconds > 0,
      "Cannot compare against a zero-duration waveform.");
    return numerator.Duration.TotalMilliseconds / denominatorMilliseconds;
  }

  private static NativeReference SynthesizeProviderDefault(int rate)
  {
    SpeechMarkup markup = BuildMarkup();
    using var synthesizer = CreateSystemSpeechSynthesizer(rate);
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

  private static PcmWaveData SynthesizeSystemSpeechThroughProduction(int rate)
  {
    SpeechMarkup markup = BuildMarkup();
    var profile = new SpeechProfileSettings(SystemSpeechVoiceName, rate, 0)
    {
      Volume = TestVolume
    };
    using var synthesizer = CreateSystemSpeechSynthesizer(rate);
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "RenderSystemSpeech",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("RenderSystemSpeech is missing.");
    object?[] arguments =
    {
      markup,
      profile,
      SystemSpeechVoiceName,
      synthesizer,
      null,
      null
    };
    return InvokeWaveMethod(method, arguments, "System.Speech");
  }

  private static PcmWaveData SynthesizeWindowsMediaThroughProduction(int rate)
  {
    VoiceInformation voice = WinRtSpeechSynthesizer.AllVoices
      .FirstOrDefault(candidate => string.Equals(
        candidate.DisplayName,
        WindowsMediaVoiceName,
        StringComparison.OrdinalIgnoreCase)) ??
      throw new InvalidOperationException(
        $"Required Windows.Media voice '{WindowsMediaVoiceName}' is not installed. " +
        "Available: " + string.Join(", ",
          WinRtSpeechSynthesizer.AllVoices.Select(voice => voice.DisplayName)));
    SpeechMarkup markup = BuildMarkup();
    var profile = new SpeechProfileSettings(WindowsMediaVoiceName, rate, 0)
    {
      Volume = TestVolume
    };
    using var synthesizer = new WinRtSpeechSynthesizer();
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "RenderWindowsMediaSpeech",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("RenderWindowsMediaSpeech is missing.");
    object?[] arguments =
    {
      markup,
      profile,
      voice.Id,
      synthesizer,
      WindowsMediaBookmarkMode.Fallback,
      null,
      null
    };
    return InvokeWaveMethod(method, arguments, "Windows.Media");
  }

  private static PcmWaveData InvokeWaveMethod(
    MethodInfo method,
    object?[] arguments,
    string provider)
  {
    try
    {
      return method.Invoke(null, arguments) as PcmWaveData ??
        throw new InvalidOperationException(
          $"Production {provider} synthesis returned no waveform.");
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

  private static SpeechSynthesizer CreateSystemSpeechSynthesizer(int rate)
  {
    var synthesizer = new SpeechSynthesizer();
    string[] installed = synthesizer.GetInstalledVoices()
      .Where(voice => voice.Enabled)
      .Select(voice => voice.VoiceInfo.Name)
      .ToArray();
    if (!installed.Contains(
          SystemSpeechVoiceName,
          StringComparer.OrdinalIgnoreCase))
    {
      synthesizer.Dispose();
      throw new InvalidOperationException(
        $"Required test voice '{SystemSpeechVoiceName}' is not installed. " +
        "Available: " + string.Join(", ", installed));
    }
    synthesizer.SelectVoice(SystemSpeechVoiceName);
    synthesizer.Rate = rate;
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
