namespace AgentPanelSpeaker;

/// <summary>
/// Carries source text plus equivalent native SAPI XML and standards-based
/// SSML markup.
/// </summary>
internal sealed record SpeechMarkup(
  string PlainText,
  string SapiXml,
  string SsmlContent);
