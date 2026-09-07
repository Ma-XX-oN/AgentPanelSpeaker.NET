import { adaptInteractiveSessionRecords } from './interactive.js';

/**
 * Returns whether a Claude direct Agent completion has the provider evidence
 * required by the historical interactive speech contract.
 *
 * @param {Object<string, *>} record - Claude source record for the completion.
 * @returns {boolean} Whether the completion is eligible for speech/timing.
 */
function isEligibleDirectClaudeCompletion(record) {
  return record?.type === 'user' &&
    record?.toolUseResult?.agentType === 'general-purpose';
}

/**
 * Adds consumer-neutral speech/timing eligibility metadata to a Claude
 * subagent completion without removing it from the canonical display model.
 *
 * The canonical subagent event remains visible for display.  `speech.eligible`
 * records whether this source record satisfied the provider evidence used by
 * interactive speech consumers, while `background_work_identity` describes
 * how an app may reconstruct its stable timer identity without reparsing the
 * provider record.
 *
 * @param {Object<string, *>} event - Interactive canonical event.
 * @param {Array<Object<string, *>>} records - Ordered Claude source records.
 * @returns {Object<string, *>} Event clone with speech/timing metadata.
 */
function withClaudeSpeechMetadata(event, records) {
  if (event?.kind !== 'subagent' || !Number.isInteger(event?.source_index)) {
    return event;
  }

  const record = records[event.source_index];
  const block = event?.blocks?.find(item => item?.type === 'subagent');
  if (!block) return event;

  const isQueueCompletion = record?.type === 'queue-operation';
  const eligible = isQueueCompletion || isEligibleDirectClaudeCompletion(record);
  const agentId = typeof block.agent_id === 'string' ? block.agent_id : null;
  const callId = typeof event?.relationships?.tool_call_id === 'string'
    ? event.relationships.tool_call_id
    : null;
  const backgroundWorkIdentity = isQueueCompletion && agentId
    ? { kind: 'task_timestamp', id: agentId }
    : callId
      ? { kind: 'tool_call', id: callId }
      : null;

  return {
    ...event,
    speech: {
      ...(event?.speech ?? {}),
      eligible,
      background_work_identity: backgroundWorkIdentity
    }
  };
}

/**
 * Adds block-level speech selection and voice-role metadata to a canonical
 * Codex User message without removing or rewriting any canonical content.
 *
 * `user_context` is disabled by default and can be enabled with the Core-owned
 * `includeUserContext` option.  The actual User prompt remains speech eligible
 * regardless of that option.  Both blocks stay in the canonical event so
 * Markdown and HTML rendering are independent of speech selection.
 *
 * @param {Object<string, *>} event - Interactive canonical event.
 * @param {Object<string, *>} options - Speech-session projection options.
 * @returns {Object<string, *>} Event clone with block-level speech metadata.
 */
function withCodexSpeechMetadata(event, options) {
  if (event?.provider !== 'codex' ||
      event?.kind !== 'message' ||
      event?.role !== 'user') {
    return event;
  }

  const includeUserContext = options?.includeUserContext === true;
  return {
    ...event,
    blocks: (event.blocks ?? []).map(block => {
      if (block?.type === 'user_context') {
        return {
          ...block,
          speech: {
            ...(block?.speech ?? {}),
            eligible: includeUserContext,
            voice_role: 'user_context'
          }
        };
      }

      if (block?.type === 'text') {
        return {
          ...block,
          speech: {
            ...(block?.speech ?? {}),
            eligible: true,
            voice_role: 'user'
          }
        };
      }

      return block;
    })
  };
}

/**
 * Adapts a provider session for speech/display consumers while preserving
 * provider-specific eligibility facts in AIConversationCore.
 *
 * This function does not create a second semantic model.  It returns the same
 * canonical interactive events and adds only projection metadata describing
 * whether canonical events/blocks participate in speech/timing behaviour.
 * `options.includeUserContext` enables Codex `user_context` speech; it defaults
 * to false while leaving the canonical display model unchanged.
 *
 * @param {string} provider - Canonical provider identifier.
 * @param {Array<Object<string, *>>} records - Ordered provider/source records.
 * @param {Object<string, *>} options - Optional provider/speech projection options.
 * @returns {Array<Object<string, *>>} Canonical events with speech metadata.
 */
export function adaptSpeechSessionRecords(provider, records, options = {}) {
  const events = adaptInteractiveSessionRecords(provider, records, options);
  if (provider === 'codex') {
    return events.map(event => withCodexSpeechMetadata(event, options));
  }
  if (provider === 'claude') {
    return events.map(event => withClaudeSpeechMetadata(event, records));
  }
  return events;
}
