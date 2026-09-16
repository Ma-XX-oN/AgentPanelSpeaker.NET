namespace AgentPanelSpeaker;

/// <summary>
/// Carries source text plus equivalent native SAPI XML and standards-based
/// SSML markup.
/// </summary>
internal sealed record SpeechMarkupWord(
  int WordIndex,
  string Text,
  int CharacterStart,
  int CharacterLength);

/// <summary>
/// Maps one raw SSML text range back to the source characters that caused it.
/// Markup-only ranges are deliberately absent from this inventory.
/// </summary>
internal sealed record SpeechMarkupProvenanceSpan(
  int SsmlCharacterStart,
  int SsmlCharacterLength,
  int SourceCharacterStart,
  int SourceCharacterLength);

/// <summary>
/// Carries source text plus equivalent speech markup and, when available,
/// exact fragment-relative ownership for each canonical transcript word.
/// </summary>
internal sealed record SpeechMarkup(
  string PlainText,
  string SapiXml,
  string SsmlContent,
  IReadOnlyList<SpeechMarkupWord>? Words = null,
  IReadOnlyList<SpeechMarkupProvenanceSpan>? SsmlProvenance = null);
