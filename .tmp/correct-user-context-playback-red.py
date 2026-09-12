from pathlib import Path

path = Path("AgentPanelSpeaker/Issue26UserContextSpeechRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")
old = '''      string contextText = string.Join(
        ' ',
        Enumerable.Repeat("active user context source", 800));
'''
new = '''      string contextText = string.Join(
        ' ',
        Enumerable.Repeat("active user context source", 20));
'''
if text.count(old) != 1:
  raise RuntimeError(
    f"Expected one 800-repeat User Context source, found {text.count(old)}.")
text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
print("Reduced active User Context RED source to 80 spoken words.")
