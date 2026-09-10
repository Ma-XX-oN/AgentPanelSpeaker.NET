using Microsoft.Web.WebView2.WinForms;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Verifies truthful, visible startup feedback for long transcript preparation.
/// </summary>
internal static class Issue55StartupProgressRegressionTestRunner
{
  private static readonly string[] ExpectedStagePrefixes =
  {
    "Preparing canonical transcript…",
    "Building transcript search index…",
    "Rendering visible transcript…"
  };

  /// <summary>
  /// Runs the issue #55 startup-progress regression suite.
  /// </summary>
  /// <returns>Zero when all regressions pass; otherwise one.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("startup-progress/shows-ordered-preparation-stages",
        TestShowsOrderedPreparationStages),
      ("startup-progress/does-not-fabricate-percentages",
        TestDoesNotFabricatePercentages)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #55 startup-progress suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #55 regressions passed."
      : $"FAIL: {failures}/{tests.Length} issue #55 regressions failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Initial transcript preparation must expose ordered, truthful stage changes
  /// instead of leaving one static loading message visible for the whole wait.
  /// </summary>
  private static void TestShowsOrderedPreparationStages()
  {
    IReadOnlyList<string> observed = CaptureStartupLoadingText();
    int previousIndex = -1;
    for (int stage = 0; stage < ExpectedStagePrefixes.Length; ++stage)
    {
      string prefix = ExpectedStagePrefixes[stage];
      int index = FindAfter(observed, prefix, previousIndex + 1);
      Require(
        index >= 0,
        $"Startup never displayed required stage '{prefix}'. Observed: " +
        string.Join(" | ", observed.Select(Compact)));
      Require(
        observed[index].Contains(
          $"Stage {stage + 1} of {ExpectedStagePrefixes.Length}",
          StringComparison.Ordinal),
        $"Stage '{prefix}' did not expose its truthful stage ordinal.");
      previousIndex = index;
    }
  }

  /// <summary>
  /// No startup stage currently has a trustworthy work-unit denominator. The UI
  /// must therefore use stage ordinals rather than inventing a percentage from
  /// elapsed time or unequal phase count.
  /// </summary>
  private static void TestDoesNotFabricatePercentages()
  {
    IReadOnlyList<string> observed = CaptureStartupLoadingText();
    foreach (string prefix in ExpectedStagePrefixes)
    {
      string? stageText = observed.FirstOrDefault(text =>
        text.StartsWith(prefix, StringComparison.Ordinal));
      Require(stageText is not null,
        $"Startup never displayed required stage '{prefix}'.");
      Require(
        !stageText.Contains('%', StringComparison.Ordinal),
        $"Startup stage '{prefix}' displayed a percentage without a real denominator: " +
        Compact(stageText));
    }
  }

  private static IReadOnlyList<string> CaptureStartupLoadingText()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      "AgentPanelSpeaker-issue55-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rollout-issue55.jsonl");
    WriteFixture(path);

    try
    {
      using var form = new Form
      {
        Width = 900,
        Height = 700,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      form.Controls.Add(view);
      form.Show();
      Application.DoEvents();

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 initialization for startup progress");

      Label loading = view.Controls
        .OfType<Label>()
        .Single(control =>
          string.Equals(
            control.Name,
            "TranscriptLoadingLabel",
            StringComparison.Ordinal));
      var observed = new List<string>();
      void Record()
      {
        string text = loading.Text;
        if (text.Length != 0 &&
            (observed.Count == 0 ||
             !string.Equals(observed[^1], text, StringComparison.Ordinal)))
        {
          observed.Add(text);
        }
      }
      loading.TextChanged += (_, _) => Record();
      Record();

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 55 startup progress fixture");
      PumpUntil(
        () =>
          ReadField<int>(view, "_windowStartIndex") >= 0 &&
          !loading.Visible,
        "startup progress fixture to finish rendering",
        timeoutMilliseconds: 60000);
      Application.DoEvents();
      return observed;
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
      catch (UnauthorizedAccessException)
      {
      }
    }
  }

  private static int FindAfter(
    IReadOnlyList<string> values,
    string prefix,
    int startIndex)
  {
    for (int index = Math.Max(0, startIndex); index < values.Count; ++index)
    {
      if (values[index].StartsWith(prefix, StringComparison.Ordinal))
      {
        return index;
      }
    }
    return -1;
  }

  private static string Compact(string text)
  {
    return text.Replace("\r", string.Empty, StringComparison.Ordinal)
      .Replace("\n", " / ", StringComparison.Ordinal);
  }

  private static void WriteFixture(string path)
  {
    var records = new List<string>();
    for (int index = 1; index <= 80; ++index)
    {
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-10T00:{index % 60:D2}:00.000Z",
        payload = new
        {
          type = "user_message",
          message = $"Issue 55 request {index:D3}."
        }
      }));
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-10T00:{index % 60:D2}:01.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = $"Issue 55 response {index:D3}."
        }
      }));
    }
    File.WriteAllLines(path, records);
  }

  private static T ReadField<T>(object target, string fieldName)
  {
    FieldInfo field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field '{fieldName}' was not found on {target.GetType().Name}.");
    object? value = field.GetValue(target);
    return value is T typed
      ? typed
      : throw new InvalidOperationException(
        $"Field '{fieldName}' had an unexpected value/type.");
  }

  private static void PumpUntil(
    Func<bool> predicate,
    string description,
    int timeoutMilliseconds = 30000)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (!predicate() && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(predicate(), $"Timed out waiting for {description}.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
