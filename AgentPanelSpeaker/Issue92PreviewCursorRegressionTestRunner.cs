namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #92 preview/cursor ownership.
/// </summary>
internal static class Issue92PreviewCursorRegressionTestRunner
{
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("preview-cursor/voice-setting-uses-preview-api",
        TestVoiceSettingUsesPreviewApi),
      ("preview-cursor/cancel-preview-preserves-cursor",
        TestCancelPreviewPreservesCursor),
      ("preview-cursor/live-end-cancel-has-explicit-name",
        TestLiveEndCancelHasExplicitName)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"Issue #92 preview cursor suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #92 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #92 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestVoiceSettingUsesPreviewApi()
  {
    string main = ReadSource("MainForm.cs");
    Require(!main.Contains(
        "_speech.SpeakUntracked(message, profile);",
        StringComparison.Ordinal),
      "Voice-setting preview still uses destructive SpeakUntracked().");
    Require(main.Contains(
        "_speech.PreviewText(",
        StringComparison.Ordinal),
      "Voice-setting preview does not use the cursor-preserving preview API.");
  }

  private static void TestCancelPreviewPreservesCursor()
  {
    string main = ReadSource("MainForm.cs");
    Require(main.Contains(
        "await _speech.CancelPreviewPreservingPositionAsync();",
        StringComparison.Ordinal),
      "Play does not await cursor-preserving preview cancellation.");

    string service = ReadSource("SpeechService.cs");
    int start = service.IndexOf(
      "public Task CancelPreviewPreservingPositionAsync()",
      StringComparison.Ordinal);
    Require(start >= 0,
      "SpeechService has no cursor-preserving preview cancellation API.");
    int end = service.IndexOf("\n  /// <summary>", start + 1,
      StringComparison.Ordinal);
    Require(end > start,
      "Could not isolate cursor-preserving preview cancellation API.");
    string method = service[start..end];
    Require(!method.Contains("MoveToLiveEndLocked", StringComparison.Ordinal),
      "Preview cancellation still moves navigation to live end.");
    Require(method.Contains("_pendingHistoryIndex", StringComparison.Ordinal),
      "Preview cancellation does not preserve the pending history cursor.");
  }

  private static void TestLiveEndCancelHasExplicitName()
  {
    string service = ReadSource("SpeechService.cs");
    Require(!service.Contains("public void CancelAll()", StringComparison.Ordinal),
      "Ambiguous CancelAll() still combines audio cancellation and navigation.");
    Require(service.Contains(
        "public void CancelAndMoveToLiveEnd()",
        StringComparison.Ordinal),
      "Intentional live-end cancellation is not explicitly named.");
  }

  private static string ReadSource(string fileName)
  {
    foreach (string start in new[]
    {
      Directory.GetCurrentDirectory(),
      AppContext.BaseDirectory
    })
    {
      DirectoryInfo? directory = new DirectoryInfo(start);
      for (int depth = 0; depth < 12 && directory is not null; ++depth)
      {
        string candidate = Path.Combine(
          directory.FullName,
          "AgentPanelSpeaker",
          fileName);
        if (File.Exists(candidate))
        {
          return File.ReadAllText(candidate);
        }
        directory = directory.Parent;
      }
    }
    throw new FileNotFoundException(
      $"Could not locate AgentPanelSpeaker/{fileName} from the test process.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
