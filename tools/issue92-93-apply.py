from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "AgentPanelSpeaker"


def read(path):
  return path.read_text(encoding="utf-8")


def write(path, text):
  path.write_text(text, encoding="utf-8", newline="\n")


def replace_once(path, old, new):
  text = read(path)
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"Expected exactly one match in {path}: {count}\n--- old ---\n{old}")
  write(path, text.replace(old, new, 1))


def replace_section(path, start_marker, end_marker, replacement):
  text = read(path)
  start = text.find(start_marker)
  if start < 0:
    raise RuntimeError(f"Start marker not found in {path}: {start_marker}")
  end = text.find(end_marker, start)
  if end < 0:
    raise RuntimeError(f"End marker not found in {path}: {end_marker}")
  write(path, text[:start] + replacement + text[end:])


def add_tests():
  issue92 = r'''namespace AgentPanelSpeaker;

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
'''

  issue93 = r'''using System.Reflection;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #93 System.Speech SSML provenance.
/// </summary>
internal static class Issue93SystemSpeechProvenanceRegressionTestRunner
{
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("system-speech-provenance/spell-out-many-events-one-word",
        TestSpellOutManyEventsOneWord),
      ("system-speech-provenance/provider-range-multiple-words",
        TestProviderRangeCanOwnMultipleWords),
      ("system-speech-provenance/ordinary-event-remains-exact",
        TestOrdinaryEventRemainsExact),
      ("system-speech-provenance/no-fixed-offset-or-string-sequence",
        TestNoFixedOffsetOrStringSequenceMapper),
      ("system-speech-provenance/no-text-completeness-rejection",
        TestNoTextCompletenessRejection)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #93 System.Speech provenance suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #93 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #93 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  private static void TestSpellOutManyEventsOneWord()
  {
    SpeechMarkup markup = BuildTrackedMarkup(
      "For local code/files, I can cite exact file locations like " +
        "scripts/AI-transcript.py.",
      new[] { "AI" });
    string ssml = BuildSsmlDocument(markup);
    int aiStart = ssml.IndexOf("AI</say-as>", StringComparison.Ordinal);
    Require(aiStart >= 0, "Generated SSML has no spell-out AI text.");

    SpeechWordBoundary a = Map(
      markup,
      ssml,
      aiStart,
      1,
      "A",
      TimeSpan.FromMilliseconds(100));
    SpeechWordBoundary i = Map(
      markup,
      ssml,
      aiStart + 1,
      1,
      "I",
      TimeSpan.FromMilliseconds(150));

    int expected = markup.Words!
      .Single(word => word.Text == "AI-transcript")
      .WordIndex;
    Require(a.WordIndex == expected && i.WordIndex == expected,
      "Separate A/I progress events did not retain AI-transcript ownership.");
    Require(a.WordCount == 1 && i.WordCount == 1,
      "Spell-out events unexpectedly widened canonical ownership.");
    Require(a.Exact && i.Exact,
      "Provider-native spell-out timing is not marked exact.");
  }

  private static void TestProviderRangeCanOwnMultipleWords()
  {
    SpeechMarkup markup = BuildTrackedMarkup(
      "scripts/AI-transcript.py.",
      new[] { "AI" });
    string ssml = BuildSsmlDocument(markup);
    int start = ssml.IndexOf("-transcript.py", StringComparison.Ordinal);
    Require(start >= 0, "Generated SSML has no transformed filename tail.");

    SpeechWordBoundary boundary = Map(
      markup,
      ssml,
      start,
      "-transcript.py".Length,
      "-transcript.py",
      TimeSpan.FromMilliseconds(200));

    int first = markup.Words!
      .Single(word => word.Text == "AI-transcript")
      .WordIndex;
    Require(boundary.WordIndex == first,
      "Transformed filename tail did not start at AI-transcript.");
    Require(boundary.WordCount == 3,
      "One provider range did not retain its three canonical owners.");
  }

  private static void TestOrdinaryEventRemainsExact()
  {
    SpeechMarkup markup = BuildTrackedMarkup("alpha beta", Array.Empty<string>());
    string ssml = BuildSsmlDocument(markup);
    int start = ssml.IndexOf("beta", StringComparison.Ordinal);
    Require(start >= 0, "Generated SSML has no beta text.");
    SpeechWordBoundary boundary = Map(
      markup,
      ssml,
      start,
      4,
      "beta",
      TimeSpan.FromMilliseconds(250));
    Require(boundary.WordIndex == 1 && boundary.WordCount == 1,
      "Ordinary provider progress did not map to the exact second word.");
    Require(boundary.Exact,
      "Ordinary provider-native timing is not exact.");
  }

  private static void TestNoFixedOffsetOrStringSequenceMapper()
  {
    string source = ReadSource("SapiSpeechEngine.cs");
    Require(!source.Contains("synthesisCharacterOffset", StringComparison.Ordinal),
      "System.Speech still assumes one fixed synthesis/plain-text offset.");
    Require(!source.Contains("FindSequentialTokenIndex", StringComparison.Ordinal),
      "System.Speech still reverse-matches provider text sequentially.");
    Require(source.Contains("SsmlProvenance", StringComparison.Ordinal),
      "System.Speech does not consume explicit SSML provenance.");
  }

  private static void TestNoTextCompletenessRejection()
  {
    string source = ReadSource("SapiSpeechEngine.cs");
    Require(!source.Contains(
        "system_speech_incomplete_word_mapping",
        StringComparison.Ordinal),
      "System.Speech still rejects exact provider timing merely because " +
        "not every textual token produced a progress event.");
  }

  private static SpeechMarkup BuildTrackedMarkup(
    string text,
    IReadOnlyList<string> spelledWords)
  {
    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
      text,
      0,
      spelledWords,
      PronunciationRuleSet.Parse(string.Empty));
    MatchCollection matches = SpeechTokenization.Matches(text);
    SpeechMarkupWord[] words = matches
      .Cast<Match>()
      .Select((match, index) => new SpeechMarkupWord(
        index,
        match.Value,
        match.Index,
        match.Length))
      .ToArray();
    return markup with { Words = words };
  }

  private static string BuildSsmlDocument(SpeechMarkup markup)
  {
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "BuildSsmlDocument",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("BuildSsmlDocument is missing.");
    return method.Invoke(null, new object[] { markup.SsmlContent, "en-US" })
      as string ?? throw new InvalidOperationException("SSML build failed.");
  }

  private static SpeechWordBoundary Map(
    SpeechMarkup markup,
    string ssml,
    int characterPosition,
    int characterCount,
    string spokenText,
    TimeSpan audioPosition)
  {
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "TryMapSystemSpeechProgress",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "System.Speech has no provenance-based progress mapper.");
    object?[] arguments =
    {
      markup,
      ssml,
      characterPosition,
      characterCount,
      spokenText,
      audioPosition,
      null
    };
    bool mapped = Convert.ToBoolean(method.Invoke(null, arguments));
    Require(mapped, $"Provider range {characterPosition}:{characterCount} did not map.");
    return arguments[6] as SpeechWordBoundary ??
      throw new InvalidOperationException("Mapped boundary was not returned.");
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
'''

  write(APP / "Issue92PreviewCursorRegressionTestRunner.cs", issue92)
  write(APP / "Issue93SystemSpeechProvenanceRegressionTestRunner.cs", issue93)

  program = APP / "Program.cs"
  handler_anchor = '''      if (args.Length == 2 &&
          string.Equals(args[1], "redundancy", StringComparison.OrdinalIgnoreCase))
'''
  handlers = '''      if (args.Length == 2 &&
          string.Equals(
            args[1],
            "preview-cursor",
            StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = RunNamedSuite(
          "preview-cursor",
          Issue92PreviewCursorRegressionTestRunner.Run);
        return;
      }

      if (args.Length == 2 &&
          string.Equals(
            args[1],
            "system-speech-provenance",
            StringComparison.OrdinalIgnoreCase))
      {
        Environment.ExitCode = RunNamedSuite(
          "system-speech-provenance",
          Issue93SystemSpeechProvenanceRegressionTestRunner.Run);
        return;
      }

'''
  text = read(program)
  if "Issue92PreviewCursorRegressionTestRunner.Run" not in text:
    if handler_anchor not in text:
      raise RuntimeError("Program suite handler anchor missing")
    text = text.replace(handler_anchor, handlers + handler_anchor, 1)

    variable_anchor = '''      int speechOwnership = RunIsolatedTestSuite(
        "speech-ownership");
'''
    variables = variable_anchor + '''      int previewCursor = RunIsolatedTestSuite(
        "preview-cursor");
      int systemSpeechProvenance = RunIsolatedTestSuite(
        "system-speech-provenance");
'''
    if variable_anchor not in text:
      raise RuntimeError("Program suite variable anchor missing")
    text = text.replace(variable_anchor, variables, 1)

    result_anchor = '''                             rewindCurrentFragment == 0 &&
                             speechOwnership == 0
'''
    result = '''                             rewindCurrentFragment == 0 &&
                             speechOwnership == 0 &&
                             previewCursor == 0 &&
                             systemSpeechProvenance == 0
'''
    if result_anchor not in text:
      raise RuntimeError("Program suite result anchor missing")
    text = text.replace(result_anchor, result, 1)
    write(program, text)


def patch_speech_markup():
  path = APP / "SpeechMarkup.cs"
  old = '''internal sealed record SpeechMarkupWord(
  int WordIndex,
  string Text,
  int CharacterStart,
  int CharacterLength);

/// <summary>
/// Carries source text plus equivalent speech markup and, when available,
/// exact fragment-relative ownership for each canonical transcript word.
/// </summary>
internal sealed record SpeechMarkup(
  string PlainText,
  string SapiXml,
  string SsmlContent,
  IReadOnlyList<SpeechMarkupWord>? Words = null);
'''
  new = '''internal sealed record SpeechMarkupWord(
  int WordIndex,
  string Text,
  int CharacterStart,
  int CharacterLength);

/// <summary>
/// Maps one raw SSML text range back to the source characters that caused it.
/// Markup-only ranges are deliberately absent from this inventory.
/// </summary>
internal sealed record SpeechMarkupProvenanceSpan(
  int SsmlCharacterStart,
  int SsmlCharacterLength,
  int SourceCharacterStart,
  int SourceCharacterLength);

/// <summary>
/// Carries source text plus equivalent speech markup and, when available,
/// exact fragment-relative ownership for each canonical transcript word.
/// </summary>
internal sealed record SpeechMarkup(
  string PlainText,
  string SapiXml,
  string SsmlContent,
  IReadOnlyList<SpeechMarkupWord>? Words = null,
  IReadOnlyList<SpeechMarkupProvenanceSpan>? SsmlProvenance = null);
'''
  replace_once(path, old, new)


def patch_builder():
  path = APP / "SpeechSapiXmlBuilder.cs"
  text = read(path)

  old_build = '''    string ssmlContent = ConvertContentToSsml(sapiContent);
    return new SpeechMarkup(
      text,
      WrapSapiPitch(sapiContent, pitchSetting),
      WrapSsmlPitch(ssmlContent, pitchSetting));
'''
  new_build = '''    (
      string ssmlContent,
      IReadOnlyList<SpeechMarkupProvenanceSpan> ssmlProvenance) =
        BuildSsmlContent(
          text,
          spelledWords,
          pronunciations,
          pauseAfter,
          pauseBefore);
    (
      string wrappedSsml,
      IReadOnlyList<SpeechMarkupProvenanceSpan> wrappedProvenance) =
        WrapSsmlPitch(ssmlContent, pitchSetting, ssmlProvenance);
    return new SpeechMarkup(
      text,
      WrapSapiPitch(sapiContent, pitchSetting),
      wrappedSsml,
      SsmlProvenance: wrappedProvenance);
'''
  if old_build not in text:
    raise RuntimeError("SpeechSapiXmlBuilder Build anchor missing")
  text = text.replace(old_build, new_build, 1)

  old_detection = '''      Match dateTimeMatch = IsoDateTimeRegex().Match(text, position);
      Match dateMatch = IsoDateRegex().Match(text, position);
      Match numericDateMatch = NumericDateRegex().Match(text, position);
      Match timeMatch = TimeRegex().Match(text, position);
      Match? spelledMatch = spelledWordRegex?.Match(text, position);
      PronunciationMatch? pronunciationMatch = pronunciations.FindNext(
        text,
        position);

      SpecialMatch? next = Earliest(
        pronunciationMatch is null
          ? null
          : new SpecialMatch(
            pronunciationMatch.Match,
            SpecialMatchKind.Pronunciation,
            pronunciationMatch.Rule),
        spelledMatch is null
          ? null
          : new SpecialMatch(
            spelledMatch,
            SpecialMatchKind.Spelling,
            null),
        new SpecialMatch(
          dateTimeMatch,
          SpecialMatchKind.IsoDateTime,
          null),
        new SpecialMatch(dateMatch, SpecialMatchKind.IsoDate, null),
        new SpecialMatch(
          numericDateMatch,
          SpecialMatchKind.NumericDate,
          null),
        new SpecialMatch(timeMatch, SpecialMatchKind.Time, null));
'''
  new_detection = '''      SpecialMatch? next = FindNextSpecialMatch(
        text,
        position,
        spelledWordRegex,
        pronunciations);
'''
  if old_detection not in text:
    raise RuntimeError("SpeechSapiXmlBuilder special-match anchor missing")
  text = text.replace(old_detection, new_detection, 1)

  start_marker = '''  /// <summary>
  /// Converts native SAPI spelling elements into SSML spelling elements.
  /// </summary>
'''
  end_marker = '''  /// <summary>
  /// Returns the earliest successful match and uses kind order on ties.
  /// </summary>
'''
  start = text.find(start_marker)
  end = text.find(end_marker, start)
  if start < 0 or end < 0:
    raise RuntimeError("SpeechSapiXmlBuilder conversion section missing")

  provenance_methods = r'''  /// <summary>
  /// Builds standards-based SSML while retaining exact source provenance for
  /// every emitted text range. XML-only syntax and inserted pauses have no
  /// canonical source owner.
  /// </summary>
  private static (
    string Content,
    IReadOnlyList<SpeechMarkupProvenanceSpan> Provenance) BuildSsmlContent(
      string text,
      IReadOnlyList<string> spelledWords,
      PronunciationRuleSet pronunciations,
      bool pauseAfter,
      bool pauseBefore)
  {
    Regex? spelledWordRegex = CreateSpelledWordRegex(spelledWords);
    var output = new StringBuilder();
    var provenance = new List<SpeechMarkupProvenanceSpan>();
    if (pauseBefore)
    {
      output.Append(SsmlBlockPause);
    }

    int position = 0;
    while (position < text.Length)
    {
      SpecialMatch? next = FindNextSpecialMatch(
        text,
        position,
        spelledWordRegex,
        pronunciations);
      if (next is null || !next.Match.Success)
      {
        AppendMappedIdentity(
          output,
          text[position..],
          position,
          provenance);
        break;
      }

      AppendMappedIdentity(
        output,
        text[position..next.Match.Index],
        position,
        provenance);
      switch (next.Kind)
      {
        case SpecialMatchKind.Pronunciation:
          PronunciationRule pronunciation = next.Pronunciation!;
          if (pronunciation.Kind == PronunciationRuleKind.Ipa)
          {
            output.Append("<phoneme alphabet=\"ipa\" ph=\"");
            AppendAttributeEscaped(output, pronunciation.Value);
            output.Append("\">");
            AppendMappedIdentity(
              output,
              next.Match.Value,
              next.Match.Index,
              provenance);
            output.Append("</phoneme>");
          }
          else
          {
            AppendMappedReplacement(
              output,
              pronunciation.Value,
              next.Match.Index,
              next.Match.Length,
              provenance);
          }
          break;

        case SpecialMatchKind.Spelling:
          output.Append("<break time=\"100ms\"/>");
          output.Append("<say-as interpret-as=\"spell-out\">");
          AppendMappedIdentity(
            output,
            next.Match.Value,
            next.Match.Index,
            provenance);
          output.Append("</say-as><break time=\"100ms\"/>");
          break;

        case SpecialMatchKind.IsoDateTime:
          AppendMappedReplacement(
            output,
            FormatIsoDateTime(next.Match.Value),
            next.Match.Index,
            next.Match.Length,
            provenance);
          break;

        case SpecialMatchKind.IsoDate:
          AppendMappedReplacement(
            output,
            FormatIsoDate(next.Match.Value),
            next.Match.Index,
            next.Match.Length,
            provenance);
          break;

        case SpecialMatchKind.NumericDate:
          AppendMappedReplacement(
            output,
            FormatNumericDate(next.Match.Value),
            next.Match.Index,
            next.Match.Length,
            provenance);
          break;

        case SpecialMatchKind.Time:
          AppendMappedReplacement(
            output,
            FormatTime(next.Match.Value),
            next.Match.Index,
            next.Match.Length,
            provenance);
          break;
      }
      position = next.Match.Index + next.Match.Length;
    }

    if (pauseAfter)
    {
      output.Append(SsmlBlockPause);
    }
    return (output.ToString(), provenance);
  }

  /// <summary>
  /// Finds the next speech transformation once so native and SSML rendering
  /// share identical source segmentation.
  /// </summary>
  private static SpecialMatch? FindNextSpecialMatch(
    string text,
    int position,
    Regex? spelledWordRegex,
    PronunciationRuleSet pronunciations)
  {
    Match dateTimeMatch = IsoDateTimeRegex().Match(text, position);
    Match dateMatch = IsoDateRegex().Match(text, position);
    Match numericDateMatch = NumericDateRegex().Match(text, position);
    Match timeMatch = TimeRegex().Match(text, position);
    Match? spelledMatch = spelledWordRegex?.Match(text, position);
    PronunciationMatch? pronunciationMatch = pronunciations.FindNext(
      text,
      position);

    return Earliest(
      pronunciationMatch is null
        ? null
        : new SpecialMatch(
          pronunciationMatch.Match,
          SpecialMatchKind.Pronunciation,
          pronunciationMatch.Rule),
      spelledMatch is null
        ? null
        : new SpecialMatch(
          spelledMatch,
          SpecialMatchKind.Spelling,
          null),
      new SpecialMatch(
        dateTimeMatch,
        SpecialMatchKind.IsoDateTime,
        null),
      new SpecialMatch(dateMatch, SpecialMatchKind.IsoDate, null),
      new SpecialMatch(
        numericDateMatch,
        SpecialMatchKind.NumericDate,
        null),
      new SpecialMatch(timeMatch, SpecialMatchKind.Time, null));
  }

  /// <summary>
  /// Appends identity text one source UTF-16 code unit at a time so XML
  /// escaping cannot destroy the synthesis-to-source coordinate relation.
  /// </summary>
  private static void AppendMappedIdentity(
    StringBuilder output,
    string text,
    int sourceStart,
    ICollection<SpeechMarkupProvenanceSpan> provenance)
  {
    for (int index = 0; index < text.Length; ++index)
    {
      string escaped = SecurityElement.Escape(text[index].ToString()) ??
        string.Empty;
      int ssmlStart = output.Length;
      output.Append(escaped);
      if (escaped.Length != 0)
      {
        provenance.Add(new SpeechMarkupProvenanceSpan(
          ssmlStart,
          escaped.Length,
          sourceStart + index,
          1));
      }
    }
  }

  /// <summary>
  /// Appends transformed speech text whose complete emitted range is owned by
  /// the complete original source range.
  /// </summary>
  private static void AppendMappedReplacement(
    StringBuilder output,
    string text,
    int sourceStart,
    int sourceLength,
    ICollection<SpeechMarkupProvenanceSpan> provenance)
  {
    string escaped = SecurityElement.Escape(text) ?? string.Empty;
    int ssmlStart = output.Length;
    output.Append(escaped);
    if (escaped.Length != 0 && sourceLength > 0)
    {
      provenance.Add(new SpeechMarkupProvenanceSpan(
        ssmlStart,
        escaped.Length,
        sourceStart,
        sourceLength));
    }
  }

'''
  text = text[:start] + provenance_methods + text[end:]

  old_wrap = '''  private static string WrapSsmlPitch(string content, int pitchSetting)
  {
    int pitchPercent = Math.Clamp(pitchSetting, -10, 10) *
      SsmlPitchPercentPerStep;
    string pitch = pitchPercent > 0
      ? $"+{pitchPercent}%"
      : $"{pitchPercent}%";
    return $"<prosody pitch=\"{pitch}\">{content}</prosody>";
  }
'''
  new_wrap = '''  private static string WrapSsmlPitch(string content, int pitchSetting)
  {
    return WrapSsmlPitch(
      content,
      pitchSetting,
      Array.Empty<SpeechMarkupProvenanceSpan>()).Content;
  }

  private static (
    string Content,
    IReadOnlyList<SpeechMarkupProvenanceSpan> Provenance) WrapSsmlPitch(
      string content,
      int pitchSetting,
      IReadOnlyList<SpeechMarkupProvenanceSpan> provenance)
  {
    int pitchPercent = Math.Clamp(pitchSetting, -10, 10) *
      SsmlPitchPercentPerStep;
    string pitch = pitchPercent > 0
      ? $"+{pitchPercent}%"
      : $"{pitchPercent}%";
    string prefix = $"<prosody pitch=\"{pitch}\">";
    SpeechMarkupProvenanceSpan[] shifted = provenance
      .Select(span => span with
      {
        SsmlCharacterStart = checked(span.SsmlCharacterStart + prefix.Length)
      })
      .ToArray();
    return ($"{prefix}{content}</prosody>", shifted);
  }
'''
  if old_wrap not in text:
    raise RuntimeError("SpeechSapiXmlBuilder WrapSsmlPitch anchor missing")
  text = text.replace(old_wrap, new_wrap, 1)
  write(path, text)


def patch_system_speech():
  path = APP / "SapiSpeechEngine.cs"
  text = read(path)
  start_marker = '''  /// <summary>
  /// Renders System.Speech SSML into an in-memory WAVE file.
  /// </summary>
'''
  end_marker = '''  /// <summary>
  /// Renders a modern Windows voice into its returned audio/WAVE stream.
  /// </summary>
'''
  start = text.find(start_marker)
  end = text.find(end_marker, start)
  if start < 0 or end < 0:
    raise RuntimeError("System.Speech render section missing")

  replacement = r'''  /// <summary>
  /// Renders System.Speech SSML into an in-memory WAVE file.
  /// </summary>
  private static PcmWaveData RenderSystemSpeech(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    string providerVoiceId,
    SystemSpeechSynthesizer synthesizer,
    out IReadOnlyList<SpeechWordBoundary> boundaries,
    out SpeechTrackingDegradation? trackingDegradation)
  {
    using var stream = new MemoryStream();
    var collected = new List<SpeechWordBoundary>();
    bool mappingFailed = false;

    synthesizer.SelectVoice(providerVoiceId);
    synthesizer.Rate = profile.Rate;
    synthesizer.Volume = profile.Volume;
    string ssml = BuildSsmlDocument(
      markup.SsmlContent,
      synthesizer.Voice.Culture.Name);

    EventHandler<System.Speech.Synthesis.SpeakProgressEventArgs> handler =
      (_, eventArgs) =>
      {
        if (!TryMapSystemSpeechProgress(
              markup,
              ssml,
              eventArgs.CharacterPosition,
              eventArgs.CharacterCount,
              eventArgs.Text,
              eventArgs.AudioPosition,
              out SpeechWordBoundary? boundary))
        {
          mappingFailed = true;
          DiagnosticLog.Write("sapi.speak_progress_unmapped", new
          {
            provider = "System.Speech",
            voice = providerVoiceId,
            eventArgs.Text,
            eventArgs.CharacterPosition,
            eventArgs.CharacterCount,
            eventArgs.AudioPosition
          });
          return;
        }

        DiagnosticLog.Write("sapi.speak_progress", new
        {
          provider = "System.Speech",
          voice = providerVoiceId,
          markup.PlainText,
          eventArgs.Text,
          eventArgs.CharacterPosition,
          eventArgs.CharacterCount,
          eventArgs.AudioPosition,
          sourcePosition = boundary.CharacterPosition,
          sourceCount = boundary.CharacterCount,
          wordIndex = boundary.WordIndex,
          wordCount = boundary.WordCount
        });
        collected.Add(boundary);
      };

    synthesizer.SpeakProgress += handler;
    try
    {
      var outputFormat = new SpeechAudioFormatInfo(
        SystemSpeechSampleRate,
        AudioBitsPerSample.Sixteen,
        AudioChannel.Mono);
      synthesizer.SetOutputToAudioStream(stream, outputFormat);
      synthesizer.SpeakSsml(ssml);
    }
    finally
    {
      synthesizer.SpeakProgress -= handler;
      synthesizer.SetOutputToNull();
    }

    PcmWaveData wave = PcmWaveData.FromPcmSamples(
      channels: 1,
      sampleRate: SystemSpeechSampleRate,
      bitsPerSample: 16,
      samples: stream.ToArray());

    int expectedWordCount = GetSystemSpeechWords(markup).Count;
    if (expectedWordCount == 0)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      trackingDegradation = null;
    }
    else if (collected.Count == 0)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      trackingDegradation = new SpeechTrackingDegradation(
        "SystemSpeech",
        providerVoiceId,
        "system_speech_returned_no_progress_events");
    }
    else if (mappingFailed)
    {
      boundaries = Array.Empty<SpeechWordBoundary>();
      trackingDegradation = new SpeechTrackingDegradation(
        "SystemSpeech",
        providerVoiceId,
        "system_speech_word_mapping_failed");
    }
    else
    {
      boundaries = collected;
      trackingDegradation = null;
    }
    return wave;
  }

  /// <summary>
  /// Translates one provider-native System.Speech progress range through the
  /// exact SSML provenance created with the speech markup.
  /// </summary>
  private static bool TryMapSystemSpeechProgress(
    SpeechMarkup markup,
    string ssml,
    int characterPosition,
    int characterCount,
    string spokenText,
    TimeSpan audioPosition,
    out SpeechWordBoundary? boundary)
  {
    boundary = null;
    if (markup.SsmlProvenance is not { Count: > 0 } provenance)
    {
      return false;
    }

    int contentStart = ssml.IndexOf(
      markup.SsmlContent,
      StringComparison.Ordinal);
    if (contentStart < 0)
    {
      return false;
    }

    int eventStart = characterPosition - contentStart;
    int eventLength = Math.Max(1, characterCount);
    int eventEnd;
    try
    {
      eventEnd = checked(eventStart + eventLength);
    }
    catch (OverflowException)
    {
      return false;
    }
    if (eventEnd <= 0 || eventStart >= markup.SsmlContent.Length)
    {
      return false;
    }

    SpeechMarkupProvenanceSpan[] owners = provenance
      .Where(span =>
        span.SsmlCharacterStart < eventEnd &&
        span.SsmlCharacterStart + span.SsmlCharacterLength > eventStart)
      .ToArray();
    if (owners.Length == 0)
    {
      return false;
    }

    IReadOnlyList<SpeechMarkupWord> words = GetSystemSpeechWords(markup);
    SpeechMarkupWord[] ownedWords = words
      .Where(word => owners.Any(owner =>
      {
        int ownerEnd = owner.SourceCharacterStart + owner.SourceCharacterLength;
        int wordEnd = word.CharacterStart + word.CharacterLength;
        return owner.SourceCharacterStart < wordEnd &&
          ownerEnd > word.CharacterStart;
      }))
      .GroupBy(word => word.WordIndex)
      .Select(group => group.First())
      .OrderBy(word => word.WordIndex)
      .ToArray();
    if (ownedWords.Length == 0)
    {
      return false;
    }

    for (int index = 1; index < ownedWords.Length; ++index)
    {
      if (ownedWords[index].WordIndex != ownedWords[index - 1].WordIndex + 1)
      {
        return false;
      }
    }

    SpeechMarkupWord first = ownedWords[0];
    SpeechMarkupWord last = ownedWords[^1];
    int sourceEnd = checked(last.CharacterStart + last.CharacterLength);
    boundary = new SpeechWordBoundary(
      audioPosition,
      first.WordIndex,
      first.CharacterStart,
      sourceEnd - first.CharacterStart,
      spokenText,
      Exact: true,
      WordCount: last.WordIndex - first.WordIndex + 1);
    return true;
  }

  /// <summary>
  /// Returns exact attached fragment words, or a plain-text token inventory for
  /// untracked preview speech that has no canonical transcript attachment.
  /// </summary>
  private static IReadOnlyList<SpeechMarkupWord> GetSystemSpeechWords(
    SpeechMarkup markup)
  {
    if (markup.Words is { Count: > 0 } exactWords)
    {
      return exactWords;
    }

    MatchCollection matches = SpeechTokenization.Matches(markup.PlainText);
    return matches
      .Cast<Match>()
      .Select((match, index) => new SpeechMarkupWord(
        index,
        match.Value,
        match.Index,
        match.Length))
      .ToArray();
  }

'''
  text = text[:start] + replacement + text[end:]
  write(path, text)


def patch_preview_cursor():
  service = APP / "SpeechService.cs"
  text = read(service)

  field_old = '''  private bool _previewActive;
  private bool _previewReturnToPause;
  private FenceActivityKey? _lastFenceActivity;
'''
  field_new = '''  private bool _previewActive;
  private bool _previewReturnToPause;
  private TaskCompletionSource<bool>? _previewCancellationCompletion;
  private FenceActivityKey? _lastFenceActivity;
'''
  if field_old not in text:
    raise RuntimeError("SpeechService preview field anchor missing")
  text = text.replace(field_old, field_new, 1)

  cancel_old = '''  /// <summary>
  /// Cancels speech and returns playback to the live end.
  /// </summary>
  public void CancelAll()
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      MoveToLiveEndLocked();
    }
  }

'''
  cancel_new = '''  /// <summary>
  /// Cancels speech and explicitly abandons navigation at the live end.
  /// </summary>
  public void CancelAndMoveToLiveEnd()
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      MoveToLiveEndLocked();
    }
  }

  /// <summary>
  /// Cancels only preview audio and preserves the exact paused transcript
  /// cursor. The returned task completes after the engine cancellation callback
  /// has restored the paused state.
  /// </summary>
  public Task CancelPreviewPreservingPositionAsync()
  {
    lock (_sync)
    {
      ThrowIfDisposed();
      if (!_previewActive && _pendingPreviewStart is null)
      {
        return Task.CompletedTask;
      }
      if (_previewCancellationCompletion is not null)
      {
        return _previewCancellationCompletion.Task;
      }

      if (_activeKind == ActiveSpeechKind.History &&
          _activeHistoryIndex >= 0)
      {
        _pendingHistoryIndex = _activeHistoryIndex;
        _pendingHistoryWordIndex = Math.Max(0, _activeWordIndex);
        _nextHistoryIndex = _activeHistoryIndex;
      }

      _pendingPreviewStart = null;
      _previewReturnToPause = true;
      RequestPauseRestoreAfterCancellationLocked(
        "preview-cancel-preserve-cursor");
      _previewCancellationCompletion =
        new TaskCompletionSource<bool>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      DiagnosticLog.Write("speech.preview_cancel_preserve_cursor", new
      {
        activeKind = _activeKind.ToString(),
        activeHistoryIndex = _activeHistoryIndex,
        pendingHistoryIndex = _pendingHistoryIndex,
        pendingHistoryWordIndex = _pendingHistoryWordIndex,
        nextHistoryIndex = _nextHistoryIndex,
        isPaused = _isPaused
      });
      _engine.Cancel();
      return _previewCancellationCompletion.Task;
    }
  }

'''
  if cancel_old not in text:
    raise RuntimeError("SpeechService CancelAll anchor missing")
  text = text.replace(cancel_old, cancel_new, 1)

  completed_anchor = '''      if (_disposed)
      {
        return;
      }

      ActiveSpeechKind completedKind = _activeKind;
'''
  completed_new = '''      if (_disposed)
      {
        return;
      }

      TaskCompletionSource<bool>? previewCancellation =
        _previewCancellationCompletion;
      _previewCancellationCompletion = null;
      previewCancellation?.TrySetResult(true);

      ActiveSpeechKind completedKind = _activeKind;
'''
  if completed_anchor not in text:
    raise RuntimeError("SpeechService EngineCompleted anchor missing")
  text = text.replace(completed_anchor, completed_new, 1)
  write(service, text)

  main = APP / "MainForm.cs"
  text = read(main)
  voice_old = '''      string message = context
        ? row.ContextPreviewMessage
        : row.MainPreviewMessage;
      _speech.SpeakUntracked(message, profile);
'''
  voice_new = '''      string message = context
        ? row.ContextPreviewMessage
        : row.MainPreviewMessage;
      _speech.PreviewText(
        message,
        profile,
        _settingsStore.GetAudioWakeSettings());
'''
  if voice_old not in text:
    raise RuntimeError("MainForm voice-preview anchor missing")
  text = text.replace(voice_old, voice_new, 1)

  text = text.replace(
    "_speech.CancelAll();",
    "_speech.CancelAndMoveToLiveEnd();",
    1)

  play_old = '''    if (_voiceSettingPreviewActive)
    {
      StopVoicePreviewTimers();
      _voiceSettingPreviewActive = false;
      _speech.CancelAll();
    }

    _playPauseTransitioning = true;
    UpdateControlState();
    try
    {
      await StartMonitoringAsync();
'''
  play_new = '''    _playPauseTransitioning = true;
    UpdateControlState();
    try
    {
      if (_voiceSettingPreviewActive)
      {
        StopVoicePreviewTimers();
        _voiceSettingPreviewActive = false;
        await _speech.CancelPreviewPreservingPositionAsync();
      }

      await StartMonitoringAsync();
'''
  if play_old not in text:
    raise RuntimeError("MainForm play/preview cancellation anchor missing")
  text = text.replace(play_old, play_new, 1)
  if "_speech.CancelAll();" in text:
    raise RuntimeError("Unconverted MainForm CancelAll call remains")
  write(main, text)


def apply_green():
  add_tests()
  patch_speech_markup()
  patch_builder()
  patch_system_speech()
  patch_preview_cursor()


if len(sys.argv) != 2 or sys.argv[1] not in {"red", "green"}:
  raise SystemExit("usage: issue92-93-apply.py red|green")

if sys.argv[1] == "red":
  add_tests()
else:
  apply_green()
