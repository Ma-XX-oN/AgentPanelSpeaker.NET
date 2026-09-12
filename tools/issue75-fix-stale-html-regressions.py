from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


regression_path = Path("AgentPanelSpeaker/RegressionTestRunner.cs")
regression = regression_path.read_text(encoding="utf-8")
regression = replace_once(
    regression,
    '''    Require(html.Contains(UnicodeProbe, StringComparison.Ordinal),
      "Presentation HTML lost the Unicode probe.");''',
    '''    Require(ContainsCanonicalBlockText(result, UnicodeProbe),
      "Presentation projection lost the canonical Unicode block text.");''',
    "presentation Unicode assertion")
regression = replace_once(
    regression,
    '''    int firstThought = result.Html.IndexOf(UnicodeProbe, StringComparison.Ordinal);
    int secondThought = result.Html.IndexOf("Second thought — naïve façade", StringComparison.Ordinal);
    int finalResponse = result.Html.IndexOf("Before horizontal rule", StringComparison.Ordinal);''',
    '''    int firstThought = result.Html.IndexOf(
      "data-source-id=\\\"thought-one\\\"",
      StringComparison.Ordinal);
    int secondThought = result.Html.IndexOf(
      "data-source-id=\\\"thought-two\\\"",
      StringComparison.Ordinal);
    int finalResponse = result.Html.IndexOf(
      "data-source-id=\\\"regression-final\\\"",
      StringComparison.Ordinal);''',
    "reasoning ownership anchors")
helper_anchor = '''  private static void RequireCoreContract(AIConversationProjection projection)
'''
helper = '''  private static bool ContainsCanonicalBlockText(
    TranscriptPresentationDomResult result,
    string expected)
  {
    return result.Units
      .SelectMany(unit =>
        unit.SpeechWords ?? Array.Empty<CanonicalSpeechWordProjection>())
      .Where(word => !string.IsNullOrWhiteSpace(word.Provenance?.BlockId))
      .GroupBy(
        word => word.Provenance!.BlockId!,
        StringComparer.Ordinal)
      .Any(group => string.Equals(
        string.Concat(group
          .OrderBy(word => word.Provenance!.BlockWordIndex)
          .Select(word => word.SeparatorBefore + word.Text)),
        expected,
        StringComparison.Ordinal));
  }

'''
regression = replace_once(
    regression,
    helper_anchor,
    helper + helper_anchor,
    "canonical block helper insertion")
regression_path.write_text(regression, encoding="utf-8", newline="\n")

rolled_path = Path("AgentPanelSpeaker/Issue35RolledBackVisibilityRegressionTestRunner.cs")
rolled = rolled_path.read_text(encoding="utf-8")
rolled = replace_once(
    rolled,
    '''      Require(
        result.Html.Contains("Original question", StringComparison.Ordinal),
        "Production DOM projection discarded the historical original turn.");
      Require(
        result.Html.Contains("Edited question", StringComparison.Ordinal),
        "Production DOM projection lost the active edited turn.");''',
    '''      Require(
        ContainsCanonicalBlockText(result, "Original question"),
        "Production DOM projection discarded the historical original turn.");
      Require(
        ContainsCanonicalBlockText(result, "Edited question"),
        "Production DOM projection lost the active edited turn.");''',
    "rolled-back canonical text assertions")
rolled_helper_anchor = '''  private static T GetField<T>(object target, string name)
'''
rolled_helper = '''  private static bool ContainsCanonicalBlockText(
    TranscriptPresentationDomResult result,
    string expected)
  {
    return result.Units
      .SelectMany(unit =>
        unit.SpeechWords ?? Array.Empty<CanonicalSpeechWordProjection>())
      .Where(word => !string.IsNullOrWhiteSpace(word.Provenance?.BlockId))
      .GroupBy(
        word => word.Provenance!.BlockId!,
        StringComparer.Ordinal)
      .Any(group => string.Equals(
        string.Concat(group
          .OrderBy(word => word.Provenance!.BlockWordIndex)
          .Select(word => word.SeparatorBefore + word.Text)),
        expected,
        StringComparison.Ordinal));
  }

'''
rolled = replace_once(
    rolled,
    rolled_helper_anchor,
    rolled_helper + rolled_helper_anchor,
    "rolled-back canonical block helper insertion")
rolled_path.write_text(rolled, encoding="utf-8", newline="\n")
