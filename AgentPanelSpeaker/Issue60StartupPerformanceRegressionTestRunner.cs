using Microsoft.Web.WebView2.WinForms;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent startup-performance regressions for issue #60.
/// </summary>
internal static class Issue60StartupPerformanceRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #60 startup-performance regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("startup-performance/visible-render-does-not-wait-for-search-index",
        TestVisibleRenderDoesNotWaitForSearchIndex)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #60 startup-performance suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #60 regressions passed."
      : $"FAIL: {failures}/{tests.Length} issue #60 regressions failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// The visible initial window must be allowed to appear before the full-file
  /// search corpus is ready. Search-index construction can continue afterward
  /// without replacing the already-rendered window, and the completed C# index
  /// must remain immediately usable without installing per-word browser maps.
  /// </summary>
  private static void TestVisibleRenderDoesNotWaitForSearchIndex()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue60-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue60.jsonl");
    WriteFixture(path);

    try
    {
      using var host = new Form
      {
        Width = 900,
        Height = 700,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      PumpUntil(
        () => webView.CoreWebView2 is not null,
        "WebView2 initialization for issue #60");
      Label loading = view.Controls
        .OfType<Label>()
        .Single(control =>
          string.Equals(
            control.Name,
            "TranscriptLoadingLabel",
            StringComparison.Ordinal));

      bool observedInitialHide = false;
      bool searchReadyAtInitialHide = true;
      loading.VisibleChanged += (_, _) =>
      {
        if (!loading.Visible &&
            !observedInitialHide &&
            ReadField<int>(view, "_windowStartIndex") >= 0)
        {
          observedInitialHide = true;
          searchReadyAtInitialHide =
            ReadNullableField(view, "_searchIndex") is not null;
        }
      };

      view.SelectSession(path, AgentSource.Codex, "Issue 60 startup fixture");
      PumpUntil(
        () => observedInitialHide,
        "initial transcript render to become visible",
        timeoutMilliseconds: 60000);
      Require(
        !searchReadyAtInitialHide,
        "Initial transcript rendering remained blocked until the full search " +
        "index was already complete.");

      int initialWindowStart = ReadField<int>(view, "_windowStartIndex");
      int initialWindowEnd = ReadField<int>(view, "_windowEndIndex");
      int initialWordCount = ReadScriptInt(
        webView,
        "document.querySelectorAll('.word').length");
      Require(initialWordCount > 0,
        "Initial visible transcript window contained no materialized words.");

      PumpUntil(
        () => ReadNullableField(view, "_searchIndex") is not null,
        "deferred search index to complete",
        timeoutMilliseconds: 60000);
      Require(
        ReadField<int>(view, "_windowStartIndex") == initialWindowStart &&
        ReadField<int>(view, "_windowEndIndex") == initialWindowEnd,
        "Deferred search-index completion replaced the already-visible window.");
      Require(ReadScriptInt(
          webView,
          "document.querySelectorAll('.word').length") == initialWordCount,
        "Deferred search-index completion rematerialized the visible word DOM.");

      TranscriptSearchIndex searchIndex =
        (TranscriptSearchIndex?)ReadNullableField(view, "_searchIndex") ??
        throw new InvalidOperationException(
          "Deferred search index disappeared after completion.");
      IReadOnlyList<TranscriptSearchMatch> matches = searchIndex.SearchAsync(
          new TranscriptSearchRequest(
            1,
            "issue60-search-token-100",
            CaseSensitive: false,
            WholeWord: false,
            Regex: false,
            VoicedOnly: false),
          CancellationToken.None)
        .GetAwaiter()
        .GetResult();
      Require(matches.Count > 0,
        "Deferred C# search index was not usable after visible rendering.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  private static void WriteFixture(string path)
  {
    var records = new List<string>();
    for (int index = 1; index <= 100; ++index)
    {
      string body = string.Join(
        " ",
        Enumerable.Repeat($"issue60-search-token-{index:D3}", 40));
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-10T00:{index % 60:D2}:00.000Z",
        payload = new
        {
          type = "user_message",
          message = $"Issue 60 request {index:D3}. {body}"
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
          message = $"Issue 60 response {index:D3}. {body}"
        }
      }));
    }
    File.WriteAllLines(path, records);
  }

  private static T ReadField<T>(object target, string fieldName)
  {
    object? value = ReadNullableField(target, fieldName);
    return value is T typed
      ? typed
      : throw new InvalidOperationException(
        $"Field '{fieldName}' had an unexpected value/type.");
  }

  private static object? ReadNullableField(object target, string fieldName)
  {
    FieldInfo field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field '{fieldName}' was not found on {target.GetType().Name}.");
    return field.GetValue(target);
  }

  private static int ReadScriptInt(WebView2 webView, string expression)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(expression);
    PumpUntil(
      () => task.IsCompleted,
      "issue #60 browser probe",
      timeoutMilliseconds: 10000);
    string json = task.GetAwaiter().GetResult();
    return int.TryParse(json, out int value) ? value : -1;
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
