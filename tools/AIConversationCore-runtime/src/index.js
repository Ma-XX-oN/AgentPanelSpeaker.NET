export {
  STYLE_ROLES,
  configureProjectionTheme,
  getDefaultProjectionTheme,
  resetProjectionTheme,
  resolveProjectionTheme
} from './projections/style.js';

export {
  buildTurnHeaderComponents,
  renderTurnHeader
} from './projections/turn-header.js';

export { renderCanonicalMarkdown } from './projections/markdown-visibility.js';
export {
  locateCanonicalWord,
  projectCanonicalWords,
  renderCanonicalHtml,
  renderCanonicalHtmlUnits
} from './projections/html-visibility.js';
export { buildCanonicalPresentation } from './projections/presentation-revisions.js';
export { projectCanonicalConversation } from './projections/structured-visibility.js';
export { createCanonicalConversationSession } from './session/canonical-session.js';
export { loadConversationSources } from './sources/conversation.js';

export { deriveTurns } from './derive/turns.js';
export { adaptChatGPTRecords } from './adapters/chatgpt.js';
export { adaptClaudeRecords, adaptClaudeToolEvents } from './adapters/claude-normalized.js';
export {
  adaptCodexRecords,
  adaptCodexToolEvents,
  resolveCodexSessionMetadata
} from './adapters/codex-retained.js';
export { adaptInteractiveSessionRecords } from './adapters/interactive.js';
export { adaptSpeechSessionRecords } from './adapters/speech-session-normalized.js';
