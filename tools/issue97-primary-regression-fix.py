from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REGRESSION = ROOT / "AgentPanelSpeaker" / "RegressionTestRunner.cs"

text = REGRESSION.read_text(encoding="utf-8")
old = '''        "<break time=\\\"100ms\\\"/><say-as interpret-as=\\\"characters\\\">" +
        "AI</say-as><break time=\\\"100ms\\\"/>",
        StringComparison.Ordinal),
      "Windows/SSML inline spelling does not use isolated characters semantics.");'''
new = '''        "<break time=\\\"100ms\\\"/><say-as interpret-as=\\\"characters\\\">" +
        "AI</say-as><sub alias=\\\"transcript\\\">-transcript</sub>.py",
        StringComparison.Ordinal),
      "Windows/SSML spelling does not use the proven isolated-prefix sub-alias form.");'''
count = text.count(old)
if count != 1:
  raise RuntimeError(
    f"Expected one primary spelled-word assertion, found {count}.")
REGRESSION.write_text(text.replace(old, new, 1), encoding="utf-8")
