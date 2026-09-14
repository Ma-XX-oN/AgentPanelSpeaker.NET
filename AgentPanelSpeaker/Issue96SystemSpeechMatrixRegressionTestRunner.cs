namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #96 System.Speech controlled matrix diagnostics.
/// </summary>
internal static class Issue96SystemSpeechMatrixRegressionTestRunner
{
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("system-speech-matrix/button-is-wired", TestButtonIsWired),
      ("system-speech-matrix/system-speech-only", TestSystemSpeechOnly),
      ("system-speech-matrix/fixed-five-case-inventory", TestFixedFiveCaseInventory),
      ("system-speech-matrix/provider-boundary-events", TestProviderBoundaryEvents),
      ("system-speech-matrix/matches-production-audio-format", TestMatchesProductionAudioFormat),
      ("system-speech-matrix/no-playback-navigation", TestNoPlaybackNavigation)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #96 System.Speech matrix suite: {tests.Length} tests");
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
        Console.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} issue #96 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #96 tests failed.");

    int issue94 = Issue94SystemSpeechSampleRateRegressionTestRunner.Run();
    return failures == 0 && issue94 == 0 ? 0 : 1;
  }

  private static void TestButtonIsWired()
  {
    string main = ReadSource("MainForm.cs");
    Require(main.Contains("_systemSpeechMatrixButton", StringComparison.Ordinal),
      "MainForm has no System.Speech matrix button.");
    Require(main.Contains("ConfigureButton(_systemSpeechMatrixButton, \"SSML Matrix...\")",
        StringComparison.Ordinal),
      "The diagnostic button is not visibly identified as SSML Matrix.");
    Require(main.Contains(
        "_systemSpeechMatrixButton.Click += SystemSpeechMatrixButtonClicked;",
        StringComparison.Ordinal),
      "The matrix button has no click action.");
  }

  private static void TestSystemSpeechOnly()
  {
    string main = ReadSource("MainForm.cs");
    string runner = ReadOptionalSource("SystemSpeechMatrixDiagnosticRunner.cs");
    Require(main.Contains(
        "voice.Provider == SpeechVoiceProvider.SystemSpeech",
        StringComparison.Ordinal),
      "MainForm does not restrict the matrix chooser to System.Speech voices.");
    Require(runner.Contains(
        "options.Voice.Provider != SpeechVoiceProvider.SystemSpeech",
        StringComparison.Ordinal),
      "The diagnostic runner does not reject non-System.Speech voices.");
  }

  private static void TestFixedFiveCaseInventory()
  {
    string source = ReadOptionalSource("SystemSpeechMatrixDiagnosticRunner.cs");
    foreach (string required in new[]
    {
      "plain-hyphenated",
      "characters-hyphenated",
      "characters-space",
      "characters-break-hyphenated",
      "characters-sub-alias",
      "AI-transcript.py",
      "interpret-as=\\\"characters\\\"",
      "<break time=\\\"100ms\\\"/>",
      "<sub alias=\\\"transcript\\\">"
    })
    {
      Require(source.Contains(required, StringComparison.Ordinal),
        $"Controlled matrix is missing {required}.");
    }
    Require(source.Contains("private static readonly SystemSpeechMatrixCase[] Cases =",
        StringComparison.Ordinal),
      "The matrix case inventory is not a fixed production constant.");
  }

  private static void TestProviderBoundaryEvents()
  {
    string source = ReadOptionalSource("SystemSpeechMatrixDiagnosticRunner.cs");
    foreach (string eventName in new[]
    {
      "speech.system_speech_matrix_started",
      "speech.system_speech_matrix_case_submitted",
      "speech.system_speech_matrix_progress",
      "speech.system_speech_matrix_case_synthesized",
      "speech.system_speech_matrix_case_playback_started",
      "speech.system_speech_matrix_case_completed",
      "speech.system_speech_matrix_completed"
    })
    {
      Require(source.Contains(eventName, StringComparison.Ordinal),
        $"Matrix diagnostic does not log {eventName}.");
    }
    int submitted = source.IndexOf(
      "DiagnosticLog.Write(\"speech.system_speech_matrix_case_submitted\"",
      StringComparison.Ordinal);
    int speak = source.IndexOf("synthesizer.SpeakSsml(ssml);", StringComparison.Ordinal);
    Require(submitted >= 0 && speak > submitted,
      "Exact matrix SSML is not logged before SpeakSsml.");
    string boundary = source[submitted..speak];
    Require(boundary.Contains("ssml", StringComparison.Ordinal) &&
            boundary.Contains("ComputeUtf8Sha256(ssml)", StringComparison.Ordinal),
      "Submitted matrix payload lacks exact SSML/SHA-256 evidence.");
    Require(source.Contains("args.CharacterPosition", StringComparison.Ordinal) &&
            source.Contains("args.CharacterCount", StringComparison.Ordinal) &&
            source.Contains("args.AudioPosition", StringComparison.Ordinal) &&
            source.Contains("args.Text", StringComparison.Ordinal),
      "Matrix progress diagnostics omit provider-native SpeakProgress fields.");
  }

  private static void TestMatchesProductionAudioFormat()
  {
    string source = ReadOptionalSource("SystemSpeechMatrixDiagnosticRunner.cs");
    Require(source.Contains("SystemSpeechSampleRate = 16000", StringComparison.Ordinal),
      "Matrix synthesis does not explicitly use the production 16 kHz rate.");
    Require(source.Contains("AudioBitsPerSample.Sixteen", StringComparison.Ordinal) &&
            source.Contains("AudioChannel.Mono", StringComparison.Ordinal),
      "Matrix synthesis does not use production 16-bit mono output.");
    Require(source.Contains("ConvertToMono16(OutputSampleRate)", StringComparison.Ordinal) &&
            source.Contains("OutputSampleRate = 48000", StringComparison.Ordinal),
      "Matrix playback does not use the production 48 kHz conversion.");
  }

  private static void TestNoPlaybackNavigation()
  {
    string main = ReadSource("MainForm.cs");
    int start = main.IndexOf(
      "private async void SystemSpeechMatrixButtonClicked(",
      StringComparison.Ordinal);
    Require(start >= 0, "Matrix click handler is missing.");
    int end = main.IndexOf(
      "  private void PronunciationsButtonClicked(",
      start,
      StringComparison.Ordinal);
    Require(end > start, "Could not isolate the matrix click handler.");
    string method = main[start..end];
    foreach (string forbidden in new[]
    {
      "CancelAndMoveToLiveEnd",
      "CancelPreviewPreservingPositionAsync",
      "BeginLiveSession",
      "LoadHistory(",
      "SeekTo",
      "RestartHistory"
    })
    {
      Require(!method.Contains(forbidden, StringComparison.Ordinal),
        $"Matrix action mutates playback/navigation through {forbidden}.");
    }
    Require(method.Contains("SystemSpeechMatrixDiagnosticRunner.RunAsync",
        StringComparison.Ordinal),
      "Matrix action is not isolated in the standalone provider diagnostic runner.");
  }

  private static string ReadOptionalSource(string fileName)
  {
    try
    {
      return ReadSource(fileName);
    }
    catch (FileNotFoundException)
    {
      return string.Empty;
    }
  }

  private static string ReadSource(string fileName)
  {
    foreach (string start in new[]
    {
      Directory.GetCurrentDirectory(),
      AppContext.BaseDirectory
    })
    {
      DirectoryInfo? directory = new DirectoryInfo(start);
      for (int depth = 0; depth < 12 && directory is not null; ++depth)
      {
        string candidate = Path.Combine(directory.FullName, "AgentPanelSpeaker", fileName);
        if (File.Exists(candidate))
        {
          return File.ReadAllText(candidate);
        }
        directory = directory.Parent;
      }
    }
    throw new FileNotFoundException(
      $"Could not locate AgentPanelSpeaker/{fileName} from the test process.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
