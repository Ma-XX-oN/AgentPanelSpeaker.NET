from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "AgentPanelSpeaker"


def replace_once(path, old, new):
  text = path.read_text(encoding="utf-8")
  if text.count(old) != 1:
    raise RuntimeError(f"Expected exactly one anchor in {path}: {old[:80]!r}")
  path.write_text(text.replace(old, new, 1), encoding="utf-8")


def write(path, text):
  path.write_text(text, encoding="utf-8")


def regression_source():
  return r'''namespace AgentPanelSpeaker;

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
    return failures == 0 ? 0 : 1;
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
      "<say-as interpret-as=\"characters\">AI</say-as>-transcript.py",
      "<say-as interpret-as=\"characters\">AI</say-as> transcript.py",
      "<say-as interpret-as=\"characters\">AI</say-as><break time=\"100ms\"/>-transcript.py",
      "<say-as interpret-as=\"characters\">AI</say-as><sub alias=\"transcript\">-transcript</sub>.py"
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
      "  /// <summary>\n  /// Opens the spelling and pronunciation-rule editor.",
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
'''


def add_program_suite():
  path = APP / "Program.cs"
  marker = '''      if (args.Length == 2 &&
          string.Equals(args[1], "redundancy", StringComparison.OrdinalIgnoreCase))
'''
  block = '''      if (args.Length == 2 &&
          string.Equals(
            args[1],
            "system-speech-matrix",
            StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = RunNamedSuite(
          "system-speech-matrix",
          Issue96SystemSpeechMatrixRegressionTestRunner.Run);
        return;
      }

'''
  replace_once(path, marker, block + marker)
  marker2 = '''      int systemSpeechProvenance = RunIsolatedTestSuite(
        "system-speech-provenance");

      Environment.ExitCode = primary == 0 &&
'''
  replacement2 = '''      int systemSpeechProvenance = RunIsolatedTestSuite(
        "system-speech-provenance");
      int systemSpeechMatrix = RunIsolatedTestSuite(
        "system-speech-matrix");

      Environment.ExitCode = primary == 0 &&
'''
  replace_once(path, marker2, replacement2)
  marker3 = '''                             previewCursor == 0 &&
                             systemSpeechProvenance == 0
        ? 0
'''
  replacement3 = '''                             previewCursor == 0 &&
                             systemSpeechProvenance == 0 &&
                             systemSpeechMatrix == 0
        ? 0
'''
  replace_once(path, marker3, replacement3)


def runner_source():
  return r'''using System.Security.Cryptography;
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
'''


def dialog_source():
  return r'''namespace AgentPanelSpeaker;

/// <summary>
/// Selects the System.Speech voice and scalar settings for the controlled SSML
/// provider matrix. Values are diagnostic-only and are not persisted.
/// </summary>
internal sealed class SystemSpeechMatrixDialog : Form
{
  private readonly ComboBox _voice = new();
  private readonly NumericUpDown _rate = new();
  private readonly NumericUpDown _volume = new();
  private readonly Button _run = new();
  private readonly Button _cancel = new();

  public SystemSpeechMatrixDialog(IReadOnlyList<InstalledSpeechVoice> voices)
  {
    ArgumentNullException.ThrowIfNull(voices);
    if (voices.Count == 0 || voices.Any(
        voice => voice.Provider != SpeechVoiceProvider.SystemSpeech))
    {
      throw new ArgumentException(
        "The matrix dialog requires one or more System.Speech voices.",
        nameof(voices));
    }

    Text = "System.Speech SSML Matrix";
    StartPosition = FormStartPosition.CenterParent;
    FormBorderStyle = FormBorderStyle.FixedDialog;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    AutoSize = true;
    AutoSizeMode = AutoSizeMode.GrowAndShrink;
    Padding = new Padding(12);

    var layout = new TableLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 6,
      Padding = new Padding(0)
    };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));

    var description = new Label
    {
      AutoSize = true,
      MaximumSize = new Size(470, 0),
      Text =
        "Runs five fixed SSML cases directly through System.Speech. " +
        "The diagnostic does not use transcript playback or change the voice cursor."
    };
    layout.Controls.Add(description, 0, 0);
    layout.SetColumnSpan(description, 2);

    _voice.DropDownStyle = ComboBoxStyle.DropDownList;
    _voice.Dock = DockStyle.Fill;
    foreach (InstalledSpeechVoice voice in voices)
    {
      _voice.Items.Add(voice);
    }
    _voice.SelectedIndex = 0;

    _rate.Minimum = -10;
    _rate.Maximum = 10;
    _rate.Value = 0;
    _rate.Width = 80;
    _volume.Minimum = 0;
    _volume.Maximum = 100;
    _volume.Value = 100;
    _volume.Width = 80;

    layout.Controls.Add(new Label { AutoSize = true, Text = "Voice:" }, 0, 1);
    layout.Controls.Add(_voice, 1, 1);
    layout.Controls.Add(new Label { AutoSize = true, Text = "Rate:" }, 0, 2);
    layout.Controls.Add(_rate, 1, 2);
    layout.Controls.Add(new Label { AutoSize = true, Text = "Volume:" }, 0, 3);
    layout.Controls.Add(_volume, 1, 3);

    var cases = new Label
    {
      AutoSize = true,
      MaximumSize = new Size(470, 0),
      Text =
        "1. AI-transcript.py\n" +
        "2. characters(AI)-transcript.py\n" +
        "3. characters(AI) transcript.py\n" +
        "4. characters(AI) + 100 ms break + -transcript.py\n" +
        "5. characters(AI) + sub(alias=transcript) + .py"
    };
    layout.Controls.Add(cases, 0, 4);
    layout.SetColumnSpan(cases, 2);

    ConfigureButton(_run, "Run matrix", DialogResult.OK);
    ConfigureButton(_cancel, "Cancel", DialogResult.Cancel);
    var buttons = new FlowLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      FlowDirection = FlowDirection.RightToLeft,
      Dock = DockStyle.Fill
    };
    buttons.Controls.Add(_cancel);
    buttons.Controls.Add(_run);
    layout.Controls.Add(buttons, 0, 5);
    layout.SetColumnSpan(buttons, 2);

    Controls.Add(layout);
    AcceptButton = _run;
    CancelButton = _cancel;
  }

  /// <summary>
  /// Returns the diagnostic settings selected by the user.
  /// </summary>
  public SystemSpeechMatrixOptions Options => new(
    (InstalledSpeechVoice)_voice.SelectedItem!,
    (int)_rate.Value,
    (int)_volume.Value);

  private static void ConfigureButton(
    Button button,
    string text,
    DialogResult result)
  {
    button.AutoSize = true;
    button.Text = text;
    button.DialogResult = result;
  }
}
'''


def modify_main():
  path = APP / "MainForm.cs"
  replace_once(
    path,
    '  private readonly Button _pronunciationsButton = new();\n  private readonly GlyphButton _audioWakeButton = new();',
    '  private readonly Button _pronunciationsButton = new();\n  private readonly Button _systemSpeechMatrixButton = new();\n  private readonly GlyphButton _audioWakeButton = new();')
  replace_once(
    path,
    '  private bool _voiceSettingPreviewActive;\n  private bool _themeApplicationPending;',
    '  private bool _voiceSettingPreviewActive;\n  private bool _systemSpeechMatrixActive;\n  private bool _themeApplicationPending;')
  replace_once(
    path,
    '    ConfigureButton(_pronunciationsButton, "Pronunciations...");\n    ConfigureUtilityGlyphButton(',
    '    ConfigureButton(_pronunciationsButton, "Pronunciations...");\n    ConfigureButton(_systemSpeechMatrixButton, "SSML Matrix...");\n    _toolTip.SetToolTip(\n      _systemSpeechMatrixButton,\n      "Run the controlled System.Speech SSML provider diagnostic");\n    ConfigureUtilityGlyphButton(')
  replace_once(
    path,
    '      _pronunciationsButton,\n      _speakExistingCheckBox, _keepDisplayOnCheckBox\n',
    '      _pronunciationsButton, _systemSpeechMatrixButton,\n      _speakExistingCheckBox, _keepDisplayOnCheckBox\n')
  replace_once(
    path,
    '      _pronunciationsButton,\n      _speakExistingCheckBox,\n',
    '      _pronunciationsButton,\n      _systemSpeechMatrixButton,\n      _speakExistingCheckBox,\n')
  replace_once(
    path,
    '    MatchButtonHeight(_pronunciationsButton, _fenceTypesTextBox.PreferredHeight);',
    '    MatchButtonHeight(_pronunciationsButton, _fenceTypesTextBox.PreferredHeight);\n    MatchButtonHeight(_systemSpeechMatrixButton, _fenceTypesTextBox.PreferredHeight);')
  replace_once(
    path,
    '    _pronunciationsButton.Click += PronunciationsButtonClicked;\n    _audioWakeButton.Click += AudioWakeButtonClicked;',
    '    _pronunciationsButton.Click += PronunciationsButtonClicked;\n    _systemSpeechMatrixButton.Click += SystemSpeechMatrixButtonClicked;\n    _audioWakeButton.Click += AudioWakeButtonClicked;')
  replace_once(
    path,
    '    _pronunciationsButton.Enabled = true;\n    UpdatePlayPauseButton(running);',
    '    _pronunciationsButton.Enabled = true;\n    _systemSpeechMatrixButton.Enabled =\n      !_systemSpeechMatrixActive &&\n      !_monitor.IsRunning &&\n      !_playPauseTransitioning &&\n      !_speech.IsSpeaking &&\n      _installedVoices.Any(voice =>\n        voice.Provider == SpeechVoiceProvider.SystemSpeech);\n    UpdatePlayPauseButton(running);')
  anchor = '''  /// <summary>
  /// Opens the spelling and pronunciation-rule editor.
  /// </summary>
  private void PronunciationsButtonClicked(
'''
  handler = '''  /// <summary>
  /// Runs the isolated System.Speech SSML provider matrix.
  /// </summary>
  private async void SystemSpeechMatrixButtonClicked(
    object? sender,
    EventArgs eventArgs)
  {
    if (_systemSpeechMatrixActive || _monitor.IsRunning ||
        _playPauseTransitioning || _speech.IsSpeaking)
    {
      return;
    }

    InstalledSpeechVoice[] voices = _installedVoices
      .Where(voice => voice.Provider == SpeechVoiceProvider.SystemSpeech)
      .ToArray();
    if (voices.Length == 0)
    {
      MessageBox.Show(
        this,
        "No System.Speech voices are available for the SSML matrix.",
        "SSML Matrix",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
      return;
    }

    using var dialog = new SystemSpeechMatrixDialog(voices);
    if (dialog.ShowDialog(this) != DialogResult.OK)
    {
      return;
    }

    _systemSpeechMatrixActive = true;
    UpdateControlState();
    try
    {
      AppendLog(
        $"System.Speech SSML matrix started: {dialog.Options.Voice.ProviderVoiceId}");
      await SystemSpeechMatrixDiagnosticRunner.RunAsync(dialog.Options);
      AppendLog("System.Speech SSML matrix completed. See diagnostic JSONL.");
    }
    catch (Exception exception)
    {
      DiagnosticLog.WriteException(
        "speech.system_speech_matrix_ui_failed",
        exception,
        source: "SSML Matrix button",
        isTerminating: false);
      AppendLog($"System.Speech SSML matrix failed: {exception.Message}");
      MessageBox.Show(
        this,
        exception.Message,
        "SSML Matrix failed",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    }
    finally
    {
      _systemSpeechMatrixActive = false;
      UpdateControlState();
    }
  }

'''
  replace_once(path, anchor, handler + anchor)


def apply_red():
  write(APP / "Issue96SystemSpeechMatrixRegressionTestRunner.cs", regression_source())
  add_program_suite()


def apply_green():
  apply_red()
  write(APP / "SystemSpeechMatrixDiagnosticRunner.cs", runner_source())
  write(APP / "SystemSpeechMatrixDialog.cs", dialog_source())
  modify_main()


def main():
  if len(sys.argv) != 2 or sys.argv[1] not in {"red", "green"}:
    raise SystemExit("usage: issue96-system-speech-matrix.py red|green")
  if sys.argv[1] == "red":
    apply_red()
  else:
    apply_green()


if __name__ == "__main__":
  main()
