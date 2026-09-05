import { adaptClaudeRecords } from './claude.js';
import { adaptCodexRecords } from './codex.js';

/**
 * Returns one source timestamp when the provider record supplies it.
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
 * Removes Claude-injected context tags from user-authored text.
 *
 * @param {string} text - Provider text.
 * @returns {string} User-facing text with injected tags removed.
 */
function stripClaudeInjectedText(text) {
  return String(text ?? '').replace(
    /<(?:ide_opened_file|ide_selection|system[-_]reminder|system|env|claude_background_info|user[-_]prompt[-_]submit[-_]hook|command[-_]name|antml:[a-z_]+)[^>]*>[\s\S]*?<\/[^>]+>/gi,
    ''
  ).trim();
}

/**
 * Removes the IDE/request wrapper Codex may prepend to a user message.
 *
 * @param {string} text - Provider text.
 * @returns {string} User-authored request text.
 */
function stripCodexUserPreamble(text) {
  const value = String(text ?? '');
  const match = value.match(/## My request for Codex:\s*\r?\n([\s\S]+)/);
  return (match?.[1] ?? value).trim();
}

/**
 * Reads text parts from a Claude queued-command prompt.
 *
 * @param {*} prompt - Provider queued-command prompt value.
 * @returns {string} User-facing prompt text.
 */
function queuedPromptText(prompt) {
  if (typeof prompt === 'string') return stripClaudeInjectedText(prompt);
  if (!Array.isArray(prompt)) return '';
  return prompt
    .filter(block => block?.type === 'text' && typeof block.text === 'string')
    .map(block => stripClaudeInjectedText(block.text))
    .filter(Boolean)
    .join('\n\n')
    .trim();
}

/**
 * Separates Claude's generated quoted queued-command card from user text.
 *
 * @param {string} text - Cleaned queued-command text.
 * @returns {{generated_context:string,user_text:string}} Structured queue text.
 */
function splitQueuedCommandText(text) {
  const normalized = String(text ?? '').replace(/\r\n/g, '\n').replace(/\r/g, '\n');
  const lines = normalized.split('\n');
  let sawQuotedLine = false;
  let leftQuotedBlock = false;
  let userStart = -1;

  for (let index = 0; index < lines.length; index += 1) {
    const quoted = lines[index].trimStart().startsWith('>');
    if (!sawQuotedLine) {
      sawQuotedLine = quoted;
      continue;
    }
    if (!lines[index].trim()) {
      leftQuotedBlock = true;
      continue;
    }
    if (leftQuotedBlock || !quoted) {
      userStart = index;
      break;
    }
  }

  if (userStart < 0) {
    return { generated_context: '', user_text: normalized.trim() };
  }
  return {
    generated_context: lines.slice(0, userStart).join('\n').trim(),
    user_text: lines.slice(userStart).join('\n').trim()
  };
}

/**
 * Creates one canonical Claude queued-command message event.
 *
 * @param {Object<string, *>} record - Claude attachment record.
 * @param {number} sourceIndex - Zero-based source record index.
 * @returns {Object<string, *>|null} Canonical queued-command event or null.
 */
function claudeQueuedCommandEvent(record, sourceIndex) {
  if (record?.type !== 'attachment' || record?.attachment?.type !== 'queued_command') {
    return null;
  }
  const text = queuedPromptText(record?.attachment?.prompt);
  if (!text) return null;
  const split = splitQueuedCommandText(text);
  const recordId = record?.uuid ?? record?.message?.id ?? null;
  const identity = recordId ?? `record:${sourceIndex}`;
  const timestamp = sourceTimestamp(record);
  const source = {
    provider: 'claude',
    record_id: recordId,
    record_index: sourceIndex,
    ...(timestamp ? { timestamp } : {})
  };
  return {
    id: `claude:${identity}:queued_command`,
    provider: 'claude',
    source_record_id: recordId,
    source_index: sourceIndex,
    kind: 'message',
    role: 'user',
    channel: null,
    visibility: 'visible',
    content_type: 'queued_command',
    blocks: [{
      id: `claude:${identity}:queued_command:block`,
      type: 'text',
      text,
      queued_command: split,
      source
    }],
    citations: [],
    resources: [],
    relationships: { tool_call_id: null },
    source
  };
}

/**
 * Creates hidden canonical lifecycle events for top-level Claude Agent starts.
 *
 * @param {Object<string, *>} record - Claude assistant record.
 * @param {number} sourceIndex - Zero-based source record index.
 * @returns {Array<Object<string, *>>} Agent-start lifecycle events in block order.
 */
function claudeAgentStartEvents(record, sourceIndex) {
  if (record?.type !== 'assistant' || !Array.isArray(record?.message?.content)) return [];
  const timestamp = sourceTimestamp(record);
  const recordId = record?.uuid ?? record?.message?.id ?? null;
  const identity = recordId ?? `record:${sourceIndex}`;
  const events = [];

  record.message.content.forEach((block, blockIndex) => {
    if (block?.type !== 'tool_use' || block?.name !== 'Agent') return;
    const callId = typeof block.id === 'string' ? block.id : null;
    const source = {
      provider: 'claude',
      record_id: recordId,
      record_index: sourceIndex,
      block_index: blockIndex,
      ...(timestamp ? { timestamp } : {})
    };
    events.push({
      id: `claude:${identity}:agent_start:${blockIndex}`,
      provider: 'claude',
      source_record_id: recordId,
      source_index: sourceIndex,
      kind: 'tool_call',
      role: 'assistant',
      channel: null,
      visibility: 'hidden',
      content_type: 'subagent_start',
      blocks: [{
        id: `claude:${identity}:agent_start:${blockIndex}:block`,
        type: 'tool_call',
        call_id: callId,
        name: 'Agent',
        input: block?.input ?? null,
        input_format: 'object',
        subagent_start: {
          description: typeof block?.input?.description === 'string'
            ? block.input.description
            : null
        },
        source
      }],
      citations: [],
      resources: [],
      relationships: { tool_call_id: callId },
      source
    });
  });
  return events;
}

/**
 * Returns one trimmed XML-like element from provider task-notification text.
 *
 * @param {string} content - Provider task notification text.
 * @param {string} name - Element local name.
 * @returns {string|null} Trimmed element contents or null.
 */
function xmlTag(content, name) {
  if (typeof content !== 'string') return null;
  const match = content.match(new RegExp(`<${name}>([\\s\\S]*?)<\\/${name}>`, 'i'));
  return match ? match[1].trim() : null;
}

/**
 * Extracts completed Claude task duration from queue-operation XML-like text.
 *
 * @param {Object<string, *>} record - Claude source record.
 * @returns {number|null} Duration in milliseconds or null.
 */
function queueDurationMilliseconds(record) {
  const text = record?.type === 'queue-operation' ? xmlTag(record?.content, 'duration_ms') : null;
  if (!text) return null;
  const value = Number(text);
  return Number.isSafeInteger(value) && value >= 0 ? value : null;
}

/**
 * Returns the user-facing task description from a Claude completion summary.
 *
 * @param {Object<string, *>} record - Claude source record.
 * @returns {string|null} Task description or null.
 */
function queueTaskDescription(record) {
  if (record?.type !== 'queue-operation') return null;
  const summary = xmlTag(record?.content, 'summary');
  if (!summary) return null;
  const match = summary.match(/^Agent\s+["“]([\s\S]+?)["”]\s+came to rest$/i);
  return match?.[1]?.trim() ?? summary.trim();
}

/**
 * Removes an agent-authored timing footer from returned subagent text.
 *
 * @param {string} text - Subagent output.
 * @returns {string} Output without the reported timing footer.
 */
function stripReportedTimingFooter(text) {
  return String(text ?? '').replace(
    /\s*```text\s*START=[\s\S]*?END=[\s\S]*?ELAPSED=[\s\S]*?```\s*$/i,
    ''
  ).trim();
}

/**
 * Adds evidenced Claude subagent timing and normalized interactive output.
 *
 * @param {Object<string, *>} event - Canonical Claude event.
 * @param {Array<Object<string, *>>} records - Ordered Claude records.
 * @returns {Object<string, *>} Interactive event clone.
 */
function withClaudeSubagentSemantics(event, records) {
  if (event?.kind !== 'subagent' || !Number.isInteger(event?.source_index)) return event;
  const record = records[event.source_index];
  const direct = Number(record?.toolUseResult?.totalDurationMs);
  const durationMs = Number.isFinite(direct) && direct >= 0
    ? direct
    : queueDurationMilliseconds(record);
  const description = queueTaskDescription(record);
  return {
    ...event,
    blocks: (event.blocks ?? []).map(block => {
      if (block?.type !== 'subagent') return block;
      return {
        ...block,
        ...(Number.isFinite(durationMs) && durationMs >= 0 ? { duration_ms: durationMs } : {}),
        ...(description ? { description } : {}),
        output: stripReportedTimingFooter(block.output)
      };
    })
  };
}

/**
 * Cleans ordinary Claude User message blocks for interactive consumers.
 *
 * @param {Object<string, *>} event - Canonical Claude event.
 * @returns {Object<string, *>} Interactive event clone.
 */
function normalizeClaudeUserEvent(event) {
  if (event?.role !== 'user' || event?.kind !== 'message') return event;
  return {
    ...event,
    blocks: (event.blocks ?? []).map(block => block?.type === 'text'
      ? { ...block, text: stripClaudeInjectedText(block.text) }
      : block)
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
 * Adds interactive-only Codex semantics needed by AgentPanelSpeaker.
 *
 * @param {Object<string, *>} event - Timestamp-enriched canonical Codex event.
 * @param {Array<Object<string, *>>} records - Ordered Codex records.
 * @returns {Object<string, *>} Interactive event clone.
 */
function normalizeCodexEvent(event, records) {
  if (!Number.isInteger(event?.source_index)) return event;
  const record = records[event.source_index];
  let normalized = event;

  if (event?.role === 'user' && event?.kind === 'message' &&
      event?.content_type === 'user_message') {
    normalized = {
      ...normalized,
      blocks: (normalized.blocks ?? []).map(block => block?.type === 'text'
        ? { ...block, text: stripCodexUserPreamble(block.text) }
        : block)
    };
  }

  if (event?.content_type === 'agent_message' &&
      typeof record?.payload?.phase === 'string' && record.payload.phase) {
    normalized = { ...normalized, channel: record.payload.phase };
  }

  if (event?.kind === 'tool_call') {
    const callBlockIndex = (normalized.blocks ?? []).findIndex(block =>
      block?.type === 'tool_call' && block?.name === 'request_user_input');
    if (callBlockIndex >= 0) {
      const argumentsObject = parseObject(record?.payload?.arguments);
      const sourceQuestions = Array.isArray(argumentsObject?.questions)
        ? argumentsObject.questions
        : [];
      const blocks = [...normalized.blocks];
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
            is_secret: Boolean(
              sourceQuestions[index]?.isSecret ?? sourceQuestions[index]?.is_secret
            )
          }))
        }
      };
      normalized = { ...normalized, blocks };
    }
  }

  return normalized;
}

/**
 * Adapts one complete Claude session with lifecycle facts required by interactive consumers.
 *
 * @param {Array<Object<string, *>>} records - Ordered Claude records.
 * @returns {Array<Object<string, *>>} Enriched canonical events in source order.
 */
function adaptClaudeSession(records) {
  const base = adaptClaudeRecords(records)
    .filter(event => !records[event.source_index]?.isSidechain)
    .map(event => normalizeClaudeUserEvent(
      withClaudeSubagentSemantics(withTimestamp(event, records), records)
    ));
  const synthetic = [];
  records.forEach((record, sourceIndex) => {
    if (record?.isSidechain) return;
    const queued = claudeQueuedCommandEvent(record, sourceIndex);
    if (queued) synthetic.push(queued);
    synthetic.push(...claudeAgentStartEvents(record, sourceIndex));
  });
  return [...base, ...synthetic].sort((left, right) => {
    const sourceDifference = (left.source_index ?? 0) - (right.source_index ?? 0);
    if (sourceDifference) return sourceDifference;
    const leftBlock = left?.source?.block_index ?? -1;
    const rightBlock = right?.source?.block_index ?? -1;
    return leftBlock - rightBlock;
  });
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
 * Adapts one complete Codex session with lifecycle facts required by interactive consumers.
 *
 * @param {Array<Object<string, *>>} records - Ordered Codex records.
 * @returns {Array<Object<string, *>>} Enriched canonical events in source order.
 */
function adaptCodexSession(records) {
  const base = adaptCodexRecords(records)
    .map(event => normalizeCodexEvent(withTimestamp(event, records), records));
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
 * Adapts a complete provider session for interactive consumers while keeping all
 * provider semantics inside AIConversationCore.
 *
 * @param {string} provider - Canonical provider identifier (`claude` or `codex`).
 * @param {Array<Object<string, *>>} records - Ordered provider/source records.
 * @returns {Array<Object<string, *>>} Enriched canonical events.
 */
export function adaptInteractiveSessionRecords(provider, records) {
  if (!Array.isArray(records)) throw new TypeError('Session records must be an array.');
  if (provider === 'claude') return adaptClaudeSession(records);
  if (provider === 'codex') return adaptCodexSession(records);
  throw new Error(`Unsupported interactive-session provider: ${provider}`);
}
