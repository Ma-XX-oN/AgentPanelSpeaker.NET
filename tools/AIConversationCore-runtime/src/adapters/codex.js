/**
 * Returns the stable source identity used to derive Codex canonical IDs.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {string} The stable source identity used to derive Codex canonical IDs.
 */
function sourceIdentity(record, sourceIndex) {
  return record?.payload?.call_id ?? `record:${sourceIndex}`;
}

/**
 * Builds canonical source provenance for a Codex source record.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {Object<string, *>} Canonical Codex source provenance for the provider record.
 */
function source(record, sourceIndex) {
  return {
    provider: 'codex',
    record_id: null,
    record_index: sourceIndex
  };
}

/**
 * Checks whether tool call.
 *
 * @param {Object<string, *>} payload - The provider payload object being classified as a tool call or tool result.
 * @returns {boolean} Whether the source record represents a supported Codex tool call.
 */
function isToolCall(payload) {
  return payload?.type === 'function_call' || payload?.type === 'custom_tool_call';
}

/**
 * Checks whether tool result.
 *
 * @param {Object<string, *>} payload - The provider payload object being classified as a tool call or tool result.
 * @returns {boolean} Whether the source record represents a supported Codex tool result.
 */
function isToolResult(payload) {
  return payload?.type === 'function_call_output' || payload?.type === 'custom_tool_call_output';
}

/**
 * Parses JSON object.
 *
 * @param {string} value - The input value to process.
 * @returns {Object<string, *>|null} The parsed non-array JSON object, or null when parsing fails or the value is not an object.
 */
function parseJsonObject(value) {
  if (typeof value !== 'string') return null;
  try {
    const parsed = JSON.parse(value);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

/**
 * Normalizes questions.
 *
 * @param {string} argumentsText - The serialized Codex tool arguments to parse and normalize.
 * @returns {Array<Object<string, *>>|null} Normalized request-user-input questions, or null when the argument JSON has no questions array.
 */
function normalizedQuestions(argumentsText) {
  const parsed = parseJsonObject(argumentsText);
  if (!Array.isArray(parsed?.questions)) return null;
  return parsed.questions.map(question => ({
    id: typeof question?.id === 'string' ? question.id : null,
    question: typeof question?.question === 'string' ? question.question : null,
    options: Array.isArray(question?.options)
      ? question.options.map(option => ({
          label: typeof option?.label === 'string' ? option.label : null,
          description: typeof option?.description === 'string' ? option.description : null
        }))
      : []
  }));
}

/**
 * Normalizes answers.
 *
 * @param {string} outputText - The serialized Codex tool output to parse and normalize.
 * @returns {Object<string, Array<string>>|null} Answers indexed by question ID, or null when the tool output has no answers object.
 */
function normalizedAnswers(outputText) {
  const parsed = parseJsonObject(outputText);
  const answers = parsed?.answers;
  if (!answers || typeof answers !== 'object' || Array.isArray(answers)) return null;
  const normalized = {};
  for (const [id, answer] of Object.entries(answers)) {
    normalized[id] = Array.isArray(answer?.answers)
      ? answer.answers.filter(value => typeof value === 'string')
      : [];
  }
  return normalized;
}

/**
 * Builds a canonical tool-call event from the provider-specific tool-call source record/block.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {Object<string, *>} A canonical Codex tool-call event preserving source index and call correlation.
 */
function toolCallEvent(record, sourceIndex) {
  const payload = record.payload;
  const sourceInfo = source(record, sourceIndex);
  const identity = sourceIdentity(record, sourceIndex);
  const callId = typeof payload.call_id === 'string' ? payload.call_id : null;
  const isFunction = payload.type === 'function_call';
  const block = {
    id: `codex:${identity}:tool_call:${sourceIndex}:block`,
    type: 'tool_call',
    call_id: callId,
    name: payload?.name ?? null,
    input: isFunction ? payload?.arguments ?? null : payload?.input ?? null,
    input_format: isFunction ? 'json_string' : 'text',
    source: sourceInfo
  };

  if (payload?.name === 'request_user_input') {
    block.request_user_input = { questions: normalizedQuestions(payload?.arguments) ?? [] };
  }
  if (payload?.name === 'apply_patch' && typeof payload?.input === 'string') {
    block.file_change = { patch: payload.input };
  }

  return {
    id: `codex:${identity}:tool_call:${sourceIndex}`,
    provider: 'codex',
    source_record_id: null,
    source_index: sourceIndex,
    kind: 'tool_call',
    role: 'assistant',
    channel: null,
    visibility: 'visible',
    content_type: payload.type,
    blocks: [block],
    citations: [],
    resources: [],
    relationships: { tool_call_id: callId },
    source: sourceInfo
  };
}

/**
 * Builds a canonical tool-result event from the provider-specific tool-result source record/block.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {Object<string, *>} A canonical Codex tool-result event preserving source index and call correlation.
 */
function toolResultEvent(record, sourceIndex) {
  const payload = record.payload;
  const sourceInfo = source(record, sourceIndex);
  const identity = sourceIdentity(record, sourceIndex);
  const callId = typeof payload.call_id === 'string' ? payload.call_id : null;
  const block = {
    id: `codex:${identity}:tool_result:${sourceIndex}:block`,
    type: 'tool_result',
    call_id: callId,
    name: null,
    output: payload?.output ?? null,
    output_format: typeof payload?.output,
    source: sourceInfo
  };
  const answers = normalizedAnswers(payload?.output);
  if (answers) block.request_user_input_response = { answers };

  return {
    id: `codex:${identity}:tool_result:${sourceIndex}`,
    provider: 'codex',
    source_record_id: null,
    source_index: sourceIndex,
    kind: 'tool_result',
    role: 'tool',
    channel: null,
    visibility: 'visible',
    content_type: payload.type,
    blocks: [block],
    citations: [],
    resources: [],
    relationships: { tool_call_id: callId },
    source: sourceInfo
  };
}

/**
 * Builds a canonical message/commentary event from provider-specific message content.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {string} role - The canonical message role assigned to the generated event.
 * @param {string} kind - The canonical kind/category being processed.
 * @param {string|null} channel - The Codex message channel, or null when the source record has no channel.
 * @param {string} text - The text value to process.
 * @param {string} contentType - The provider content-type label preserved in source provenance.
 * @returns {Object<string, *>} A canonical Codex message, commentary, or reasoning-summary event for the supplied provider text.
 */
function messageEvent(record, sourceIndex, role, kind, channel, text, contentType) {
  const sourceInfo = source(record, sourceIndex);
  return {
    id: `codex:record:${sourceIndex}:${kind}`,
    provider: 'codex',
    source_record_id: null,
    source_index: sourceIndex,
    kind,
    role,
    channel,
    visibility: 'visible',
    content_type: contentType,
    blocks: [{
      id: `codex:record:${sourceIndex}:${kind}:block`,
      type: kind === 'reasoning_summary' ? 'reasoning_summary' : 'text',
      ...(kind === 'reasoning_summary'
        ? { summary: null, content: text, chunks: null, finished: null }
        : { text }),
      source: sourceInfo
    }],
    citations: [],
    resources: [],
    relationships: { tool_call_id: null },
    source: sourceInfo
  };
}

/**
 * Adapts Codex records without applying branch-history semantics.
 *
 * @param {Array<Object<string, *>>} records - Ordered Codex source records.
 * @returns {Array<Object<string, *>>} Base canonical Codex events in provider source order.
 */
function adaptBaseCodexRecords(records) {
  const events = [];
  records.forEach((record, sourceIndex) => {
    const payload = record?.payload;
    if (!payload || typeof payload !== 'object') return;

    if (record.type === 'response_item') {
      if (isToolCall(payload)) events.push(toolCallEvent(record, sourceIndex));
      if (isToolResult(payload)) events.push(toolResultEvent(record, sourceIndex));
      return;
    }

    if (record.type !== 'event_msg') return;
    if (payload.type === 'user_message' && typeof payload.message === 'string') {
      events.push(messageEvent(record, sourceIndex, 'user', 'message', null,
        payload.message, 'user_message'));
      return;
    }
    if (payload.type === 'agent_reasoning' && typeof payload.text === 'string') {
      events.push(messageEvent(record, sourceIndex, 'assistant', 'reasoning_summary', null,
        payload.text, 'agent_reasoning'));
      return;
    }
    if (payload.type === 'agent_message' && typeof payload.message === 'string') {
      const channel = payload.phase === 'commentary' ? 'commentary' : 'final';
      const kind = channel === 'commentary' ? 'commentary' : 'message';
      events.push(messageEvent(record, sourceIndex, 'assistant', kind, channel,
        payload.message, 'agent_message'));
    }
  });
  return events;
}

/**
 * Formats a Codex model identifier for human-readable presentation.
 *
 * @param {string|null} model - Provider model identifier.
 * @returns {string} Human-readable model label.
 */
function modelLabel(model) {
  if (typeof model !== 'string') return '';
  return model.replace(/^gpt-/i, 'GPT-');
}

/**
 * Creates a visible canonical notice for an evidenced model transition.
 *
 * @param {Object<string, *>} record - Replacement User source record.
 * @param {number} sourceIndex - Replacement User source index.
 * @param {string} previousModel - Previous revision model identifier.
 * @param {string} currentModel - Replacement revision model identifier.
 * @returns {Object<string, *>} Canonical model-change notice.
 */
function modelChangeEvent(record, sourceIndex, previousModel, currentModel) {
  const sourceInfo = source(record, sourceIndex);
  const text = `Model changed from ${modelLabel(previousModel)} to ${modelLabel(currentModel)}`;
  return {
    id: `codex:record:${sourceIndex}:model_change`,
    provider: 'codex',
    source_record_id: null,
    source_index: sourceIndex,
    kind: 'notice',
    role: 'system',
    channel: null,
    visibility: 'visible',
    content_type: 'model_change',
    blocks: [{
      id: `codex:record:${sourceIndex}:model_change:block`,
      type: 'text',
      text,
      source: sourceInfo
    }],
    citations: [],
    resources: [],
    relationships: { tool_call_id: null },
    source: sourceInfo
  };
}

/**
 * Returns a Markdown heading suffix for independent revision/execution status.
 *
 * @param {Object<string, *>} interaction - Canonical Codex interaction state.
 * @returns {string} Parenthesized status suffix, or an empty string.
 */
function interactionHeadingSuffix(interaction) {
  const statuses = [];
  if (interaction.revision_status !== 'normal') statuses.push(interaction.revision_status);
  if (interaction.execution_status === 'aborted') statuses.push('aborted');
  return statuses.length ? ` (${statuses.join(', ')})` : '';
}

/**
 * Applies Codex rollback, edit, abort, and model-change semantics to canonical events.
 *
 * @param {Array<Object<string, *>>} records - Ordered Codex source records.
 * @param {Array<Object<string, *>>} baseEvents - Base canonical events in source order.
 * @param {Object<string, *>} options - Codex normalization options.
 * @returns {Array<Object<string, *>>} Canonical events with Codex revision semantics.
 */
function applyRevisionSemantics(records, baseEvents, options) {
  const includeRolledBackTurns = options?.includeRolledBackTurns === true;
  const eventsBySource = new Map();
  for (const event of baseEvents) {
    const list = eventsBySource.get(event.source_index) ?? [];
    list.push(event);
    eventsBySource.set(event.source_index, list);
  }

  const active = [];
  const interactionBySource = new Map();
  const modelNotices = new Map();
  let currentInteraction = null;
  let currentModel = null;
  let pendingHistory = [];

  records.forEach((record, sourceIndex) => {
    const payload = record?.payload;
    if (record?.type === 'turn_context' && typeof payload?.model === 'string') {
      currentModel = payload.model;
      return;
    }

    if (record?.type === 'event_msg' && payload?.type === 'turn_aborted') {
      if (currentInteraction) currentInteraction.execution_status = 'aborted';
      return;
    }

    if (record?.type === 'event_msg' && payload?.type === 'thread_rolled_back') {
      const count = Number.isInteger(payload?.num_turns) && payload.num_turns > 0
        ? payload.num_turns
        : 0;
      const popped = [];
      for (let index = 0; index < count && active.length; index += 1) {
        const interaction = active.pop();
        interaction.rolled_back = true;
        popped.unshift(interaction);
      }
      if (popped.length) {
        const inherited = popped.flatMap(interaction => interaction.history.length
          ? [...interaction.history, interaction]
          : [interaction]);
        pendingHistory = inherited.filter((interaction, index, list) =>
          list.indexOf(interaction) === index);
        currentInteraction = active.at(-1) ?? null;
      }
      return;
    }

    if (record?.type === 'event_msg' && payload?.type === 'user_message') {
      const previous = pendingHistory.at(-1) ?? null;
      const interaction = {
        model: currentModel,
        history: pendingHistory,
        rolled_back: false,
        revision_status: pendingHistory.length ? 'edited' : 'normal',
        execution_status: 'completed'
      };
      if (pendingHistory.length) {
        pendingHistory.forEach((historical, index) => {
          historical.revision_status = index === 0 ? 'original' : 'superseded';
        });
        if (previous?.model && interaction.model && previous.model !== interaction.model) {
          modelNotices.set(sourceIndex,
            modelChangeEvent(record, sourceIndex, previous.model, interaction.model));
        }
      }
      pendingHistory = [];
      active.push(interaction);
      currentInteraction = interaction;
    }

    if (currentInteraction && eventsBySource.has(sourceIndex)) {
      interactionBySource.set(sourceIndex, currentInteraction);
    }
  });

  if (pendingHistory.length) {
    pendingHistory.forEach((historical, index) => {
      historical.revision_status = index === 0 ? 'original' : 'superseded';
    });
  }

  const output = [];
  for (const event of baseEvents) {
    const interaction = interactionBySource.get(event.source_index);
    if (event.role === 'user' && event.kind === 'message') {
      const notice = modelNotices.get(event.source_index);
      if (notice) output.push(notice);
    }
    if (interaction?.rolled_back && !includeRolledBackTurns) continue;
    if (!interaction) {
      output.push(event);
      continue;
    }
    const headingSuffix = event.role === 'user' && event.kind === 'message'
      ? interactionHeadingSuffix(interaction)
      : '';
    output.push({
      ...event,
      revision_status: interaction.revision_status,
      execution_status: interaction.execution_status,
      model: interaction.model,
      ...(headingSuffix
        ? { projection: { ...(event.projection ?? {}), heading_suffix: headingSuffix } }
        : {})
    });
  }
  return output;
}

/**
 * Resolves Codex session metadata from a rollout and optional session index records.
 *
 * The caller owns source discovery. The core owns interpretation of the supplied
 * Codex records and does not depend on where either source originated.
 *
 * @param {Array<Object<string, *>>} records - Ordered Codex rollout records.
 * @param {Array<Object<string, *>>} sessionIndexRecords - Ordered Codex session-index records.
 * @returns {Object<string, string|null>} Canonical Codex session metadata.
 */
export function resolveCodexSessionMetadata(records, sessionIndexRecords = []) {
  if (!Array.isArray(records)) throw new TypeError('Codex records must be an array.');
  if (!Array.isArray(sessionIndexRecords)) {
    throw new TypeError('Codex session-index records must be an array.');
  }

  let sessionId = null;
  for (const record of records) {
    if (record?.type === 'session_meta' && typeof record?.payload?.id === 'string' &&
        record.payload.id.trim()) {
      sessionId = record.payload.id.trim();
      break;
    }
  }

  let title = null;
  if (sessionId) {
    for (const record of sessionIndexRecords) {
      if (record?.id !== sessionId || typeof record?.thread_name !== 'string') continue;
      const candidate = record.thread_name.trim();
      if (candidate) title = candidate;
    }
  }

  return {
    session_id: sessionId,
    title,
    title_source: title ? 'codex-session-index' : 'none'
  };
}

/**
 * Adapts Codex tool events.
 *
 * @param {Array<Object<string, *>>} records - The ordered provider/source records to process.
 * @returns {Array<Object<string, *>>} Canonical Codex tool events in provider source order.
 */
export function adaptCodexToolEvents(records) {
  if (!Array.isArray(records)) throw new TypeError('Codex records must be an array.');
  return adaptBaseCodexRecords(records).filter(event =>
    event.kind === 'tool_call' || event.kind === 'tool_result');
}

/**
 * Adapts Codex records.
 *
 * @param {Array<Object<string, *>>} records - The ordered provider/source records to process.
 * @param {Object<string, *>} options - Optional Codex normalization options.
 * @returns {Array<Object<string, *>>} Canonical Codex events in provider source order.
 */
export function adaptCodexRecords(records, options = {}) {
  if (!Array.isArray(records)) throw new TypeError('Codex records must be an array.');
  const baseEvents = adaptBaseCodexRecords(records);
  return applyRevisionSemantics(records, baseEvents, options);
}
