from pathlib import Path

path = Path(__file__).resolve().parents[1] / "AgentPanelSpeaker" / "Issue96SystemSpeechMatrixRegressionTestRunner.cs"
text = path.read_text(encoding="utf-8")

start = text.index("  private static void TestFixedFiveCaseInventory()")
end = text.index("  private static void TestProviderBoundaryEvents()", start)
replacement = r'''  private static void TestFixedFiveCaseInventory()
  {
    string source = ReadOptionalSource("SystemSpeechMatrixDiagnosticRunner.cs");
    foreach (string required in new[]
    {
      "plain-hyphenated",
      "characters-hyphenated",
      "characters-space",
      "characters-break-hyphenated",
      "characters-sub-alias",
      "AI-transcript.py",
      "interpret-as=\\\"characters\\\"",
      "<break time=\\\"100ms\\\"/>",
      "<sub alias=\\\"transcript\\\">"
    })
    {
      Require(source.Contains(required, StringComparison.Ordinal),
        $"Controlled matrix is missing {required}.");
    }
    Require(source.Contains("private static readonly SystemSpeechMatrixCase[] Cases =",
        StringComparison.Ordinal),
      "The matrix case inventory is not a fixed production constant.");
  }

'''
text = text[:start] + replacement + text[end:]

old = '''    int end = main.IndexOf(
      "  /// <summary>\\n  /// Opens the spelling and pronunciation-rule editor.",
      start,
      StringComparison.Ordinal);
'''
new = '''    int end = main.IndexOf(
      "  private void PronunciationsButtonClicked(",
      start,
      StringComparison.Ordinal);
'''
if old not in text:
  raise RuntimeError("Could not find issue #96 handler-boundary oracle")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
