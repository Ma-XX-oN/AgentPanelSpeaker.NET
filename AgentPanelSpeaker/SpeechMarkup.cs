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
/// Carries source text plus equivalent speech markup and, when available,
/// exact fragment-relative ownership for each canonical transcript word.
/// </summary>
internal sealed record SpeechMarkup(
  string PlainText,
  string SapiXml,
  string SsmlContent,
  IReadOnlyList<SpeechMarkupWord>? Words = null);
