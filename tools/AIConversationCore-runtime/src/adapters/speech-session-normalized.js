import { adaptSpeechSessionRecords as adaptSpeechSessionRecordsRaw } from './speech-session.js';

/**
 * Removes empty Claude User text blocks left after interactive injected-context
 * cleanup, and removes a message event when no visible blocks remain.
 *
 * @param {Object<string, *>} event - Canonical speech-session event.
 * @returns {Object<string, *>|null} Visible event clone or null when empty.
 */
function normalizeVisibleClaudeUserEvent(event) {
  if (event?.provider !== 'claude' || event?.kind !== 'message' || event?.role !== 'user') {
    return event;
  }

  const blocks = (event.blocks ?? []).filter(block =>
    block?.type !== 'text' || (typeof block.text === 'string' && block.text.length > 0)
  );
  if (!blocks.length) return null;
  return { ...event, blocks };
}

/**
 * Adapts a speech/display session while ensuring Claude-injected context cannot
 * survive as an empty visible User event.
 *
 * @param {string} provider - Canonical provider identifier.
 * @param {Array<Object<string, *>>} records - Ordered provider/source records.
 * @param {Object<string, *>} options - Optional provider normalization options.
 * @returns {Array<Object<string, *>>} Canonical speech-session events.
 */
export function adaptSpeechSessionRecords(provider, records, options = {}) {
  const events = adaptSpeechSessionRecordsRaw(provider, records, options);
  if (provider !== 'claude') return events;
  return events
    .map(normalizeVisibleClaudeUserEvent)
    .filter(event => event !== null);
}
