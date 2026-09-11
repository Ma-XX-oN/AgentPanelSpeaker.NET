import {
  adaptClaudeRecords as adaptClaudeRecordsRaw,
  adaptClaudeToolEvents
} from './claude.js';

// Matches Claude Code XML blocks that are injected into text but are not user-visible transcript content.
const CLAUDE_SYSTEM_TAG_RE = /<(?:ide_opened_file|ide_selection|system[-_]reminder|system|env|claude_background_info|user[-_]prompt[-_]submit[-_]hook|command[-_]name|antml:[a-z_]+)[^>]*>.*?<\/[^>]+>/gis;

/**
 * Removes Claude Code's system-injected XML blocks from provider text.
 *
 * The source representation is a Claude text block that may contain injected XML
 * alongside user-visible text.  The canonical representation is only the visible
 * text that the historical AI-transcript.py renderer exposed.
 *
 * @param {string} text - Provider text to normalize.
 * @returns {string} Visible Claude text with injected XML removed and outer whitespace trimmed.
 */
function stripClaudeSystemText(text) {
  return text.replace(CLAUDE_SYSTEM_TAG_RE, '').trim();
}

/**
 * Adapts Claude records while preserving the established suppression of injected
 * system XML so those blocks cannot create empty or duplicate visible turns.
 *
 * @param {Array<Object<string, *>>} records - Ordered Claude provider records.
 * @returns {Array<Object<string, *>>} Canonical Claude events with only visible message text retained.
 */
export function adaptClaudeRecords(records) {
  const events = adaptClaudeRecordsRaw(records);
  const normalized = [];

  for (const event of events) {
    if (event?.kind !== 'message' || !Array.isArray(event.blocks)) {
      normalized.push(event);
      continue;
    }

    const blocks = event.blocks.flatMap(block => {
      if (block?.type !== 'text' || typeof block.text !== 'string') return [block];
      const text = stripClaudeSystemText(block.text);
      if (!text) return [];
      return [{ ...block, text }];
    });

    if (!blocks.length) continue;
    normalized.push({ ...event, blocks });
  }

  return normalized;
}

export { adaptClaudeToolEvents };
