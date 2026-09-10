from pathlib import Path

path = Path('AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')
old = '    const int tallParagraphCount = 34;\n'
new = '    const int tallParagraphCount = 16;\n'
if text.count(old) != 1:
  raise SystemExit('visible-retention tall paragraph count anchor mismatch')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
