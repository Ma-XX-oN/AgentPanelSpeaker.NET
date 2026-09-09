using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// End-to-end transcript output regressions whose expected results are authored
/// independently of the rendering, virtualization, payload, and WebView code
/// under test.
/// </summary>
internal static class Issue44IndependentOutputOracleRegressionTestRunner
{
  private const string UserContextPrompt = "Actual user prompt.";

  /// <summary>
  /// Runs the independent-output-oracle regression suite.
  /// </summary>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("output-oracle/codex-user-context-browser-dom",
        TestCodexUserContextBrowserDom),
      ("output-oracle/claude-grouped-thought-browser-dom",
        TestClaudeGroupedThoughtBrowserDom),
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
  /// Loads a fixed Codex fixture through the real TranscriptView and compares
  /// the final browser DOM to an independently-authored semantic oracle.
  /// </summary>
  private static void TestCodexUserContextBrowserDom()
  {
    string[] records =
    {
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T15:16:00.000Z",
        payload = new
        {
          type = "user_message",
          message = "Earlier prompt."
        }
      }),
      JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = "2026-09-06T15:16:01.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = "Earlier answer."
        }
      }),
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
            UserContextPrompt
        }
      })
    };

    JsonElement actual = RenderAndProbe(
      AgentSource.Codex,
      records,
      BuildUserContextProbeScript());
    AssertUserContextOracle(actual);
  }

  /// <summary>
  /// Loads a fixed Claude grouped-thought fixture through the real
  /// TranscriptView and compares final browser containment to fixed expectations.
  /// </summary>
  private static void TestClaudeGroupedThoughtBrowserDom()
  {
    string[] records =
    {
      "{\"type\":\"user\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:01.000Z\",\"uuid\":\"thought-user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"Please reason through this.\"}]}}",
      "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:02.000Z\",\"uuid\":\"thought-one\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"First thought has unique alpha words.\"}]}}",
      "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:03.000Z\",\"uuid\":\"thought-two\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"Second thought has unique beta words.\"}]}}",
      "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:04.000Z\",\"uuid\":\"thought-three\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"Third thought has unique gamma words.\"}]}}",
      "{\"type\":\"assistant\",\"isSidechain\":false,\"timestamp\":\"2026-01-05T12:00:05.000Z\",\"uuid\":\"thought-final\",\"message\":{\"model\":\"claude-test\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Final Claude answer after thoughts.\"}]}}"
    };

    JsonElement actual = RenderAndProbe(
      AgentSource.Claude,
      records,
      BuildClaudeThoughtProbeScript());

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
  /// Proves the oracle itself rejects the exact semantic mutation observed in
  /// production: context body content escaping the disclosure while the summary
  /// and prompt remain visible.
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

    bool rejected = false;
    try
    {
      AssertUserContextOracle(document.RootElement);
    }
    catch (InvalidOperationException)
    {
      rejected = true;
    }

    if (!rejected)
    {
      throw new InvalidOperationException(
        "Independent User Context oracle accepted escaped disclosure content.");
    }
  }

  private static void AssertUserContextOracle(JsonElement actual)
  {
    RequireBoolean(actual, "detailsExists", true);
    RequireString(actual, "summary", "# Context from my IDE setup:");
    RequireBoolean(actual, "activeFileInside", true);
    RequireBoolean(actual, "selectionInside", true);
    RequireBoolean(actual, "tabsInside", true);
    RequireBoolean(actual, "tabPathInside", true);
    RequireBoolean(actual, "promptInside", false);
    RequireBoolean(actual, "promptVisible", true);
    RequireBoolean(actual, "promptAfterDetails", true);
    RequireBoolean(actual, "virtualSectionInsideDetails", false);
  }

  private static string BuildUserContextProbeScript()
  {
    string prompt = JsonSerializer.Serialize(UserContextPrompt);
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

  private static string BuildClaudeThoughtProbeScript()
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

  private static JsonElement RenderAndProbe(
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
      PumpUntilCompleted(probe, "browser output-oracle probe");
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
        $"Output oracle mismatch for {property}: expected {expected}, actual {value ?? "<missing>"}.");
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
