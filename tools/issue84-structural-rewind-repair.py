#!/usr/bin/env python3

from __future__ import annotations

import argparse
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OLD_CORE = "76042b398c2f50ff2b146f86e98e3d43881ba216"
NEW_CORE = "134d5735b44b8d131d30d5b98a6e3a06320a113f"
TEST_PATH = ROOT / "AgentPanelSpeaker/Issue84RewindCurrentFragmentRegressionTestRunner.cs"

TEST_SOURCE = r'''using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #84 structural rewind navigation.
/// </summary>
internal static class Issue84RewindCurrentFragmentRegressionTestRunner
{
  private const int CurrentFragmentIndex = 2;

  /// <summary>
  /// Runs the issue #84 navigation regressions.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("rewind-current-fragment/canonical-structural-boundaries",
        TestCanonicalStructuralBoundaries),
      ("rewind-current-fragment/paused-mid-fragment-restarts-current",
        TestPausedMidFragmentRestartsCurrent),
      ("rewind-current-fragment/active-mid-fragment-restarts-current",
        TestActiveMidFragmentRestartsCurrent),
      ("rewind-current-fragment/active-first-word-pending-grace-restarts-current",
        TestActiveFirstWordPendingGraceRestartsCurrent),
      ("rewind-current-fragment/active-first-word-running-grace-restarts-current",
        TestActiveFirstWordRunningGraceRestartsCurrent),
      ("rewind-current-fragment/active-first-word-expired-grace-moves-previous",
        TestActiveFirstWordExpiredGraceMovesPrevious),
      ("rewind-current-fragment/paused-first-word-moves-previous",
        TestPausedFirstWordMovesPrevious),
      ("rewind-current-fragment/later-word-start-does-not-arm-grace",
        TestLaterWordStartDoesNotArmGrace),
      ("rewind-current-fragment/named-grace-period",
        TestNamedGracePeriod)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #84 structural rewind regression suite: {tests.Length} tests");
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

  private static void TestCanonicalStructuralBoundaries()
  {
    CanonicalSpeechWordProjection[] words =
    {
      Word(1, "In", "", true),
      Word(2, "practice", " ", false),
      Word(3, ":", "", false),
      Word(4, "For", "\n\n", true),
      Word(5, "web", " ", false),
      Word(6, "research", " ", false),
      Word(7, ".", "", false),
      Word(8, "For", "\n", true),
      Word(9, "local", " ", false),
      Word(10, "code", " ", false),
      Word(11, "files", " ", false),
      Word(12, ".", "", false),
      Word(13, "Soft", "\n\n", true),
      Word(14, "line", " ", false),
      Word(15, "wrap", "\n", false),
      Word(16, "continues", " ", false),
      Word(17, ".", "", false)
    };

    MethodInfo method = typeof(JsonlSessionMonitor).GetMethod(
      "BuildCanonicalSpeechParts",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Canonical speech-part builder is missing.");
    object result = method.Invoke(null, new object[] { words }) ??
      throw new InvalidOperationException(
        "Canonical speech-part builder returned null.");
    FieldInfo item1 = result.GetType().GetField("Item1") ??
      throw new InvalidOperationException(
        "Canonical speech-part result has no Item1 field.");
    var parts = item1.GetValue(result) as IReadOnlyList<SpeechTextPart> ??
      throw new InvalidOperationException(
        "Canonical speech-part result did not contain speech parts.");
    string[] actual = parts.Select(part => part.Text).ToArray();
    string[] expected =
    {
      "In practice:",
      "For web research.",
      "For local code files.",
      "Soft line wrap continues."
    };
    Require(actual.SequenceEqual(expected),
      "Core structural navigation boundaries were not preserved as independent speech fragments.");
  }

  private static void TestPausedMidFragmentRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    Require(speech.TrySeekToTranscriptWord(3002, out _),
      "Could not place the paused cursor inside the current fragment.");

    TranscriptPlaybackPosition? observed = null;
    speech.PlaybackPositionChanged += position => observed = position;
    Require(speech.TryRewindSentence(out string text),
      "Paused rewind reported no destination.");
    Require(text == "For local code files",
      "Paused rewind moved away from the current fragment.");
    Require(observed is not null &&
        observed.State == TranscriptPlaybackState.Paused &&
        observed.Word == "For",
      "Paused rewind did not move the marker to word zero of the current fragment.");
  }

  private static void TestActiveMidFragmentRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 2);

    Require(speech.TryRewindSentence(out string text),
      "Active rewind reported no destination.");
    Require(text == "For local code files",
      "Active rewind moved away from the current fragment.");
    Require(ReadInt(speech, "_pendingHistoryIndex") == CurrentFragmentIndex &&
        ReadInt(speech, "_pendingHistoryWordIndex") == 0,
      "Active rewind did not queue word zero of the current fragment.");
  }

  private static void TestActiveFirstWordPendingGraceRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 0);
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", true);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      null);

    Require(speech.TryRewindSentence(out string text),
      "First-word pending-grace rewind reported no destination.");
    Require(text == "For local code files",
      "First-word rewind inside the pre-boundary grace state did not restart the current fragment.");
  }

  private static void TestActiveFirstWordRunningGraceRestartsCurrent()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 0);
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", false);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp());

    Require(speech.TryRewindSentence(out string text),
      "First-word running-grace rewind reported no destination.");
    Require(text == "For local code files",
      "First-word rewind inside the running grace period did not restart the current fragment.");
  }

  private static void TestActiveFirstWordExpiredGraceMovesPrevious()
  {
    using SpeechService speech = CreateSpeech();
    SetActivePosition(speech, CurrentFragmentIndex, 0);
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", false);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp() - Stopwatch.Frequency);

    Require(speech.TryRewindSentence(out string text),
      "Expired-grace rewind reported no previous fragment.");
    Require(text == "For web research",
      "Expired first-word grace did not move to the immediately preceding structural fragment.");
  }

  private static void TestPausedFirstWordMovesPrevious()
  {
    using SpeechService speech = CreateSpeech();
    Require(speech.TrySeekToTranscriptWord(3001, out _),
      "Could not place the paused cursor on the first word of the current fragment.");
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", true);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp());

    TranscriptPlaybackPosition? observed = null;
    speech.PlaybackPositionChanged += position => observed = position;
    Require(speech.TryRewindSentence(out string text),
      "Paused first-word rewind reported no previous fragment.");
    Require(text == "For web research",
      "Paused first-word rewind did not move to the immediately preceding structural fragment.");
    Require(observed is not null &&
        observed.State == TranscriptPlaybackState.Paused &&
        observed.Word == "For",
      "Paused first-word rewind did not land at word zero of the previous fragment.");
  }

  private static void TestLaterWordStartDoesNotArmGrace()
  {
    using SpeechService speech = CreateSpeech();
    SetFieldIfPresent(speech, "_rewindCurrentFragmentGracePending", true);
    SetFieldIfPresent(
      speech,
      "_rewindCurrentFragmentGraceStartedTimestamp",
      Stopwatch.GetTimestamp());
    InvokePlaybackStartPreparation(speech, 1);

    Require(ReadBool(speech, "_rewindCurrentFragmentGracePending") is false,
      "Playback starting after word zero incorrectly armed the rewind grace window.");
    Require(ReadNullableLong(
        speech,
        "_rewindCurrentFragmentGraceStartedTimestamp") is null,
      "Playback starting after word zero retained a rewind grace timestamp.");
  }

  private static void TestNamedGracePeriod()
  {
    FieldInfo field = typeof(SpeechService).GetField(
      "RewindCurrentFragmentGracePeriod",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Named RewindCurrentFragmentGracePeriod field is missing.");
    Require(field.GetValue(null) is TimeSpan period &&
        period == TimeSpan.FromMilliseconds(500),
      "Named rewind grace period is not 500 ms.");
  }

  private static CanonicalSpeechWordProjection Word(
    long id,
    string text,
    string separator,
    bool navigationBoundaryBefore)
  {
    string json = JsonSerializer.Serialize(new
    {
      id,
      text,
      separator_before = separator,
      groups = Array.Empty<string>(),
      provenance = (object?)null,
      navigation_boundary_before = navigationBoundaryBefore
    });
    return JsonSerializer.Deserialize<CanonicalSpeechWordProjection>(json) ??
      throw new InvalidOperationException(
        "Could not deserialize canonical speech-word fixture.");
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
        Fragment(10, 1000, "In practice", "In", "practice"),
        Fragment(10, 2000, "For web research", "For", "web", "research"),
        Fragment(10, 3000, "For local code files", "For", "local", "code", "files")
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);
    return speech;
  }

  private static SpeechFragment Fragment(
    long nodeId,
    long wordIdBase,
    string text,
    params string[] words)
  {
    var mapped = new List<SpeechFragmentWord>();
    int searchStart = 0;
    for (int index = 0; index < words.Length; ++index)
    {
      int start = text.IndexOf(
        words[index],
        searchStart,
        StringComparison.Ordinal);
      if (start < 0)
      {
        throw new InvalidOperationException(
          $"Fixture word {words[index]} was not found in {text}.");
      }
      mapped.Add(new SpeechFragmentWord(
        wordIdBase + index + 1,
        words[index],
        start,
        words[index].Length));
      searchStart = start + words[index].Length;
    }
    return new SpeechFragment(
      nodeId,
      ContentCategory.Assistant,
      SpeechFragmentKind.Prose,
      text,
      TranscriptWords: mapped.ToArray());
  }

  private static void SetActivePosition(
    SpeechService speech,
    int historyIndex,
    int wordIndex)
  {
    SetField(speech, "_pendingHistoryIndex", null);
    SetField(speech, "_pendingHistoryWordIndex", 0);
    SetField(speech, "_activeHistoryIndex", historyIndex);
    SetField(speech, "_activeWordIndex", wordIndex);
    SetField(speech, "_activeWordBaseIndex", wordIndex);
    SetField(speech, "_activeKind", ParseActiveKind(speech, "History"));
    SetField(speech, "_isPaused", false);
  }

  private static void InvokePlaybackStartPreparation(
    SpeechService speech,
    int wordIndex)
  {
    MethodInfo method = speech.GetType().GetMethod(
      "PrepareRewindCurrentFragmentGraceLocked",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Playback-start rewind-grace preparation method is missing.");
    method.Invoke(speech, new object[] { wordIndex });
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

  private static bool ReadBool(SpeechService speech, string name)
  {
    return GetField(speech, name).GetValue(speech) is bool value
      ? value
      : throw new InvalidOperationException($"{name} is not a Boolean field.");
  }

  private static long? ReadNullableLong(SpeechService speech, string name)
  {
    object? value = GetField(speech, name).GetValue(speech);
    return value switch
    {
      long number => number,
      null => null,
      _ => throw new InvalidOperationException($"{name} is not a nullable long field.")
    };
  }

  private static void SetFieldIfPresent(
    SpeechService speech,
    string name,
    object? value)
  {
    FieldInfo? field = speech.GetType().GetField(
      name,
      BindingFlags.Instance | BindingFlags.NonPublic);
    field?.SetValue(speech, value);
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


def replace_once(path: Path, old: str, new: str) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"Expected exactly one match in {path}: found {count} for {old[:100]!r}"
    )
  path.write_text(text.replace(old, new, 1), encoding="utf-8")


def replace_all_exact(path: Path, old: str, new: str, minimum: int = 1) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count < minimum:
    raise RuntimeError(f"Expected at least {minimum} matches in {path}, found {count}.")
  path.write_text(text.replace(old, new), encoding="utf-8")


def install_tests() -> None:
  TEST_PATH.write_text(TEST_SOURCE, encoding="utf-8")


def patch_projection() -> None:
  path = ROOT / "AgentPanelSpeaker/CanonicalHtmlUnitProjection.cs"
  replace_once(
    path,
    '  [property: JsonPropertyName("provenance")]\n'
    '    CanonicalSpeechWordProvenanceProjection? Provenance);\n',
    '  [property: JsonPropertyName("provenance")]\n'
    '    CanonicalSpeechWordProvenanceProjection? Provenance,\n'
    '  [property: JsonPropertyName("navigation_boundary_before")]\n'
    '    bool NavigationBoundaryBefore = false);\n'
  )


def patch_monitor() -> None:
  path = ROOT / "AgentPanelSpeaker/JsonlSessionMonitor.cs"
  replace_once(
    path,
    "      bool newOrderedItem = current.Count != 0 &&\n"
    "        fence.Length == 0 &&\n"
    "        word.SeparatorBefore.Contains('\\n') &&\n"
    "        IsOrderedListOrdinal(word.Text);\n"
    "      if (fenceChanged || newFenceLine || newOrderedItem)\n",
    "      bool newNavigationUnit = current.Count != 0 &&\n"
    "        fence.Length == 0 &&\n"
    "        word.NavigationBoundaryBefore;\n"
    "      if (fenceChanged || newFenceLine || newNavigationUnit)\n"
  )
  text = path.read_text(encoding="utf-8")
  pattern = re.compile(
    r"\n  private static bool IsOrderedListOrdinal\(string text\)\n"
    r"  \{\n"
    r"    if \(text\.Length < 2 \|\| text\[\^1\] is not \('\.' or '\)'\)\)\n"
    r"    \{\n"
    r"      return false;\n"
    r"    \}\n"
    r"    return text\[\.\.\^1\]\.All\(char\.IsDigit\);\n"
    r"  \}\n"
  )
  updated, count = pattern.subn("\n", text, count=1)
  if count != 1:
    raise RuntimeError("Expected exactly one obsolete ordered-list helper.")
  path.write_text(updated, encoding="utf-8")


def patch_speech_service() -> None:
  path = ROOT / "AgentPanelSpeaker/SpeechService.cs"
  replace_once(
    path,
    "  private const int MaximumHistoryEntries = 5000;\n",
    "  private const int MaximumHistoryEntries = 5000;\n"
    "  private static readonly TimeSpan RewindCurrentFragmentGracePeriod =\n"
    "    TimeSpan.FromMilliseconds(500);\n"
  )
  replace_once(
    path,
    "  private long _activeBoundaryTimestamp;\n"
    "  private string _activeWord = string.Empty;\n",
    "  private long _activeBoundaryTimestamp;\n"
    "  private bool _rewindCurrentFragmentGracePending;\n"
    "  private long? _rewindCurrentFragmentGraceStartedTimestamp;\n"
    "  private string _activeWord = string.Empty;\n"
  )
  replace_once(
    path,
    "      int anchor = GetNavigationAnchorLocked();\n"
    "      bool restartCurrent = anchor >= 0 && anchor < _history.Count &&\n"
    "        ((_pendingHistoryIndex == anchor && _pendingHistoryWordIndex > 0) ||\n"
    "         (_activeHistoryIndex == anchor && _activeWordIndex > 0));\n",
    "      int anchor = GetNavigationAnchorLocked();\n"
    "      bool pastFirstWord =\n"
    "        (_pendingHistoryIndex == anchor && _pendingHistoryWordIndex > 0) ||\n"
    "        (_activeHistoryIndex == anchor && _activeWordIndex > 0);\n"
    "      bool restartCurrent = anchor >= 0 && anchor < _history.Count &&\n"
    "        (pastFirstWord ||\n"
    "         IsRewindCurrentFragmentGraceActiveLocked(anchor));\n"
  )
  marker = """  /// <summary>\n  /// Moves one eligible fragment forward and continues playback.\n  /// </summary>\n  public bool TryForwardSentence(out string text)\n"""
  helpers = """  /// <summary>\n  /// Returns whether playback is still inside the first-word rewind grace\n  /// window for the current active history fragment.\n  /// </summary>\n  private bool IsRewindCurrentFragmentGraceActiveLocked(int anchor)\n  {\n    if (_activeKind != ActiveSpeechKind.History ||\n        _isPaused ||\n        _activeHistoryIndex != anchor ||\n        _activeWordIndex != 0)\n    {\n      return false;\n    }\n    if (_rewindCurrentFragmentGracePending)\n    {\n      return true;\n    }\n    return _rewindCurrentFragmentGraceStartedTimestamp is long started &&\n      Stopwatch.GetElapsedTime(started) < RewindCurrentFragmentGracePeriod;\n  }\n\n  /// <summary>\n  /// Prepares first-word rewind grace for a new history playback start.\n  /// The timer itself begins only when reading reaches word zero.\n  /// </summary>\n  private void PrepareRewindCurrentFragmentGraceLocked(int startWordIndex)\n  {\n    _rewindCurrentFragmentGracePending = startWordIndex == 0;\n    _rewindCurrentFragmentGraceStartedTimestamp = null;\n  }\n\n  /// <summary>\n  /// Starts first-word rewind grace at an actual playback/resume point.\n  /// </summary>\n  private void StartRewindCurrentFragmentGraceLocked()\n  {\n    _rewindCurrentFragmentGracePending = false;\n    _rewindCurrentFragmentGraceStartedTimestamp = Stopwatch.GetTimestamp();\n  }\n\n  /// <summary>\n  /// Clears first-word rewind grace when it cannot apply.\n  /// </summary>\n  private void ClearRewindCurrentFragmentGraceLocked()\n  {\n    _rewindCurrentFragmentGracePending = false;\n    _rewindCurrentFragmentGraceStartedTimestamp = null;\n  }\n\n"""
  replace_once(path, marker, helpers + marker)

  replace_once(
    path,
    "        else if (hasActiveUtterance)\n"
    "        {\n"
    "          _engine.Resume();\n"
    "          ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);\n"
    "        }\n",
    "        else if (hasActiveUtterance)\n"
    "        {\n"
    "          _engine.Resume();\n"
    "          if (_activeKind == ActiveSpeechKind.History &&\n"
    "              _activeWordIndex == 0)\n"
    "          {\n"
    "            StartRewindCurrentFragmentGraceLocked();\n"
    "          }\n"
    "          else\n"
    "          {\n"
    "            ClearRewindCurrentFragmentGraceLocked();\n"
    "          }\n"
    "          ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);\n"
    "        }\n"
  )
  replace_once(
    path,
    "      if (_activeKind != ActiveSpeechKind.None)\n"
    "      {\n"
    "        _engine.Pause();\n"
    "        _pauseStartedUtc = DateTimeOffset.UtcNow;\n",
    "      if (_activeKind != ActiveSpeechKind.None)\n"
    "      {\n"
    "        ClearRewindCurrentFragmentGraceLocked();\n"
    "        _engine.Pause();\n"
    "        _pauseStartedUtc = DateTimeOffset.UtcNow;\n"
  )

  replace_once(
    path,
    "      _activeWordIndex = Math.Max(_activeWordIndex, mappedWordIndex);\n"
    "      _activeWord = GetFragmentWordText(\n",
    "      _activeWordIndex = Math.Max(_activeWordIndex, mappedWordIndex);\n"
    "      if (_rewindCurrentFragmentGracePending)\n"
    "      {\n"
    "        if (_activeWordBaseIndex == 0 && _activeWordIndex == 0)\n"
    "        {\n"
    "          StartRewindCurrentFragmentGraceLocked();\n"
    "        }\n"
    "        else\n"
    "        {\n"
    "          ClearRewindCurrentFragmentGraceLocked();\n"
    "        }\n"
    "      }\n"
    "      _activeWord = GetFragmentWordText(\n"
  )

  replace_once(
    path,
    "    _activeProfile = profile.Normalize();\n"
    "    _activePauseAfter = pauseAfter;\n"
    "    _pauseStartedUtc = null;\n"
    "    SetActiveKindLocked(ActiveSpeechKind.History);\n"
    "    ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);\n",
    "    _activeProfile = profile.Normalize();\n"
    "    _activePauseAfter = pauseAfter;\n"
    "    _pauseStartedUtc = null;\n"
    "    PrepareRewindCurrentFragmentGraceLocked(boundedWordIndex);\n"
    "    SetActiveKindLocked(ActiveSpeechKind.History);\n"
    "    ReportPlaybackPositionLocked(TranscriptPlaybackState.Speaking);\n"
  )

  replace_once(
    path,
    "    _activeBoundaryTimestamp = Stopwatch.GetTimestamp();\n"
    "    SpeakConfiguredLocked(\n"
    "      remaining,\n"
    "      _activeProfile,\n"
    "      _activePauseAfter);\n",
    "    _activeBoundaryTimestamp = Stopwatch.GetTimestamp();\n"
    "    PrepareRewindCurrentFragmentGraceLocked(_activeWordIndex);\n"
    "    SpeakConfiguredLocked(\n"
    "      remaining,\n"
    "      _activeProfile,\n"
    "      _activePauseAfter);\n"
  )

  replace_once(
    path,
    "    _activeBoundaryTimestamp = Stopwatch.GetTimestamp();\n"
    "    ReportPlaybackPositionLocked(TranscriptPlaybackState.Paused);\n"
    "    DiagnosticLog.Write(\"speech.paused_navigation\", new\n",
    "    _activeBoundaryTimestamp = Stopwatch.GetTimestamp();\n"
    "    ClearRewindCurrentFragmentGraceLocked();\n"
    "    ReportPlaybackPositionLocked(TranscriptPlaybackState.Paused);\n"
    "    DiagnosticLog.Write(\"speech.paused_navigation\", new\n"
  )


def patch_pins_and_runtime() -> None:
  replace_all_exact(
    ROOT / "AgentPanelSpeaker/AIConversationCoreClient.cs",
    OLD_CORE,
    NEW_CORE)
  replace_all_exact(
    ROOT / "tools/AIConversationCore-worker.mjs",
    OLD_CORE,
    NEW_CORE)
  replace_all_exact(
    ROOT / ".github/workflows/core-integration-validation.yml",
    OLD_CORE,
    NEW_CORE,
    minimum=3)
  replace_all_exact(ROOT / "DESIGN.md", OLD_CORE, NEW_CORE)

  readme = ROOT / "README.md"
  text = readme.read_text(encoding="utf-8")
  updated, count = re.subn(
    r"(The runtime uses one persistent Node bridge \(`tools/AIConversationCore-worker\.mjs`\) pinned to AIConversationCore commit `)[0-9a-f]{40}(`\.)",
    rf"\g<1>{NEW_CORE}\2",
    text,
    count=1)
  if count != 1:
    raise RuntimeError("README current Core-pin statement was not found exactly once.")
  readme.write_text(updated, encoding="utf-8")

  runtime = ROOT / "tools/AIConversationCore-runtime"
  source = ROOT / "dependencies/AIConversationCore/src"
  runtime_source = runtime / "src"
  if runtime_source.exists():
    shutil.rmtree(runtime_source)
  shutil.copytree(source, runtime_source)
  (runtime / "CORE_COMMIT").write_text(NEW_CORE + "\n", encoding="utf-8")


def patch_production() -> None:
  patch_projection()
  patch_monitor()
  patch_speech_service()
  patch_pins_and_runtime()


def main() -> None:
  parser = argparse.ArgumentParser()
  parser.add_argument("mode", choices=("test", "production"))
  args = parser.parse_args()
  if args.mode == "test":
    install_tests()
  else:
    patch_production()


if __name__ == "__main__":
  main()
