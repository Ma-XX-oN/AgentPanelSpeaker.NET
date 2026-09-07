import { buildCanonicalPresentation as buildBasePresentation } from './presentation.js';

/**
 * Returns the visible revision/execution suffix for one canonical User event.
 *
 * @param {Object<string, *>} event - Canonical User event.
 * @returns {string} Parenthesized status suffix or an empty string.
 */
function userStatusSuffix(event) {
  const statuses = [];
  if (event?.revision_status && event.revision_status !== 'normal') {
    statuses.push(event.revision_status);
  }
  if (event?.execution_status === 'aborted') statuses.push('aborted');
  return statuses.length ? ` (${statuses.join(', ')})` : '';
}

/**
 * Builds the canonical presentation tree and carries canonical User status into
 * the actor label used by structural HTML/interactive consumers.
 *
 * @param {Array<Object<string, *>>} events - Ordered canonical event stream.
 * @returns {Object<string, *>} Canonical presentation tree.
 */
export function buildCanonicalPresentation(events) {
  const presentation = buildBasePresentation(events);
  const eventsById = new Map(events.map(event => [event?.id, event]));

  for (const turn of presentation.turns ?? []) {
    if (turn?.actor?.role !== 'user') continue;
    const sourceEvent = (turn.source ?? [])
      .map(source => eventsById.get(source?.event_id))
      .find(event => event?.role === 'user' && event?.kind === 'message');
    const suffix = userStatusSuffix(sourceEvent);
    if (!suffix) continue;
    turn.actor = {
      ...turn.actor,
      label: `${turn.actor.label}${suffix}`,
      revision_status: sourceEvent.revision_status,
      execution_status: sourceEvent.execution_status
    };
  }

  return presentation;
}
