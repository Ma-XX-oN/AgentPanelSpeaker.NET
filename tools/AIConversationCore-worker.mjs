#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { pathToFileURL } from 'node:url';

const CORE_COMMIT = 'c97dc6f6a0d6e2bd6d21bc145da722645b19e002';
const sessions = new Map();

/**
 * Returns the configured development checkout, the repository submodule when
 * running from source, or the runtime bundled beside this worker.
 *
 * @returns {string} Absolute AIConversationCore runtime path.
 */
function coreRootPath() {
  const configured = process.env.AI_CONVERSATION_CORE;
  if (configured) return path.resolve(configured);

  const sourceCheckout = path.resolve(
    import.meta.dirname,
    '..',
    'dependencies',
    'AIConversationCore'
  );
  if (existsSync(path.join(sourceCheckout, 'src', 'index.js'))) {
    return sourceCheckout;
  }

  return path.resolve(import.meta.dirname, 'AIConversationCore-runtime');
}

/**
 * Reads the exact core revision represented by one runtime path.
 *
 * Bundled runtimes carry a CORE_COMMIT marker. Source/development checkouts are
 * verified directly with git so a stale marker cannot make a different source
 * tree appear to satisfy the pin.
 *
 * @param {string} root - AIConversationCore runtime or checkout root.
 * @returns {string} Exact represented commit SHA.
 */
function representedCoreCommit(root) {
  const marker = path.join(root, 'CORE_COMMIT');
  if (existsSync(marker)) {
    return readFileSync(marker, 'utf8').trim();
  }

  try {
    return execFileSync('git', ['-C', root, 'rev-parse', 'HEAD'], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe']
    }).trim();
  } catch (error) {
    throw new Error(
      `Cannot verify AIConversationCore checkout at ${root}: ${error.message}`
    );
  }
}

/**
 * Verifies that the runtime core is the exact version expected by this
 * AgentPanelSpeaker branch.
 *
 * @returns {void}
 */
function verifyCorePin() {
  const root = coreRootPath();
  const actual = representedCoreCommit(root);
  if (actual !== CORE_COMMIT) {
    throw new Error(
      `AIConversationCore commit mismatch: expected ${CORE_COMMIT}, found ${actual}`
    );
  }
}

verifyCorePin();
const core = await import(
  pathToFileURL(path.join(coreRootPath(), 'src', 'index.js')).href
);

/**
 * Normalizes provider-native records through the canonical speech-session seam.
 *
 * This operation remains only for compatibility with callers not yet migrated
 * to retained sessions. Interactive AgentPanelSpeaker paths should use the
 * session_* operations below.
 *
 * @param {string} provider - Canonical provider identifier.
 * @param {Array<Object<string, *>>} records - Ordered source records.
 * @param {Object<string, *>} options - Optional provider normalization options.
 * @returns {Array<Object<string, *>>} Ordered canonical events.
 */
function adapt(provider, records, options) {
  if (provider === 'claude' || provider === 'codex') {
    return core.adaptSpeechSessionRecords(provider, records, {
      ...options,
      includeRolledBackTurns: true,
      includeUserContext: true
    });
  }
  if (provider === 'chatgpt') return core.adaptChatGPTRecords(records);
  throw new Error(`Unsupported provider: ${provider}`);
}

/**
 * Resolves projection-time options without allowing visibility preferences to
 * alter retained canonical identity.
 *
 * @param {Object<string, *>} request - Bridge request.
 * @returns {Object<string, boolean>} Projection options.
 */
function projectionOptions(request) {
  return {
    includeRolledBackTurns: request?.options?.includeRolledBackTurns === true,
    includeUserContext: request?.options?.includeUserContext === true
  };
}

/**
 * Returns a retained session or throws a useful protocol error.
 *
 * @param {string} sessionId - Caller-owned session identifier.
 * @returns {Object<string, *>} Retained session entry.
 */
function requireSession(sessionId) {
  const entry = sessions.get(sessionId);
  if (!entry) throw new Error(`Unknown retained session: ${sessionId}`);
  return entry;
}


/**
 * Adds Core-rendered HTML virtualization units to one structured projection.
 *
 * The worker forwards completed Core HTML and metadata; it never interprets or
 * recreates presentation semantics.
 *
 * @param {Object<string, *>} projection - Structured Core projection.
 * @param {Object<string, boolean>} options - Effective projection options.
 * @returns {Object<string, *>} Projection including ordered Core HTML units.
 */
function withHtmlUnits(projection, options) {
  return {
    ...projection,
    html_units: core.renderCanonicalHtmlUnits(projection?.events ?? [], options)
  };
}

/**
 * Executes one bridge request.
 *
 * @param {Object<string, *>} request - Decoded line-delimited JSON request.
 * @returns {Object<string, *>} JSON-serializable bridge response.
 */
function execute(request) {
  if (request?.operation === 'ping') {
    return {
      ok: true,
      core_commit: CORE_COMMIT
    };
  }

  const options = projectionOptions(request);

  if (request?.operation === 'session_create') {
    if (typeof request.session_id !== 'string' || !request.session_id) {
      throw new TypeError('session_create request requires session_id');
    }
    if (!Array.isArray(request.records)) {
      throw new TypeError('session_create request records must be an array');
    }
    const session = core.createCanonicalConversationSession({
      provider: request.provider,
      records: request.records
    });
    sessions.set(request.session_id, {
      provider: request.provider,
      session
    });
    return {
      ok: true,
      core_commit: CORE_COMMIT,
      projection: withHtmlUnits(session.project(options), options),
      diagnostics: session.diagnostics
    };
  }

  if (request?.operation === 'session_project') {
    const entry = requireSession(request.session_id);
    return {
      ok: true,
      core_commit: CORE_COMMIT,
      projection: withHtmlUnits(entry.session.project(options), options),
      diagnostics: entry.session.diagnostics
    };
  }

  if (request?.operation === 'session_append') {
    if (!Array.isArray(request.records)) {
      throw new TypeError('session_append request records must be an array');
    }
    const entry = requireSession(request.session_id);
    entry.session.append(request.records);
    return {
      ok: true,
      core_commit: CORE_COMMIT,
      projection: withHtmlUnits(entry.session.project(options), options),
      diagnostics: entry.session.diagnostics
    };
  }

  if (request?.operation === 'session_close') {
    sessions.delete(request.session_id);
    return {
      ok: true,
      core_commit: CORE_COMMIT
    };
  }

  if (request?.operation !== 'project') {
    throw new Error(`Unsupported operation: ${request?.operation}`);
  }
  if (!Array.isArray(request.records)) {
    throw new TypeError('project request records must be an array');
  }

  const events = adapt(request.provider, request.records, options);
  const projection = core.projectCanonicalConversation(events, options);

  if (request.provider === 'codex') {
    const loaded = core.loadConversationSources({
      provider: 'codex',
      primarySource: { records: request.records },
      supplementarySources: request.supplementary_sources ?? {},
      options
    });
    projection.session_metadata = loaded.session_metadata;
  }

  return {
    ok: true,
    core_commit: CORE_COMMIT,
    projection: withHtmlUnits(projection, options)
  };
}

const input = createInterface({ input: process.stdin, crlfDelay: Infinity });
for await (const line of input) {
  if (!line.trim()) continue;
  try {
    process.stdout.write(`${JSON.stringify(execute(JSON.parse(line)))}\n`);
  } catch (error) {
    process.stdout.write(`${JSON.stringify({
      ok: false,
      error: error instanceof Error ? error.message : String(error)
    })}\n`);
  }
}
