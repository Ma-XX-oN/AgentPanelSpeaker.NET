import { buildCanonicalPresentation as buildBasePresentation } from './presentation.js';
import { isHistoricalRevision } from './revision-visibility.js';

/**
 * Returns the visible revision/execution suffix for one canonical turn event.
 *
 * @param {Object<string, *>} event - Canonical User/Assistant event.
 * @returns {string} Parenthesized status suffix or an empty string.
 */
function turnStatusSuffix(event) {
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
 * Returns all canonical events referenced by one presentation turn.
 *
 * @param {Object<string, *>} turn - Canonical presentation turn.
 * @param {Map<string, Object<string, *>>} eventsById - Canonical events by ID.
 * @returns {Array<Object<string, *>>} Resolved source events in turn order.
 */
function sourceEventsForTurn(turn, eventsById) {
  return (turn?.source ?? [])
    .map(source => eventsById.get(source?.event_id))
    .filter(Boolean);
}

/**
 * Finds the canonical message that supplies revision metadata for one
 * presentation turn.
 *
 * Assistant turns may begin with reasoning/commentary before their final
 * message, so the matching message is located from the turn's complete source
 * list rather than assumed to be its first event.
 *
 * @param {Object<string, *>} turn - Canonical presentation turn.
 * @param {Array<Object<string, *>>} sourceEvents - Resolved turn source events.
 * @returns {Object<string, *>|null} Matching message event or null.
 */
function revisionMessageForTurn(turn, sourceEvents) {
  const role = turn?.actor?.role;
  if (role !== 'user' && role !== 'assistant') return null;
  return sourceEvents.find(event =>
    event?.role === role && event?.kind === 'message') ?? null;
}

/**
 * Returns Core-owned effective visibility metadata for one revision-bearing
 * presentation turn.
 *
 * A revision lineage remains one stable presentation inventory.  Projection
 * visibility may change without changing IDs, status, or depth, so downstream
 * interactive consumers receive both the current effective visibility and the
 * stable fact that a turn belongs to revision history.  Consumers therefore do
 * not need to interpret status strings or provider-native rollback markers.
 *
 * @param {Object<string, *>} revisionEvent - Revision-bearing message event.
 * @param {Array<Object<string, *>>} sourceEvents - Resolved turn source events.
 * @returns {Object<string, boolean>} Effective and stable revision visibility facts.
 */
function revisionTurnProjection(revisionEvent, sourceEvents) {
  const visible = sourceEvents.length === 0 ||
    sourceEvents.some(event => event?.projection?.visible !== false);
  return {
    visible,
    revision_history_controlled:
      typeof revisionEvent?.revision_status === 'string' &&
      revisionEvent.revision_status !== 'normal',
    historical_revision: isHistoricalRevision(revisionEvent)
  };
}

/**
 * Builds the canonical presentation tree and carries canonical revision status,
 * depth, and effective visibility into both User and Assistant turns.
 *
 * @param {Array<Object<string, *>>} events - Ordered canonical event stream.
 * @returns {Object<string, *>} Canonical presentation tree.
 */
export function buildCanonicalPresentation(events) {
  const presentation = buildBasePresentation(events);
  const eventsById = new Map(events.map(event => [event?.id, event]));

  for (const turn of presentation.turns ?? []) {
    const sourceEvents = sourceEventsForTurn(turn, eventsById);
    const sourceEvent = revisionMessageForTurn(turn, sourceEvents);
    const suffix = turnStatusSuffix(sourceEvent);
    if (!suffix) continue;
    turn.actor = {
      ...turn.actor,
      label: `${turn.actor.label}${suffix}`,
      revision_status: sourceEvent.revision_status,
      revision_depth: sourceEvent.revision_depth,
      execution_status: sourceEvent.execution_status
    };
    turn.projection = {
      ...(turn.projection ?? {}),
      ...revisionTurnProjection(sourceEvent, sourceEvents)
    };
  }

  return presentation;
}
