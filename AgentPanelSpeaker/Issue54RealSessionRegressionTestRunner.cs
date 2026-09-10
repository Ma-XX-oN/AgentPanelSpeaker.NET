using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent production-path regressions for the real large-session defects
/// tracked by issue #54 and its child issues.
/// </summary>
internal static class Issue54RealSessionRegressionTestRunner
{
  private const int PairCount = 120;

  /// <summary>
  /// Runs the real-session regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("real-session/scroll-replacement-does-not-reverse-user-intent",
        TestReplacementScrollDoesNotReverseUserIntent),
      ("real-session/follow-enable-reattaches-retained-voice-cursor",
        TestFollowEnableReattachesRetainedVoiceCursor)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #54 real-session regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #54 real-session tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #54 real-session tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Reproduces issue #56. A downward physical wheel gesture remains the active
  /// user intent while a virtual-window replacement restores its anchor with an
  /// upward programmatic scroll. The restoration must not be reclassified as a
  /// new upward user gesture and request a competing scroll-up window shift.
  /// </summary>
  private static void TestReplacementScrollDoesNotReverseUserIntent()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue56-scroll-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue56-scroll.jsonl");
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
      var shiftReasons = new List<string>();
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (!rootElement.TryGetProperty("type", out JsonElement typeElement) ||
              typeElement.ValueKind != JsonValueKind.String ||
              typeElement.GetString() != "window-shift")
          {
            return;
          }
          if (rootElement.TryGetProperty("reason", out JsonElement reasonElement) &&
              reasonElement.ValueKind == JsonValueKind.String)
          {
            shiftReasons.Add(reasonElement.GetString() ?? string.Empty);
          }
        }
        catch (JsonException)
        {
          // Production owns malformed-message handling. This observer records
          // only well-formed virtual-window requests.
        }
      };

      view.SelectSession(path, AgentSource.Codex, "Issue 56 scroll fixture");
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
      PumpUntilCompleted(middleWindow, "middle issue #56 virtual window");
      Require(
        ReadField<int>(view, "_windowStartIndex") > 0,
        "Issue #56 fixture has no unloaded content above the middle window.");

      // Put the first materialized unit near the upper prefetch boundary using
      // an explicitly guarded programmatic scroll. Let that guard expire so the
      // test starts from a stable browser position.
      ExecuteVoidScript(
        webView,
        """
(() => {
  const records = [...document.querySelectorAll('.virtual-record')];
  const first = records[0];
  programmaticScrollUntil = performance.now() + 500;
  const desiredTop = -window.innerHeight * 0.25;
  const delta = first.getBoundingClientRect().top - desiredTop;
  window.scrollBy(0, delta);
})()
""");
      PumpMessages(650);

      int shiftsBefore = shiftReasons.Count;
      ExecuteVoidScript(
        webView,
        """
(() => {
  virtualShiftPending = false;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: 160,
    bubbles: true,
    cancelable: true
  }));

  // This is the opposite-sign scroll produced by replacement/anchor
  // restoration while the physical downward-wheel intent is still alive.
  programmaticScrollUntil = performance.now() + 1000;
  window.scrollBy(0, -160);
})()
""");
      PumpMessages(500);

      string[] competing = shiftReasons
        .Skip(shiftsBefore)
        .Where(reason => reason == "scroll-up")
        .ToArray();
      Require(
        competing.Length == 0,
        "A replacement-induced upward scroll reversed the active downward " +
        "physical user intent and requested scroll-up.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces issue #57. While Follow Speech is OFF, a paused voice cursor can
  /// remain outside the materialized virtual window. An explicit OFF-to-ON
  /// settings transition must immediately materialize and apply that retained
  /// cursor without requiring another playback callback. Reapplying settings
  /// while Follow is already ON must not replay or reposition the cursor.
  /// </summary>
  private static void TestFollowEnableReattachesRetainedVoiceCursor()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue57-follow-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue57-follow.jsonl");
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

      TranscriptSettings followOff =
        TranscriptSettings.Default with { FollowSpeech = false };
      TranscriptSettings followOn =
        TranscriptSettings.Default with { FollowSpeech = true };
      view.ApplySettings(followOff, dark: false);
      view.SelectSession(path, AgentSource.Codex, "Issue 57 follow fixture");
      WaitForTranscriptRender(view);

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      IReadOnlyList<TranscriptNodeIdentity> identities =
        ReadField<IReadOnlyList<TranscriptNodeIdentity>>(view, "_identities");
      int initialStart = ReadField<int>(view, "_windowStartIndex");
      Require(initialStart > 0,
        "Issue #57 fixture did not leave any earlier virtual units unloaded.");

      TranscriptNodeIdentity? targetIdentity = null;
      int targetIndex = -1;
      foreach (TranscriptNodeIdentity identity in identities)
      {
        if (identity.Segments.Count == 0 ||
            !document.TryGetIndex(
              identity.RecordNumber,
              identity.SourceId,
              out int candidateIndex) ||
            candidateIndex >= initialStart)
        {
          continue;
        }
        targetIdentity = identity;
        targetIndex = candidateIndex;
        break;
      }
      Require(targetIdentity is not null,
        "Issue #57 fixture did not expose a speakable unloaded target.");

      string fragment = targetIdentity!.Segments[0];
      string word = SpeechTokenization.First(fragment);
      Require(!string.IsNullOrWhiteSpace(word),
        "Issue #57 target fragment contained no speakable word.");
      var position = new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Paused,
        fragment,
        0,
        word,
        targetIdentity.NodeId,
        0,
        word.Length,
        Stopwatch.GetTimestamp());

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      int matchingPlaybackApplied = 0;
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (!rootElement.TryGetProperty("type", out JsonElement typeElement) ||
              typeElement.ValueKind != JsonValueKind.String ||
              typeElement.GetString() != "playback-applied" ||
              !rootElement.TryGetProperty("nodeId", out JsonElement nodeElement) ||
              nodeElement.ValueKind != JsonValueKind.Number ||
              nodeElement.GetInt64() != targetIdentity.NodeId ||
              !rootElement.TryGetProperty("wordIndex", out JsonElement wordElement) ||
              wordElement.ValueKind != JsonValueKind.Number ||
              wordElement.GetInt32() != 0)
          {
            return;
          }
          ++matchingPlaybackApplied;
        }
        catch (JsonException)
        {
          // Production owns malformed-message handling. This observer records
          // only the exact retained playback position required by issue #57.
        }
      };

      view.ShowPlaybackPosition(position);
      PumpMessages(300);
      Require(
        targetIndex < ReadField<int>(view, "_windowStartIndex") ||
        targetIndex > ReadField<int>(view, "_windowEndIndex"),
        "Follow OFF unexpectedly materialized the retained issue #57 cursor.");
      TranscriptPlaybackPosition retainedBefore =
        ReadField<TranscriptPlaybackPosition>(
          view,
          "_lastLocatedContentPosition");
      Require(retainedBefore == position,
        "TranscriptView did not retain the paused voice cursor while Follow was OFF.");
      int appliedBeforeEnable = matchingPlaybackApplied;

      view.ApplySettings(followOn, dark: false);
      PumpUntil(
        () =>
          targetIndex >= ReadField<int>(view, "_windowStartIndex") &&
          targetIndex <= ReadField<int>(view, "_windowEndIndex") &&
          matchingPlaybackApplied > appliedBeforeEnable,
        "Follow ON to materialize and apply the retained voice cursor",
        timeoutMilliseconds: 5000);

      TranscriptPlaybackPosition retainedAfter =
        ReadField<TranscriptPlaybackPosition>(
          view,
          "_lastLocatedContentPosition");
      Require(retainedAfter == position,
        "Enabling Follow changed the retained speech cursor instead of reattaching to it.");

      int appliedAfterEnable = matchingPlaybackApplied;
      int startAfterEnable = ReadField<int>(view, "_windowStartIndex");
      int endAfterEnable = ReadField<int>(view, "_windowEndIndex");
      view.ApplySettings(followOn, dark: true);
      PumpMessages(500);
      Require(
        matchingPlaybackApplied == appliedAfterEnable,
        "Applying settings while Follow was already ON replayed the voice cursor.");
      Require(
        ReadField<int>(view, "_windowStartIndex") == startAfterEnable &&
        ReadField<int>(view, "_windowEndIndex") == endAfterEnable,
        "Applying settings while Follow was already ON repositioned the virtual window.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  private static Form CreateOffscreenHost()
  {
    return new Form
    {
      Width = 900,
      Height = 700,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-30000, -30000)
    };
  }

  private static void WaitForViewInitialization(TranscriptView view)
  {
    WebView2 webView = ReadField<WebView2>(view, "_webView");
    PumpUntil(
      () => webView.CoreWebView2 is not null,
      "WebView2 core initialization");
  }

  private static void WaitForTranscriptRender(TranscriptView view)
  {
    WebView2 webView = ReadField<WebView2>(view, "_webView");
    PumpUntil(
      () =>
        ReadField<int>(view, "_windowStartIndex") >= 0 &&
        ReadField<int>(view, "_windowEndIndex") >=
          ReadField<int>(view, "_windowStartIndex") &&
        webView.Visible,
      "production transcript window to finish rendering");
  }

  private static void WriteFixture(string path)
  {
    var records = new List<string>(PairCount * 2);
    for (int index = 1; index <= PairCount; ++index)
    {
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T01:{index % 60:D2}:00.000Z",
        payload = new
        {
          type = "user_message",
          message = $"Issue 56 request {index:D3}."
        }
      }));
      records.Add(JsonSerializer.Serialize(new
      {
        type = "event_msg",
        timestamp = $"2026-09-09T01:{index % 60:D2}:01.000Z",
        payload = new
        {
          type = "agent_message",
          phase = "final",
          message = $"Issue 56 response {index:D3}."
        }
      }));
    }
    File.WriteAllLines(path, records);
  }

  private static Task InvokeTask(
    object target,
    string methodName,
    params object?[] arguments)
  {
    MethodInfo method = target.GetType().GetMethod(
      methodName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Method '{methodName}' was not found on {target.GetType().Name}.");
    return method.Invoke(target, arguments) as Task ??
      throw new InvalidOperationException(
        $"Method '{methodName}' did not return a Task.");
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

  private static void ExecuteVoidScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "browser issue #54 action");
  }

  private static void PumpMessages(int milliseconds)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
  }

  private static void PumpUntil(
    Func<bool> predicate,
    string description,
    int timeoutMilliseconds = 60000)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (!predicate() && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(predicate(), $"Timed out waiting for {description}.");
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
}