import {
  adaptCodexRecords as adaptLegacyCodexRecords,
  adaptCodexToolEvents,
  resolveCodexSessionMetadata
} from './codex.js';

/**
 * Builds the canonical revision/execution heading suffix for one event.
 *
 * Revision depth is part of the status token so all consumers can render
 * `original 0`, `superseded N`, and `edited N` without re-deriving provider
 * rollback semantics.
 *
 * @param {Object<string, *>} event - Canonical Codex event.
 * @returns {string} Parenthesized heading suffix or an empty string.
 */
function revisionHeadingSuffix(event) {
  const statuses = [];
  if (event?.revision_status && event.revision_status !== 'normal') {
    statuses.push(Number.isInteger(event.revision_depth)
      ? `${event.revision_status} ${event.revision_depth}`
      : event.revision_status);
  }
  if (event?.execution_status === 'aborted') statuses.push('aborted');
  return statuses.length ? ` (${statuses.join(', ')})` : '';
}

/**
 * Adds zero-based canonical revision depth to the complete Codex event stream.
 *
 * The legacy revision normalizer already assigns one revision status to every
 * event belonging to a historical/current interaction.  A User message starts
 * each interaction, so depth advances only at that canonical boundary and is
 * then copied to all subsequent events in the same interaction.  Normal turns
 * reset the lineage.  This consumes canonical revision semantics only; it does
 * not inspect provider rollback records.
 *
 * @param {Array<Object<string, *>>} events - Complete canonical Codex events.
 * @returns {Array<Object<string, *>>} Events carrying stable revision depth.
 */
function withRevisionDepth(events) {
  let depth = null;
  return events.map(event => {
    const isUserMessage = event?.role === 'user' && event?.kind === 'message';
    if (isUserMessage) {
      if (event.revision_status === 'original') {
        depth = 0;
      } else if (event.revision_status === 'superseded' ||
                 event.revision_status === 'edited') {
        depth = Number.isInteger(depth) ? depth + 1 : 0;
      } else {
        depth = null;
      }
    }

    if (!Number.isInteger(depth) ||
        !event?.revision_status || event.revision_status === 'normal') {
      return event;
    }

    const revisionEvent = {
      ...event,
      revision_depth: depth
    };
    const headingSuffix = event.kind === 'message' &&
      (event.role === 'user' || event.role === 'assistant')
        ? revisionHeadingSuffix(revisionEvent)
        : '';
    if (!headingSuffix) return revisionEvent;
    return {
      ...revisionEvent,
      projection: {
        ...(event.projection ?? {}),
        heading_suffix: headingSuffix
      }
    };
  });
}

/**
 * Adapts Codex records into the complete canonical revision inventory.
 *
 * Visibility is intentionally not a normalization option.  The legacy adapter
 * still understands `includeRolledBackTurns`; this retained adapter always asks
 * it for the complete revision history so projection settings can change later
 * without reparsing provider input or renumbering canonical events.
 *
 * @param {Array<Object<string, *>>} records - Ordered Codex source records.
 * @param {Object<string, *>} _options - Reserved normalization options.
 * @returns {Array<Object<string, *>>} Complete canonical Codex event inventory.
 */
export function adaptCodexRecords(records, _options = {}) {
  return withRevisionDepth(
    adaptLegacyCodexRecords(records, { includeRolledBackTurns: true })
  );
}

export {
  adaptCodexToolEvents,
  resolveCodexSessionMetadata
};
