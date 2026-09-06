// Version of the provider-independent canonical presentation-tree contract.
const PRESENTATION_SCHEMA_VERSION = 2;

/**
 * Returns the human-readable actor label for a canonical provider.
 *
 * @param {string|null} provider - Canonical provider identifier.
 * @returns {string} Human-readable actor label.
 */
function providerLabel(provider) {
  if (provider === 'claude') return 'Claude';
  if (provider === 'codex') return 'Codex';
  return 'ChatGPT';
}

/**
 * Returns stable source provenance for one canonical event.
 *
 * @param {Object<string, *>} event - Canonical event.
 * @returns {Object<string, *>} Stable source identity for presentation mapping.
 */
function sourceRef(event) {
  const source = event?.source && typeof event.source === 'object'
    ? event.source
    : {};
  return {
    event_id: event?.id ?? null,
    provider: event?.provider ?? source.provider ?? null,
    record_id: event?.source_record_id ?? source.record_id ?? null,
    record_index: Number.isInteger(event?.source_index)
      ? event.source_index
      : Number.isInteger(source.record_index) ? source.record_index : null,
    block_indexes: (event?.blocks ?? [])
      .map(block => block?.source?.block_index)
      .filter(Number.isInteger)
  };
}

/**
 * Returns the canonical tool correlation identifier for an event.
 *
 * @param {Object<string, *>} event - Canonical tool event.
 * @returns {string|null} Tool-call correlation identifier or null.
 */
function toolCallId(event) {
  return event?.relationships?.tool_call_id ??
    event?.blocks?.find(block =>
      block?.type === 'tool_call' || block?.type === 'tool_result')?.call_id ??
    null;
}

/**
 * Returns the canonical tool block carried by an event.
 *
 * @param {Object<string, *>} event - Canonical tool event.
 * @returns {Object<string, *>|null} Canonical tool block or null.
 */
function toolBlock(event) {
  return event?.blocks?.find(block =>
    block?.type === 'tool_call' || block?.type === 'tool_result') ?? null;
}

/**
 * Returns whether a tool carries an explicit interactive transcript semantic.
 *
 * Interactive tools are not ordinary hidden reasoning activity. They are kept
 * explicit in the tree so serializers can present their User/Agent interaction
 * without treating the provider name itself as a rendering rule.
 *
 * @param {Object<string, *>} block - Canonical tool block.
 * @returns {boolean} Whether the tool has an explicit interactive semantic.
 */
function isInteractiveTool(block) {
  return Boolean(
    block?.ask_user_question ||
    block?.ask_user_question_response ||
    block?.exit_plan ||
    block?.exit_plan_response ||
    block?.request_user_input ||
    block?.request_user_input_response
  );
}

/**
 * Builds a Markdown-content presentation node from one canonical event.
 *
 * The original canonical blocks remain attached so format serializers can apply
 * citations, resources, and Markdown conversion without reparsing a rendered
 * transcript.
 *
 * @param {Object<string, *>} event - Canonical visible content event.
 * @param {string} kind - Presentation node kind.
 * @param {Array<Object<string, *>>|null} blocksOverride - Optional block subset.
 * @returns {Object<string, *>} Presentation content node.
 */
function contentNode(event, kind = 'markdown', blocksOverride = null) {
  return {
    id: `presentation:${event.id}:${kind}`,
    kind,
    atomic: false,
    event_id: event.id,
    source: [sourceRef(event)],
    role: event.role ?? null,
    channel: event.channel ?? null,
    content_type: event.content_type ?? null,
    blocks: blocksOverride ?? event.blocks ?? [],
    citations: event.citations ?? [],
    resources: event.resources ?? []
  };
}

/**
 * Builds ordered User-turn children with attachments before Markdown body text.
 *
 * Existing verified transcript presentation places uploaded images/attachments
 * before the User body. Canonical source block order remains retained on the
 * event, but presentation does not invent arbitrary inline attachment semantics.
 *
 * @param {Object<string, *>} event - Canonical User message event.
 * @returns {Array<Object<string, *>>} Ordered User presentation children.
 */
function userChildren(event) {
  const blocks = Array.isArray(event?.blocks) ? event.blocks : [];
  const attachments = blocks.filter(block =>
    block?.type === 'image' ||
    block?.type === 'attachment' ||
    block?.type === 'file'
  );
  const body = blocks.filter(block => !attachments.includes(block));
  const children = [];
  if (attachments.length) {
    children.push(contentNode(event, 'attachments', attachments));
  }
  if (body.length || !attachments.length) {
    children.push(contentNode(event, 'markdown', body));
  }
  return children;
}

/**
 * Builds one reasoning presentation item.
 *
 * @param {Object<string, *>} event - Canonical reasoning event.
 * @returns {Object<string, *>} Reasoning presentation item.
 */
function reasoningNode(event) {
  return {
    ...contentNode(event, 'reasoning'),
    atomic: true
  };
}

/**
 * Builds one tool presentation node.
 *
 * @param {Object<string, *>} event - Canonical tool event.
 * @returns {Object<string, *>} Tool presentation node.
 */
function toolNode(event) {
  const block = toolBlock(event);
  return {
    id: `presentation:tool:${event.id}`,
    kind: 'tool',
    atomic: true,
    call_id: toolCallId(event),
    name: block?.name ?? null,
    interactive: isInteractiveTool(block),
    call: event.kind === 'tool_call' ? block : null,
    result: event.kind === 'tool_result' ? block : null,
    event_ids: [event.id],
    source: [sourceRef(event)]
  };
}

/**
 * Attaches a tool-result event to a prior tool-call presentation node.
 *
 * @param {Object<string, *>} node - Existing tool node.
 * @param {Object<string, *>} event - Canonical tool-result event.
 * @returns {void} The supplied tool node is updated in place.
 */
function attachToolResult(node, event) {
  const block = toolBlock(event);
  node.result = block;
  node.interactive = node.interactive || isInteractiveTool(block);
  node.event_ids.push(event.id);
  node.source.push(sourceRef(event));
}

/**
 * Creates a new presentation turn.
 *
 * @param {Object<string, *>} event - First event assigned to the turn.
 * @param {number} turnIndex - Zero-based presentation turn index.
 * @param {string} role - Presentation actor role.
 * @param {Object<string, *>} [actorOverride] - Optional explicit actor metadata.
 * @returns {Object<string, *>} New presentation turn.
 */
function createTurn(event, turnIndex, role, actorOverride = {}) {
  const provider = event?.provider ?? event?.source?.provider ?? null;
  const actor = {
    role,
    provider,
    label: role === 'user' ? 'User' : providerLabel(provider),
    ...actorOverride
  };
  return {
    id: `presentation:turn:${turnIndex}:${event.id}`,
    kind: 'turn',
    atomic: false,
    actor,
    source: [],
    children: []
  };
}

/**
 * Adds one event source to a turn without duplicating the same event identity.
 *
 * @param {Object<string, *>} turn - Presentation turn.
 * @param {Object<string, *>} event - Canonical event.
 * @returns {void} The supplied turn is updated in place.
 */
function addTurnSource(turn, event) {
  if (turn.source.some(source => source.event_id === event.id)) return;
  turn.source.push(sourceRef(event));
}

/**
 * Creates a reasoning group at the current turn position.
 *
 * @param {Object<string, *>} turn - Agent presentation turn.
 * @param {Object<string, *>} event - First event in the reasoning group.
 * @param {number} groupIndex - Zero-based reasoning-group index in the turn.
 * @returns {Object<string, *>} New reasoning group.
 */
function createReasoningGroup(turn, event, groupIndex) {
  const group = {
    id: `${turn.id}:reasoning:${groupIndex}:${event.id}`,
    kind: 'reasoning_group',
    atomic: true,
    source: [],
    thought_count: 0,
    children: []
  };
  turn.children.push(group);
  return group;
}

/**
 * Creates a separately-addressable subagent turn.
 *
 * @param {Object<string, *>} event - Canonical subagent event.
 * @param {number} turnIndex - Zero-based presentation turn index.
 * @returns {Object<string, *>} Subagent presentation turn.
 */
function createSubagentTurn(event, turnIndex) {
  const block = event?.blocks?.find(item => item?.type === 'subagent');
  const provider = event?.provider ?? event?.source?.provider ?? null;
  const agentId = block?.agent_id ?? null;
  const label = `${providerLabel(provider)} Sub-agent${agentId ? ` ${agentId}` : ''}`;
  const turn = createTurn(event, turnIndex, 'assistant', {
    kind: 'subagent',
    label,
    agent_id: agentId
  });
  addTurnSource(turn, event);
  turn.children.push({
    id: `presentation:subagent:${event.id}`,
    kind: 'subagent_content',
    atomic: false,
    event_id: event.id,
    source: [sourceRef(event)],
    block: block ?? null
  });
  return turn;
}

/**
 * Builds the provider-independent canonical presentation tree.
 *
 * User/Agent turns preserve canonical event order. Consecutive reasoning and
 * ordinary tool activity form one reasoning group. Visible Assistant
 * response/commentary closes the current reasoning group but remains inside the
 * same Agent turn. Later reasoning starts a new group in that same turn.
 *
 * @param {Array<Object<string, *>>} events - Ordered canonical event stream.
 * @returns {Object<string, *>} Canonical presentation tree.
 */
export function buildCanonicalPresentation(events) {
  if (!Array.isArray(events)) {
    throw new TypeError('buildCanonicalPresentation expects an event array');
  }

  const turns = [];
  let currentTurn = null;
  let reasoningGroup = null;
  let reasoningGroupIndex = 0;
  const toolsByCallId = new Map();

  /**
   * Closes the current reasoning run without ending the surrounding turn.
   *
   * @returns {void} The active reasoning-group reference is cleared.
   */
  const closeReasoning = () => {
    reasoningGroup = null;
  };

  /**
   * Ensures that an Agent turn exists for the supplied event.
   *
   * @param {Object<string, *>} event - Canonical event assigned to the Agent turn.
   * @returns {Object<string, *>} Existing or newly created Agent presentation turn.
   */
  const ensureAgentTurn = event => {
    if (!currentTurn || currentTurn.actor.role !== 'assistant' ||
        currentTurn.actor.kind === 'subagent') {
      currentTurn = createTurn(event, turns.length, 'assistant');
      turns.push(currentTurn);
      reasoningGroupIndex = 0;
      toolsByCallId.clear();
    }
    addTurnSource(currentTurn, event);
    return currentTurn;
  };

  /**
   * Ensures that a reasoning group exists at the current Agent position.
   *
   * @param {Object<string, *>} event - Canonical reasoning/tool event assigned to the group.
   * @returns {Object<string, *>} Existing or newly created reasoning group.
   */
  const ensureReasoningGroup = event => {
    const turn = ensureAgentTurn(event);
    if (!reasoningGroup) {
      reasoningGroup = createReasoningGroup(
        turn,
        event,
        reasoningGroupIndex++
      );
    }
    return reasoningGroup;
  };

  for (const event of events) {
    if (!event || event.visibility === 'hidden') continue;

    if (event.kind === 'subagent') {
      closeReasoning();
      currentTurn = createSubagentTurn(event, turns.length);
      turns.push(currentTurn);
      reasoningGroupIndex = 0;
      toolsByCallId.clear();
      currentTurn = null;
      continue;
    }

    if (event.kind === 'message' && event.role === 'user') {
      closeReasoning();
      currentTurn = createTurn(event, turns.length, 'user');
      addTurnSource(currentTurn, event);
      currentTurn.children.push(...userChildren(event));
      turns.push(currentTurn);
      currentTurn = null;
      reasoningGroupIndex = 0;
      toolsByCallId.clear();
      continue;
    }

    if (event.kind === 'reasoning_summary') {
      const group = ensureReasoningGroup(event);
      group.children.push(reasoningNode(event));
      group.source.push(sourceRef(event));
      group.thought_count += 1;
      continue;
    }

    if (event.kind === 'tool_call') {
      const node = toolNode(event);
      if (node.interactive) {
        closeReasoning();
        const turn = ensureAgentTurn(event);
        turn.children.push({ ...node, kind: 'interaction' });
      } else {
        const group = ensureReasoningGroup(event);
        group.children.push(node);
        group.source.push(sourceRef(event));
      }
      if (node.call_id) toolsByCallId.set(node.call_id, node);
      continue;
    }

    if (event.kind === 'tool_result') {
      const callId = toolCallId(event);
      const existing = callId ? toolsByCallId.get(callId) : null;
      if (existing) {
        attachToolResult(existing, event);
        addTurnSource(ensureAgentTurn(event), event);
        const group = currentTurn?.children?.find(child =>
          child.kind === 'reasoning_group' &&
          child.children.includes(existing));
        if (group) group.source.push(sourceRef(event));
        continue;
      }

      const node = toolNode(event);
      if (node.interactive) {
        closeReasoning();
        const turn = ensureAgentTurn(event);
        turn.children.push({ ...node, kind: 'interaction' });
      } else {
        const group = ensureReasoningGroup(event);
        group.children.push(node);
        group.source.push(sourceRef(event));
      }
      continue;
    }

    if ((event.kind === 'message' || event.kind === 'commentary') &&
        event.role === 'assistant') {
      closeReasoning();
      const turn = ensureAgentTurn(event);
      turn.children.push(contentNode(
        event,
        event.kind === 'commentary' ? 'commentary' : 'markdown'
      ));
      continue;
    }

    if (event.kind === 'notice') {
      closeReasoning();
      const turn = ensureAgentTurn(event);
      turn.children.push(contentNode(event, 'notice'));
    }
  }

  return {
    schema_version: PRESENTATION_SCHEMA_VERSION,
    kind: 'conversation',
    turns
  };
}
