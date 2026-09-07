import { adaptCodexRecords } from './codex.js';
import { adaptInteractiveSessionRecords as adaptLegacyInteractiveSessionRecords } from './session.js';

/**
 * Returns one source timestamp when a provider record supplies it.
 *
 * @param {Object<string, *>} record - Provider/source record.
 * @returns {string|null} Source timestamp or null.
 */
function sourceTimestamp(record) {
  return typeof record?.timestamp === 'string' ? record.timestamp : null;
}

/**
 * Copies source timestamp provenance onto an event and its blocks.
 *
 * @param {Object<string, *>} event - Canonical event.
 * @param {Array<Object<string, *>>} records - Ordered provider/source records.
 * @returns {Object<string, *>} Canonical event clone with timestamp provenance.
 */
function withTimestamp(event, records) {
  const sourceIndex = Number.isInteger(event?.source_index)
    ? event.source_index
    : event?.source?.record_index;
  const timestamp = Number.isInteger(sourceIndex)
    ? sourceTimestamp(records[sourceIndex])
    : null;
  if (!timestamp) return event;
  return {
    ...event,
    source: { ...(event.source ?? {}), timestamp },
    blocks: (event.blocks ?? []).map(block => ({
      ...block,
      source: { ...(block.source ?? {}), timestamp }
    }))
  };
}

/**
 * Parses a Codex function-call argument value into an object when possible.
 *
 * @param {*} value - Provider arguments value.
 * @returns {Object<string, *>|null} Parsed object or null.
 */
function parseObject(value) {
  if (value && typeof value === 'object' && !Array.isArray(value)) return value;
  if (typeof value !== 'string') return null;
  try {
    const parsed = JSON.parse(value);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

/**
 * Adds interactive Codex metadata without changing recorded text.
 *
 * Agent-message phase is retained as the canonical channel used by speech
 * consumers. request_user_input retains source `isSecret` metadata that is not
 * represented by the ordinary provider adapter.
 *
 * @param {Object<string, *>} event - Canonical Codex event.
 * @param {Array<Object<string, *>>} records - Ordered Codex source records.
 * @returns {Object<string, *>} Enriched event clone.
 */
function withCodexInteractiveSemantics(event, records) {
  if (!Number.isInteger(event?.source_index)) return event;
  const record = records[event.source_index];
  let enriched = event;

  if (record?.type === 'event_msg' &&
      record?.payload?.type === 'agent_message' &&
      typeof record?.payload?.phase === 'string' &&
      record.payload.phase) {
    enriched = { ...enriched, channel: record.payload.phase };
  }

  if (enriched?.kind !== 'tool_call') return enriched;
  const callBlockIndex = (enriched.blocks ?? []).findIndex(block =>
    block?.type === 'tool_call' && block?.name === 'request_user_input');
  if (callBlockIndex < 0) return enriched;

  const argumentsObject = parseObject(record?.payload?.arguments);
  const sourceQuestions = Array.isArray(argumentsObject?.questions)
    ? argumentsObject.questions
    : [];
  const blocks = [...enriched.blocks];
  const block = blocks[callBlockIndex];
  const questions = Array.isArray(block?.request_user_input?.questions)
    ? block.request_user_input.questions
    : [];
  blocks[callBlockIndex] = {
    ...block,
    request_user_input: {
      ...(block.request_user_input ?? {}),
      questions: questions.map((question, index) => ({
        ...question,
        is_secret: Boolean(sourceQuestions[index]?.isSecret ?? sourceQuestions[index]?.is_secret)
      }))
    }
  };
  return { ...enriched, blocks };
}

/**
 * Creates one hidden Codex lifecycle event for a task-completion record.
 *
 * @param {Object<string, *>} record - Codex source record.
 * @param {number} sourceIndex - Zero-based source record index.
 * @returns {Object<string, *>|null} Canonical lifecycle event or null.
 */
function codexTaskCompleteEvent(record, sourceIndex) {
  if (record?.type !== 'event_msg' || record?.payload?.type !== 'task_complete') return null;
  const timestamp = sourceTimestamp(record);
  const source = {
    provider: 'codex',
    record_id: null,
    record_index: sourceIndex,
    ...(timestamp ? { timestamp } : {})
  };
  return {
    id: `codex:record:${sourceIndex}:task_complete`,
    provider: 'codex',
    source_record_id: null,
    source_index: sourceIndex,
    kind: 'notice',
    role: 'system',
    channel: null,
    visibility: 'hidden',
    content_type: 'task_complete',
    blocks: [],
    lifecycle: { type: 'task_complete', timestamp },
    relationships: { tool_call_id: null },
    source
  };
}

/**
 * Creates one hidden Codex completed-plan lifecycle event used by speech consumers.
 *
 * @param {Object<string, *>} record - Codex source record.
 * @param {number} sourceIndex - Zero-based source record index.
 * @returns {Object<string, *>|null} Canonical completed-plan event or null.
 */
function codexCompletedPlanEvent(record, sourceIndex) {
  const item = record?.type === 'event_msg' && record?.payload?.type === 'item_completed'
    ? record.payload.item
    : null;
  if (!item || String(item.type ?? '').toLowerCase() !== 'plan' || typeof item.text !== 'string') {
    return null;
  }
  const text = item.text.trim();
  if (!text) return null;
  const timestamp = sourceTimestamp(record);
  const source = {
    provider: 'codex',
    record_id: null,
    record_index: sourceIndex,
    ...(timestamp ? { timestamp } : {})
  };
  return {
    id: `codex:record:${sourceIndex}:completed_plan`,
    provider: 'codex',
    source_record_id: null,
    source_index: sourceIndex,
    kind: 'notice',
    role: 'assistant',
    channel: null,
    visibility: 'hidden',
    content_type: 'completed_plan',
    blocks: [{
      id: `codex:record:${sourceIndex}:completed_plan:block`,
      type: 'text',
      text,
      source
    }],
    relationships: { tool_call_id: null },
    source
  };
}

/**
 * Adapts one complete Codex session without removing provider-recorded User content.
 *
 * @param {Array<Object<string, *>>} records - Ordered Codex source records.
 * @param {Object<string, *>} options - Optional Codex normalization options.
 * @returns {Array<Object<string, *>>} Enriched canonical events in source order.
 */
function adaptCodexInteractiveSession(records, options) {
  const base = adaptCodexRecords(records, options)
    .map(event => withCodexInteractiveSemantics(withTimestamp(event, records), records));
  const synthetic = [];
  records.forEach((record, sourceIndex) => {
    const completion = codexTaskCompleteEvent(record, sourceIndex);
    if (completion) synthetic.push(completion);
    const plan = codexCompletedPlanEvent(record, sourceIndex);
    if (plan) synthetic.push(plan);
  });
  return [...base, ...synthetic].sort((left, right) =>
    (left.source_index ?? 0) - (right.source_index ?? 0));
}

/**
 * Adapts a complete provider session for interactive consumers.
 *
 * Claude continues through the established adapter. Codex uses the canonical
 * Codex adapter directly so recorded IDE context is never removed and revision
 * options remain shared across all consumers.
 *
 * @param {string} provider - Canonical provider identifier (`claude` or `codex`).
 * @param {Array<Object<string, *>>} records - Ordered provider/source records.
 * @param {Object<string, *>} options - Optional provider normalization options.
 * @returns {Array<Object<string, *>>} Enriched canonical events.
 */
export function adaptInteractiveSessionRecords(provider, records, options = {}) {
  if (!Array.isArray(records)) throw new TypeError('Session records must be an array.');
  if (provider === 'codex') return adaptCodexInteractiveSession(records, options);
  return adaptLegacyInteractiveSessionRecords(provider, records);
}
