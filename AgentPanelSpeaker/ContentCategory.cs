namespace AgentPanelSpeaker;

/// <summary>
/// Identifies the conversational role that owns one speech fragment.
/// </summary>
internal enum ContentCategory
{
  Assistant,
  Reasoning,
  User
}

/// <summary>
/// Identifies how a speech fragment was derived from Markdown.
/// </summary>
internal enum SpeechFragmentKind
{
  Prose,
  FencedCodeLine
}
