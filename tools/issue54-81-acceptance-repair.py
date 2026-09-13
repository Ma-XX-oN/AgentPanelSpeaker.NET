from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
ISSUE75 = ROOT / "AgentPanelSpeaker" / "Issue75CoreWordIdMigrationRegressionTestRunner.cs"
REGRESSION = ROOT / "AgentPanelSpeaker" / "RegressionTestRunner.cs"
TRANSCRIPT = ROOT / "AgentPanelSpeaker" / "TranscriptView.cs"
SPELLING = ROOT / "AgentPanelSpeaker" / "SpeechSapiXmlBuilder.cs"


def replace_once(path: Path, old: str, new: str) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{path}: expected one replacement target, found {count}")
  path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")


def add_tests() -> None:
  replace_once(
    ISSUE75,
    '    Require(!shell.Contains("markAlignedVoiceSelectableWords(", StringComparison.Ordinal),\n'
    '      "Browser still calls the removed legacy aligned-word eligibility stamper.");\n'
    '    Require(!shell.Contains("dataset.nodeWordIndex", StringComparison.Ordinal),',
    '    Require(!shell.Contains("markAlignedVoiceSelectableWords(", StringComparison.Ordinal),\n'
    '      "Browser still calls the removed legacy aligned-word eligibility stamper.");\n'
    '    Require(!shell.Contains("findSpeechLexicalAlignment(", StringComparison.Ordinal),\n'
    '      "Browser still calls the removed legacy lexical-alignment fallback.");\n'
    '    Require(!shell.Contains("dataset.nodeWordIndex", StringComparison.Ordinal),')

  replace_once(
    REGRESSION,
    '    Require(set.Contains("Api"), "Spelled-word lookup should be case-insensitive.");\n'
    '  }',
    '    Require(set.Contains("Api"), "Spelled-word lookup should be case-insensitive.");\n'
    '    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(\n'
    '      "scripts/AI-transcript.py",\n'
    '      pitchSetting: 0,\n'
    '      new[] { "AI" },\n'
    '      PronunciationRuleSet.Parse(string.Empty));\n'
    '    Require(\n'
    '      markup.SsmlContent.Contains(\n'
    '        "<say-as interpret-as=\\\"spell-out\\\">AI</say-as>",\n'
    '        StringComparison.Ordinal),\n'
    '      "Windows/SSML spelling does not use explicit spell-out semantics.");\n'
    '    Require(\n'
    '      !markup.SsmlContent.Contains(\n'
    '        "interpret-as=\\\"characters\\\"",\n'
    '        StringComparison.Ordinal),\n'
    '      "Windows/SSML spelling still uses the ambiguous characters interpretation.");\n'
    '    Require(\n'
    '      string.Equals(markup.PlainText, "scripts/AI-transcript.py", StringComparison.Ordinal),\n'
    '      "Spell-out markup changed the source/display text identity.");\n'
    '  }')


def add_production() -> None:
  replace_once(
    SPELLING,
    '        "<say-as interpret-as=\\\"characters\\\">",',
    '        "<say-as interpret-as=\\\"spell-out\\\">",')

  text = TRANSCRIPT.read_text(encoding="utf-8")
  pattern = re.compile(
    r"\n      let lexicalAlignment = findSpeechLexicalAlignment\(.*?"
    r"\n        continue;\n      \}\n\n      const failureKey",
    re.DOTALL)
  updated, count = pattern.subn("\n      const failureKey", text, count=1)
  if count != 1:
    raise RuntimeError(
      f"{TRANSCRIPT}: expected one stale lexical-alignment fallback block, found {count}")
  TRANSCRIPT.write_text(updated, encoding="utf-8", newline="\n")


def main() -> None:
  if len(sys.argv) != 2 or sys.argv[1] not in {"tests", "production"}:
    raise SystemExit("usage: issue54-81-acceptance-repair.py tests|production")
  if sys.argv[1] == "tests":
    add_tests()
  else:
    add_production()


if __name__ == "__main__":
  main()
