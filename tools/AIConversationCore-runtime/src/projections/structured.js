import { deriveTurns } from '../derive/turns.js';
import { renderCanonicalMarkdown } from './markdown.js';

/**
 * Clones one canonical event with renderer provenance enabled.
 *
 * The canonical event itself remains unchanged.  The clone is used only for the
 * Markdown projection so consumers can associate renderer-generated structures
 * with source records without reinterpreting provider-native JSON.
 *
 * @param {Object<string, *>} event - Canonical event to project.
 * @returns {Object<string, *>} Canonical event clone with debug provenance enabled.
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
 * Returns the zero-based source block index when canonical provenance supplies it.
 *
 * @param {Object<string, *>} block - Canonical content block.
 * @returns {number|null} Source block index, or null when unavailable.
 */
function sourceBlockIndex(block) {
  return Number.isInteger(block?.source?.block_index)
    ? block.source.block_index
    : Number.isInteger(block?.source?.part_index)
      ? block.source.part_index
      : null;
}

/**
 * Projects one canonical block into a C#-friendly structured unit.
 *
 * The original canonical block is retained verbatim in `block`.  Convenience
 * identity fields are copied beside it so consumers do not need to rediscover
 * source/event relationships from Markdown or provider-native records.
 *
 * @param {Object<string, *>} event - Canonical event that owns the block.
 * @param {Object<string, *>} block - Canonical block to project.
 * @param {number} blockIndex - Zero-based canonical block index in the event.
 * @returns {Object<string, *>} Structured canonical projection unit.
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
 * Projects canonical events into one deterministic structured consumer payload.
 *
 * This is deliberately a projection of the existing canonical model, not a
 * second provider-neutral semantic schema.  `events` retain the canonical data,
 * `units` provide flattened event/block identity for interactive consumers,
 * `turns` reuse the canonical turn derivation, and `markdown` reuses the canonical
 * renderer with explicit source provenance comments enabled.
 *
 * @param {Array<Object<string, *>>} events - Ordered canonical events.
 * @returns {Object<string, *>} Structured canonical consumer projection.
 */
export function projectCanonicalConversation(events) {
  if (!Array.isArray(events)) {
    throw new TypeError('Canonical events must be an array.');
  }

  const units = [];
  for (const event of events) {
    const blocks = Array.isArray(event?.blocks) ? event.blocks : [];
    blocks.forEach((block, blockIndex) => {
      units.push(projectBlock(event, block, blockIndex));
    });
  }

  return {
    schema_version: 1,
    events,
    turns: deriveTurns(events),
    units,
    markdown: renderCanonicalMarkdown(events.map(withRenderProvenance))
  };
}
