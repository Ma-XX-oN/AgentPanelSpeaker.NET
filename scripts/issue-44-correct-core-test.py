from pathlib import Path

path = Path("AgentPanelSpeaker/CoreRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")

replacements = [
  (
    '("core/codex-user-context-production-display", TestCodexUserContextMarkdownHtmlParity),',
    '("core/codex-user-context-formatter-parity", TestCodexUserContextMarkdownHtmlParity),'
  ),
  (
    '  /// Verifies one Codex IDE-context record through canonical Markdown, the\n'
    '  /// legacy direct-HTML formatter, and the real production DOM/virtual-document\n'
    '  /// path used by TranscriptView.\n',
    '  /// Verifies one Codex IDE-context record through canonical Markdown, the\n'
    '  /// legacy direct-HTML formatter, and the canonical presentation DOM formatter.\n'
    '  /// Final browser output is covered separately by the independent output-oracle\n'
    '  /// acceptance suite.\n'
  ),
  (
    '      TranscriptVirtualDocument virtualDocument =\n'
    '        TranscriptVirtualDocument.Build(productionHtml);\n'
    '      string displayedHtml = virtualDocument.CreateFullWindow().Html;\n'
    '      Require(displayedHtml.Contains(\n'
    '          "<details class=\\"user-context-details\\"",\n'
    '          StringComparison.Ordinal),\n'
    '        "Production virtual document dropped Codex IDE context.");\n'
    '      Require(displayedHtml.Contains(prompt, StringComparison.Ordinal),\n'
    '        "Production virtual document dropped the actual User prompt.");\n\n',
    ''
  )
]

for old, new in replacements:
  count = text.count(old)
  if count != 1:
    raise SystemExit(f"Expected exactly one match, found {count}: {old[:80]!r}")
  text = text.replace(old, new, 1)

path.write_text(text, encoding="utf-8")
