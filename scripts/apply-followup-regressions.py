from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one match, found {count}")
  return text.replace(old, new, 1)


runner_path = Path("AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs")
runner = runner_path.read_text(encoding="utf-8")
runner = replace_once(
  runner,
  '''      ("real-session/scroll-replacement-does-not-reverse-user-intent",
        TestReplacementScrollDoesNotReverseUserIntent),
      ("real-session/live-end-window-retains-preceding-unit",
        TestLiveEndWindowRetainsPrecedingUnit)''',
  '''      ("real-session/scroll-replacement-does-not-reverse-user-intent",
        TestReplacementScrollDoesNotReverseUserIntent),
      ("real-session/stale-window-shift-is-coalesced",
        TestStaleWindowShiftIsCoalesced),
      ("real-session/directional-shift-keeps-prefetch-headroom",
        TestDirectionalShiftKeepsPrefetchHeadroom),
      ("real-session/live-end-window-retains-preceding-unit",
        TestLiveEndWindowRetainsPrecedingUnit)''',
  "issue54 test list")
runner = replace_once(
  runner,
  '''  /// <summary>
  /// Reproduces issue #58 without splitting Core atomic units. A final turn can''',
  '''  /// <summary>
  /// Reproduces the remaining issue #56 duplicate-window work. Two shift
  /// requests produced from the same browser window are the same navigation
  /// transaction. Once the first replacement advances the window, the stale
  /// second request must not rebuild the same materialized range again.
  /// </summary>
  private static void TestStaleWindowShiftIsCoalesced()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue56-stale-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue56-stale.jsonl");
    WriteFixture(path);

    try
    {
      using var host = CreateOffscreenHost();
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;
      WaitForViewInitialization(view);
      view.ApplySettings(
        TranscriptSettings.Default with { FollowSpeech = false },
        dark: false);

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      int mappingInstallSummaries = 0;
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (rootElement.TryGetProperty("type", out JsonElement typeElement) &&
              typeElement.ValueKind == JsonValueKind.String &&
              typeElement.GetString() == "mapping-install-summary")
          {
            ++mappingInstallSummaries;
          }
        }
        catch (JsonException)
        {
        }
      };

      view.SelectSession(path, AgentSource.Codex, "Issue 56 stale shift fixture");
      WaitForTranscriptRender(view);
      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int middleIndex = document.Count / 2;
      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "test-precondition",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(middleWindow, "middle stale-shift virtual window");
      PumpMessages(100);

      int start = ReadField<int>(view, "_windowStartIndex");
      int end = ReadField<int>(view, "_windowEndIndex");
      Require(start > 0 && end > start,
        "Issue #56 stale-shift fixture did not create a movable middle window.");
      int summariesBefore = mappingInstallSummaries;

      ExecuteVoidScript(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const reference = records[0];
  const anchor = reference?.querySelector('.record-anchor');
  if (!reference || !anchor) throw new Error('No virtual shift reference record.');
  const message = {
    type:'window-shift',
    reason:'scroll-up',
    focalIndex:Number(reference.dataset.virtualIndex || -1),
    sourceStartIndex:windowStartIndex,
    sourceEndIndex:windowEndIndex,
    visibleStartIndex:Number(reference.dataset.virtualIndex || -1),
    visibleEndIndex:Number(reference.dataset.virtualIndex || -1),
    viewportHeight:window.innerHeight,
    anchorRecordNumber:Number(anchor.dataset.jsonlRecord || 0),
    anchorSourceId:anchor.dataset.sourceId || '',
    anchorOffset:anchor.getBoundingClientRect().top
  };
  chrome.webview.postMessage(message);
  chrome.webview.postMessage(message);
})()
""");
      PumpMessages(2500);

      int replacements = mappingInstallSummaries - summariesBefore;
      Require(
        replacements == 1,
        $"Two stale requests from one source window caused {replacements} " +
        "full browser mapping installs instead of one coalesced replacement.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces the remaining issue #56 scroll-ahead deficit. Once a downward
  /// shift is requested, the resulting window must carry enough materialized
  /// content beyond the currently visible range to absorb continued physical
  /// scrolling while the next asynchronous replacement is prepared.
  /// </summary>
  private static void TestDirectionalShiftKeepsPrefetchHeadroom()
  {
    const int recordCount = 30;
    const double viewportHeight = 700.0;
    CanonicalHtmlUnitProjection[] units = Enumerable.Range(0, recordCount)
      .Select(index => CreateUnit(
        index,
        $"prefetch-{index}",
        $"<p>Issue 56 prefetch record {index}.</p>"))
      .ToArray();
    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(units);
    const int layoutGeneration = 17;
    document.UpdateMeasuredHeights(
      Enumerable.Range(0, recordCount)
        .ToDictionary(index => index, _ => viewportHeight),
      layoutGeneration);

    TranscriptWindow shifted = document.CreateShiftedWindow(
      focalIndex: 12,
      currentStartIndex: 8,
      currentEndIndex: 12,
      direction: 1,
      viewportHeight: viewportHeight,
      protectedStartIndex: 11,
      protectedEndIndex: 12);

    Require(
      shifted.StartIndex <= 11,
      "Directional shift discarded a protected physically visible Core unit.");
    Require(
      shifted.EndIndex - 12 >= 4,
      "Directional shift left fewer than four viewport-heights of measured " +
      "materialized headroom beyond the visible range.");
  }

  /// <summary>
  /// Reproduces issue #58 without splitting Core atomic units. A final turn can''',
  "issue56 follow-up methods")
runner_path.write_text(runner, encoding="utf-8")

progress_path = Path("AgentPanelSpeaker/Issue55StartupProgressRegressionTestRunner.cs")
progress = progress_path.read_text(encoding="utf-8")
progress = replace_once(
  progress,
  '''  private static readonly string[] ExpectedProgressDescriptions =
  {
    "Preparing canonical transcript…",
    "Building transcript search index…",
    "Rendering visible transcript…"
  };''',
  '''  private static readonly string[] ExpectedProgressDescriptions =
  {
    "Preparing canonical transcript…",
    "Rendering visible transcript…"
  };''',
  "issue55 non-blocking search expectation")
progress_path.write_text(progress, encoding="utf-8")

program_path = Path("AgentPanelSpeaker/Program.cs")
program = program_path.read_text(encoding="utf-8")
program = replace_once(
  program,
  '''      if (args.Length == 2 &&
          string.Equals(args[1], "real-session-fixes", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = RunNamedSuite(
          "real-session-fixes",
          () => RunWithWinFormsMessageLoop(
            Issue54RealSessionRegressionTestRunner.Run));
        return;
      }

      if (args.Length == 2)''',
  '''      if (args.Length == 2 &&
          string.Equals(args[1], "real-session-fixes", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = RunNamedSuite(
          "real-session-fixes",
          () => RunWithWinFormsMessageLoop(
            Issue54RealSessionRegressionTestRunner.Run));
        return;
      }

      if (args.Length == 2 &&
          string.Equals(args[1], "startup-performance", StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = RunNamedSuite(
          "startup-performance",
          () => RunWithWinFormsMessageLoop(
            Issue60StartupPerformanceRegressionTestRunner.Run));
        return;
      }

      if (args.Length == 2)''',
  "Program named startup-performance suite")
program = replace_once(
  program,
  '''      int startupProgress = RunIsolatedTestSuite("startup-progress");
      int realSessionFixes = RunIsolatedTestSuite("real-session-fixes");

      Environment.ExitCode = primary == 0 &&''',
  '''      int startupProgress = RunIsolatedTestSuite("startup-progress");
      int realSessionFixes = RunIsolatedTestSuite("real-session-fixes");
      int startupPerformance = RunIsolatedTestSuite("startup-performance");

      Environment.ExitCode = primary == 0 &&''',
  "Program startup-performance isolation")
program = replace_once(
  program,
  '''                             wordMaterialization == 0 &&
                             startupProgress == 0 &&
                             realSessionFixes == 0
        ? 0''',
  '''                             wordMaterialization == 0 &&
                             startupProgress == 0 &&
                             realSessionFixes == 0 &&
                             startupPerformance == 0
        ? 0''',
  "Program startup-performance gate")
program_path.write_text(program, encoding="utf-8")

issue60_path = Path("AgentPanelSpeaker/Issue60StartupPerformanceRegressionTestRunner.cs")
if issue60_path.exists():
  raise RuntimeError("Issue60StartupPerformanceRegressionTestRunner.cs already exists")
issue60_path.write_text(r'''using Microsoft.Web.WebView2.WinForms;
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
  /// search corpus is ready. Search-index construction can continue after that
  /// point, and its stable word map must subsequently attach to the already
  /// rendered browser window without replacing that window.
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

      PumpUntil(
        () => ReadNullableField(view, "_searchIndex") is not null,
        "deferred search index to complete",
        timeoutMilliseconds: 60000);
      PumpUntil(
        () => ReadScriptInt(
          webView,
          "document.querySelectorAll('.word[data-word-id]').length") > 0,
        "deferred stable word maps to attach to the visible window",
        timeoutMilliseconds: 30000);
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
''', encoding="utf-8")
