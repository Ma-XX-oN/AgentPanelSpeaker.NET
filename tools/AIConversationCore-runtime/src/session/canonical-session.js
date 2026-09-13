import { adaptChatGPTRecords } from '../adapters/chatgpt.js';
import { adaptClaudeRecords } from '../adapters/claude-normalized.js';
import { adaptSpeechSessionRecords } from '../adapters/speech-session-normalized.js';
import { renderCanonicalHtml } from '../projections/html-visibility.js';
import { renderCanonicalMarkdown } from '../projections/markdown-visibility.js';
import { projectCanonicalConversation } from '../projections/structured-visibility.js';

/**
 * Adapts one complete provider record inventory exactly once for a retained
 * canonical session.
 *
 * Codex is normalized through the established interactive speech-session seam
 * with all User Context retained. Presentation-time options can then change
 * speech eligibility without returning to provider records.
 *
 * @param {string} provider - Canonical provider identifier.
 * @param {Array<Object<string, *>>} records - Ordered provider records.
 * @returns {Array<Object<string, *>>} Complete canonical event inventory.
 */
function normalizeInitialEvents(provider, records) {
  if (provider === 'codex') {
    return adaptSpeechSessionRecords(provider, records, { includeUserContext: true });
  }
  if (provider === 'claude') return adaptClaudeRecords(records);
  if (provider === 'chatgpt') return adaptChatGPTRecords(records);
  throw new Error(`Unsupported provider: ${provider}`);
}

/**
 * Builds the heading suffix used by canonical Codex revision presentation.
 *
 * @param {Object<string, *>} interaction - Retained Codex interaction state.
 * @returns {string} Parenthesized heading suffix or an empty string.
 */
function interactionHeadingSuffix(interaction) {
  const statuses = [];
  if (interaction.revision_status !== 'normal') {
    statuses.push(Number.isInteger(interaction.revision_depth)
      ? `${interaction.revision_status} ${interaction.revision_depth}`
      : interaction.revision_status);
  }
  if (interaction.execution_status === 'aborted') statuses.push('aborted');
  return statuses.length ? ` (${statuses.join(', ')})` : '';
}

/**
 * Formats one provider model identifier for a user-visible model-change notice.
 *
 * @param {*} value - Provider model identifier.
 * @returns {string} Display model label.
 */
function modelLabel(value) {
  return String(value ?? '').replace(/^gpt-/i, 'GPT-');
}

/**
 * Creates the canonical model-change notice emitted when a replacement Codex
 * turn changes model relative to the rolled-back revision it replaces.
 *
 * @param {Object<string, *>} record - Replacement User source record.
 * @param {number} sourceIndex - Replacement User source index.
 * @param {string} previousModel - Previous revision model identifier.
 * @param {string} currentModel - Replacement revision model identifier.
 * @returns {Object<string, *>} Canonical model-change notice.
 */
function modelChangeEvent(record, sourceIndex, previousModel, currentModel) {
  const source = {
    provider: 'codex',
    record_id: null,
    record_index: sourceIndex
  };
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
      source
    }],
    citations: [],
    resources: [],
    relationships: { tool_call_id: null },
    source
  };
}

/**
 * Maintains only the revision state required to apply future Codex records to a
 * retained canonical event inventory.
 *
 * The tracker scans initial provider records once. Subsequent calls process only
 * newly appended records; previously seen records are never revisited.
 */
class CodexRevisionTracker {
  /**
   * Creates tracker state from one initial record inventory.
   *
   * @param {Array<Object<string, *>>} records - Initial ordered Codex records.
   */
  constructor(records) {
    this.currentModel = null;
    this.currentInteraction = null;
    this.active = [];
    this.pendingHistory = [];
    this.interactionBySource = new Map();
    this.modelNotices = new Map();
    for (let index = 0; index < records.length; index += 1) {
      this.processRecord(records[index], index);
    }
    this.finalizePendingHistory();
  }

  /**
   * Processes one newly observed Codex record and updates revision state.
   *
   * @param {Object<string, *>} record - New Codex source record.
   * @param {number} sourceIndex - Stable source-record index.
   * @returns {void}
   */
  processRecord(record, sourceIndex) {
    const payload = record?.payload;
    if (record?.type === 'turn_context' && typeof payload?.model === 'string') {
      this.currentModel = payload.model;
      return;
    }

    if (record?.type === 'event_msg' && payload?.type === 'turn_aborted') {
      if (this.currentInteraction) this.currentInteraction.execution_status = 'aborted';
      return;
    }

    if (record?.type === 'event_msg' && payload?.type === 'thread_rolled_back') {
      const count = Number.isInteger(payload?.num_turns) && payload.num_turns > 0
        ? payload.num_turns
        : 0;
      const popped = [];
      for (let index = 0; index < count && this.active.length; index += 1) {
        const interaction = this.active.pop();
        interaction.rolled_back = true;
        popped.unshift(interaction);
      }
      if (popped.length) {
        const inherited = popped.flatMap(interaction => interaction.history.length
          ? [...interaction.history, interaction]
          : [interaction]);
        this.pendingHistory = inherited.filter((interaction, index, list) =>
          list.indexOf(interaction) === index);
        this.currentInteraction = this.active.at(-1) ?? null;
        this.finalizePendingHistory();
      }
      return;
    }

    if (record?.type === 'event_msg' && payload?.type === 'user_message') {
      const previous = this.pendingHistory.at(-1) ?? null;
      const interaction = {
        model: this.currentModel,
        history: this.pendingHistory,
        rolled_back: false,
        revision_status: this.pendingHistory.length ? 'edited' : 'normal',
        revision_depth: this.pendingHistory.length ? this.pendingHistory.length : null,
        execution_status: 'completed'
      };
      this.finalizePendingHistory();
      if (previous?.model && interaction.model && previous.model !== interaction.model) {
        this.modelNotices.set(sourceIndex, {
          record,
          previousModel: previous.model,
          currentModel: interaction.model
        });
      }
      this.pendingHistory = [];
      this.active.push(interaction);
      this.currentInteraction = interaction;
    }

    if (this.currentInteraction) {
      this.interactionBySource.set(sourceIndex, this.currentInteraction);
    }
  }

  /**
   * Assigns stable original/superseded status and zero-based depth to pending
   * revision history.
   *
   * @returns {void}
   */
  finalizePendingHistory() {
    this.pendingHistory.forEach((historical, index) => {
      historical.revision_status = index === 0 ? 'original' : 'superseded';
      historical.revision_depth = index;
    });
  }
}

/**
 * Applies retained revision state to one canonical Codex event without changing
 * its canonical ID or source index.
 *
 * @param {Object<string, *>} event - Canonical Codex event.
 * @param {CodexRevisionTracker} tracker - Current retained revision tracker.
 * @returns {Object<string, *>} Event with current revision metadata.
 */
function applyTrackedRevision(event, tracker) {
  const interaction = tracker.interactionBySource.get(event?.source_index);
  if (!interaction) return event;
  const headingSuffix = event.kind === 'message' &&
    (event.role === 'user' || event.role === 'assistant')
      ? interactionHeadingSuffix(interaction)
      : '';
  const projection = { ...(event?.projection ?? {}) };
  if (headingSuffix) projection.heading_suffix = headingSuffix;
  else delete projection.heading_suffix;

  return {
    ...event,
    revision_status: interaction.revision_status,
    revision_depth: interaction.revision_depth,
    execution_status: interaction.execution_status,
    model: interaction.model,
    ...(Object.keys(projection).length ? { projection } : {})
  };
}

/**
 * Applies speech-selection options to already-normalized canonical blocks.
 *
 * User/IDE context remains in the canonical inventory at all times. This clone
 * changes only projection metadata so speech consumers can toggle eligibility
 * without provider adaptation or canonical identity changes.
 *
 * @param {Array<Object<string, *>>} events - Retained canonical events.
 * @param {Object<string, *>} options - Projection options.
 * @returns {Array<Object<string, *>>} Projection-local event clones.
 */
function applySpeechSelection(events, options) {
  const includeUserContext = options?.includeUserContext === true;
  return events.map(event => {
    if (event?.provider !== 'codex' || event?.kind !== 'message' || event?.role !== 'user') {
      return event;
    }
    let changed = false;
    const blocks = (event.blocks ?? []).map(block => {
      if (block?.type !== 'user_context') return block;
      changed = true;
      return {
        ...block,
        speech: {
          ...(block?.speech ?? {}),
          eligible: includeUserContext,
          voice_role: 'user_context'
        }
      };
    });
    return changed ? { ...event, blocks } : event;
  });
}

/**
 * Normalizes only newly appended Codex records while preserving their global
 * source indexes and interactive/speech semantics.
 *
 * A sparse array is intentional: JavaScript `forEach` skips holes, so the
 * established interactive adapter visits only the appended records while still
 * observing their stable absolute indexes. No previously seen provider record
 * is reread. User Context is retained as eligible in canonical session state;
 * later projections can disable it without adapting again.
 *
 * @param {number} firstSourceIndex - Absolute source index of the first new record.
 * @param {Array<Object<string, *>>} records - Newly appended Codex records.
 * @returns {Array<Object<string, *>>} Canonical events created by the appended records.
 */
function normalizeAppendedCodexRecords(firstSourceIndex, records) {
  const sparse = new Array(firstSourceIndex + records.length);
  records.forEach((record, offset) => {
    sparse[firstSourceIndex + offset] = record;
  });
  return adaptSpeechSessionRecords('codex', sparse, { includeUserContext: true });
}

/**
 * Retains one normalized canonical conversation so presentation options can be
 * changed repeatedly without reparsing provider input.
 */
class CanonicalConversationSession {
  /**
   * Creates one retained canonical conversation session.
   *
   * @param {Object<string, *>} input - Session construction request.
   * @param {string} input.provider - Canonical provider identifier.
   * @param {Array<Object<string, *>>} input.records - Initial ordered records.
   */
  constructor({ provider, records }) {
    if (typeof provider !== 'string' || !provider) {
      throw new TypeError('provider must be a non-empty string.');
    }
    if (!Array.isArray(records)) {
      throw new TypeError('records must be an array.');
    }
    this.provider = provider;
    this.records = [...records];
    this._events = normalizeInitialEvents(provider, this.records);
    this._tracker = provider === 'codex'
      ? new CodexRevisionTracker(this.records)
      : null;
    this._projectionCount = 0;
    this._appendedRecordsProcessed = 0;
  }

  /**
   * Returns the retained complete canonical event inventory.
   *
   * @returns {Array<Object<string, *>>} Current canonical events.
   */
  get events() {
    return this._events;
  }

  /**
   * Returns normalization/projection counters used to verify retained-session
   * behavior in diagnostics and regression tests.
   *
   * @returns {Object<string, number>} Retained-session diagnostics.
   */
  get diagnostics() {
    return {
      initial_normalization_passes: 1,
      full_renormalization_passes: 0,
      appended_records_processed: this._appendedRecordsProcessed,
      projection_count: this._projectionCount
    };
  }

  /**
   * Projects the retained canonical inventory with presentation/speech options.
   *
   * @param {Object<string, *>} options - Projection options.
   * @returns {Object<string, *>} Structured canonical projection.
   */
  project(options = {}) {
    this._projectionCount += 1;
    return projectCanonicalConversation(applySpeechSelection(this._events, options), options);
  }

  /**
   * Renders canonical Markdown from the retained canonical inventory.
   *
   * @param {Object<string, *>} options - Projection options.
   * @returns {string} Canonical Markdown.
   */
  renderMarkdown(options = {}) {
    return renderCanonicalMarkdown(this._events, options);
  }

  /**
   * Renders canonical HTML from the retained canonical inventory.
   *
   * @param {Object<string, *>} options - Projection options.
   * @returns {string} Canonical HTML retaining revision identity.
   */
  renderHtml(options = {}) {
    return renderCanonicalHtml(this._events, options);
  }

  /**
   * Appends provider records without rereading or renormalizing the unchanged
   * prefix of the session.
   *
   * Codex append is currently supported because its live rollout is the consumer
   * requiring retained revision-history updates. Other providers reject append
   * rather than silently falling back to a full re-normalization.
   *
   * @param {Array<Object<string, *>>} records - Newly appended provider records.
   * @returns {void}
   */
  append(records) {
    if (!Array.isArray(records)) throw new TypeError('records must be an array.');
    if (!records.length) return;
    if (this.provider !== 'codex' || !this._tracker) {
      throw new Error(`Incremental append is not implemented for provider: ${this.provider}`);
    }

    const firstSourceIndex = this.records.length;
    const appendedEvents = normalizeAppendedCodexRecords(firstSourceIndex, records);
    records.forEach((record, offset) => {
      const sourceIndex = firstSourceIndex + offset;
      this.records.push(record);
      this._tracker.processRecord(record, sourceIndex);
    });
    this._tracker.finalizePendingHistory();

    const existingById = new Map(this._events.map(event => [event.id, event]));
    for (const event of appendedEvents) existingById.set(event.id, event);
    for (const [sourceIndex, notice] of this._tracker.modelNotices.entries()) {
      const event = modelChangeEvent(
        notice.record,
        sourceIndex,
        notice.previousModel,
        notice.currentModel);
      existingById.set(event.id, event);
    }

    this._events = [...existingById.values()]
      .map(event => applyTrackedRevision(event, this._tracker))
      .sort((left, right) => {
        const sourceOrder = (left.source_index ?? 0) - (right.source_index ?? 0);
        if (sourceOrder !== 0) return sourceOrder;
        const leftNotice = left.content_type === 'model_change' ? 0 : 1;
        const rightNotice = right.content_type === 'model_change' ? 0 : 1;
        return leftNotice - rightNotice;
      });
    this._appendedRecordsProcessed += records.length;
  }
}

/**
 * Creates one retained canonical conversation session.
 *
 * @param {Object<string, *>} input - Session construction request.
 * @param {string} input.provider - Canonical provider identifier.
 * @param {Array<Object<string, *>>} input.records - Initial ordered provider records.
 * @returns {CanonicalConversationSession} Retained canonical session.
 */
export function createCanonicalConversationSession(input) {
  return new CanonicalConversationSession(input);
}
