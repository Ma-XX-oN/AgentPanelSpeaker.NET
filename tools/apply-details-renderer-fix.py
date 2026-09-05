from pathlib import Path

view = Path('AgentPanelSpeaker/TranscriptView.cs')
text = view.read_text(encoding='utf-8')
old = '        string html = Markdown.ToHtml(markdown, _pipeline);'
new = '        string html = TranscriptHtmlRenderer.ToHtml(markdown, _pipeline);'
if old not in text:
  raise SystemExit('TranscriptView Markdown.ToHtml call not found')
view.write_text(text.replace(old, new, 1), encoding='utf-8')

program = Path('tools/AgentPanelSpeaker.DisplayParity/Program.cs')
text = program.read_text(encoding='utf-8')
old = '''  string html = Markdown.ToHtml(\n    markdown,\n    new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());'''
new = '''  string html = TranscriptHtmlRenderer.ToHtml(\n    markdown,\n    new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());'''
if old not in text:
  raise SystemExit('DisplayParity Markdown.ToHtml call not found')
program.write_text(text.replace(old, new, 1), encoding='utf-8')
