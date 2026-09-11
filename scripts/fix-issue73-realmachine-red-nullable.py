from pathlib import Path

path = Path(__file__).resolve().parents[1] / "AgentPanelSpeaker" / "Issue26UserContextSpeechRegressionTestRunner.cs"
text = path.read_text(encoding="utf-8")
old = '''    Require(relocation is not null,\n      "Disabling SpeakUserContext left the paused cursor on newly ineligible User Context.");\n    Require(relocation.State == TranscriptPlaybackState.Paused &&\n        relocation.NodeId == user.NodeId &&\n        string.Equals(relocation.FragmentText, user.Text, StringComparison.Ordinal) &&\n        string.Equals(\n          relocation.Word,\n          SpeechTokenization.First(user.Text),\n          StringComparison.Ordinal),\n      "Disabling SpeakUserContext did not move the paused cursor forward to the next eligible User fragment.");\n'''
new = '''    TranscriptPlaybackPosition relocated = relocation ??\n      throw new InvalidOperationException(\n        "Disabling SpeakUserContext left the paused cursor on newly ineligible User Context.");\n    Require(relocated.State == TranscriptPlaybackState.Paused &&\n        relocated.NodeId == user.NodeId &&\n        string.Equals(relocated.FragmentText, user.Text, StringComparison.Ordinal) &&\n        string.Equals(\n          relocated.Word,\n          SpeechTokenization.First(user.Text),\n          StringComparison.Ordinal),\n      "Disabling SpeakUserContext did not move the paused cursor forward to the next eligible User fragment.");\n'''
if old not in text:
  raise SystemExit("nullable assertion block not found")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
