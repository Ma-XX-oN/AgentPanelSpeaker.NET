from pathlib import Path

path = Path("AgentPanelSpeaker/AIConversationCoreClient.cs")
text = path.read_text(encoding="utf-8")

old = (
  '  [property: JsonPropertyName("units")] CanonicalUnitProjection[] Units,\n'
  '  [property: JsonPropertyName("html_units")]\n'
  '    CanonicalHtmlUnitProjection[] HtmlUnits,\n'
  '  [property: JsonPropertyName("presentation")]\n')
new = (
  '  [property: JsonPropertyName("units")] CanonicalUnitProjection[] Units,\n'
  '  [property: JsonPropertyName("presentation")]\n')
if text.count(old) != 1:
  raise SystemExit("required HtmlUnits constructor insertion was not found exactly once")
text = text.replace(old, new, 1)

old_tail = (
  '  [property: JsonPropertyName("session_metadata")]\n'
  '    AIConversationSessionMetadata? SessionMetadata = null);')
new_tail = (
  '  [property: JsonPropertyName("session_metadata")]\n'
  '    AIConversationSessionMetadata? SessionMetadata = null,\n'
  '  [property: JsonPropertyName("html_units")]\n'
  '    CanonicalHtmlUnitProjection[]? HtmlUnits = null);')
if text.count(old_tail) != 1:
  raise SystemExit("AIConversationProjection optional tail was not found exactly once")
text = text.replace(old_tail, new_tail, 1)

path.write_text(text, encoding="utf-8")
