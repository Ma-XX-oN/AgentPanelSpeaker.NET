using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Browser-output regressions whose source documents and expected semantic
/// results are fixed independently of production rendering and virtualization.
/// </summary>
internal static class Issue44IndependentOutputOracleRegressionTestRunner
{
  private const string ContextSummary = "# Context from my IDE setup:";
  private const string Prompt = "Actual user prompt.";

  /// <summary>Runs the independent browser-output oracle suite.</summary>
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
    JsonElement actual = ExecuteWindowAndProbe(
      document.CreateWindow(contextIndex),
      UserContextProbeScript());
    AssertUserContextOracle(actual);
  }

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
    JsonElement actual = ExecuteWindowAndProbe(
      document.CreateWindow(thoughtIndex),
      GroupedThoughtProbeScript());

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
  /// Installs the actual virtual-window HTML in the real transcript browser
  /// shell. The JavaScript call is transport plumbing only; expected output is
  /// specified separately by the fixed oracle above.
  /// </summary>
  private static JsonElement ExecuteWindowAndProbe(
    TranscriptWindow window,
    string probeScript)
  {
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

    string replaceScript = BuildWindowInstallScript(window);
    Task<string> replace = webView.CoreWebView2.ExecuteScriptAsync(replaceScript);
    PumpUntilCompleted(replace, "production virtual-window install");

    Task<string> probe = webView.CoreWebView2.ExecuteScriptAsync(probeScript);
    PumpUntilCompleted(probe, "independent browser-output probe");
    string encoded = JsonSerializer.Deserialize<string>(probe.Result) ??
      throw new InvalidOperationException("Browser output probe returned no JSON.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static string BuildWindowInstallScript(TranscriptWindow window)
  {
    return "replaceTranscriptWindow(" +
      JsonSerializer.Serialize(window.Html) + ",false,[],[]," +
      window.StartIndex + "," +
      window.EndIndex + "," +
      window.TopSpacerHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
      window.BottomSpacerHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) +
      ",null,null,null,null,null,null,[],\"\");";
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
