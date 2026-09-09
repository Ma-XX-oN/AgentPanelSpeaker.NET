using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Browser-output acceptance regressions with independently-authored semantic
/// expectations. Production code is used only to produce the actual browser DOM;
/// it never supplies the expected result.
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
      ("output-oracle/codex-user-context-production-browser-dom",
        TestCodexUserContextProductionBrowserDom),
      ("output-oracle/claude-grouped-thought-production-browser-dom",
        TestClaudeGroupedThoughtProductionBrowserDom),
      ("output-oracle/user-context-containment-mutation-is-rejected",
        TestUserContextContainmentMutationIsRejected)
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
  /// Sends a fixed Codex source record through the real TranscriptView and
  /// compares the final browser DOM to fixed semantic expectations.
  /// </summary>
  private static void TestCodexUserContextProductionBrowserDom()
  {
    string[] records =
    {
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T15:17:11.000Z",
        payload = new
        {
          type = "user_message",
          message =
            "# Context from my IDE setup:\n\n" +
            "## Active file: sessions/example.jsonl\n\n" +
            "## Active selection of the file:\n" +
            "selected line\n\n" +
            "## Open tabs:\n" +
            "- example.jsonl: sessions/example.jsonl\n\n" +
            "## My request for Codex:\n" +
            Prompt
        }
      })
    };

    JsonElement actual = RenderProductionAndProbe(
      AgentSource.Codex,
      records,
      UserContextProbeScript());
    AssertUserContextOracle(actual);
  }

  /// <summary>
  /// Sends fixed Claude records through the real TranscriptView and compares
  /// final browser containment to fixed semantic expectations.
  /// </summary>
  private static void TestClaudeGroupedThoughtProductionBrowserDom()
  {
    string[] records =
    {
      "{\"type\":\"user\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:01.000Z\",\"uuid\":\"thought-user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"Please reason through this.\"}]}}",
      "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:02.000Z\",\"uuid\":\"thought-one\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"First thought has unique alpha words.\"}]}}",
      "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:03.000Z\",\"uuid\":\"thought-two\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"Second thought has unique beta words.\"}]}}",
      "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:04.000Z\",\"uuid\":\"thought-three\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"Third thought has unique gamma words.\"}]}}",
      "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:05.000Z\",\"uuid\":\"thought-final\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Final Claude answer after thoughts.\"}]}}"
    };

    JsonElement actual = RenderProductionAndProbe(
      AgentSource.Claude,
      records,
      GroupedThoughtProbeScript());

    RequireBoolean(actual, "detailsExists", true);
    RequireString(actual, "summary", "Having 3 thoughts");
    RequireBoolean(actual, "firstThoughtInside", true);
    RequireBoolean(actual, "secondThoughtInside", true);
    RequireBoolean(actual, "thirdThoughtInside", true);
    RequireBoolean(actual, "finalInside", false);
    RequireBoolean(actual, "finalVisible", true);
    RequireIntegerAtLeast(actual, "anchorCountInside", 3);
  }

  /// <summary>
  /// Applies an independently-authored structural mutation in which User
  /// Context content escapes its details disclosure, and proves the semantic
  /// oracle rejects the resulting browser output.
  /// </summary>
  private static void TestUserContextContainmentMutationIsRejected()
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
      </details>
      <h2>Active selection of the file:</h2><p>selected line</p>
      <h2>Open tabs:</h2><ul><li>example.jsonl: sessions/example.jsonl</li></ul>
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
      "Fixed bounded-window reproducer did not expose its context identity.");
    JsonElement actual = ExecuteWindowAndProbe(
      document.CreateWindow(contextIndex),
      UserContextProbeScript());

    try
    {
      AssertUserContextOracle(actual);
    }
    catch (InvalidOperationException)
    {
      return;
    }

    throw new InvalidOperationException(
      "Independent User Context oracle accepted deliberately broken " +
      "context containment.");
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
    virtualSectionPresent: !!root?.querySelector('section.virtual-record')
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
    virtualSectionPresent: !!root?.querySelector('section.virtual-record'),
    anchorCountInside: details
      ? details.querySelectorAll('span.record-anchor').length
      : 0
  });
})()
""";
  }

  /// <summary>
  /// Renders a fixed JSONL fixture through the real TranscriptView and returns
  /// only browser facts. Expected facts are supplied separately by the tests.
  /// </summary>
  private static JsonElement RenderProductionAndProbe(
    AgentSource source,
    IReadOnlyList<string> records,
    string probeScript)
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-output-oracle-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string path = Path.Combine(root, "fixture.jsonl");
      File.WriteAllLines(path, records);

      using var host = new Form
      {
        Width = 900,
        Height = 700,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-32000, -32000)
      };
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => ReadField<bool>(view, "_initialized"),
        "TranscriptView WebView initialization");

      view.SelectSession(path, source, "Independent output oracle fixture");
      PumpUntil(
        () =>
        {
          bool refreshInProgress = ReadField<bool>(view, "_refreshInProgress");
          Label loading = ReadField<Label>(view, "_loadingLabel");
          Label failure = ReadField<Label>(view, "_failureLabel");
          if (failure.Visible)
          {
            throw new InvalidOperationException(
              "TranscriptView reported render failure: " + failure.Text);
          }
          return !refreshInProgress && webView.Visible && !loading.Visible;
        },
        "TranscriptView fixture render");

      Task<string> probe = webView.CoreWebView2.ExecuteScriptAsync(probeScript);
      PumpUntilCompleted(probe, "production browser-output probe");
      string encoded = JsonSerializer.Deserialize<string>(probe.Result) ??
        throw new InvalidOperationException("Browser probe returned no JSON string.");
      using JsonDocument document = JsonDocument.Parse(encoded);
      return document.RootElement.Clone();
    }
    finally
    {
      try
      {
        Directory.Delete(root, recursive: true);
      }
      catch (IOException)
      {
      }
    }
  }

  /// <summary>
  /// Installs a deliberately bounded virtual window in the real transcript
  /// browser shell for the negative issue #37 reproducer.
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
    host.Show();
    _ = host.Handle;
    _ = webView.Handle;

    Task ensure = webView.EnsureCoreWebView2Async();
    PumpUntilCompleted(ensure, "negative-reproducer WebView2 initialization");

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
    PumpUntilCompleted(navigated.Task, "negative-reproducer shell navigation");

    string replaceScript = BuildWindowInstallScript(window);
    Task<string> replace = webView.CoreWebView2.ExecuteScriptAsync(replaceScript);
    PumpUntilCompleted(replace, "negative bounded-window install");

    Task<string> probe = webView.CoreWebView2.ExecuteScriptAsync(probeScript);
    PumpUntilCompleted(probe, "negative-reproducer browser probe");
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
      window.TopSpacerHeight.ToString(
        System.Globalization.CultureInfo.InvariantCulture) + "," +
      window.BottomSpacerHeight.ToString(
        System.Globalization.CultureInfo.InvariantCulture) +
      ",null,null,null,null,null,null,[],\"\");";
  }

  private static T ReadField<T>(object target, string name)
  {
    FieldInfo? field = target.GetType().GetField(
      name,
      BindingFlags.NonPublic | BindingFlags.Instance);
    if (field?.GetValue(target) is not T value)
    {
      throw new InvalidOperationException(
        $"Could not read production field {name} as {typeof(T).Name}.");
    }
    return value;
  }

  private static void PumpUntil(
    Func<bool> predicate,
    string description,
    int timeoutMilliseconds = 30000)
  {
    var timer = Stopwatch.StartNew();
    while (!predicate())
    {
      if (timer.ElapsedMilliseconds >= timeoutMilliseconds)
      {
        throw new TimeoutException(
          $"Timed out waiting for {description} after {timeoutMilliseconds} ms.");
      }
      Application.DoEvents();
      Thread.Sleep(10);
    }
  }

  private static void PumpUntilCompleted(Task task, string description)
  {
    PumpUntil(() => task.IsCompleted, description);
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
