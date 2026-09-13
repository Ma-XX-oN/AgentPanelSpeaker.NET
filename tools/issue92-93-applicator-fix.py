from pathlib import Path

path = Path(__file__).with_name("issue92-93-apply.py")
source = path.read_text(encoding="utf-8")
start_marker = "  old_wrap = '''  private static string WrapSsmlPitch"
end_marker = "  write(path, text)\n\n\ndef patch_system_speech():"
start = source.find(start_marker)
end = source.find(end_marker, start)
if start < 0 or end < 0:
  raise RuntimeError("Could not isolate stale WrapSsmlPitch applicator block")

wrap_start_marker = '''  /// <summary>
  /// Applies the System.Speech relative pitch setting.
  /// </summary>
'''
wrap_end_marker = '''  /// <summary>
  /// Expands an ISO date-time into a form voices read naturally.
  /// </summary>
'''
new_wrap_section = '''  /// <summary>
  /// Applies the System.Speech relative pitch setting.
  /// </summary>
  private static string WrapSsmlPitch(string content, int pitchSetting)
  {
    return WrapSsmlPitch(
      content,
      pitchSetting,
      Array.Empty<SpeechMarkupProvenanceSpan>()).Content;
  }

  private static (
    string Content,
    IReadOnlyList<SpeechMarkupProvenanceSpan> Provenance) WrapSsmlPitch(
      string content,
      int pitchSetting,
      IReadOnlyList<SpeechMarkupProvenanceSpan> provenance)
  {
    int pitchPercent = Math.Clamp(pitchSetting, -10, 10) *
      SsmlPitchPercentPerStep;
    string pitch = pitchPercent > 0
      ? $"+{pitchPercent}%"
      : $"{pitchPercent}%";
    string prefix = $"<prosody pitch=\\\"{pitch}\\\">";
    SpeechMarkupProvenanceSpan[] shifted = provenance
      .Select(span => span with
      {
        SsmlCharacterStart = checked(span.SsmlCharacterStart + prefix.Length)
      })
      .ToArray();
    return ($"{prefix}{content}</prosody>", shifted);
  }

'''

replacement = (
  "  wrap_start_marker = " + repr(wrap_start_marker) + "\n" +
  "  wrap_end_marker = " + repr(wrap_end_marker) + "\n" +
  "  wrap_start = text.find(wrap_start_marker)\n" +
  "  wrap_end = text.find(wrap_end_marker, wrap_start)\n" +
  "  if wrap_start < 0 or wrap_end < 0:\n" +
  "    raise RuntimeError(\"SpeechSapiXmlBuilder WrapSsmlPitch section missing\")\n" +
  "  new_wrap_section = " + repr(new_wrap_section) + "\n" +
  "  text = text[:wrap_start] + new_wrap_section + text[wrap_end:]\n" +
  "  write(path, text)\n"
)

source = source[:start] + replacement + source[end + len("  write(path, text)\n"):]
path.write_text(source, encoding="utf-8", newline="\n")
