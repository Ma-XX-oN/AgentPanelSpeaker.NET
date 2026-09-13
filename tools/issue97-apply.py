from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
TEST = ROOT / "AgentPanelSpeaker" / "Issue93SystemSpeechProvenanceRegressionTestRunner.cs"
BUILDER = ROOT / "AgentPanelSpeaker" / "SpeechSapiXmlBuilder.cs"


def replace_once(path: Path, old: str, new: str) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"Expected one match in {path}, found {count}: {old[:120]!r}")
  path.write_text(text.replace(old, new, 1), encoding="utf-8")


def apply_red() -> None:
  replace_once(
    TEST,
    '      ("system-speech-provenance/provider-boundary-payload-logged",\n'
    '        TestProviderBoundaryPayloadLogged),',
    '      ("system-speech-provenance/hyphenated-sub-alias-maps-source",\n'
    '        TestHyphenatedSubAliasMapsSource),\n'
    '      ("system-speech-provenance/provider-boundary-payload-logged",\n'
    '        TestProviderBoundaryPayloadLogged),')

  marker = '  private static void TestProviderBoundaryPayloadLogged()\n'
  method = '''  private static void TestHyphenatedSubAliasMapsSource()\n  {\n    const string text = "scripts/AI-transcript.py";\n    SpeechMarkup markup = BuildTrackedMarkup(text, new[] { "AI" });\n    string ssml = BuildSsmlDocument(markup);\n    const string expected =\n      "<say-as interpret-as=\\\"characters\\\">AI</say-as>" +\n      "<sub alias=\\\"transcript\\\">-transcript</sub>.py";\n    int sequenceStart = ssml.IndexOf(expected, StringComparison.Ordinal);\n    Require(sequenceStart >= 0,\n      "System.Speech does not use the proven case-5 sub-alias form for AI-transcript.py.");\n\n    int contentAliasStart = markup.SsmlContent.IndexOf(\n      "alias=\\\"transcript\\\"",\n      StringComparison.Ordinal);\n    Require(contentAliasStart >= 0,\n      "System.Speech SSML content has no transcript alias.");\n    contentAliasStart += "alias=\\\"".Length;\n    int sourceStart = text.IndexOf("-transcript", StringComparison.Ordinal);\n    Require(sourceStart >= 0, "Test source has no -transcript span.");\n    SpeechMarkupProvenanceSpan? aliasProvenance = markup.SsmlProvenance?\n      .SingleOrDefault(span =>\n        span.SsmlCharacterStart == contentAliasStart &&\n        span.SsmlCharacterLength == "transcript".Length);\n    SpeechMarkupProvenanceSpan provenAlias = aliasProvenance ??\n      throw new InvalidOperationException(\n        "The sub alias value has no explicit provenance.");\n    Require(\n      provenAlias.SourceCharacterStart == sourceStart &&\n      provenAlias.SourceCharacterLength == "-transcript".Length,\n      "The sub alias provenance does not own the complete original -transcript span.");\n\n    int subStart = ssml.IndexOf("<sub alias=", StringComparison.Ordinal);\n    int aliasStart = subStart < 0\n      ? -1\n      : ssml.IndexOf("transcript", subStart, StringComparison.Ordinal);\n    Require(aliasStart >= 0, "Wrapped SSML has no transcript alias value.");\n    SpeechWordBoundary transcript = Map(\n      markup,\n      ssml,\n      aliasStart,\n      "transcript".Length,\n      "transcript",\n      TimeSpan.FromMilliseconds(200));\n    int expectedWord = markup.Words!\n      .Single(word => word.Text == "AI-transcript")\n      .WordIndex;\n    Require(\n      transcript.WordIndex == expectedWord && transcript.WordCount == 1,\n      "Provider progress over the sub alias did not map exactly to AI-transcript.");\n    Require(transcript.Exact,\n      "Provider-native sub-alias timing is not marked exact.");\n\n    int extensionStart = ssml.IndexOf(\n      ".py",\n      sequenceStart,\n      StringComparison.Ordinal);\n    Require(extensionStart >= 0, "Wrapped SSML has no .py extension.");\n    SpeechWordBoundary dot = Map(\n      markup,\n      ssml,\n      extensionStart,\n      1,\n      ".",\n      TimeSpan.FromMilliseconds(300));\n    SpeechWordBoundary py = Map(\n      markup,\n      ssml,\n      extensionStart + 1,\n      2,\n      "py",\n      TimeSpan.FromMilliseconds(350));\n    Require(\n      dot.WordIndex == markup.Words!.Single(word => word.Text == ".").WordIndex &&\n      py.WordIndex == markup.Words!.Single(word => word.Text == "py").WordIndex,\n      "The .py extension lost its separate canonical ownership.");\n  }\n\n'''
  replace_once(TEST, marker, method + marker)


def apply_full() -> None:
  apply_red()

  replace_once(
    TEST,
    '''  private static void TestProviderRangeCanOwnMultipleWords()\n  {\n    SpeechMarkup markup = BuildTrackedMarkup(\n      "scripts/AI-transcript.py.",\n      new[] { "AI" });\n    string ssml = BuildSsmlDocument(markup);\n    int start = ssml.IndexOf("-transcript.py", StringComparison.Ordinal);\n    Require(start >= 0, "Generated SSML has no transformed filename tail.");\n\n    SpeechWordBoundary boundary = Map(\n      markup,\n      ssml,\n      start,\n      "-transcript.py".Length,\n      "-transcript.py",\n      TimeSpan.FromMilliseconds(200));\n\n    int first = markup.Words!\n      .Single(word => word.Text == "AI-transcript")\n      .WordIndex;\n    Require(boundary.WordIndex == first,\n      "Transformed filename tail did not start at AI-transcript.");\n    Require(boundary.WordCount == 3,\n      "One provider range did not retain its three canonical owners.");\n  }\n''',
    '''  private static void TestProviderRangeCanOwnMultipleWords()\n  {\n    SpeechMarkup markup = BuildTrackedMarkup(\n      "alpha beta",\n      Array.Empty<string>());\n    string ssml = BuildSsmlDocument(markup);\n    int start = ssml.IndexOf("alpha beta", StringComparison.Ordinal);\n    Require(start >= 0, "Generated SSML has no alpha beta text.");\n\n    SpeechWordBoundary boundary = Map(\n      markup,\n      ssml,\n      start,\n      "alpha beta".Length,\n      "alpha beta",\n      TimeSpan.FromMilliseconds(200));\n\n    Require(boundary.WordIndex == 0,\n      "Provider range across two words did not start at alpha.");\n    Require(boundary.WordCount == 2,\n      "One provider range did not retain both canonical word owners.");\n    Require(boundary.Exact,\n      "Provider-native multi-word timing is not marked exact.");\n  }\n''')

  replace_once(
    BUILDER,
    '''        case SpecialMatchKind.Spelling:\n          output.Append("<break time=\\\"100ms\\\"/>");\n          output.Append("<say-as interpret-as=\\\"characters\\\">");\n          AppendMappedIdentity(\n            output,\n            next.Match.Value,\n            next.Match.Index,\n            provenance);\n          output.Append("</say-as><break time=\\\"100ms\\\"/>");\n          break;\n''',
    '''        case SpecialMatchKind.Spelling:\n          output.Append("<break time=\\\"100ms\\\"/>");\n          output.Append("<say-as interpret-as=\\\"characters\\\">");\n          AppendMappedIdentity(\n            output,\n            next.Match.Value,\n            next.Match.Index,\n            provenance);\n          output.Append("</say-as>");\n          int spellingEnd = next.Match.Index + next.Match.Length;\n          int substitutedEnd = AppendSystemSpeechHyphenTailSubstitution(\n            output,\n            text,\n            spellingEnd,\n            provenance);\n          if (substitutedEnd != spellingEnd)\n          {\n            position = substitutedEnd;\n            continue;\n          }\n          output.Append("<break time=\\\"100ms\\\"/>");\n          break;\n''')

  marker = '''  /// <summary>\n  /// Finds the next speech transformation once so native and SSML rendering\n'''
  helper = '''  /// <summary>\n  /// Applies the provider-proven workaround for System.Speech inheriting\n  /// spelling semantics across an immediately following hyphenated word.\n  /// The spoken alias and displayed source text both retain exact ownership of\n  /// the original hyphenated source span.\n  /// </summary>\n  private static int AppendSystemSpeechHyphenTailSubstitution(\n    StringBuilder output,\n    string text,\n    int sourceStart,\n    ICollection<SpeechMarkupProvenanceSpan> provenance)\n  {\n    if (sourceStart < 0 ||\n        sourceStart + 1 >= text.Length ||\n        text[sourceStart] != '-' ||\n        !IsSystemSpeechAliasCharacter(text[sourceStart + 1]))\n    {\n      return sourceStart;\n    }\n\n    int sourceEnd = sourceStart + 2;\n    while (sourceEnd < text.Length &&\n           IsSystemSpeechAliasCharacter(text[sourceEnd]))\n    {\n      ++sourceEnd;\n    }\n\n    string alias = text[(sourceStart + 1)..sourceEnd];\n    output.Append("<sub alias=\\\"");\n    int aliasSsmlStart = output.Length;\n    AppendAttributeEscaped(output, alias);\n    int aliasSsmlLength = output.Length - aliasSsmlStart;\n    if (aliasSsmlLength != 0)\n    {\n      provenance.Add(new SpeechMarkupProvenanceSpan(\n        aliasSsmlStart,\n        aliasSsmlLength,\n        sourceStart,\n        sourceEnd - sourceStart));\n    }\n    output.Append("\\\">");\n    AppendMappedIdentity(\n      output,\n      text[sourceStart..sourceEnd],\n      sourceStart,\n      provenance);\n    output.Append("</sub>");\n    return sourceEnd;\n  }\n\n  private static bool IsSystemSpeechAliasCharacter(char value)\n  {\n    return char.IsLetterOrDigit(value) || value == '_';\n  }\n\n'''
  replace_once(BUILDER, marker, helper + marker)


if len(sys.argv) != 2 or sys.argv[1] not in {"red", "full"}:
  raise SystemExit("Usage: issue97-apply.py red|full")

if sys.argv[1] == "red":
  apply_red()
else:
  apply_full()
