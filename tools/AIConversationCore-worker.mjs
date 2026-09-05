#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { pathToFileURL } from 'node:url';

const CORE_COMMIT = 'e2e86e844e0600b5bd6a8966b464931598308899';

/**
 * Returns the configured or sibling AIConversationCore checkout.
 *
 * @returns {string} Absolute AIConversationCore repository path.
 */
function coreRootPath() {
  const configured = process.env.AI_CONVERSATION_CORE;
  if (configured) return path.resolve(configured);
  return path.resolve(import.meta.dirname, '..', '..', 'AIConversationCore');
}

/**
 * Verifies that the runtime core is the exact Phase-8 version expected by this
 * AgentPanelSpeaker branch.
 *
 * @returns {void}
 */
function verifyCorePin() {
  let actual;
  try {
    actual = execFileSync('git', ['-C', coreRootPath(), 'rev-parse', 'HEAD'], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe']
    }).trim();
  } catch (error) {
    throw new Error(
      `Cannot verify AIConversationCore checkout at ${coreRootPath()}: ${error.message}`
    );
  }
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
 * Normalizes provider-native records through AIConversationCore.
 *
 * @param {string} provider - Canonical provider identifier.
 * @param {Array<Object<string, *>>} records - Ordered source records.
 * @returns {Array<Object<string, *>>} Ordered canonical events.
 */
function adapt(provider, records) {
  if (provider === 'claude') return core.adaptClaudeRecords(records);
  if (provider === 'codex') return core.adaptCodexRecords(records);
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

  const events = adapt(request.provider, request.records);
  return {
    ok: true,
    core_commit: CORE_COMMIT,
    projection: core.projectCanonicalConversation(events)
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
