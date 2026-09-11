using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
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
      ("ctrl-click-voice-pointer/ctrl-hold-outlines-only-addressable-words",
        TestCtrlHoldOutlinesOnlyAddressableWords),
      ("ctrl-click-voice-pointer/ctrl-release-and-blur-remove-outlines",
        TestCtrlReleaseAndBlurRemoveOutlines),
      ("ctrl-click-voice-pointer/ctrl-click-posts-exact-voice-position",
        TestCtrlClickPostsExactVoicePosition),
      ("ctrl-click-voice-pointer/replacement-retains-held-ctrl-affordance",
        TestReplacementRetainsHeldCtrlAffordance)
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
  /// Holding Ctrl exposes exactly the rendered words with an exact speech
  /// node/word coordinate. Unmapped display text must not look selectable.
  /// </summary>
  private static void TestCtrlHoldOutlinesOnlyAddressableWords()
  {
    using BrowserFixture fixture = BrowserFixture.Create();
    InstallTranscript(fixture.WebView, 42, 1, "alpha beta gamma", "alpha beta");

    JsonElement result = ExecuteJsonProbe(
      fixture.WebView,
      """
(() => {
  window.dispatchEvent(new KeyboardEvent('keydown', {
    key:'Control',
    code:'ControlLeft',
    ctrlKey:true,
    bubbles:true
  }));
  const selectable = [...document.querySelectorAll('.word[data-node-word-index]')]
    .map(word => ({
      text:word.textContent,
      nodeId:Number(word.dataset.nodeId || 0),
      nodeWordIndex:Number(word.dataset.nodeWordIndex ?? -1)
    }));
  return JSON.stringify({
    mode:document.body.classList.contains('voice-pointer-select-mode'),
    selectable,
    gammaSelectable:[...document.querySelectorAll('.word')]
      .some(word => word.textContent === 'gamma' &&
        word.hasAttribute('data-node-word-index'))
  });
})()
""");

    Require(result.GetProperty("mode").GetBoolean(),
      "Holding Ctrl did not enable the voice-pointer selection affordance.");
    JsonElement selectable = result.GetProperty("selectable");
    Require(selectable.GetArrayLength() == 2,
      "Ctrl affordance did not expose exactly the two voiced words.");
    Require(selectable[0].GetProperty("text").GetString() == "alpha" &&
        selectable[0].GetProperty("nodeId").GetInt64() == 42 &&
        selectable[0].GetProperty("nodeWordIndex").GetInt32() == 0,
      "First selectable word has the wrong speech coordinate.");
    Require(selectable[1].GetProperty("text").GetString() == "beta" &&
        selectable[1].GetProperty("nodeId").GetInt64() == 42 &&
        selectable[1].GetProperty("nodeWordIndex").GetInt32() == 1,
      "Second selectable word has the wrong speech coordinate.");
    Require(!result.GetProperty("gammaSelectable").GetBoolean(),
      "Unmapped display text was incorrectly advertised as selectable.");
  }

  /// <summary>
  /// Ctrl mode is momentary. Both key release and focus loss must clear it so
  /// rectangles cannot remain stuck when modifier state is lost.
  /// </summary>
  private static void TestCtrlReleaseAndBlurRemoveOutlines()
  {
    using BrowserFixture fixture = BrowserFixture.Create();
    InstallTranscript(fixture.WebView, 42, 1, "alpha beta", "alpha beta");

    JsonElement result = ExecuteJsonProbe(
      fixture.WebView,
      """
(() => {
  const ctrlDown = () => window.dispatchEvent(new KeyboardEvent('keydown', {
    key:'Control',
    code:'ControlLeft',
    ctrlKey:true,
    bubbles:true
  }));
  ctrlDown();
  const afterDown = document.body.classList.contains('voice-pointer-select-mode');
  window.dispatchEvent(new KeyboardEvent('keyup', {
    key:'Control',
    code:'ControlLeft',
    ctrlKey:false,
    bubbles:true
  }));
  const afterUp = document.body.classList.contains('voice-pointer-select-mode');
  ctrlDown();
  window.dispatchEvent(new Event('blur'));
  const afterBlur = document.body.classList.contains('voice-pointer-select-mode');
  return JSON.stringify({afterDown, afterUp, afterBlur});
})()
""");

    Require(result.GetProperty("afterDown").GetBoolean(),
      "Ctrl-down did not enable selection mode.");
    Require(!result.GetProperty("afterUp").GetBoolean(),
      "Ctrl-up did not remove selectable-word rectangles.");
    Require(!result.GetProperty("afterBlur").GetBoolean(),
      "Window blur did not clear Ctrl selection mode.");
  }

  /// <summary>
  /// Only Ctrl+click on an addressable word may request a seek, and the posted
  /// coordinate must identify the exact clicked speech token.
  /// </summary>
  private static void TestCtrlClickPostsExactVoicePosition()
  {
    using BrowserFixture fixture = BrowserFixture.Create();
    InstallTranscript(fixture.WebView, 42, 1, "alpha beta gamma", "alpha beta");
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
    button:0,
    ctrlKey:false,
    bubbles:true,
    cancelable:true
  }));
  gamma.dispatchEvent(new MouseEvent('click', {
    button:0,
    ctrlKey:true,
    bubbles:true,
    cancelable:true
  }));
})()
""");
      PumpFor(150);
      Require(seekCount == 0,
        "Ordinary click or Ctrl+click on an unmapped word requested a seek.");

      ExecuteScript(
        fixture.WebView,
        """
(() => {
  const beta = [...document.querySelectorAll('.word')]
    .find(word => word.textContent === 'beta');
  if (!beta) throw new Error('Selectable beta word is missing.');
  beta.dispatchEvent(new MouseEvent('click', {
    button:0,
    ctrlKey:true,
    bubbles:true,
    cancelable:true
  }));
})()
""");
      PumpUntilCompleted(seekMessage.Task, "issue #73 Ctrl+click seek message");
      JsonElement result = seekMessage.Task.Result;

      Require(seekCount == 1,
        "Ctrl+click emitted more than one voice-pointer seek request.");
      Require(result.GetProperty("nodeId").GetInt64() == 42,
        "Ctrl+click posted the wrong speech node id.");
      Require(result.GetProperty("nodeWordIndex").GetInt32() == 1,
        "Ctrl+click posted the wrong speech word index.");
    }
    finally
    {
      fixture.WebView.CoreWebView2.WebMessageReceived -= MessageReceived;
    }
  }

  /// <summary>
  /// The Ctrl-held affordance is represented as page state rather than a
  /// one-time mutation of existing words, so newly materialized words inherit
  /// the rectangles without another key transition.
  /// </summary>
  private static void TestReplacementRetainsHeldCtrlAffordance()
  {
    using BrowserFixture fixture = BrowserFixture.Create();
    InstallTranscript(fixture.WebView, 42, 1, "alpha beta", "alpha beta");

    JsonElement result = ExecuteJsonProbe(
      fixture.WebView,
      """
(() => {
  window.dispatchEvent(new KeyboardEvent('keydown', {
    key:'Control',
    code:'ControlLeft',
    ctrlKey:true,
    bubbles:true
  }));
  replaceTranscript(
    '<span class="record-anchor" data-jsonl-record="2"></span>' +
      '<p>delta epsilon tail</p>',
    false,
    [{NodeId:99, RecordNumber:2, Segments:['delta epsilon']}]);
  return JSON.stringify({
    mode:document.body.classList.contains('voice-pointer-select-mode'),
    selectable:[...document.querySelectorAll('.word[data-node-word-index]')]
      .map(word => ({
        text:word.textContent,
        nodeId:Number(word.dataset.nodeId || 0),
        nodeWordIndex:Number(word.dataset.nodeWordIndex ?? -1)
      }))
  });
})()
""");

    Require(result.GetProperty("mode").GetBoolean(),
      "Window replacement dropped held-Ctrl selection mode.");
    JsonElement selectable = result.GetProperty("selectable");
    Require(selectable.GetArrayLength() == 2,
      "Newly materialized voiced words did not inherit Ctrl affordances.");
    Require(selectable[0].GetProperty("text").GetString() == "delta" &&
        selectable[0].GetProperty("nodeId").GetInt64() == 99 &&
        selectable[0].GetProperty("nodeWordIndex").GetInt32() == 0,
      "Replacement mapped the first selectable word incorrectly.");
    Require(selectable[1].GetProperty("text").GetString() == "epsilon" &&
        selectable[1].GetProperty("nodeWordIndex").GetInt32() == 1,
      "Replacement mapped the second selectable word incorrectly.");
  }

  private static void InstallTranscript(
    WebView2 webView,
    long nodeId,
    int recordNumber,
    string visibleText,
    string segment)
  {
    string script =
      "replaceTranscript(" +
      JsonSerializer.Serialize(
        $"<span class=\"record-anchor\" data-jsonl-record=\"{recordNumber}\"></span>" +
        $"<p>{visibleText}</p>") +
      ",false," +
      JsonSerializer.Serialize(new[]
      {
        new
        {
          NodeId = nodeId,
          RecordNumber = recordNumber,
          Segments = new[] { segment }
        }
      }) +
      ");";
    ExecuteScript(webView, script);
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
