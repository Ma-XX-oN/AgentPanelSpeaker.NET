namespace AgentPanelSpeaker;

/// <summary>
/// Identifies the conversational role and main/context style that owns one
/// speech fragment.
/// </summary>
internal enum ContentCategory
{
  Assistant,
  Reasoning,
  SubagentAssistant,
  SubagentReasoning,
  User,
  UserContext
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

/// <summary>
/// Identifies whether cleaned Markdown belongs to ordinary narration or quoted
/// context.
/// </summary>
internal enum SpeechTextStyle
{
  Main,
  Context
}
