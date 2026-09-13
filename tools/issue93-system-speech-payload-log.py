from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
ENGINE = ROOT / "AgentPanelSpeaker" / "SapiSpeechEngine.cs"
RUNNER = ROOT / "AgentPanelSpeaker" / "Issue93SystemSpeechProvenanceRegressionTestRunner.cs"


def replace_once(path: Path, old: str, new: str) -> None:
  text = path.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(
      f"Expected exactly one match in {path}: {old[:120]!r}; got {count}")
  path.write_text(text.replace(old, new, 1), encoding="utf-8")


def add_regression() -> None:
  replace_once(
    RUNNER,
    '''      ("system-speech-provenance/spelling-scope-stops-before-hyphenated-tail",\n        TestSpellingScopeStopsBeforeHyphenatedTail),\n''',
    '''      ("system-speech-provenance/spelling-scope-stops-before-hyphenated-tail",\n        TestSpellingScopeStopsBeforeHyphenatedTail),\n      ("system-speech-provenance/provider-boundary-payload-logged",\n        TestProviderBoundaryPayloadLogged),\n''')

  anchor = '''  private static void TestSpellOutManyEventsOneWord()\n  {\n'''
  method = r'''  private static void TestProviderBoundaryPayloadLogged()
  {
    string source = ReadSource("SapiSpeechEngine.cs");
    int start = source.IndexOf(
      "  private static PcmWaveData RenderSystemSpeech(",
      StringComparison.Ordinal);
    Require(start >= 0, "RenderSystemSpeech is missing.");
    int end = source.IndexOf(
      "  private static bool TryMapSystemSpeechProgress(",
      start,
      StringComparison.Ordinal);
    Require(end > start, "Could not isolate RenderSystemSpeech diagnostics.");
    string method = source[start..end];

    Require(method.Contains(
        "DiagnosticLog.Write(\"speech.system_speech_markup_ssml_content\"",
        StringComparison.Ordinal),
      "System.Speech does not log the exact markup SSML content.");
    Require(method.Contains(
        "DiagnosticLog.Write(\"speech.system_speech_ssml_document\"",
        StringComparison.Ordinal),
      "System.Speech does not log the fully wrapped SSML document.");
    int submitted = method.IndexOf(
      "DiagnosticLog.Write(\"speech.system_speech_ssml_submitted\"",
      StringComparison.Ordinal);
    int speak = method.IndexOf(
      "synthesizer.SpeakSsml(ssml);",
      StringComparison.Ordinal);
    Require(submitted >= 0 && speak > submitted,
      "The final System.Speech payload is not logged immediately before SpeakSsml.");
    string boundary = method[submitted..speak];
    Require(boundary.Contains("ssml", StringComparison.Ordinal) &&
            boundary.Contains("ComputeUtf8Sha256(ssml)", StringComparison.Ordinal),
      "The provider-boundary diagnostic does not include the exact SSML and its SHA-256.");
    Require(source.Contains(
        "SHA256.HashData(Encoding.UTF8.GetBytes(value))",
        StringComparison.Ordinal),
      "System.Speech payload fingerprint is not SHA-256 over UTF-8 bytes.");
  }

'''
  replace_once(RUNNER, anchor, method + anchor)


def add_production() -> None:
  replace_once(
    ENGINE,
    '''    string ssml = BuildSsmlDocument(\n      markup.SsmlContent,\n      synthesizer.Voice.Culture.Name);\n\n    EventHandler<System.Speech.Synthesis.SpeakProgressEventArgs> handler =\n''',
    '''    string ssml = BuildSsmlDocument(\n      markup.SsmlContent,\n      synthesizer.Voice.Culture.Name);\n    DiagnosticLog.Write("speech.system_speech_markup_ssml_content", new\n    {\n      provider = "System.Speech",\n      voice = providerVoiceId,\n      culture = synthesizer.Voice.Culture.Name,\n      characterLength = markup.SsmlContent.Length,\n      utf8ByteLength = Encoding.UTF8.GetByteCount(markup.SsmlContent),\n      sha256 = ComputeUtf8Sha256(markup.SsmlContent),\n      ssmlContent = markup.SsmlContent\n    });\n    DiagnosticLog.Write("speech.system_speech_ssml_document", new\n    {\n      provider = "System.Speech",\n      voice = providerVoiceId,\n      culture = synthesizer.Voice.Culture.Name,\n      characterLength = ssml.Length,\n      utf8ByteLength = Encoding.UTF8.GetByteCount(ssml),\n      sha256 = ComputeUtf8Sha256(ssml),\n      ssml\n    });\n\n    EventHandler<System.Speech.Synthesis.SpeakProgressEventArgs> handler =\n''')

  replace_once(
    ENGINE,
    '''      synthesizer.SetOutputToAudioStream(stream, outputFormat);\n      synthesizer.SpeakSsml(ssml);\n''',
    '''      synthesizer.SetOutputToAudioStream(stream, outputFormat);\n      DiagnosticLog.Write("speech.system_speech_ssml_submitted", new\n      {\n        provider = "System.Speech",\n        voice = providerVoiceId,\n        culture = synthesizer.Voice.Culture.Name,\n        characterLength = ssml.Length,\n        utf8ByteLength = Encoding.UTF8.GetByteCount(ssml),\n        sha256 = ComputeUtf8Sha256(ssml),\n        ssml\n      });\n      synthesizer.SpeakSsml(ssml);\n''')

  replace_once(
    ENGINE,
    '''  /// <summary>\n  /// Translates one provider-native System.Speech progress range through the\n''',
    '''  /// <summary>\n  /// Returns a stable fingerprint for the exact UTF-8 diagnostic payload.\n  /// </summary>\n  private static string ComputeUtf8Sha256(string value)\n  {\n    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));\n    return Convert.ToHexString(hash).ToLowerInvariant();\n  }\n\n  /// <summary>\n  /// Translates one provider-native System.Speech progress range through the\n''')

  replace_once(
    ENGINE,
    '''using System.Security;\nusing System.Text.RegularExpressions;\n''',
    '''using System.Security;\nusing System.Security.Cryptography;\nusing System.Text;\nusing System.Text.RegularExpressions;\n''')


def main() -> None:
  if len(sys.argv) != 2 or sys.argv[1] not in {"red", "green"}:
    raise SystemExit("usage: issue93-system-speech-payload-log.py red|green")
  add_regression()
  if sys.argv[1] == "green":
    add_production()


if __name__ == "__main__":
  main()
