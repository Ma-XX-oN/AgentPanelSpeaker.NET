from pathlib import Path

path = Path("AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

broken = '''    string tallResponse = string.Join(
      "

",
'''
fixed = '''    string tallResponse = string.Join(
      "\\n\\n",
'''

if text.count(broken) != 1:
  raise SystemExit(
    f"expected one malformed tall-response separator, found {text.count(broken)}")

path.write_text(text.replace(broken, fixed, 1), encoding="utf-8")
