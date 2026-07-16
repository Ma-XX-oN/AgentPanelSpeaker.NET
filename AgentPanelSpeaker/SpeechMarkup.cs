namespace AgentPanelSpeaker;

/// <summary>
/// Carries equivalent native SAPI XML and System.Speech SSML markup.
/// </summary>
internal sealed record SpeechMarkup(
  string SapiXml,
  string SsmlContent);
