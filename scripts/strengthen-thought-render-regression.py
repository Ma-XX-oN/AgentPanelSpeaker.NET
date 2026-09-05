from pathlib import Path

path = Path('tools/AgentPanelSpeaker.DisplayParity/Program.cs')
text = path.read_text(encoding='utf-8')
old = '''    Reject(
      html,
      "<p>&gt;",
      "literal Markdown quote marker in grouped Claude thoughts",
      failures);
    Reject(
      html,
      "<p>***</p>",
      "literal Markdown thought separator in grouped Claude thoughts",
      failures);
'''
new = '''    Reject(
      html,
      "&gt;",
      "literal Markdown quote marker in grouped Claude thoughts",
      failures);
    Reject(
      html,
      "***",
      "literal Markdown thought separator in grouped Claude thoughts",
      failures);
    Require(
      html,
      "<hr",
      "rendered Markdown thought separator in grouped Claude thoughts",
      failures);
'''
count = text.count(old)
if count != 1:
  raise RuntimeError(f'Expected one grouped-thought regression seam, found {count}.')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
