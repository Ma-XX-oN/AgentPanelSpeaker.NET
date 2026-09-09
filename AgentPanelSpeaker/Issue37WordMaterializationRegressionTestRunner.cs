using Microsoft.Web.WebView2.WinForms;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Independent browser acceptance tests for issue #37 word materialization.
/// The expected browser facts are fixed by the test; production code is used
/// only to produce the actual window payload and browser state.
/// </summary>
internal static class Issue37WordMaterializationRegressionTestRunner
{
  private const int BulkRecordNumber = 1;
  private const string BulkSourceId = "bulk";
  private const int SpeechRecordNumber = 2;
  private const string SpeechSourceId = "speech";
  private const long SpeechNodeId = 42;
  private const string SpeechFragment = "Speak me now.";
  private const string FindNeedle = "UNIQUEFILLERNEEDLE";
  private const int FillerRepeats = 1000;

  /// <summary>
  /// Runs the issue #37 selective-word-materialization acceptance suite.
  /// </summary>
  /// <returns>Zero when every independent browser oracle passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("virtual-window/unvoiced-content-is-not-eagerly-wrapped",
        TestUnvoicedContentIsNotEagerlyWrapped),
      ("virtual-window/unvoiced-find-result-materializes-on-demand",
        TestUnvoicedFindResultMaterializesOnDemand)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #37 word-materialization acceptance suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #37 word-materialization tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #37 word-materialization tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// A large collapsed, unvoiced record must remain present as ordinary DOM
  /// text, while only the small speech record pays the eager word-span cost.
  /// Playback must still resolve and highlight the speech fragment.
  /// </summary>
  private static void TestUnvoicedContentIsNotEagerlyWrapped()
  {
    using BrowserFixture fixture = CreateBrowserFixture();
    JsonElement actual = ExecuteJsonProbe(
      fixture.WebView,
      $$"""
(() => {
  setPlayback('speaking', {{JsonSerializer.Serialize(SpeechFragment)}}, 0,
    'Speak', {{SpeechNodeId}}, false);
  return JSON.stringify({
    totalWordCount: document.querySelectorAll('.word').length,
    bulkWordCount: document.querySelectorAll(
      '.word[data-record-number="{{BulkRecordNumber}}"]' +
      '[data-source-id="{{BulkSourceId}}"]').length,
    speechWordCount: document.querySelectorAll(
      '.word[data-record-number="{{SpeechRecordNumber}}"]' +
      '[data-source-id="{{SpeechSourceId}}"]').length,
    fillerPresent: transcript.textContent.includes('{{FindNeedle}}'),
    activeText: [...transcript.querySelectorAll('.word.active')]
      .map(word => word.textContent).join('')
  });
})()
""");

    Require(actual.GetProperty("fillerPresent").GetBoolean(),
      "Large unvoiced record disappeared from the browser DOM.");
    Require(actual.GetProperty("bulkWordCount").GetInt32() == 0,
      "Large unvoiced record was eagerly expanded into word spans.");
    Require(actual.GetProperty("speechWordCount").GetInt32() > 0,
      "Speech record was not eagerly mapped for playback.");
    Require(actual.GetProperty("totalWordCount").GetInt32() <= 16,
      "Initial mapping created more word spans than the fixed speech fixture requires.");
    Require(string.Equals(
        actual.GetProperty("activeText").GetString(),
        "Speak",
        StringComparison.Ordinal),
      "Selective eager mapping broke speech highlighting.");
  }

  /// <summary>
  /// Full-text Find still owns the complete C# corpus. Navigating to a match in
  /// an unvoiced record must lazily materialize that record's stable word spans
  /// and highlight the exact match without remapping the rest of the window.
  /// </summary>
  private static void TestUnvoicedFindResultMaterializesOnDemand()
  {
    using BrowserFixture fixture = CreateBrowserFixture();
    IReadOnlyList<TranscriptSearchMatch> matches = fixture.SearchIndex.SearchAsync(
      new TranscriptSearchRequest(
        1,
        FindNeedle,
        CaseSensitive: true,
        WholeWord: true,
        Regex: false,
        VoicedOnly: false),
      CancellationToken.None).GetAwaiter().GetResult();
    Require(matches.Count == 1,
      $"Fixed Find fixture produced {matches.Count} matches instead of one.");

    string matchesJson = JsonSerializer.Serialize(matches);
    JsonElement actual = ExecuteJsonProbe(
      fixture.WebView,
      $$"""
(() => {
  findMatches = ({{matchesJson}}).map(normalizeFindMatch);
  findSearchPending = false;
  currentFindMatch = 0;
  void showFindMatch(0, 'issue37-lazy-find');
  return JSON.stringify({
    bulkWordCount: document.querySelectorAll(
      '.word[data-record-number="{{BulkRecordNumber}}"]' +
      '[data-source-id="{{BulkSourceId}}"]').length,
    highlightedText: [...transcript.querySelectorAll('.word.find-current')]
      .map(word => word.textContent).join(''),
    currentCountText: findCount.textContent
  });
})()
""");

    Require(actual.GetProperty("bulkWordCount").GetInt32() > 0,
      "Find did not lazily materialize the unvoiced target record.");
    Require(string.Equals(
        actual.GetProperty("highlightedText").GetString(),
        FindNeedle,
        StringComparison.Ordinal),
      "Find did not highlight the exact lazily materialized match.");
    Require(string.Equals(
        actual.GetProperty("currentCountText").GetString(),
        "1 of 1",
        StringComparison.Ordinal),
      "Find navigation did not complete after lazy materialization.");
  }

  private static BrowserFixture CreateBrowserFixture()
  {
    string html = BuildFixtureHtml();
    TranscriptNodeIdentity[] identities =
    {
      new(
        SpeechNodeId,
        SpeechRecordNumber,
        SpeechSourceId,
        new[] { SpeechFragment })
    };
    TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(
      html,
      identities,
      CancellationToken.None);
    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);
    Require(document.TryGetIndex(
        SpeechRecordNumber,
        SpeechSourceId,
        out int speechIndex),
      "Fixed fixture did not expose the speech record identity.");
    TranscriptWindow window = document.CreateWindow(speechIndex);
    Require(window.Records.Any(record => record.Identities.Any(identity =>
        identity.RecordNumber == BulkRecordNumber &&
        string.Equals(identity.SourceId, BulkSourceId, StringComparison.Ordinal))),
      "Fixed virtual window did not include the bulk record.");

    string replaceScript = BuildProductionReplaceWindowScript(
      window,
      identities,
      searchIndex);
    return BrowserFixture.Create(replaceScript, searchIndex);
  }

  private static string BuildFixtureHtml()
  {
    var filler = new StringBuilder();
    filler.Append(FindNeedle);
    for (int index = 0; index < FillerRepeats; ++index)
    {
      filler.Append(" irrelevant-token-");
      filler.Append(index);
    }

    return $$"""
<section class="transcript-turn">
  <span class="record-anchor" data-jsonl-record="{{BulkRecordNumber}}" data-source-id="{{BulkSourceId}}"></span>
  <details>
    <summary>Large collapsed tool output</summary>
    <p>{{filler}}</p>
  </details>
  <span class="record-anchor" data-jsonl-record="{{SpeechRecordNumber}}" data-source-id="{{SpeechSourceId}}"></span>
  <p>{{SpeechFragment}}</p>
</section>
""";
  }

  private static string BuildProductionReplaceWindowScript(
    TranscriptWindow window,
    IReadOnlyList<TranscriptNodeIdentity> identities,
    TranscriptSearchIndex searchIndex)
  {
    using var host = new Form
    {
      Width = 320,
      Height = 240,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
    using var productionView = new TranscriptView { Dock = DockStyle.Fill };
    host.Controls.Add(productionView);
    host.Show();
    Application.DoEvents();
    FieldInfo webViewField = typeof(TranscriptView).GetField(
      "_webView",
      BindingFlags.NonPublic | BindingFlags.Instance) ??
      throw new InvalidOperationException("TranscriptView._webView was not found.");
    var productionWebView = (WebView2?)webViewField.GetValue(productionView) ??
      throw new InvalidOperationException("TranscriptView WebView was unavailable.");
    PumpUntilCompleted(
      productionWebView.EnsureCoreWebView2Async(),
      "production payload-builder WebView initialization");

    FieldInfo identitiesField = typeof(TranscriptView).GetField(
      "_identities",
      BindingFlags.NonPublic | BindingFlags.Instance) ??
      throw new InvalidOperationException("TranscriptView._identities was not found.");
    FieldInfo searchIndexField = typeof(TranscriptView).GetField(
      "_searchIndex",
      BindingFlags.NonPublic | BindingFlags.Instance) ??
      throw new InvalidOperationException("TranscriptView._searchIndex was not found.");
    MethodInfo build = typeof(TranscriptView).GetMethod(
      "BuildReplaceWindowScript",
      BindingFlags.NonPublic | BindingFlags.Instance) ??
      throw new InvalidOperationException(
        "TranscriptView.BuildReplaceWindowScript() was not found.");

    identitiesField.SetValue(productionView, identities);
    searchIndexField.SetValue(productionView, searchIndex);
    return build.Invoke(productionView, new object?[]
    {
      window,
      false,
      null,
      null,
      null,
      null,
      null,
      null,
      null
    }) as string ?? throw new InvalidOperationException(
      "Production window payload builder returned no script.");
  }

  private static JsonElement ExecuteJsonProbe(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #37 browser probe");
    string encoded = JsonSerializer.Deserialize<string>(task.Result) ??
      throw new InvalidOperationException("Browser probe returned no JSON string.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
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
    private BrowserFixture(
      Form host,
      WebView2 webView,
      TranscriptSearchIndex searchIndex)
    {
      Host = host;
      WebView = webView;
      SearchIndex = searchIndex;
    }

    public Form Host { get; }
    public WebView2 WebView { get; }
    public TranscriptSearchIndex SearchIndex { get; }

    public static BrowserFixture Create(
      string replaceScript,
      TranscriptSearchIndex searchIndex)
    {
      var host = new Form
      {
        Width = 900,
        Height = 700,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      var webView = new WebView2 { Dock = DockStyle.Fill };
      host.Controls.Add(webView);
      host.Show();
      Application.DoEvents();

      Task ensure = webView.EnsureCoreWebView2Async();
      PumpUntilCompleted(ensure, "issue #37 WebView initialization");
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
      PumpUntilCompleted(navigated.Task, "issue #37 transcript shell navigation");

      Task<string> replace = webView.CoreWebView2.ExecuteScriptAsync(replaceScript);
      PumpUntilCompleted(replace, "issue #37 production window installation");
      return new BrowserFixture(host, webView, searchIndex);
    }

    public void Dispose()
    {
      WebView.Dispose();
      Host.Dispose();
    }
  }
}
