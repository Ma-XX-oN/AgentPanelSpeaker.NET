import { projectRevisionVisibility } from './revision-visibility.js';
import { projectCanonicalConversation as projectBaseConversation } from './structured.js';

/**
 * Resolves the effective visibility and revision metadata for one presentation
 * node.
 *
 * @param {Object<string, *>} node - Canonical presentation node or turn.
 * @param {Map<string, Object<string, *>>} eventsById - Projected events by canonical ID.
 * @returns {Object<string, *>} Projection metadata for the presentation node.
 */
function nodeProjection(node, eventsById) {
  const sourceEvents = (node?.source ?? [])
    .map(source => eventsById.get(source?.event_id))
    .filter(Boolean);
  const visible = sourceEvents.length === 0 ||
    sourceEvents.some(event => event?.projection?.visible !== false);
  const revisionEvent = sourceEvents.find(event =>
    typeof event?.revision_status === 'string' && event.revision_status.length);
  return {
    visible,
    ...(revisionEvent?.revision_status
      ? { revision_status: revisionEvent.revision_status }
      : {}),
    ...(Number.isInteger(revisionEvent?.revision_depth)
      ? { revision_depth: revisionEvent.revision_depth }
      : {})
  };
}

/**
 * Adds visibility metadata to the canonical presentation tree without removing
 * nodes or changing their IDs/order.
 *
 * @param {Object<string, *>} presentation - Structured presentation wrapper.
 * @param {Array<Object<string, *>>} events - Projected canonical events.
 * @returns {Object<string, *>} Presentation wrapper with visibility metadata.
 */
function annotatePresentation(presentation, events) {
  if (!events.some(event => event?.projection?.visible === false)) {
    return presentation;
  }
  const eventsById = new Map(events.map(event => [event?.id, event]));

  /**
   * Clones one presentation node recursively with projection metadata.
   *
   * @param {Object<string, *>} node - Presentation node.
   * @returns {Object<string, *>} Annotated node clone.
   */
  const annotateNode = node => ({
    ...node,
    projection: {
      ...(node?.projection ?? {}),
      ...nodeProjection(node, eventsById)
    },
    ...(Array.isArray(node?.children)
      ? { children: node.children.map(annotateNode) }
      : {})
  });

  const tree = presentation?.tree ?? {};
  return {
    ...presentation,
    tree: {
      ...tree,
      turns: (tree.turns ?? []).map(annotateNode)
    }
  };
}

/**
 * Returns the canonical Markdown projection for the current effective
 * visibility while preserving the base structured renderer's provenance/header
 * enrichment.
 *
 * @param {Array<Object<string, *>>} projectedEvents - Full projected inventory.
 * @param {Object<string, *>} fullResult - Base projection of the full inventory.
 * @returns {string} Markdown for effectively visible events.
 */
function visibleMarkdown(projectedEvents, fullResult) {
  const visibleEvents = projectedEvents.filter(event =>
    event?.projection?.visible !== false);
  if (visibleEvents.length === projectedEvents.length) return fullResult.markdown;
  return projectBaseConversation(visibleEvents).markdown;
}

/**
 * Projects a complete canonical event inventory for interactive consumers.
 *
 * Visibility changes annotate the same canonical events and presentation nodes;
 * they never remove or renumber them.  This preserves stable speech, search,
 * highlighting, and virtualization identities while allowing downstream UI to
 * hide/show historical revisions cheaply. Markdown remains a serialization of
 * the effectively visible projection and therefore omits hidden revisions.
 *
 * @param {Array<Object<string, *>>} events - Complete canonical event inventory.
 * @param {Object<string, *>} options - Projection options.
 * @returns {Object<string, *>} Structured canonical projection.
 */
export function projectCanonicalConversation(events, options = {}) {
  const projectedEvents = projectRevisionVisibility(events, options);
  const result = projectBaseConversation(projectedEvents);
  const hasRevisionProjection = projectedEvents !== events;
  if (!hasRevisionProjection) return result;

  return {
    ...result,
    events: projectedEvents,
    presentation: annotatePresentation(result.presentation, projectedEvents),
    projection_options: {
      include_rolled_back_turns: options?.includeRolledBackTurns === true
    },
    markdown: visibleMarkdown(projectedEvents, result)
  };
}
