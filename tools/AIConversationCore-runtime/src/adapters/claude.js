/**
 * Returns the stable source-record identity used to derive Claude canonical IDs.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {string} The stable source identity used to derive Claude canonical IDs.
 */
function sourceRecordIdentity(record, sourceIndex) {
  return record?.uuid ?? record?.message?.id ?? `record:${sourceIndex}`;
}

/**
 * Builds canonical source provenance for a Claude record or content block.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {number|null} blockIndex - The zero-based block index.
 * @returns {Object<string, *>} Canonical Claude source provenance for the record or content block.
 */
function baseSource(record, sourceIndex, blockIndex = null) {
  const source = {
    provider: 'claude',
    record_id: record?.uuid ?? record?.message?.id ?? null,
    record_index: sourceIndex
  };
  if (Number.isInteger(blockIndex)) source.block_index = blockIndex;
  return source;
}

/**
 * Builds a canonical text block from one provider text content block.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @param {number} blockIndex - The zero-based block index.
 * @returns {Object<string, *>} A canonical text block derived from the Claude source block.
 */
function textBlock(record, sourceIndex, block, blockIndex) {
  const sourceIdentity = sourceRecordIdentity(record, sourceIndex);
  return {
    id: `claude:${sourceIdentity}:text:${blockIndex}`,
    type: 'text',
    text: block.text,
    source: baseSource(record, sourceIndex, blockIndex)
  };
}

/**
 * Builds a canonical reasoning-summary block from one provider reasoning block.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @param {number} blockIndex - The zero-based block index.
 * @returns {Object<string, *>} A canonical reasoning-summary block derived from the Claude thinking block.
 */
function reasoningBlock(record, sourceIndex, block, blockIndex) {
  const sourceIdentity = sourceRecordIdentity(record, sourceIndex);
  return {
    id: `claude:${sourceIdentity}:reasoning:${blockIndex}`,
    type: 'reasoning_summary',
    summary: null,
    content: block.thinking,
    chunks: null,
    finished: null,
    source: baseSource(record, sourceIndex, blockIndex)
  };
}

/**
 * Normalizes ask user question.
 *
 * @param {Object<string, *>} input - The provider tool-input object being normalized.
 * @returns {Object<string, *>|null} Normalized AskUserQuestion question data, or null when the provider input has no questions array.
 */
function normalizedAskUserQuestion(input) {
  if (!Array.isArray(input?.questions)) return null;
  return {
    questions: input.questions.map(question => ({
      question: typeof question?.question === 'string' ? question.question : null,
      header: typeof question?.header === 'string' ? question.header : null,
      multi_select: Boolean(question?.multiSelect),
      options: Array.isArray(question?.options)
        ? question.options.map(option => ({
            label: typeof option?.label === 'string' ? option.label : null,
            description: typeof option?.description === 'string' ? option.description : null
          }))
        : []
    }))
  };
}

/**
 * Builds a canonical tool-call event from the provider-specific tool-call source record/block.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @param {number} blockIndex - The zero-based block index.
 * @returns {Object<string, *>} A canonical Claude tool-call event preserving source identity and call correlation.
 */
function toolCallEvent(record, sourceIndex, block, blockIndex) {
  const sourceIdentity = sourceRecordIdentity(record, sourceIndex);
  const source = baseSource(record, sourceIndex, blockIndex);
  const callId = typeof block.id === 'string' ? block.id : null;
  const canonicalBlock = {
    id: `claude:${sourceIdentity}:tool_call:${blockIndex}:block`,
    type: 'tool_call',
    call_id: callId,
    name: block?.name ?? null,
    input: block?.input ?? null,
    input_format: 'object',
    caller: block?.caller ?? null,
    source
  };
  if (block?.name === 'AskUserQuestion') {
    canonicalBlock.ask_user_question = normalizedAskUserQuestion(block?.input) ?? { questions: [] };
  }
  if (block?.name === 'ExitPlanMode') {
    canonicalBlock.exit_plan = {
      plan: typeof block?.input?.plan === 'string' ? block.input.plan : null,
      plan_file_path: typeof block?.input?.planFilePath === 'string' ? block.input.planFilePath : null
    };
  }
  return {
    id: `claude:${sourceIdentity}:tool_call:${blockIndex}`,
    provider: 'claude',
    source_record_id: source.record_id,
    source_index: sourceIndex,
    kind: 'tool_call',
    role: record?.message?.role ?? 'assistant',
    channel: null,
    visibility: 'visible',
    content_type: 'tool_use',
    blocks: [canonicalBlock],
    relationships: { tool_call_id: callId },
    source
  };
}

/**
 * Normalizes exit plan response.
 *
 * @param {string} text - The text value to process.
 * @returns {Object<string, *>|null} Normalized exit-plan approval metadata, or null when the provider output is not a recognizable approval response.
 */
function normalizeExitPlanResponse(text) {
  if (typeof text !== 'string') return null;
  const heading = text.match(/^#{0,6}\s*Approved Plan(?::|\s)/m);
  if (!heading || heading.index == null) return { intro: text.trim(), approved_plan: null };
  return {
    intro: text.slice(0, heading.index).trim(),
    approved_plan: text.slice(heading.index).trim()
  };
}

/**
 * Builds a canonical tool-result event from the provider-specific tool-result source record/block.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @param {number} blockIndex - The zero-based block index.
 * @param {string|null} callName - The normalized provider tool name associated with the correlated call, or null when unavailable.
 * @returns {Object<string, *>} A canonical Claude tool-result event preserving source identity and call correlation.
 */
function toolResultEvent(record, sourceIndex, block, blockIndex, callName = null) {
  const sourceIdentity = sourceRecordIdentity(record, sourceIndex);
  const source = baseSource(record, sourceIndex, blockIndex);
  const callId = typeof block.tool_use_id === 'string' ? block.tool_use_id : null;
  const canonicalBlock = {
    id: `claude:${sourceIdentity}:tool_result:${blockIndex}:block`,
    type: 'tool_result',
    call_id: callId,
    name: callName,
    output: block?.content ?? null,
    output_format: Array.isArray(block?.content) ? 'blocks' : typeof block?.content,
    is_error: block?.is_error ?? null,
    source
  };
  if (callName === 'AskUserQuestion') {
    canonicalBlock.ask_user_question_response = { text: textFromToolResult(block?.content) };
  }
  if (callName === 'ExitPlanMode') {
    canonicalBlock.exit_plan_response = normalizeExitPlanResponse(textFromToolResult(block?.content));
  }
  return {
    id: `claude:${sourceIdentity}:tool_result:${blockIndex}`,
    provider: 'claude',
    source_record_id: source.record_id,
    source_index: sourceIndex,
    kind: 'tool_result',
    role: record?.message?.role ?? 'user',
    channel: null,
    visibility: 'visible',
    content_type: 'tool_result',
    blocks: [canonicalBlock],
    relationships: { tool_call_id: callId },
    source
  };
}

/**
 * Builds a canonical message/commentary event from provider-specific message content.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @param {number} blockIndex - The zero-based block index.
 * @returns {Object<string, *>} A canonical Claude message event for the provider text block.
 */
function messageEvent(record, sourceIndex, block, blockIndex) {
  const sourceIdentity = sourceRecordIdentity(record, sourceIndex);
  const source = baseSource(record, sourceIndex, blockIndex);
  return {
    id: `claude:${sourceIdentity}:message:${blockIndex}`,
    provider: 'claude',
    source_record_id: source.record_id,
    source_index: sourceIndex,
    kind: 'message',
    role: record?.message?.role ?? null,
    channel: null,
    visibility: 'visible',
    content_type: 'text',
    blocks: [textBlock(record, sourceIndex, block, blockIndex)],
    citations: [],
    resources: [],
    relationships: { tool_call_id: null },
    source
  };
}

/**
 * Builds a canonical reasoning-summary event from provider-specific reasoning content.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @param {number} blockIndex - The zero-based block index.
 * @returns {Object<string, *>} A canonical Claude reasoning-summary event for the provider thinking block.
 */
function reasoningEvent(record, sourceIndex, block, blockIndex) {
  const sourceIdentity = sourceRecordIdentity(record, sourceIndex);
  const source = baseSource(record, sourceIndex, blockIndex);
  return {
    id: `claude:${sourceIdentity}:reasoning:${blockIndex}`,
    provider: 'claude',
    source_record_id: source.record_id,
    source_index: sourceIndex,
    kind: 'reasoning_summary',
    role: 'assistant',
    channel: null,
    visibility: 'visible',
    content_type: 'thinking',
    blocks: [reasoningBlock(record, sourceIndex, block, blockIndex)],
    citations: [],
    resources: [],
    relationships: { tool_call_id: null },
    source
  };
}

/**
 * Builds a canonical notice event from provider-specific synthetic notice content.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @param {number} blockIndex - The zero-based block index.
 * @returns {Object<string, *>} A canonical system notice event for the synthetic provider message.
 */
function noticeEvent(record, sourceIndex, block, blockIndex) {
  const sourceIdentity = sourceRecordIdentity(record, sourceIndex);
  const source = baseSource(record, sourceIndex, blockIndex);
  return {
    id: `claude:${sourceIdentity}:notice:${blockIndex}`,
    provider: 'claude',
    source_record_id: source.record_id,
    source_index: sourceIndex,
    kind: 'notice',
    role: 'system',
    channel: null,
    visibility: 'visible',
    content_type: 'synthetic_notice',
    blocks: [{ id: `claude:${sourceIdentity}:notice:${blockIndex}:block`, type: 'text', text: block.text, source }],
    citations: [],
    resources: [],
    relationships: { tool_call_id: null },
    source
  };
}

/**
 * Extracts displayable text from a Claude tool-result string or text-block array.
 *
 * @param {string|Array<Object<string, *>>} content - The provider/canonical content being converted to display text.
 * @returns {string} Displayable text extracted from a tool-result string or concatenated text blocks.
 */
function textFromToolResult(content) {
  if (typeof content === 'string') return content;
  if (!Array.isArray(content)) return '';
  return content.filter(block => block?.type === 'text' && typeof block.text === 'string')
    .map(block => block.text).join('\n');
}

/**
 * Extracts the internal Claude subagent ID embedded in an Agent tool result.
 *
 * @param {string} text - The text value to process.
 * @returns {string|null} The internal Claude subagent identifier embedded in the tool result, or null when absent.
 */
function agentIdFromResult(text) {
  if (typeof text !== 'string') return null;
  const match = text.match(/^agentId:\s*([^\s]+)\s*\(internal ID - do not mention to user\.\)$/m);
  return match?.[1] ?? null;
}

/**
 * Removes the internal Agent-ID control line from Claude subagent output.
 *
 * @param {string} text - The text value to process.
 * @returns {string} Subagent result text with the internal agent-ID control line removed.
 */
function cleanAgentResult(text) {
  if (typeof text !== 'string') return '';
  return text.split('\n')
    .filter(line => !/^agentId:\s*[^\s]+\s*\(internal ID - do not mention to user\.\)$/.test(line))
    .join('\n').trim();
}

/**
 * Builds a canonical subagent event from Claude Agent completion data.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {string} agentId - The agent id.
 * @param {string|null} description - The provider subagent description, or null when none was supplied.
 * @param {string} output - The displayable provider tool/subagent output text.
 * @param {string|null} callId - The call id.
 * @param {number|null} sourceBlockIndex - The zero-based source block index of the completion record.
 * @param {Object<string, *>|null} invocationSource - Canonical source provenance for the Agent invocation that produced this completion, or null when unavailable.
 * @returns {Object<string, *>} A canonical Claude subagent completion event retaining both completion and invocation provenance.
 */
function subagentEvent(record, sourceIndex, agentId, description, output, callId,
                       sourceBlockIndex = null, invocationSource = null) {
  const sourceIdentity = sourceRecordIdentity(record, sourceIndex);
  const source = baseSource(record, sourceIndex, sourceBlockIndex);
  return {
    id: `claude:${sourceIdentity}:subagent:${agentId}`,
    provider: 'claude', source_record_id: source.record_id, source_index: sourceIndex,
    kind: 'subagent', role: 'assistant', channel: null, visibility: 'visible', content_type: 'subagent',
    blocks: [{ id: `claude:${sourceIdentity}:subagent:${agentId}:block`, type: 'subagent', agent_id: agentId, description, output, source }],
    citations: [], resources: [],
    relationships: {
      tool_call_id: callId ?? null,
      invocation_source: invocationSource
    },
    source
  };
}

/**
 * Returns the trimmed contents of one named XML-like tag from Claude queue-operation text.
 *
 * @param {string} content - The provider/canonical content being converted to display text.
 * @param {string} name - The name associated with the value being processed.
 * @returns {string|null} Trimmed contents of the named XML-like tag, or null when the tag is absent.
 */
function xmlTag(content, name) {
  if (typeof content !== 'string') return null;
  const match = content.match(new RegExp(`<${name}>([\\s\\S]*?)</${name}>`));
  return match ? match[1].trim() : null;
}

/**
 * Converts a completed Claude queue-operation task notification into a canonical subagent event.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {Object<string, *>|null} A canonical subagent event for a completed queue-operation notification, or null when the source record is not such a completion.
 */
function queueSubagentEvent(record, sourceIndex) {
  if (record?.type !== 'queue-operation' || typeof record?.content !== 'string') return null;
  if (!record.content.includes('<task-notification>') || xmlTag(record.content, 'status') !== 'completed') return null;
  const taskId = xmlTag(record.content, 'task-id');
  const summary = xmlTag(record.content, 'summary');
  const result = xmlTag(record.content, 'result');
  if (!taskId || !result) return null;
  const descriptionMatch = summary?.match(/^Agent\s+"([\s\S]+)"\s+came to rest$/);
  return subagentEvent(record, sourceIndex, taskId,
    descriptionMatch?.[1] ?? summary ?? null, result, xmlTag(record.content, 'tool-use-id'));
}

/**
 * Adapts Claude tool events.
 *
 * @param {Array<Object<string, *>>} records - The ordered provider/source records to process.
 * @returns {Array<Object<string, *>>} Canonical Claude tool-call/tool-result events in source record/block order.
 */
export function adaptClaudeToolEvents(records) {
  if (!Array.isArray(records)) throw new TypeError('Claude records must be an array.');
  // Canonical events are appended in the same order as their source records/blocks.
  const events = [];
  records.forEach((record, sourceIndex) => {
    const content = record?.message?.content;
    if (!Array.isArray(content)) return;
    content.forEach((block, blockIndex) => {
      if (!block || typeof block !== 'object') return;
      if (block.type === 'tool_use') events.push(toolCallEvent(record, sourceIndex, block, blockIndex));
      if (block.type === 'tool_result') events.push(toolResultEvent(record, sourceIndex, block, blockIndex));
    });
  });
  return events;
}

/**
 * Adapts Claude records.
 *
 * @param {Array<Object<string, *>>} records - The ordered provider/source records to process.
 * @returns {Array<Object<string, *>>} Canonical Claude events in provider source order, including correlated subagent/tool events.
 */
export function adaptClaudeRecords(records) {
  if (!Array.isArray(records)) throw new TypeError('Claude records must be an array.');
  // Canonical events are appended in the same order as their source records/blocks.
  const events = [];
  // Maps Claude subagent call IDs to their source call metadata so later results can be correlated.
  const agentCalls = new Map();
  // Maps tool-use IDs to tool names so result events retain the originating tool identity.
  const toolNames = new Map();

  records.forEach((record, sourceIndex) => {
    const queuedSubagent = queueSubagentEvent(record, sourceIndex);
    if (queuedSubagent) { events.push(queuedSubagent); return; }
    const content = record?.message?.content;
    if (!Array.isArray(content)) return;

    if (record?.type === 'assistant' && record?.message?.model === '<synthetic>') {
      content.forEach((block, blockIndex) => {
        if (block?.type === 'text' && typeof block.text === 'string') events.push(noticeEvent(record, sourceIndex, block, blockIndex));
      });
      return;
    }

    content.forEach((block, blockIndex) => {
      if (!block || typeof block !== 'object') return;
      if (block.type === 'text' && typeof block.text === 'string') { events.push(messageEvent(record, sourceIndex, block, blockIndex)); return; }
      if (block.type === 'thinking' && typeof block.thinking === 'string') { events.push(reasoningEvent(record, sourceIndex, block, blockIndex)); return; }
      if (block.type === 'tool_use') {
        if (typeof block.id === 'string') toolNames.set(block.id, block.name ?? null);
        if (block.name === 'Agent' && typeof block.id === 'string') {
          agentCalls.set(block.id, {
            description: typeof block?.input?.description === 'string' ? block.input.description : null,
            source: baseSource(record, sourceIndex, blockIndex)
          });
          return;
        }
        events.push(toolCallEvent(record, sourceIndex, block, blockIndex));
        return;
      }
      if (block.type !== 'tool_result') return;
      const callId = typeof block.tool_use_id === 'string' ? block.tool_use_id : null;
      const agentCall = callId ? agentCalls.get(callId) : null;
      if (agentCall) {
        const rawOutput = textFromToolResult(block.content);
        events.push(subagentEvent(
          record,
          sourceIndex,
          agentIdFromResult(rawOutput) ?? callId,
          agentCall.description,
          cleanAgentResult(rawOutput),
          callId,
          blockIndex,
          agentCall.source
        ));
        return;
      }
      events.push(toolResultEvent(record, sourceIndex, block, blockIndex, callId ? toolNames.get(callId) : null));
    });
  });
  return events;
}
