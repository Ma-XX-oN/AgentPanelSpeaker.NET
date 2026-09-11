using Microsoft.Web.WebView2.WinForms;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #67 Find-origin selection.
/// </summary>
internal static class Issue67FindOriginRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #67 Find-origin regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("find-origin/voice-cursor-rotation-remains-correct",
        TestVoiceCursorRotationRemainsCorrect),
      ("find-origin/previous-find-location-becomes-next-search-origin",
        TestPreviousFindLocationBecomesNextSearchOrigin),
      ("find-origin/find-result-origin-is-treated-as-provided",
        TestFindResultOriginIsTreatedAsProvided)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #67 Find-origin regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #67 Find-origin tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #67 Find-origin tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Existing C# match rotation already starts a first-time Find after the
  /// resolved voice cursor. Preserve that behavior while fixing the separate
  /// previous-Find-origin defect.
  /// </summary>
  private static void TestVoiceCursorRotationRemainsCorrect()
  {
    MethodInfo rotate = typeof(TranscriptView).GetMethod(
      "RotateMatchesAfterOrigin",
      BindingFlags.NonPublic | BindingFlags.Static) ??
      throw new InvalidOperationException(
        "TranscriptView.RotateMatchesAfterOrigin() is missing.");

    TranscriptSearchMatch[] matches =
    {
      new(0, 10, 2, 2, 101, 0),
      new(1, 20, 5, 7, 201, 0),
      new(2, 20, 10, 10, 202, 0),
      new(3, 30, 1, 1, 301, 0)
    };
    object? result = rotate.Invoke(null, new object?[] { matches, 20, 8 });
    var rotated = result as IReadOnlyList<TranscriptSearchMatch> ??
      throw new InvalidOperationException(
        "RotateMatchesAfterOrigin() returned no match list.");

    Require(rotated.Count == 4,
      "Voice-origin rotation changed the number of Find matches.");
    Require(rotated[0].RecordNumber == 20 &&
        rotated[0].StartWordIndex == 10,
      "Voice-origin rotation did not start at the first match after the cursor.");
    Require(rotated[1].RecordNumber == 30,
      "Voice-origin rotation did not preserve forward transcript order.");
    Require(rotated[2].RecordNumber == 10,
      "Voice-origin rotation did not preserve wrap order.");
  }

  /// <summary>
  /// Once Find has a current result, editing/re-running the query must use that
  /// found location as the next origin instead of falling back to the voice
  /// cursor. Explicit browser text selection remains higher priority.
  /// </summary>
  private static void TestPreviousFindLocationBecomesNextSearchOrigin()
  {
    using BrowserFixture fixture = BrowserFixture.Create();
    JsonElement result = ExecuteJsonProbe(
      fixture.WebView,
      """
(() => {
  findMatches = [{
    fileOrdinal: 4,
    recordNumber: 55,
    startWordIndex: 7,
    endWordIndex: 8,
    nodeId: 9001,
    nodeWordIndex: 3
  }].map(normalizeFindMatch);
  currentFindMatch = 0;
  return JSON.stringify(getFindOrigin());
})()
""");

    Require(string.Equals(
        result.GetProperty("kind").GetString(),
        "find",
        StringComparison.Ordinal),
      "Previous Find result was not selected as the next Find origin.");
    Require(result.GetProperty("recordNumber").GetInt32() == 55,
      "Previous Find origin lost its record number.");
    Require(result.GetProperty("wordIndex").GetInt32() == 7,
      "Previous Find origin lost its record-relative word index.");
  }

  /// <summary>
  /// A browser-provided previous-Find origin is authoritative just like an
  /// explicit selection. C# must not overwrite it with the voice cursor.
  /// </summary>
  private static void TestFindResultOriginIsTreatedAsProvided()
  {
    MethodInfo resolve = typeof(TranscriptView).GetMethod(
      "IsProvidedFindOriginKind",
      BindingFlags.NonPublic | BindingFlags.Static) ??
      throw new InvalidOperationException(
        "TranscriptView.IsProvidedFindOriginKind() is missing.");

    bool selection = InvokeOriginKind(resolve, "selection");
    bool find = InvokeOriginKind(resolve, "find");
    bool voice = InvokeOriginKind(resolve, "voice");

    Require(selection,
      "Explicit selection origin stopped being authoritative.");
    Require(find,
      "Previous Find result is not treated as an authoritative origin.");
    Require(!voice,
      "Voice origin was incorrectly classified as browser-provided.");
  }

  private static bool InvokeOriginKind(MethodInfo method, string kind)
  {
    object? result = method.Invoke(null, new object?[] { kind });
    return result is bool value
      ? value
      : throw new InvalidOperationException(
        "IsProvidedFindOriginKind() did not return a Boolean.");
  }

  private static JsonElement ExecuteJsonProbe(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #67 browser probe");
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
        "issue #67 WebView initialization");
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
      PumpUntilCompleted(navigated.Task, "issue #67 transcript shell navigation");
      return new BrowserFixture(host, webView);
    }

    public void Dispose()
    {
      WebView.Dispose();
      Host.Dispose();
    }
  }
}
