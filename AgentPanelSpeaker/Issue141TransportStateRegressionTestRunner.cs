using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Production MainForm regressions for issue #141 transport ownership.
/// </summary>
internal static class Issue141TransportStateRegressionTestRunner
{
  private const string FixtureText =
    "Retained playback must stay at this exact sentence.";

  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("transport-state/keyboard-pause-keeps-active-speech-owned",
        TestKeyboardPauseKeepsActiveSpeechOwned),
      ("transport-state/button-pause-keeps-active-speech-owned",
        TestButtonPauseKeepsActiveSpeechOwned)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #141 transport-state suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #141 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #141 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestKeyboardPauseKeepsActiveSpeechOwned()
  {
    using var fixture = new TransportFixture();
    MainForm form = fixture.Form;
    SpeechService speech = fixture.Speech;
    JsonlSessionMonitor monitor = fixture.Monitor;
    GlyphButton playPause = fixture.PlayPause;
    ComboBox theme = fixture.Theme;

    fixture.SeedActiveSpeech();
    playPause.Focus();
    Application.DoEvents();
    Require(playPause.Focused,
      "Play/Pause did not establish the keyboard-focus precondition.");

    int beforeHistoryIndex = ReadField<int>(speech, "_activeHistoryIndex");
    int beforeNextIndex = ReadField<int>(speech, "_nextHistoryIndex");

    bool handled = InvokeTransportShortcut(form, Keys.K, "K");
    Application.DoEvents();

    Require(handled, "Keyboard K was not accepted as Play/Pause.");
    Require(!monitor.IsRunning,
      "Keyboard pause restarted monitoring after the monitor had stopped.");
    Require(speech.IsSpeaking,
      "Keyboard pause discarded ownership of the active speech fragment.");
    Require(speech.IsPaused,
      "Keyboard pause did not pause the active speech fragment.");
    Require(speech.HasHistory,
      "Keyboard pause cleared the retained speech history.");
    Require(
      ReadField<int>(speech, "_activeHistoryIndex") == beforeHistoryIndex,
      "Keyboard pause moved the active history index.");
    Require(
      ReadField<int>(speech, "_nextHistoryIndex") == beforeNextIndex,
      "Keyboard pause moved the next history index.");
    Require(playPause.Focused,
      "Keyboard pause transferred focus away from Play/Pause.");
    Require(!theme.Focused,
      "Keyboard pause transferred focus to the Theme ComboBox.");

    handled = InvokeTransportShortcut(form, Keys.K, "K");
    Application.DoEvents();

    Require(handled, "Second keyboard K was not accepted as Play/Pause.");
    Require(!monitor.IsRunning,
      "Keyboard resume restarted monitoring.");
    Require(speech.IsSpeaking && !speech.IsPaused,
      "Keyboard resume did not resume the retained active speech.");
    Require(
      ReadField<int>(speech, "_activeHistoryIndex") == beforeHistoryIndex,
      "Keyboard resume moved the active history index.");
    Require(
      ReadField<int>(speech, "_nextHistoryIndex") == beforeNextIndex,
      "Keyboard resume moved the next history index.");
    Require(playPause.Focused && !theme.Focused,
      "Keyboard resume did not retain transport focus.");
    Require(
      playPause.Drawing == GlyphButtonDrawing.Pause,
      "Active speech with a stopped monitor did not display the Pause glyph.");
  }

  private static void TestButtonPauseKeepsActiveSpeechOwned()
  {
    using var fixture = new TransportFixture();
    SpeechService speech = fixture.Speech;
    JsonlSessionMonitor monitor = fixture.Monitor;
    GlyphButton playPause = fixture.PlayPause;

    fixture.SeedActiveSpeech();
    int beforeHistoryIndex = ReadField<int>(speech, "_activeHistoryIndex");
    int beforeNextIndex = ReadField<int>(speech, "_nextHistoryIndex");

    playPause.PerformClick();
    Application.DoEvents();

    Require(!monitor.IsRunning,
      "Button pause restarted monitoring after the monitor had stopped.");
    Require(speech.IsSpeaking && speech.IsPaused,
      "Button pause did not retain and pause the active speech.");
    Require(speech.HasHistory,
      "Button pause cleared the retained history.");
    Require(
      ReadField<int>(speech, "_activeHistoryIndex") == beforeHistoryIndex &&
      ReadField<int>(speech, "_nextHistoryIndex") == beforeNextIndex,
      "Button pause changed the retained playback position.");

    playPause.PerformClick();
    Application.DoEvents();

    Require(!monitor.IsRunning,
      "Button resume restarted monitoring.");
    Require(speech.IsSpeaking && !speech.IsPaused,
      "Button resume did not resume the retained active speech.");
    Require(
      ReadField<int>(speech, "_activeHistoryIndex") == beforeHistoryIndex &&
      ReadField<int>(speech, "_nextHistoryIndex") == beforeNextIndex,
      "Button resume changed the retained playback position.");
  }

  private static bool InvokeTransportShortcut(
    MainForm form,
    Keys key,
    string shortcut)
  {
    MethodInfo method = typeof(MainForm).GetMethod(
      "ActivateTransportShortcut",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "MainForm.ActivateTransportShortcut was not found.");
    return Convert.ToBoolean(method.Invoke(
      form,
      new object[] { key, shortcut, "issue-141-regression" }));
  }

  private static T ReadField<T>(object target, string name)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field {name} was not found on {target.GetType().Name}.");
    object? value = field.GetValue(target);
    return value is T typed
      ? typed
      : throw new InvalidOperationException(
        $"Field {name} was not {typeof(T).Name}.");
  }

  private static void SetField<T>(
    object target,
    string name,
    T value)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field {name} was not found on {target.GetType().Name}.");
    field.SetValue(target, value);
  }

  private static void SetEnumField(
    object target,
    string name,
    string enumValue)
  {
    FieldInfo field = target.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field {name} was not found on {target.GetType().Name}.");
    object parsed = Enum.Parse(field.FieldType, enumValue);
    field.SetValue(target, parsed);
  }

  private static void InvokeVoid(
    object target,
    string methodName)
  {
    MethodInfo method = target.GetType().GetMethod(
      methodName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Method {methodName} was not found.");
    _ = method.Invoke(target, null);
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }

  private sealed class TransportFixture : IDisposable
  {
    private readonly string _root;
    public MainForm Form { get; }
    public SpeechService Speech { get; }
    public JsonlSessionMonitor Monitor { get; }
    public GlyphButton PlayPause { get; }
    public ComboBox Theme { get; }

    public TransportFixture()
    {
      _root = Path.Combine(
        Path.GetTempPath(),
        $"AgentPanelSpeaker-issue141-{Guid.NewGuid():N}");
      Directory.CreateDirectory(_root);
      string path = Path.Combine(_root, "transport.jsonl");
      File.WriteAllText(
        path,
        BuildClaudeJsonl(),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

      Form = new MainForm
      {
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      SetField(Form, "_pathIsManual", false);
      ReadField<TextBox>(Form, "_sessionPathTextBox").Text = string.Empty;
      Form.Show();
      Application.DoEvents();

      Speech = ReadField<SpeechService>(Form, "_speech");
      Monitor = ReadField<JsonlSessionMonitor>(Form, "_monitor");
      PlayPause = ReadField<GlyphButton>(Form, "_playPauseButton");
      Theme = ReadField<ComboBox>(Form, "_themeComboBox");

      SetField(Form, "_loadingSettings", true);
      try
      {
        ReadField<ComboBox>(Form, "_sourceComboBox").SelectedItem =
          AgentSource.Claude;
        ReadField<TextBox>(Form, "_sessionPathTextBox").Text = path;
        ReadField<CheckBox>(Form, "_followLatestCheckBox").Checked = false;
        ReadField<CheckBox>(Form, "_speakExistingCheckBox").Checked = true;
        SetField(Form, "_pathIsManual", true);
        SetField<SpeechHistorySnapshot?>(Form, "_selectedSessionHistory", null);
        SetField<string?>(Form, "_selectedSessionHistoryPath", null);
      }
      finally
      {
        SetField(Form, "_loadingSettings", false);
      }
      InvokeVoid(Form, "UpdateControlState");
      Application.DoEvents();
    }

    public void SeedActiveSpeech()
    {
      Monitor.Stop("issue-141-precondition");
      var fragment = new SpeechFragment(
        141,
        ContentCategory.Assistant,
        SpeechFragmentKind.Prose,
        FixtureText,
        TranscriptWords: new[]
        {
          new SpeechFragmentWord(141001, "Retained", 0, 8),
          new SpeechFragmentWord(141002, "playback", 9, 8),
          new SpeechFragmentWord(141003, "must", 18, 4)
        },
        FragmentId: 141);
      Speech.LoadHistory(
        new[] { fragment },
        Array.Empty<TurnCompletion>(),
        Array.Empty<BackgroundWorkEvent>(),
        PlaybackStartMode.LiveEnd);

      SetEnumField(Speech, "_activeKind", "History");
      SetField(Speech, "_reportedSpeaking", true);
      SetField(Speech, "_isPaused", false);
      SetField(Speech, "_pauseStartedUtc", null as DateTimeOffset?);
      SetField(Speech, "_activeHistoryIndex", 0);
      SetField(Speech, "_nextHistoryIndex", 1);
      SetField<int?>(Speech, "_pendingHistoryIndex", null);
      SetField(Speech, "_pendingHistoryWordIndex", 0);
      SetField(Speech, "_activeTranscriptText", FixtureText);
      SetField(Speech, "_activeWordIndex", 1);
      SetField(Speech, "_activeWordCount", 1);
      SetField(Speech, "_activeWord", "playback");
      SetField(Speech, "_activeCharacterPosition", 9);
      SetField(Speech, "_activeCharacterCount", 8);
      SetField(Speech, "_activeBoundaryTimestamp", Stopwatch.GetTimestamp());

      InvokeVoid(Form, "UpdateControlState");
      Application.DoEvents();

      Require(!Monitor.IsRunning,
        "Fixture monitor unexpectedly started.");
      Require(Speech.IsSpeaking && !Speech.IsPaused && Speech.HasHistory,
        "Fixture did not establish active retained speech.");
    }

    public void Dispose()
    {
      Monitor.Stop("issue-141-cleanup");
      Form.Close();
      Form.Dispose();
      Application.DoEvents();
      try
      {
        Directory.Delete(_root, recursive: true);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }

    private static string BuildClaudeJsonl()
    {
      object[] records =
      {
        new
        {
          uuid = "issue141-user",
          type = "user",
          isSidechain = false,
          timestamp = "2026-09-18T00:00:00.000Z",
          message = new
          {
            role = "user",
            content = new[] { new { type = "text", text = "Prompt." } }
          }
        },
        new
        {
          uuid = "issue141-assistant",
          type = "assistant",
          isSidechain = false,
          timestamp = "2026-09-18T00:00:01.000Z",
          message = new
          {
            model = "claude-test",
            role = "assistant",
            content = new[] { new { type = "text", text = "Answer." } }
          }
        }
      };
      return string.Join(
        Environment.NewLine,
        records.Select(JsonSerializer.Serialize)) +
        Environment.NewLine;
    }
  }
}
