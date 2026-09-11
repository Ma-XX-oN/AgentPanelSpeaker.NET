using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #73 Ctrl+click voice-pointer navigation.
/// </summary>
internal static class Issue73CtrlClickVoicePointerRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #73 Ctrl+click voice-pointer regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("ctrl-click-voice-pointer/current-speech-eligibility-is-authoritative",
        TestCurrentSpeechEligibilityIsAuthoritative),
      ("ctrl-click-voice-pointer/browser-affordance-and-click-contract",
        TestBrowserAffordanceAndClickContract),
      ("ctrl-click-voice-pointer/host-ctrl-routing-does-not-require-webview-focus",
        TestHostCtrlRoutingDoesNotRequireWebViewFocus)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #73 Ctrl+click voice-pointer regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #73 Ctrl+click tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #73 Ctrl+click tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Selectability is current playback eligibility, not merely DOM/node
  /// mapping. Node-word coordinates remain stable when earlier fragments are
  /// disabled, so enabling a category or fence never renumbers later words.
  /// </summary>
  private static void TestCurrentSpeechEligibilityIsAuthoritative()
  {
    bool reasoningSpoken = false;
    var spokenFenceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "cpp"
    };
    using var speech = new SpeechService();
    speech.SetPolicyProviders(
      category => category == ContentCategory.Reasoning && !reasoningSpoken
        ? new SpeechProfileSettings(SpeechProfileSettings.NotSpoken, 0, 0)
        : new SpeechProfileSettings("Test voice", 0, 0),
      fenceType => spokenFenceTypes.Contains(fenceType),
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);

    SpeechFragment[] fragments =
    {
      new(42, ContentCategory.Assistant, SpeechFragmentKind.Prose, "alpha beta"),
      new(42, ContentCategory.Reasoning, SpeechFragmentKind.Prose, "hidden"),
      new(
        42,
        ContentCategory.Assistant,
        SpeechFragmentKind.FencedCodeLine,
        "gamma",
        FenceType: "python"),
      new(
        42,
        ContentCategory.Assistant,
        SpeechFragmentKind.FencedCodeLine,
        "delta",
        FenceType: "cpp")
    };
    speech.LoadHistory(
      fragments,
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    MethodInfo getRanges = typeof(SpeechService).GetMethod(
      "GetSeekableTranscriptWordRanges",
      BindingFlags.Instance | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "SpeechService.GetSeekableTranscriptWordRanges() is missing.");

    TranscriptRangeProbe[] initial = ReadRanges(getRanges.Invoke(speech, null));
    RequireRanges(
      initial,
      new TranscriptRangeProbe(42, 0, 2),
      new TranscriptRangeProbe(42, 4, 1));

    Require(!speech.TrySeekToTranscriptWord(42, 2, out _),
      "A currently Not Spoken reasoning word was seekable.");
    Require(!speech.TrySeekToTranscriptWord(42, 3, out _),
      "A currently disabled fenced-code word was seekable.");
    Require(speech.TrySeekToTranscriptWord(42, 4, out string delta) &&
        delta == "delta",
      "Stable node-global word index 4 did not seek the eligible later word.");

    reasoningSpoken = true;
    TranscriptRangeProbe[] reasoningEnabled = ReadRanges(
      getRanges.Invoke(speech, null));
    RequireRanges(
      reasoningEnabled,
      new TranscriptRangeProbe(42, 0, 3),
      new TranscriptRangeProbe(42, 4, 1));
    Require(speech.TrySeekToTranscriptWord(42, 2, out string hidden) &&
        hidden == "hidden",
      "Enabling reasoning did not make its existing stable word coordinate seekable.");

    spokenFenceTypes.Add("python");
    TranscriptRangeProbe[] allEnabled = ReadRanges(getRanges.Invoke(speech, null));
    RequireRanges(allEnabled, new TranscriptRangeProbe(42, 0, 5));
    Require(speech.TrySeekToTranscriptWord(42, 3, out string gamma) &&
        gamma == "gamma",
      "Enabling the fence type did not make its stable word coordinate seekable.");
  }

  /// <summary>
  /// Ctrl is a momentary discoverability affordance: only currently eligible
  /// mapped words are boxed and clickable, replacement inherits held state,
  /// ordinary click is untouched, and key release or blur removes the boxes.
  /// </summary>
  private static void TestBrowserAffordanceAndClickContract()
  {
    using BrowserFixture fixture = BrowserFixture.Create();
    var seekMessage = new TaskCompletionSource<JsonElement>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    int seekCount = 0;

    void MessageReceived(
      object? sender,
      CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
      using JsonDocument document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
      JsonElement root = document.RootElement;
      if (root.TryGetProperty("type", out JsonElement typeElement) &&
          string.Equals(typeElement.GetString(), "find-seek", StringComparison.Ordinal) &&
          root.TryGetProperty("source", out JsonElement sourceElement) &&
          string.Equals(sourceElement.GetString(), "ctrl-click", StringComparison.Ordinal))
      {
        ++seekCount;
        seekMessage.TrySetResult(root.Clone());
      }
    }

    fixture.WebView.CoreWebView2.WebMessageReceived += MessageReceived;
    try
    {
      JsonElement initial = ExecuteJsonProbe(
        fixture.WebView,
        """
(() => {
  replaceTranscript(
    '<span class="record-anchor" data-jsonl-record="1"></span>' +
      '<p>alpha beta gamma</p>',
    false,
    [{NodeId:42, RecordNumber:1, Segments:['alpha beta gamma']}]);
  setSeekableVoiceRanges([{NodeId:42, StartNodeWordIndex:0, WordCount:2}]);
  const renderedWords = [...document.querySelectorAll('.word')];
  const classesBeforeCtrl = renderedWords.map(word => word.className);
  window.dispatchEvent(new KeyboardEvent('keydown', {
    key:'Control', code:'ControlLeft', ctrlKey:true, bubbles:true
  }));
  const classesAfterCtrl = renderedWords.map(word => word.className);
  const words = renderedWords.map(word => ({
    text:word.textContent,
    nodeId:Number(word.dataset.nodeId || 0),
    nodeWordIndex:Number(word.dataset.nodeWordIndex ?? -1),
    selectable:word.classList.contains('voice-selectable'),
    excluded:word.classList.contains('voice-excluded')
  }));
  return JSON.stringify({
    mode:document.body.classList.contains('voice-pointer-select-mode'),
    ctrlChangedWordClasses:JSON.stringify(classesBeforeCtrl) !==
      JSON.stringify(classesAfterCtrl),
    words
  });
})()
""");

      Require(initial.GetProperty("mode").GetBoolean(),
        "Holding Ctrl did not enable the voice-pointer selection affordance.");
      Require(!initial.GetProperty("ctrlChangedWordClasses").GetBoolean(),
        "Ctrl-down iterated/mutated individual word classes instead of only toggling page state.");
      JsonElement words = initial.GetProperty("words");
      Require(words.GetArrayLength() == 3,
        "Browser fixture did not materialize all three mapped words.");
      RequireWord(words[0], "alpha", 42, 0, selectable: true, excluded: false);
      RequireWord(words[1], "beta", 42, 1, selectable: true, excluded: false);
      RequireWord(words[2], "gamma", 42, 2, selectable: true, excluded: true);

      ExecuteScript(
        fixture.WebView,
        """
(() => {
  const alpha = [...document.querySelectorAll('.word')]
    .find(word => word.textContent === 'alpha');
  const gamma = [...document.querySelectorAll('.word')]
    .find(word => word.textContent === 'gamma');
  if (!alpha || !gamma) throw new Error('Fixture words are missing.');
  alpha.dispatchEvent(new MouseEvent('click', {
    button:0, ctrlKey:false, bubbles:true, cancelable:true
  }));
  gamma.dispatchEvent(new MouseEvent('click', {
    button:0, ctrlKey:true, bubbles:true, cancelable:true
  }));
})()
""");
      PumpFor(150);
      Require(seekCount == 0,
        "Ordinary click or Ctrl+click on a currently ineligible word requested a seek.");

      ExecuteScript(
        fixture.WebView,
        """
(() => {
  const beta = [...document.querySelectorAll('.word')]
    .find(word => word.textContent === 'beta');
  if (!beta) throw new Error('Selectable beta word is missing.');
  beta.dispatchEvent(new MouseEvent('click', {
    button:0, ctrlKey:true, bubbles:true, cancelable:true
  }));
})()
""");
      PumpUntilCompleted(seekMessage.Task, "issue #73 Ctrl+click seek message");
      JsonElement seek = seekMessage.Task.Result;
      Require(seekCount == 1,
        "Ctrl+click emitted more than one voice-pointer seek request.");
      Require(seek.GetProperty("nodeId").GetInt64() == 42 &&
          seek.GetProperty("nodeWordIndex").GetInt32() == 1,
        "Ctrl+click did not post the exact clicked speech coordinate.");

      JsonElement replacement = ExecuteJsonProbe(
        fixture.WebView,
        """
(() => {
  setSeekableVoiceRanges([{NodeId:99, StartNodeWordIndex:0, WordCount:2}]);
  replaceTranscript(
    '<span class="record-anchor" data-jsonl-record="2"></span>' +
      '<p>delta epsilon tail</p>',
    false,
    [{NodeId:99, RecordNumber:2, Segments:['delta epsilon tail']}]);
  const selectable = [...document.querySelectorAll('.word')]
    .filter(word => word.classList.contains('voice-selectable') &&
      !word.classList.contains('voice-excluded'))
    .map(word => word.textContent);
  window.dispatchEvent(new KeyboardEvent('keyup', {
    key:'Control', code:'ControlLeft', ctrlKey:false, bubbles:true
  }));
  const afterUp = document.body.classList.contains('voice-pointer-select-mode');
  window.dispatchEvent(new KeyboardEvent('keydown', {
    key:'Control', code:'ControlLeft', ctrlKey:true, bubbles:true
  }));
  window.dispatchEvent(new Event('blur'));
  const afterBrowserBlur = document.body.classList.contains(
    'voice-pointer-select-mode');
  setVoicePointerSelectMode(false);
  const afterHostDeactivate = document.body.classList.contains(
    'voice-pointer-select-mode');
  return JSON.stringify({
    heldThroughReplacement:selectable.join(' ') === 'delta epsilon',
    afterUp,
    afterBrowserBlur,
    afterHostDeactivate
  });
})()
""");

      Require(replacement.GetProperty("heldThroughReplacement").GetBoolean(),
        "Newly materialized eligible words did not inherit held-Ctrl affordances.");
      Require(!replacement.GetProperty("afterUp").GetBoolean(),
        "Ctrl-up did not remove selectable-word rectangles.");
      Require(replacement.GetProperty("afterBrowserBlur").GetBoolean(),
        "WebView focus loss incorrectly impersonated Ctrl-up while the app remained active.");
      Require(!replacement.GetProperty("afterHostDeactivate").GetBoolean(),
        "Host deactivation did not clear Ctrl selection mode.");
    }
    finally
    {
      fixture.WebView.CoreWebView2.WebMessageReceived -= MessageReceived;
    }
  }

  /// <summary>
  /// Ctrl selection mode must be driven by the containing WinForms window as
  /// well as browser key events so focus on another app control still exposes
  /// the currently seekable transcript words.  Key release is likewise routed
  /// by the host without consuming the original key message.
  /// </summary>
  private static void TestHostCtrlRoutingDoesNotRequireWebViewFocus()
  {
    MethodInfo setMode = typeof(TranscriptView).GetMethod(
      "SetVoicePointerSelectMode",
      BindingFlags.Instance | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "TranscriptView.SetVoicePointerSelectMode(bool) is missing.");
    Require(setMode.GetParameters() is [{ ParameterType: var parameterType }] &&
        parameterType == typeof(bool),
      "TranscriptView.SetVoicePointerSelectMode has the wrong contract.");

    MethodInfo resolver = typeof(MainForm).GetMethod(
      "TryGetVoicePointerSelectModeMessage",
      BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "MainForm host Ctrl-message resolver is missing.");

    object?[] down = { 0x0100, (IntPtr)(int)Keys.ControlKey, false };
    Require((bool)(resolver.Invoke(null, down) ?? false) &&
        down[2] is true,
      "Host WM_KEYDOWN for Ctrl did not enable voice-pointer selection mode.");

    object?[] up = { 0x0101, (IntPtr)(int)Keys.ControlKey, true };
    Require((bool)(resolver.Invoke(null, up) ?? false) &&
        up[2] is false,
      "Host WM_KEYUP for Ctrl did not disable voice-pointer selection mode.");

    object?[] ordinary = { 0x0100, (IntPtr)(int)Keys.A, false };
    Require(!(bool)(resolver.Invoke(null, ordinary) ?? true),
      "An ordinary host key was incorrectly treated as Ctrl selection state.");
  }

  private static TranscriptRangeProbe[] ReadRanges(object? value)
  {
    if (value is not IEnumerable ranges)
    {
      throw new InvalidOperationException(
        "GetSeekableTranscriptWordRanges() returned no enumerable result.");
    }

    var result = new List<TranscriptRangeProbe>();
    foreach (object? item in ranges)
    {
      if (item is null)
      {
        continue;
      }
      Type type = item.GetType();
      result.Add(new TranscriptRangeProbe(
        ReadInt64Property(type, item, "NodeId"),
        ReadInt32Property(type, item, "StartNodeWordIndex"),
        ReadInt32Property(type, item, "WordCount")));
    }
    return result.ToArray();
  }

  private static long ReadInt64Property(Type type, object instance, string name)
  {
    return type.GetProperty(name)?.GetValue(instance) is long value
      ? value
      : throw new InvalidOperationException($"Range property {name} is missing.");
  }

  private static int ReadInt32Property(Type type, object instance, string name)
  {
    return type.GetProperty(name)?.GetValue(instance) is int value
      ? value
      : throw new InvalidOperationException($"Range property {name} is missing.");
  }

  private static void RequireRanges(
    IReadOnlyList<TranscriptRangeProbe> actual,
    params TranscriptRangeProbe[] expected)
  {
    Require(actual.Count == expected.Length,
      $"Expected {expected.Length} seekable range(s), found {actual.Count}.");
    for (int index = 0; index < expected.Length; ++index)
    {
      Require(actual[index] == expected[index],
        $"Seekable range {index} was {actual[index]}, expected {expected[index]}.");
    }
  }

  private static void RequireWord(
    JsonElement word,
    string text,
    long nodeId,
    int nodeWordIndex,
    bool selectable,
    bool excluded)
  {
    Require(string.Equals(
        word.GetProperty("text").GetString(),
        text,
        StringComparison.Ordinal),
      $"Expected rendered word {text}.");
    Require(word.GetProperty("nodeId").GetInt64() == nodeId,
      $"Rendered word {text} has the wrong node id.");
    Require(word.GetProperty("nodeWordIndex").GetInt32() == nodeWordIndex,
      $"Rendered word {text} has the wrong node-global word index.");
    Require(word.GetProperty("selectable").GetBoolean() == selectable,
      $"Rendered word {text} has the wrong structural selectable class.");
    Require(word.GetProperty("excluded").GetBoolean() == excluded,
      $"Rendered word {text} has the wrong current exclusion class.");
  }

  private static JsonElement ExecuteJsonProbe(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #73 browser probe");
    string encoded = JsonSerializer.Deserialize<string>(task.Result) ??
      throw new InvalidOperationException("Browser probe returned no JSON string.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static void ExecuteScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #73 browser script");
  }

  private static void PumpFor(int milliseconds)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
  }

  private static void PumpUntilCompleted(
    Task task,
    string description,
    int timeoutMilliseconds = 30000)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (!task.IsCompleted && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(task.IsCompleted, $"Timed out waiting for {description}.");
    task.GetAwaiter().GetResult();
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }

  private readonly record struct TranscriptRangeProbe(
    long NodeId,
    int StartNodeWordIndex,
    int WordCount);

  private sealed class BrowserFixture : IDisposable
  {
    private BrowserFixture(Form host, WebView2 webView)
    {
      Host = host;
      WebView = webView;
    }

    public Form Host { get; }
    public WebView2 WebView { get; }

    public static BrowserFixture Create()
    {
      var host = new Form
      {
        Width = 800,
        Height = 600,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      var webView = new WebView2 { Dock = DockStyle.Fill };
      host.Controls.Add(webView);
      host.Show();
      Application.DoEvents();

      PumpUntilCompleted(
        webView.EnsureCoreWebView2Async(),
        "issue #73 WebView initialization");
      MethodInfo shellMethod = typeof(TranscriptView).GetMethod(
        "BuildShellHtml",
        BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException(
          "TranscriptView.BuildShellHtml() was not found.");
      string shell = shellMethod.Invoke(null, null) as string ??
        throw new InvalidOperationException("Transcript shell was empty.");
      var navigated = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      webView.NavigationCompleted += (_, eventArgs) =>
      {
        if (eventArgs.IsSuccess)
        {
          navigated.TrySetResult(true);
        }
        else
        {
          navigated.TrySetException(new InvalidOperationException(
            $"WebView navigation failed: {eventArgs.WebErrorStatus}."));
        }
      };
      webView.CoreWebView2.NavigateToString(shell);
      PumpUntilCompleted(navigated.Task, "issue #73 transcript shell navigation");
      return new BrowserFixture(host, webView);
    }

    public void Dispose()
    {
      WebView.Dispose();
      Host.Dispose();
    }
  }
}
