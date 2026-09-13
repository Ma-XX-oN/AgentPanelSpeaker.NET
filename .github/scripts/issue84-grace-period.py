from pathlib import Path
import sys


def replace_once(path_name: str, old: str, new: str, label: str) -> None:
  path = Path(path_name)
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"{label}: expected exactly one replacement sentinel, found {count}")
  path.write_text(text.replace(old, new, 1), encoding="utf-8")


if len(sys.argv) != 2 or sys.argv[1] not in {"test", "production"}:
  raise SystemExit("usage: issue84-grace-period.py test|production")

if sys.argv[1] == "test":
  replace_once(
    "AgentPanelSpeaker/Issue84RewindCurrentFragmentRegressionTestRunner.cs",
    "period == TimeSpan.FromMilliseconds(500),",
    "period == TimeSpan.FromSeconds(1),",
    "one-second grace assertion")
  replace_once(
    "AgentPanelSpeaker/Issue84RewindCurrentFragmentRegressionTestRunner.cs",
    '"Named rewind grace period is not 500 ms.");',
    '"Named rewind grace period is not 1 second.");',
    "one-second grace failure text")
  print("Issue #84 one-second grace RED staged.")
else:
  replace_once(
    "AgentPanelSpeaker/SpeechService.cs",
    "TimeSpan.FromMilliseconds(500);",
    "TimeSpan.FromSeconds(1);",
    "production grace period")
  replace_once(
    "AgentPanelSpeaker/SpeechService.cs",
    "// PreviousSentence owns both the destination and its 500 ms policy.",
    "// PreviousSentence owns both the destination and its 1-second policy.",
    "production grace policy comment")
  print("Issue #84 one-second production grace staged.")
