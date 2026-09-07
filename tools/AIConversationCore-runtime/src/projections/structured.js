import { deriveTurns } from '../derive/turns.js';
import { renderCanonicalMarkdown } from './markdown-revisions.js';
import { buildCanonicalPresentation } from './presentation-revisions.js';

/** Version of the structured interactive projection. */
const STRUCTURED_SCHEMA_VERSION = 2;
/** Policy declaring that consumers split only at presentation-tree boundaries. */
const PRESENTATION_SPLIT_POLICY = 'presentation-tree';

/**
 * Returns the canonical source block index when one exists.
 *
 * @param {Object<string, *>} block - Canonical block.
 * @returns {number|null} Source block index or null.
 */
function sourceBlockIndex(block) {
  const value = block?.source?.block_index;
  return Number.isInteger(value) ? value : null;
}

/**
 * Projects one canonical block into the flattened interactive-consumer shape.
 *
 * @param {Object<string, *>} event - Canonical owner event.
 * @param {Object<string, *>} block - Canonical block.
 * @param {number} blockIndex - Canonical block ordinal within the event.
 * @returns {Object<string, *>} Flattened canonical unit.
 */
function projectBlock(event, block, blockIndex) {
  return {
    id: block?.id ?? `${event.id}:block:${blockIndex}`,
    event_id: event.id,
    provider: event.provider ?? null,
    source_record_id: event.source_record_id ?? event?.source?.record_id ?? null,
    source_index: Number.isInteger(event.source_index)
      ? event.source_index
      : Number.isInteger(event?.source?.record_index)
        ? event.source.record_index
        : null,
    source_block_index: sourceBlockIndex(block),
    event_kind: event.kind ?? null,
    role: event.role ?? null,
    channel: event.channel ?? null,
    visibility: event.visibility ?? null,
    content_type: event.content_type ?? null,
    block_type: block?.type ?? null,
    block
  };
}

/**
 * Converts presentation source aliases to the compatibility structural-unit shape.
 *
 * @param {Object<string, *>} node - Atomic presentation node.
 * @returns {Object<string, *>} Structural-unit descriptor.
 */
function structuralUnit(node) {
  const sources = Array.isArray(node?.source) ? node.source : [];
  return {
    id: node.id,
    kind: node.kind,
    atomic: Boolean(node.atomic),
    source_indexes: sources
      .map(source => source?.record_index)
      .filter(Number.isInteger),
    source_record_ids: sources
      .map(source => source?.record_id)
      .filter(value => value != null)
      .map(String)
  };
}

/**
 * Collects atomic presentation nodes recursively in display order.
 *
 * @param {Object<string, *>} presentation - Canonical presentation tree.
 * @returns {Array<Object<string, *>>} Atomic structural-unit descriptors.
 */
function collectStructuralUnits(presentation) {
  const units = [];

  /**
   * Visits one presentation node recursively.
   *
   * @param {Object<string, *>} node - Presentation node.
   * @returns {void} No value is returned.
   */
  const visit = node => {
    if (!node || typeof node !== 'object') return;
    if (node.atomic) units.push(structuralUnit(node));
    for (const child of node.children ?? []) visit(child);
  };

  for (const turn of presentation.turns ?? []) visit(turn);
  return units;
}

/**
 * Enables renderer provenance without mutating caller canonical events.
 *
 * This compatibility Markdown remains available while consumers migrate to the
 * presentation tree. It is not used to discover structural boundaries.
 *
 * @param {Object<string, *>} event - Canonical event.
 * @returns {Object<string, *>} Event copy with debug provenance enabled.
 */
function withRenderProvenance(event) {
  return {
    ...event,
    projection: {
      ...(event?.projection ?? {}),
      debug_provenance: true
    }
  };
}

/**
 * Projects canonical events for interactive consumers.
 *
 * The presentation tree is authoritative for structural grouping, atomicity,
 * source aliases, and display order. Markdown remains a compatibility/output
 * serialization only and is never reparsed to infer those semantics.
 *
 * @param {Array<Object<string, *>>} events - Ordered canonical events.
 * @returns {Object<string, *>} Structured projection.
 */
export function projectCanonicalConversation(events) {
  if (!Array.isArray(events)) {
    throw new TypeError('projectCanonicalConversation expects an event array');
  }

  const units = [];
  for (const event of events) {
    const blocks = Array.isArray(event?.blocks) ? event.blocks : [];
    for (let blockIndex = 0; blockIndex < blocks.length; ++blockIndex) {
      units.push(projectBlock(event, blocks[blockIndex], blockIndex));
    }
  }

  const presentation = buildCanonicalPresentation(events);
  return {
    schema_version: STRUCTURED_SCHEMA_VERSION,
    events,
    turns: deriveTurns(events),
    units,
    presentation: {
      schema_version: presentation.schema_version,
      split_policy: PRESENTATION_SPLIT_POLICY,
      structural_units: collectStructuralUnits(presentation),
      tree: presentation
    },
    markdown: renderCanonicalMarkdown(events.map(withRenderProvenance))
  };
}
