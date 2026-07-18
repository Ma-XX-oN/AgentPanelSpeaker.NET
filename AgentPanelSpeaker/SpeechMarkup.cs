namespace AgentPanelSpeaker;

/// <summary>
/// Carries equivalent native SAPI XML and standards-based SSML markup.
/// </summary>
internal sealed record SpeechMarkup(
  string SapiXml,
  string SsmlContent);
