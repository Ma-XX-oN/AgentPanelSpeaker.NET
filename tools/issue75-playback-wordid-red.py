from pathlib import Path

PATH = Path('AgentPanelSpeaker/Issue75CoreWordIdMigrationRegressionTestRunner.cs')
text = PATH.read_text(encoding='utf-8')

old = '''      ("core-word-id/policy-is-centralized-without-per-word-eligibility-mutation",\n        TestPolicyIsCentralizedWithoutPerWordEligibilityMutation)\n'''
new = '''      ("core-word-id/policy-is-centralized-without-per-word-eligibility-mutation",\n        TestPolicyIsCentralizedWithoutPerWordEligibilityMutation),\n      ("core-word-id/playback-highlights-exact-canonical-word-id",\n        TestPlaybackHighlightsExactCanonicalWordId),\n      ("core-word-id/off-window-playback-requests-canonical-word",\n        TestOffWindowPlaybackRequestsCanonicalWord),\n      ("core-word-id/playback-host-retains-core-lookup-session",\n        TestPlaybackHostRetainsCoreLookupSession)\n'''
if text.count(old) != 1:
  raise RuntimeError('Expected one issue #75 test-list insertion point.')
text = text.replace(old, new)

marker = '''  private static AIConversationProjection ProjectClaude(string text)\n'''
methods = r'''  /// <summary>
  /// A Core-backed playback notification must highlight the exact canonical
  /// word owner even when another visible word has identical text. Text/node
  /// matching is not a valid identity fallback when WordId is present.
  /// </summary>
  private static void TestPlaybackHighlightsExactCanonicalWordId()
  {
    using BrowserFixture fixture = BrowserFixture.Create();
    ExecuteBrowserScript(
      fixture.WebView,
      """
(() => {
  replaceTranscript(
    '<span class="record-anchor" data-jsonl-record="1"></span>' +
      '<p><span id="word-7001">same</span> ' +
      '<span id="word-7002">same</span></p>',
    false,
    []);
})()
""");

    fixture.WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
    {
      type = "playback",
      sequence = 1,
      state = "speaking",
      fragmentText = "same",
      wordIndex = 0,
      wordText = "same",
      nodeId = 999,
      wordId = 7002,
      characterPosition = 0,
      characterCount = 4,
      boundaryTimestamp = 1,
      follow = false
    }));
    PumpFor(250);

    JsonElement result = ExecuteBrowserJsonProbe(
      fixture.WebView,
      """
(() => JSON.stringify({
  first:document.getElementById('word-7001')?.classList.contains('active') === true,
  second:document.getElementById('word-7002')?.classList.contains('active') === true
}))()
""");
    Require(!result.GetProperty("first").GetBoolean(),
      "Duplicate visible text captured playback from the requested Core word ID.");
    Require(result.GetProperty("second").GetBoolean(),
      "Playback did not highlight the exact requested Core word owner.");
  }

  /// <summary>
  /// If a Core playback word is outside the materialized virtual window, the
  /// browser must request that exact numeric word from the host rather than
  /// searching visible text or reconstructing a record/node coordinate.
  /// </summary>
  private static void TestOffWindowPlaybackRequestsCanonicalWord()
  {
    using BrowserFixture fixture = BrowserFixture.Create();
    var request = new TaskCompletionSource<JsonElement>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.WebView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
    {
      using JsonDocument document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
      JsonElement root = document.RootElement;
      if (root.TryGetProperty("type", out JsonElement type) &&
          string.Equals(type.GetString(), "window-for-word", StringComparison.Ordinal))
      {
        request.TrySetResult(root.Clone());
      }
    };

    ExecuteBrowserScript(
      fixture.WebView,
      """
(() => {
  replaceTranscript(
    '<span class="record-anchor" data-jsonl-record="1"></span>' +
      '<p><span id="word-8001">visible</span></p>',
    false,
    []);
})()
""");
    fixture.WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
    {
      type = "playback",
      sequence = 1,
      state = "speaking",
      fragmentText = "duplicate",
      wordIndex = 0,
      wordText = "duplicate",
      nodeId = 123,
      wordId = 9001,
      characterPosition = 0,
      characterCount = 9,
      boundaryTimestamp = 1,
      follow = true
    }));
    PumpFor(300);

    Require(request.Task.IsCompleted,
      "Off-window Core playback did not request host materialization by word ID.");
    JsonElement message = request.Task.GetAwaiter().GetResult();
    Require(message.GetProperty("wordId").GetInt64() == 9001,
      "Off-window playback requested a different canonical word ID.");
    Require(!message.TryGetProperty("nodeId", out _) &&
        !message.TryGetProperty("nodeWordIndex", out _),
      "Off-window playback still crosses the host boundary with legacy identity.");
  }

  /// <summary>
  /// The display projection must retain a Core session for authoritative
  /// word-to-unit lookup, and TranscriptView must expose one word-ID window
  /// materialization path. A consumer word-to-unit map is not acceptable.
  /// </summary>
  private static void TestPlaybackHostRetainsCoreLookupSession()
  {
    RequireProperty(typeof(TranscriptPresentationDomResult), "CoreSessionId");

    MethodInfo? locate = typeof(TranscriptPresentationDomFormatter).GetMethod(
      "LocateRetainedWord",
      BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
      binder: null,
      types: new[] { typeof(string), typeof(long) },
      modifiers: null);
    Require(locate is not null,
      "Transcript display projection exposes no retained Core word lookup.");

    MethodInfo? render = typeof(TranscriptView).GetMethod(
      "RenderWindowForWordAsync",
      BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    Require(render is not null,
      "TranscriptView has no Core word-ID window-materialization path.");
  }

'''
if text.count(marker) != 1:
  raise RuntimeError('Expected one issue #75 method insertion point.')
text = text.replace(marker, methods + marker)

helper_marker = '''  private static void Require(bool condition, string message)\n'''
helpers = r'''  private static JsonElement ExecuteBrowserJsonProbe(
    Microsoft.Web.WebView2.WinForms.WebView2 webView,
    string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #75 browser JSON probe");
    string encoded = JsonSerializer.Deserialize<string>(task.Result) ??
      throw new InvalidOperationException("Browser probe returned no JSON string.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static void ExecuteBrowserScript(
    Microsoft.Web.WebView2.WinForms.WebView2 webView,
    string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #75 browser script");
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

  private sealed class BrowserFixture : IDisposable
  {
    private BrowserFixture(
      Form host,
      Microsoft.Web.WebView2.WinForms.WebView2 webView)
    {
      Host = host;
      WebView = webView;
    }

    public Form Host { get; }
    public Microsoft.Web.WebView2.WinForms.WebView2 WebView { get; }

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
      var webView = new Microsoft.Web.WebView2.WinForms.WebView2
      {
        Dock = DockStyle.Fill
      };
      host.Controls.Add(webView);
      host.Show();
      Application.DoEvents();
      PumpUntilCompleted(
        webView.EnsureCoreWebView2Async(),
        "issue #75 WebView initialization");

      string shell = ShellHtml();
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
      PumpUntilCompleted(navigated.Task, "issue #75 transcript shell navigation");
      return new BrowserFixture(host, webView);
    }

    public void Dispose()
    {
      WebView.Dispose();
      Host.Dispose();
    }
  }

'''
if text.count(helper_marker) != 1:
  raise RuntimeError('Expected one issue #75 helper insertion point.')
text = text.replace(helper_marker, helpers + helper_marker)

PATH.write_text(text, encoding='utf-8', newline='\n')
