#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { pathToFileURL } from 'node:url';

const CORE_COMMIT = '9ab9e4f5bd5f4e4a02653267ed118c378912617f';

/**
 * Returns the configured development checkout or the runtime bundled beside
 * this worker.
 *
 * @returns {string} Absolute AIConversationCore runtime path.
 */
function coreRootPath() {
  const configured = process.env.AI_CONVERSATION_CORE;
  if (configured) return path.resolve(configured);
  return path.resolve(import.meta.dirname, 'AIConversationCore-runtime');
}

/**
 * Reads the exact core revision represented by one runtime path.
 *
 * Bundled runtimes carry a CORE_COMMIT marker and therefore do not depend on
 * git. An explicitly configured development checkout may fall back to git so
 * CI and local development can still point at the source repository directly.
 *
 * @param {string} root - AIConversationCore runtime or checkout root.
 * @returns {string} Exact represented commit SHA.
 */
function representedCoreCommit(root) {
  const marker = path.join(root, 'CORE_COMMIT');
  if (existsSync(marker)) {
    return readFileSync(marker, 'utf8').trim();
  }

  if (process.env.AI_CONVERSATION_CORE) {
    try {
      return execFileSync('git', ['-C', root, 'rev-parse', 'HEAD'], {
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'pipe']
      }).trim();
    } catch (error) {
      throw new Error(
        `Cannot verify configured AIConversationCore checkout at ${root}: ${error.message}`
      );
    }
  }

  throw new Error(
    `Bundled AIConversationCore runtime is missing CORE_COMMIT at ${marker}`
  );
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
 * @param {string} provider - Canonical provider identifier.
 * @param {Array<Object<string, *>>} records - Ordered source records.
 * @param {Object<string, *>} options - Optional provider normalization options.
 * @returns {Array<Object<string, *>>} Ordered canonical events.
 */
function adapt(provider, records, options) {
  if (provider === 'claude' || provider === 'codex') {
    return core.adaptSpeechSessionRecords(provider, records, options);
  }
  if (provider === 'chatgpt') return core.adaptChatGPTRecords(records);
  throw new Error(`Unsupported provider: ${provider}`);
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
  if (request?.operation !== 'project') {
    throw new Error(`Unsupported operation: ${request?.operation}`);
  }
  if (!Array.isArray(request.records)) {
    throw new TypeError('project request records must be an array');
  }

  const options = {
    includeRolledBackTurns: request?.options?.includeRolledBackTurns === true
  };
  const events = adapt(request.provider, request.records, options);
  const projection = core.projectCanonicalConversation(events);

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
    projection
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
