from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
REGRESSION = ROOT / "AgentPanelSpeaker" / "RegressionTestRunner.cs"
PROGRAM = ROOT / "AgentPanelSpeaker" / "Program.cs"
SPEECH = ROOT / "AgentPanelSpeaker" / "SpeechService.cs"
BUILDER = ROOT / "AgentPanelSpeaker" / "SpeechSapiXmlBuilder.cs"
ISSUE84 = ROOT / "AgentPanelSpeaker" / "Issue84RewindCurrentFragmentRegressionTestRunner.cs"


def replace_once(path: Path, old: str, new: str) -> None:
  text = path.read_text(encoding="utf-8")
  if text.count(old) != 1:
    raise RuntimeError(
      f"Expected exactly one match in {path}: {old[:80]!r}; got {text.count(old)}")
  path.write_text(text.replace(old, new, 1), encoding="utf-8")


def add_regressions() -> None:
  replace_once(
    REGRESSION,
    '''    Require(\n      markup.SsmlContent.Contains(\n        "<say-as interpret-as=\\\"spell-out\\\">AI</say-as>",\n        StringComparison.Ordinal),\n      "Windows/SSML spelling does not use explicit spell-out semantics.");\n''',
    '''    Require(\n      markup.SsmlContent.Contains(\n        "<break time=\\\"100ms\\\"/><say-as interpret-as=\\\"spell-out\\\">" +\n        "AI</say-as><break time=\\\"100ms\\\"/>",\n        StringComparison.Ordinal),\n      "Windows/SSML inline spelling is not isolated from surrounding speech.");\n''')

  issue84 = r'''using System.Reflection;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #84 rewind-current-fragment navigation.
/// </summary>
internal static class Issue84RewindCurrentFragmentRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #84 navigation regressions.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("rewind-current-fragment/paused-mid-fragment-restarts-current",
        TestPausedMidFragmentRestartsCurrent),
      ("rewind-current-fragment/active-mid-fragment-restarts-current",
        TestActiveMidFragmentRestartsCurrent)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #84 rewind-current-fragment regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #84 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #84 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestPausedMidFragmentRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    Require(speech.TrySeekToTranscriptWord(2002, out _),
      "Could not place the paused cursor inside the current fragment.");

    TranscriptPlaybackPosition? observed = null;
    speech.PlaybackPositionChanged += position => observed = position;
    Require(speech.TryRewindSentence(out string text),
      "Paused rewind reported no destination.");
    Require(text == "For local code files",
      "Paused rewind moved to the preceding fragment instead of the current fragment.");
    Require(observed is not null &&
        observed.State == TranscriptPlaybackState.Paused &&
        observed.Word == "For",
      "Paused rewind did not move the marker to word zero of the current fragment.");
    Require(ReadInt(speech, "_pendingHistoryIndex") == 1 &&
        ReadInt(speech, "_pendingHistoryWordIndex") == 0,
      "Paused rewind did not retain word-zero as the pending resume position.");
  }

  private static void TestActiveMidFragmentRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    SetField(speech, "_pendingHistoryIndex", null);
    SetField(speech, "_pendingHistoryWordIndex", 0);
    SetField(speech, "_activeHistoryIndex", 1);
    SetField(speech, "_activeWordIndex", 2);
    SetField(speech, "_activeKind", ParseActiveKind(speech, "History"));
    SetField(speech, "_isPaused", false);

    Require(speech.TryRewindSentence(out string text),
      "Active rewind reported no destination.");
    Require(text == "For local code files",
      "Active rewind moved to the preceding fragment instead of the current fragment.");
    Require(ReadInt(speech, "_pendingHistoryIndex") == 1 &&
        ReadInt(speech, "_pendingHistoryWordIndex") == 0,
      "Active rewind did not queue word zero of the current fragment.");
  }

  private static SpeechService CreateSpeech()
  {
    var speech = new SpeechService();
    speech.SetPolicyProviders(
      _ => new SpeechProfileSettings("Test voice", 0, 0),
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.LoadHistory(
      new[]
      {
        new SpeechFragment(
          10,
          ContentCategory.Assistant,
          SpeechFragmentKind.Prose,
          "In practice For web research",
          TranscriptWords: new[]
          {
            new SpeechFragmentWord(1001, "In", 0, 2),
            new SpeechFragmentWord(1002, "practice", 3, 8),
            new SpeechFragmentWord(1003, "For", 12, 3),
            new SpeechFragmentWord(1004, "web", 16, 3),
            new SpeechFragmentWord(1005, "research", 20, 8)
          }),
        new SpeechFragment(
          10,
          ContentCategory.Assistant,
          SpeechFragmentKind.Prose,
          "For local code files",
          TranscriptWords: new[]
          {
            new SpeechFragmentWord(2001, "For", 0, 3),
            new SpeechFragmentWord(2002, "local", 4, 5),
            new SpeechFragmentWord(2003, "code", 10, 4),
            new SpeechFragmentWord(2004, "files", 15, 5)
          })
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);
    return speech;
  }

  private static object ParseActiveKind(SpeechService speech, string value)
  {
    FieldInfo field = GetField(speech, "_activeKind");
    return Enum.Parse(field.FieldType, value);
  }

  private static int ReadInt(SpeechService speech, string name)
  {
    object? value = GetField(speech, name).GetValue(speech);
    return value switch
    {
      int number => number,
      null => -1,
      _ => throw new InvalidOperationException($"{name} is not an integer field.")
    };
  }

  private static void SetField(SpeechService speech, string name, object? value)
  {
    GetField(speech, name).SetValue(speech, value);
  }

  private static FieldInfo GetField(SpeechService speech, string name)
  {
    return speech.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"SpeechService field {name} is missing.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
'''
  if ISSUE84.exists():
    raise RuntimeError(f"{ISSUE84} already exists")
  ISSUE84.write_text(issue84, encoding="utf-8")

  dispatch_anchor = '''      if (args.Length == 2 &&\n          string.Equals(\n            args[1],\n            "core-word-id-migration",\n            StringComparison.OrdinalIgnoreCase))\n      {\n'''
  dispatch = '''      if (args.Length == 2 &&\n          string.Equals(\n            args[1],\n            "rewind-current-fragment",\n            StringComparison.OrdinalIgnoreCase))\n      {\n        Environment.ExitCode = RunNamedSuite(\n          "rewind-current-fragment",\n          Issue84RewindCurrentFragmentRegressionTestRunner.Run);\n        return;\n      }\n\n'''
  replace_once(PROGRAM, dispatch_anchor, dispatch + dispatch_anchor)

  replace_once(
    PROGRAM,
    '''      int coreWordIdMigration = RunIsolatedTestSuite(\n        "core-word-id-migration");\n\n      Environment.ExitCode = primary == 0 &&\n''',
    '''      int coreWordIdMigration = RunIsolatedTestSuite(\n        "core-word-id-migration");\n      int rewindCurrentFragment = RunIsolatedTestSuite(\n        "rewind-current-fragment");\n\n      Environment.ExitCode = primary == 0 &&\n''')
  replace_once(
    PROGRAM,
    '''                             ctrlClickVoicePointer == 0 &&\n                             coreWordIdMigration == 0\n        ? 0\n''',
    '''                             ctrlClickVoicePointer == 0 &&\n                             coreWordIdMigration == 0 &&\n                             rewindCurrentFragment == 0\n        ? 0\n''')


def apply_production() -> None:
  replace_once(
    BUILDER,
    '''      .Replace(\n        "<spell>",\n        "<say-as interpret-as=\\\"spell-out\\\">",\n        StringComparison.Ordinal)\n      .Replace("</spell>", "</say-as>", StringComparison.Ordinal);\n''',
    '''      .Replace(\n        "<spell>",\n        "<break time=\\\"100ms\\\"/>" +\n          "<say-as interpret-as=\\\"spell-out\\\">",\n        StringComparison.Ordinal)\n      .Replace(\n        "</spell>",\n        "</say-as><break time=\\\"100ms\\\"/>",\n        StringComparison.Ordinal);\n''')

  replace_once(
    SPEECH,
    '''  public bool TryRewindSentence(out string text)\n  {\n    lock (_sync)\n    {\n      int anchor = GetNavigationAnchorLocked();\n      int candidate = FindPreviousEligibleLocked(\n        anchor >= _history.Count ? _history.Count - 1 : anchor - 1);\n      return RestartCandidateLocked(candidate, out text);\n    }\n  }\n''',
    '''  public bool TryRewindSentence(out string text)\n  {\n    lock (_sync)\n    {\n      int anchor = GetNavigationAnchorLocked();\n      bool restartCurrent = anchor >= 0 && anchor < _history.Count &&\n        ((_pendingHistoryIndex == anchor && _pendingHistoryWordIndex > 0) ||\n         (_activeHistoryIndex == anchor && _activeWordIndex > 0));\n      int candidate = restartCurrent\n        ? anchor\n        : FindPreviousEligibleLocked(\n          anchor >= _history.Count ? _history.Count - 1 : anchor - 1);\n      LogNavigationLocked("rewind-sentence", anchor, candidate);\n      return RestartCandidateLocked(candidate, out text);\n    }\n  }\n''')

  replace_once(
    SPEECH,
    '''    _pendingUntracked = null;\n    ClearProcessingTimeAnnouncementLocked();\n    _pendingHistoryIndex = index;\n    _nextHistoryIndex = index;\n''',
    '''    _pendingUntracked = null;\n    ClearProcessingTimeAnnouncementLocked();\n    _pendingHistoryIndex = index;\n    _pendingHistoryWordIndex = 0;\n    _nextHistoryIndex = index;\n''')


if __name__ == "__main__":
  if len(sys.argv) != 2 or sys.argv[1] not in {"regressions", "production"}:
    raise SystemExit("usage: issue81-84-repair.py regressions|production")
  if sys.argv[1] == "regressions":
    add_regressions()
  else:
    apply_production()
