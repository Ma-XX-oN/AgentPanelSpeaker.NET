/**
 * Checks whether visible turn event.
 *
 * @param {Object<string, *>|null} event - The canonical event being inspected, normalized, or rendered.
 * @returns {boolean} Whether the canonical event is a visible User/Assistant turn event.
 */
function isVisibleTurnEvent(event) {
  return event?.visibility === 'visible' &&
    (event?.role === 'user' || event?.role === 'assistant') &&
    (event?.kind === 'message' || event?.kind === 'commentary' || event?.kind === 'reasoning_summary');
}

/**
 * Handles source record.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {Object<string, *>} The normalized source-provenance record carried by a derived turn.
 */
function sourceRecord(event) {
  const source = event?.source && typeof event.source === 'object' ? event.source : {};
  const sourceIndex = Number.isInteger(event?.source_index)
    ? event.source_index
    : Number.isInteger(source.record_index) ? source.record_index : null;
  const recordId = event?.source_record_id ?? source.record_id ?? null;

  return {
    record_id: recordId,
    record_index: sourceIndex,
    turn_id: source.turn_id ?? recordId,
    create_time: source.create_time ?? null,
    update_time: source.update_time ?? null,
    turn_exchange_id: source.turn_exchange_id ?? event?.relationships?.turn_exchange_id ?? null,
    working_turn_id: source.working_turn_id ?? event?.relationships?.working_turn_id ?? null
  };
}

/**
 * Handles append event.
 *
 * @param {Object<string, *>} turn - The derived canonical turn whose identity or header is being projected.
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {void} No value is returned; the supplied turn is updated in place.
 */
function appendEvent(turn, event) {
  turn.event_ids.push(event.id);
  turn.source.record_ids.push(event.source_record_id);
  turn.source.records.push(sourceRecord(event));
}

/**
 * Handles new turn.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {number} turnIndex - The zero-based turn index.
 * @returns {Object<string, *>} A new derived turn initialized from the supplied canonical event.
 */
function newTurn(event, turnIndex) {
  return {
    id: `turn:${event.id}`,
    index: turnIndex,
    role: event.role,
    event_ids: [event.id],
    source: {
      provider: event.source?.provider ?? event.provider ?? null,
      record_ids: [event.source_record_id],
      records: [sourceRecord(event)]
    }
  };
}

/**
 * Derives turns.
 *
 * @param {Array<Object<string, *>>} events - The ordered canonical events to process.
 * @returns {Array<Object<string, *>>} Derived turns in the same canonical event order, with contiguous Assistant activity grouped into its turn.
 */
export function deriveTurns(events) {
  if (!Array.isArray(events)) throw new TypeError('Canonical events must be an array.');

  // Derived turns are accumulated in canonical event order; no provider event reordering occurs here.
  const turns = [];
  // Tracks turn indexes that already contain a visible message so later assistant activity is attached correctly.
  const turnsWithMessage = new Set();
  for (const event of events) {
    if (!isVisibleTurnEvent(event)) continue;

    const current = turns.at(-1);
    if (event.kind === 'reasoning_summary' && event.role === 'assistant' &&
        current?.role === 'assistant' && !turnsWithMessage.has(current.id)) {
      appendEvent(current, event);
      continue;
    }

    if (event.kind === 'commentary' && current?.role === 'assistant') {
      appendEvent(current, event);
      continue;
    }

    if (event.kind === 'message' && event.role === 'assistant' &&
        current?.role === 'assistant' && !turnsWithMessage.has(current.id)) {
      appendEvent(current, event);
      turnsWithMessage.add(current.id);
      continue;
    }

    const turn = newTurn(event, turns.length);
    turns.push(turn);
    if (event.kind === 'message') turnsWithMessage.add(turn.id);
  }
  return turns;
}
