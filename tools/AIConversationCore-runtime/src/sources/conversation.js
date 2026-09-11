import { readFileSync } from 'node:fs';

import { adaptChatGPTRecords } from '../adapters/chatgpt.js';
import { adaptClaudeRecords } from '../adapters/claude-normalized.js';
import {
  adaptCodexRecords,
  resolveCodexSessionMetadata
} from '../adapters/codex.js';
import { projectCanonicalConversation } from '../projections/structured.js';

/**
 * Parses one JSONL text source into ordered records.
 *
 * @param {string} text - Complete UTF-8 JSONL text.
 * @param {string} label - Human-readable source label used in parse errors.
 * @returns {Array<Object<string, *>>} Ordered decoded JSON records.
 */
function parseJsonl(text, label) {
  if (typeof text !== 'string') {
    throw new TypeError(`${label} text must be a string.`);
  }

  const records = [];
  const lines = text.split(/\r?\n/);
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim();
    if (!line) continue;
    try {
      const record = JSON.parse(line);
      if (!record || typeof record !== 'object' || Array.isArray(record)) {
        throw new TypeError('record is not a JSON object');
      }
      records.push(record);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      throw new SyntaxError(`${label} line ${index + 1}: ${message}`);
    }
  }
  return records;
}

/**
 * Reads one caller-supplied source without performing provider-location discovery.
 *
 * Accepted forms are `{ path }`, `{ text }`, or `{ records }`. A bare string is
 * treated as a filesystem path for convenience. The caller decides which source
 * to supply; the core owns reading/parsing the supplied source.
 *
 * @param {string|Object<string, *>} source - Caller-supplied source descriptor.
 * @param {string} label - Human-readable source label used in errors.
 * @returns {Array<Object<string, *>>} Ordered decoded source records.
 */
function sourceRecords(source, label) {
  if (typeof source === 'string') {
    return parseJsonl(readFileSync(source, 'utf8'), label);
  }
  if (!source || typeof source !== 'object' || Array.isArray(source)) {
    throw new TypeError(`${label} must be a source descriptor.`);
  }
  if (Array.isArray(source.records)) return source.records;
  if (typeof source.text === 'string') return parseJsonl(source.text, label);
  if (typeof source.path === 'string' && source.path.trim()) {
    return parseJsonl(readFileSync(source.path, 'utf8'), label);
  }
  throw new TypeError(`${label} must provide path, text, or records.`);
}

/**
 * Adapts one provider's records into canonical events.
 *
 * @param {string} provider - Canonical provider identifier.
 * @param {Array<Object<string, *>>} records - Ordered provider records.
 * @param {Object<string, *>} options - Provider normalization options.
 * @returns {Array<Object<string, *>>} Ordered canonical events.
 */
function adapt(provider, records, options) {
  if (provider === 'chatgpt') return adaptChatGPTRecords(records);
  if (provider === 'claude') return adaptClaudeRecords(records);
  if (provider === 'codex') return adaptCodexRecords(records, options);
  throw new Error(`Unsupported provider: ${provider}`);
}

/**
 * Resolves a readable fallback title from the first recorded Codex User request.
 *
 * IDE context remains untouched in transcript content. This helper only derives
 * session metadata from the already-recorded message and prefers text after the
 * standard `## My request for Codex:` marker when present.
 *
 * @param {Array<Object<string, *>>} records - Ordered Codex rollout records.
 * @returns {string|null} Readable fallback title or null.
 */
function codexUserFallbackTitle(records) {
  for (const record of records) {
    const payload = record?.type === 'event_msg' ? record?.payload : null;
    if (payload?.type !== 'user_message' || typeof payload?.message !== 'string') continue;
    let message = payload.message.replace(/\r\n/g, '\n');
    const marker = '## My request for Codex:';
    const markerIndex = message.indexOf(marker);
    if (markerIndex >= 0) message = message.slice(markerIndex + marker.length);
    const candidate = message.split('\n').map(line => line.trim()).find(Boolean) ?? '';
    if (candidate) return candidate;
  }
  return null;
}

/**
 * Loads one conversation from caller-supplied primary and supplementary sources.
 *
 * The caller owns source discovery/origin. This function owns reading and JSONL
 * parsing of the sources it is given, provider normalization, supplementary
 * metadata interpretation, and structured presentation projection.
 *
 * `supplementarySources.codexSessionIndex` is optional and is interpreted only
 * for Codex. The core never searches for `~/.codex`, `CODEX_HOME`, or another
 * provider-specific filesystem location.
 *
 * @param {Object<string, *>} input - Conversation source request.
 * @param {string} input.provider - Canonical provider identifier.
 * @param {string|Object<string, *>} input.primarySource - Primary provider source.
 * @param {Object<string, *>} [input.supplementarySources={}] - Optional supplementary sources.
 * @param {Object<string, *>} [input.options={}] - Provider normalization options.
 * @returns {Object<string, *>} Loaded records, canonical events, metadata, and projection.
 */
export function loadConversationSources({
  provider,
  primarySource,
  supplementarySources = {},
  options = {}
}) {
  if (typeof provider !== 'string' || !provider) {
    throw new TypeError('provider must be a non-empty string.');
  }
  const records = sourceRecords(primarySource, `${provider} primary source`);
  const events = adapt(provider, records, options);

  let sessionMetadata = null;
  if (provider === 'codex') {
    const indexSource = supplementarySources?.codexSessionIndex;
    const sessionIndexRecords = indexSource == null
      ? []
      : sourceRecords(indexSource, 'Codex session index');
    sessionMetadata = resolveCodexSessionMetadata(records, sessionIndexRecords);
    if (!sessionMetadata.title) {
      const title = codexUserFallbackTitle(records);
      if (title) {
        sessionMetadata = {
          ...sessionMetadata,
          title,
          title_source: 'codex-user-message'
        };
      }
    }
  }

  return {
    provider,
    records,
    events,
    session_metadata: sessionMetadata,
    projection: projectCanonicalConversation(events)
  };
}
