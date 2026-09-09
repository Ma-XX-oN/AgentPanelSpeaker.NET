from pathlib import Path

path = Path("AgentPanelSpeaker/CoreRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")
old = '      int htmlPrompt = html.IndexOf(prompt, StringComparison.Ordinal);'
new = (
  '      int htmlPrompt = html.IndexOf(\n'
  '        "What time is it in Paris?",\n'
  '        StringComparison.Ordinal);')
if text.count(old) != 1:
  raise SystemExit(
    f"expected one direct HTML prompt lookup, found {text.count(old)}")
text = text.replace(old, new, 1)

old = '      int productionPrompt = productionHtml.IndexOf(prompt, StringComparison.Ordinal);'
new = (
  '      int productionPrompt = productionHtml.IndexOf(\n'
  '        "What time is it in Paris?",\n'
  '        StringComparison.Ordinal);')
if text.count(old) != 1:
  raise SystemExit(
    f"expected one production HTML prompt lookup, found {text.count(old)}")
text = text.replace(old, new, 1)

path.write_text(text, encoding="utf-8")
