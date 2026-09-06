from pathlib import Path

path = Path('tools/AgentPanelSpeaker.DisplayParity/Program.cs')
text = path.read_text(encoding='utf-8')
old = 'int outerStart = html.IndexOf("<details>", StringComparison.Ordinal);'
new = 'int outerStart = html.IndexOf("<details", StringComparison.Ordinal);'
if old not in text:
  raise SystemExit('interleaved outer details assertion not found')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
