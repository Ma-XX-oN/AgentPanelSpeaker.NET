namespace AgentPanelSpeaker;

/// <summary>
/// Identifies the conversational role and foreground/background style that owns
/// one speech fragment.
/// </summary>
internal enum ContentCategory
{
  Assistant,
  Reasoning,
  SubagentAssistant,
  SubagentReasoning,
  User
}

/// <summary>
/// Identifies one shared voice row in the speech-settings matrix.
/// </summary>
internal enum SpeechRole
{
  Agent,
  Subagent,
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
