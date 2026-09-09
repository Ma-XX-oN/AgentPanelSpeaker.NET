using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Browser-output regressions whose input and expected semantic result are
/// independently authored. Production virtualization/payload/browser code is
/// used only to produce the actual value being tested.
/// </summary>
internal static class Issue44IndependentOutputOracleRegressionTestRunner
{
  private const string ContextSummary = "# Context from my IDE setup:";
  private const string Prompt = "Actual user prompt.";

  /// <summary>
  /// Runs the independent browser-output oracle suite.
  /// </summary>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("output-oracle/codex-user-context-window-browser-dom",
        TestUserContextWindowBrowserDom),
      ("output-oracle/claude-grouped-thought-window-browser-dom",
        TestGroupedThoughtWindowBrowserDom),
      ("output-oracle/rejects-escaped-context-mutation",
        TestUserContextOracleRejectsEscapedMutation)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #44 independent-output-oracle suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #44 independent-output " +
        "oracle tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #44 independent-output " +
        "oracle tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Sends a fixed, valid User Context document through the exact bounded-window
  /// payload and real browser parser. The expected containment is specified by
  /// this test and is never derived from production output.
  /// </summary>
  private static void TestUserContextWindowBrowserDom()
  {
    const string sourceHtml = """
<section class="transcript-turn">
  <span class="record-anchor" data-jsonl-record="10" data-source-id="previous"></span>
  <p>Previous visible content.</p>
  <blockquote class="transcript-turn-body">
    <blockquote class="user-context">
      <details class="user-context-details">
        <summary># Context from my IDE setup:</summary>
        <span class="record-anchor" data-jsonl-record="11" data-source-id="context"></span>
        <h2>Active file:</h2><p>sessions/example.jsonl</p>
        <h2>Active selection of the file:</h2><p>selected line</p>
        <h2>Open tabs:</h2><ul><li>example.jsonl: sessions/example.jsonl</li></ul>
      </details>
    </blockquote>
    <div class="presentation-content">
      <span class="record-anchor" data-jsonl-record="12" data-source-id="prompt"></span>
      <p>Actual user prompt.</p>
    </div>
  </blockquote>
</section>
""";

    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(sourceHtml);
    Require(document.TryGetIndex(11, "context", out int contextIndex),
      "Fixed User Context input did not expose its context identity.");
    TranscriptWindow window = document.CreateWindow(contextIndex);
    JsonElement actual = ExecuteWindowAndProbe(window, UserContextProbeScript());
    AssertUserContextOracle(actual);
  }

  /// <summary>
  /// Sends a fixed grouped-thought disclosure through the same production
  /// window/browser path and checks independently-authored containment facts.
  /// </summary>
  private static void TestGroupedThoughtWindowBrowserDom()
  {
    const string sourceHtml = """
<section class="transcript-turn">
  <span class="record-anchor" data-jsonl-record="20" data-source-id="previous"></span>
  <p>Previous visible content.</p>
  <details class="reasoning">
    <summary>Having 3 thoughts</summary>
    <div class="reasoning-body">
      <span class="record-anchor" data-jsonl-record="21" data-source-id="thought-one"></span>
      <p>First thought has unique alpha words.</p>
      <span class="record-anchor" data-jsonl-record="22" data-source-id="thought-two"></span>
      <p>Second thought has unique beta words.</p>
      <span class="record-anchor" data-jsonl-record="23" data-source-id="thought-three"></span>
      <p>Third thought has unique gamma words.</p>
    </div>
  </details>
  <span class="record-anchor" data-jsonl-record="24" data-source-id="final"></span>
  <p>Final Claude answer after thoughts.</p>
</section>
""";

    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(sourceHtml);
    Require(document.TryGetIndex(22, "thought-two", out int thoughtIndex),
      "Fixed grouped-thought input did not expose its middle thought identity.");
    TranscriptWindow window = document.CreateWindow(thoughtIndex);
    JsonElement actual = ExecuteWindowAndProbe(window, GroupedThoughtProbeScript());

    RequireBoolean(actual, "detailsExists", true);
    RequireString(actual, "summary", "Having 3 thoughts");
    RequireBoolean(actual, "firstThoughtInside", true);
    RequireBoolean(actual, "secondThoughtInside", true);
    RequireBoolean(actual, "thirdThoughtInside", true);
    RequireBoolean(actual, "finalInside", false);
    RequireBoolean(actual, "finalVisible", true);
    RequireBoolean(actual, "virtualSectionInsideDetails", false);
    RequireIntegerAtLeast(actual, "anchorCountInside", 3);
  }

  /// <summary>
  /// Proves the fixed oracle rejects the exact semantic mutation observed on the
  /// user's machine instead of merely accepting whatever production emitted.
  /// </summary>
  private static void TestUserContextOracleRejectsEscapedMutation()
  {
    using JsonDocument document = JsonDocument.Parse("""
{
  "detailsExists": true,
  "summary": "# Context from my IDE setup:",
  "activeFileInside": false,
  "selectionInside": false,
  "tabsInside": false,
  "tabPathInside": false,
  "promptInside": false,
  "promptVisible": true,
  "promptAfterDetails": true,
  "virtualSectionInsideDetails": false
}
""");

    try
    {
      AssertUserContextOracle(document.RootElement);
    }
    catch (InvalidOperationException)
    {
      return;
    }

    throw new InvalidOperationException(
      "Independent User Context oracle accepted escaped disclosure content.");
  }

  private static void AssertUserContextOracle(JsonElement actual)
  {
    RequireBoolean(actual, "detailsExists", true);
    RequireString(actual, "summary", ContextSummary);
    RequireBoolean(actual, "activeFileInside", true);
    RequireBoolean(actual, "selectionInside", true);
    RequireBoolean(actual, "tabsInside", true);
    RequireBoolean(actual, "tabPathInside", true);
    RequireBoolean(actual, "promptInside", false);
    RequireBoolean(actual, "promptVisible", true);
    RequireBoolean(actual, "promptAfterDetails", true);
    RequireBoolean(actual, "virtualSectionInsideDetails", false);
  }

  private static string UserContextProbeScript()
  {
    string prompt = JsonSerializer.Serialize(Prompt);
    return $$"""
(() => {
  const root = document.querySelector('#transcript');
  const details = root?.querySelector('details.user-context-details') ?? null;
  const promptElement = root
    ? [...root.querySelectorAll('.presentation-content')]
        .find(element => element.textContent.includes({{prompt}})) ?? null
    : null;
  const text = details?.textContent ?? '';
  return JSON.stringify({
    detailsExists: !!details,
    summary: details?.querySelector(':scope > summary')?.textContent?.trim() ?? '',
    activeFileInside: text.includes('Active file:'),
    selectionInside: text.includes('Active selection of the file:'),
    tabsInside: text.includes('Open tabs:'),
    tabPathInside: text.includes('sessions/example.jsonl'),
    promptInside: text.includes({{prompt}}),
    promptVisible: !!root && root.textContent.includes({{prompt}}),
    promptAfterDetails: !!details && !!promptElement &&
      !!(details.compareDocumentPosition(promptElement) & Node.DOCUMENT_POSITION_FOLLOWING),
    virtualSectionInsideDetails: !!details &&
      !!details.querySelector('section.virtual-record')
  });
})()
""";
  }

  private static string GroupedThoughtProbeScript()
  {
    return """
(() => {
  const root = document.querySelector('#transcript');
  const details = root?.querySelector('details.reasoning') ?? null;
  const text = details?.textContent ?? '';
  const finalText = 'Final Claude answer after thoughts.';
  return JSON.stringify({
    detailsExists: !!details,
    summary: details?.querySelector(':scope > summary')?.textContent?.trim() ?? '',
    firstThoughtInside: text.includes('First thought has unique alpha words.'),
    secondThoughtInside: text.includes('Second thought has unique beta words.'),
    thirdThoughtInside: text.includes('Third thought has unique gamma words.'),
    finalInside: text.includes(finalText),
    finalVisible: !!root && root.textContent.includes(finalText),
    virtualSectionInsideDetails: !!details &&
      !!details.querySelector('section.virtual-record'),
    anchorCountInside: details
      ? details.querySelectorAll('span.record-anchor').length
      : 0
  });
})()
""";
  }

  /// <summary>
  /// Uses the exact production bounded-window payload builder and actual WebView2
  /// shell. The test supplies the expected semantic result, never node scopes,
  /// browser repair results, or a production-derived expected document.
  /// </summary>
  private static JsonElement ExecuteWindowAndProbe(
    TranscriptWindow window,
    string probeScript)
  {
    string replaceScript = BuildProductionWindowScript(window);

    using var host = new Form
    {
      Width = 800,
      Height = 600,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-32000, -32000)
    };
    using var webView = new WebView2 { Dock = DockStyle.Fill };
    host.Controls.Add(webView);
    _ = host.Handle;
    _ = webView.Handle;

    Task ensure = webView.EnsureCoreWebView2Async();
    PumpUntilCompleted(ensure, "independent-oracle WebView2 initialization");

    MethodInfo? shellMethod = typeof(TranscriptView).GetMethod(
      "BuildShellHtml",
      BindingFlags.NonPublic | BindingFlags.Static);
    Require(shellMethod is not null,
      "TranscriptView.BuildShellHtml() could not be located.");
    string shell = shellMethod!.Invoke(null, null) as string ?? string.Empty;
    Require(shell.Length != 0,
      "TranscriptView.BuildShellHtml() returned no browser shell.");

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
    PumpUntilCompleted(navigated.Task, "independent-oracle shell navigation");

    Task<string> replace = webView.CoreWebView2.ExecuteScriptAsync(replaceScript);
    PumpUntilCompleted(replace, "production bounded-window payload");

    Task<string> probe = webView.CoreWebView2.ExecuteScriptAsync(probeScript);
    PumpUntilCompleted(probe, "independent browser-output probe");
    string encoded = JsonSerializer.Deserialize<string>(probe.Result) ??
      throw new InvalidOperationException("Browser output probe returned no JSON.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static string BuildProductionWindowScript(TranscriptWindow window)
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
    _ = host.Handle;
    _ = productionView.Handle;

    FieldInfo? webViewField = typeof(TranscriptView).GetField(
      "_webView",
      BindingFlags.NonPublic | BindingFlags.Instance);
    MethodInfo? buildReplaceWindow = typeof(TranscriptView).GetMethod(
      "BuildReplaceWindowScript",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Require(webViewField is not null && buildReplaceWindow is not null,
      "Production bounded-window payload members could not be located.");

    var internalWebView = (WebView2?)webViewField!.GetValue(productionView);
    Require(internalWebView is not null,
      "Production TranscriptView WebView could not be located.");
    Task ensure = internalWebView!.EnsureCoreWebView2Async();
    PumpUntilCompleted(ensure, "production window-payload WebView initialization");

    object?[] arguments =
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
    };
    string script = buildReplaceWindow!.Invoke(productionView, arguments)
      as string ?? string.Empty;
    Require(script.Length != 0,
      "Production BuildReplaceWindowScript returned no browser payload.");
    return script;
  }

  private static void PumpUntilCompleted(
    Task task,
    string description,
    int timeoutMilliseconds = 30000)
  {
    var timer = Stopwatch.StartNew();
    while (!task.IsCompleted)
    {
      if (timer.ElapsedMilliseconds >= timeoutMilliseconds)
      {
        throw new TimeoutException(
          $"Timed out waiting for {description} after {timeoutMilliseconds} ms.");
      }
      Application.DoEvents();
      Thread.Sleep(10);
    }
    task.GetAwaiter().GetResult();
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }

  private static void RequireBoolean(
    JsonElement actual,
    string property,
    bool expected)
  {
    if (!actual.TryGetProperty(property, out JsonElement value) ||
        value.ValueKind is not JsonValueKind.True and not JsonValueKind.False ||
        value.GetBoolean() != expected)
    {
      throw new InvalidOperationException(
        $"Output oracle mismatch for {property}: expected {expected}, actual " +
        $"{(actual.TryGetProperty(property, out JsonElement found) ? found.GetRawText() : "<missing>")}.");
    }
  }

  private static void RequireString(
    JsonElement actual,
    string property,
    string expected)
  {
    string? value = actual.TryGetProperty(property, out JsonElement element)
      ? element.GetString()
      : null;
    if (!string.Equals(value, expected, StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
        $"Output oracle mismatch for {property}: expected {expected}, actual " +
        (value ?? "<missing>") + ".");
    }
  }

  private static void RequireIntegerAtLeast(
    JsonElement actual,
    string property,
    int minimum)
  {
    if (!actual.TryGetProperty(property, out JsonElement element) ||
        !element.TryGetInt32(out int value) ||
        value < minimum)
    {
      throw new InvalidOperationException(
        $"Output oracle mismatch for {property}: expected at least {minimum}, actual " +
        $"{(actual.TryGetProperty(property, out JsonElement found) ? found.GetRawText() : "<missing>")}.");
    }
  }
}
