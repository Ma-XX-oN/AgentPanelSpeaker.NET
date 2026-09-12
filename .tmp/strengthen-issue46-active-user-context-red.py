from pathlib import Path

path = Path("AgentPanelSpeaker/Issue46IndependentRegressionOracleTestRunner.cs")
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str, label: str) -> None:
  global text
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one anchor, found {count}.")
  text = text.replace(old, new, 1)


replace_once(
  '''          user.NodeId,
          timeoutMilliseconds: 750),''',
  '''          user.NodeId,
          user.Text,
          timeoutMilliseconds: 750),''',
  "mutation destination text")
replace_once(
  '''        user.NodeId,
        timeoutMilliseconds: 10000);''',
  '''        user.NodeId,
        user.Text,
        timeoutMilliseconds: 10000);''',
  "real-control destination text")
replace_once(
  '''          position.State == TranscriptPlaybackState.Speaking &&
          position.NodeId == context.NodeId),''',
  '''          position.State == TranscriptPlaybackState.Speaking &&
          position.NodeId == context.NodeId &&
          string.Equals(
            position.FragmentText,
            context.Text,
            StringComparison.Ordinal)),''',
  "context restart identity")
replace_once(
  '''    int start,
    long userNodeId,
    int timeoutMilliseconds)''',
  '''    int start,
    long userNodeId,
    string userText,
    int timeoutMilliseconds)''',
  "destination oracle signature")
replace_once(
  '''            position.State == TranscriptPlaybackState.Speaking &&
            position.NodeId == userNodeId))''',
  '''            position.State == TranscriptPlaybackState.Speaking &&
            position.NodeId == userNodeId &&
            string.Equals(
              position.FragmentText,
              userText,
              StringComparison.Ordinal)))''',
  "destination oracle exact fragment")

path.write_text(text, encoding="utf-8")
print("Strengthened Issue46 active User Context RED with exact fragment identity.")
