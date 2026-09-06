from pathlib import Path

path = Path('AgentPanelSpeaker/TranscriptPresentationDomFormatter.cs')
text = path.read_text(encoding='utf-8')
old = '''    children.Add(Element(
      "summary",
      null,
      Text(string.IsNullOrWhiteSpace(GetString(node, "name"))
        ? "Tool"
        : GetString(node, "name"))));'''
new = '''    children.Add(Element(
      "summary",
      null,
      Text(ToolSummary(node))));'''
if old not in text:
  raise SystemExit('tool summary block not found')
text = text.replace(old, new, 1)
marker = '''  private static TranscriptDomNode BuildAttachments(
    JsonElement node,
    HashSet<int> emittedSourceIndexes)
  {'''
helper = '''  private static string ToolSummary(JsonElement node)
  {
    if (node.TryGetProperty("call", out JsonElement call) &&
        call.ValueKind == JsonValueKind.Object &&
        call.TryGetProperty("input", out JsonElement input) &&
        input.ValueKind == JsonValueKind.Object)
    {
      string description = GetString(input, "description");
      if (!string.IsNullOrWhiteSpace(description))
      {
        return description;
      }
    }

    string name = GetString(node, "name");
    return string.IsNullOrWhiteSpace(name) ? "Tool" : name;
  }

'''
if marker not in text:
  raise SystemExit('BuildAttachments marker not found')
text = text.replace(marker, helper + marker, 1)
path.write_text(text, encoding='utf-8')
