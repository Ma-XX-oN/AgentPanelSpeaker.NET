import {
  renderCanonicalBlockHtml,
  renderCanonicalHtmlUnits as renderBaseHtmlUnits
} from './html.js';
import { buildCanonicalPresentation } from './presentation-revisions.js';
import {
  isHistoricalRevision,
  projectRevisionVisibility
} from './revision-visibility.js';
import { collapseCanonicalWordFragments } from './word-element.js';
import {
  annotateCanonicalHtmlWords,
  canonicalWordDescriptorsFromHtml,
  createCanonicalWordState
} from './word-identity.js';

/**
 * Resolves canonical revision metadata for one presentation turn.
 *
 * @param {Object<string, *>} turn - Canonical presentation turn.
 * @param {Map<string, Object<string, *>>} eventsById - Projected events by ID.
 * @returns {Object<string, *>} Revision and effective-visibility metadata.
 */
function turnRevisionProjection(turn, eventsById) {
  const sourceEvents = (turn?.source ?? [])
    .map(source => eventsById.get(source?.event_id))
    .filter(Boolean);
  const revisionEvent = sourceEvents.find(event =>
    typeof event?.revision_status === 'string' && event.revision_status.length);
  const visible = sourceEvents.length === 0 ||
    sourceEvents.some(event => event?.projection?.visible !== false);
  return {
    visible,
    revision_status: revisionEvent?.revision_status ?? null,
    revision_depth: Number.isInteger(revisionEvent?.revision_depth)
      ? revisionEvent.revision_depth
      : null,
    historical: revisionEvent ? isHistoricalRevision(revisionEvent) : false
  };
}

/**
 * Applies canonical revision/visibility attributes to one already-rendered turn.
 *
 * This remains a Core serializer operation. Interactive consumers receive the
 * completed unit and never need to interpret revision status or rewrite HTML.
 *
 * @param {string} html - Base canonical HTML for one complete turn.
 * @param {Map<string, Object<string, *>>} turnsById - Turn projection metadata.
 * @returns {string} Canonical turn HTML with revision visibility attributes.
 */
function applyTurnRevisionAttributes(html, turnsById) {
  return html.replace(
    /<section class="transcript-turn" data-presentation-id="([^"]*)">/,
    (match, id) => {
      const projection = turnsById.get(id);
      if (!projection) return match;
      const status = projection.revision_status;
      const className = status
        ? `transcript-turn revision-${status}`
        : 'transcript-turn';
      const statusAttribute = status
        ? ` data-revision-status="${status}"`
        : '';
      const depthAttribute = Number.isInteger(projection.revision_depth)
        ? ` data-revision-depth="${projection.revision_depth}"`
        : '';
      const hiddenAttribute = projection.historical && !projection.visible
        ? ' hidden'
        : '';
      const historicalAttribute = projection.historical
        ? ' data-revision-historical="true"'
        : '';
      return `<section class="${className}" data-presentation-id="${id}"` +
        `${statusAttribute}${depthAttribute}${hiddenAttribute}${historicalAttribute}>`;
    }
  );
}

/**
 * Returns the canonical blocks that contribute interactive words for one
 * presentation leaf.
 *
 * @param {Object<string, *>} node - Canonical presentation node.
 * @returns {Array<Object<string, *>>} Ordered word-bearing blocks.
 */
function wordBlocksForPresentationNode(node) {
  if (node?.kind === 'subagent_content') {
    return node?.block ? [node.block] : [];
  }
  if ([
    'user_context',
    'reasoning',
    'markdown',
    'commentary',
    'notice'
  ].includes(node?.kind)) {
    return Array.isArray(node?.blocks) ? node.blocks : [];
  }
  return [];
}

/**
 * Appends authoritative word provenance for one presentation subtree.
 *
 * Block word texts are rendered and tokenized by the same Core helpers as
 * the complete projection.  The complete-unit render later verifies this
 * sequence exactly before provenance is attached, so this path can never
 * silently align by text or ordinal when the renderings disagree.
 *
 * @param {Object<string, *>} node - Canonical presentation subtree.
 * @param {Array<Object<string, *>>} output - Ordered provenance descriptors.
 * @returns {void} Descriptors are appended in canonical render order.
 */
function appendWordProvenance(node, output) {
  if (node?.kind === 'reasoning_group') {
    for (const child of node?.children ?? []) {
      appendWordProvenance(child, output);
    }
    return;
  }
  if (node?.kind === 'tool' || node?.kind === 'interaction' ||
      node?.kind === 'attachments') return;

  for (const block of wordBlocksForPresentationNode(node)) {
    const html = '<div class="presentation-content">' +
      renderCanonicalBlockHtml(block) + '</div>';
    const descriptors = canonicalWordDescriptorsFromHtml(html);
    descriptors.forEach((descriptor, blockWordIndex) => {
      output.push({
        text: descriptor.text,
        separator_before: descriptor.separator_before,
        provenance: {
          presentation_id: node?.id ?? null,
          event_id: node?.event_id ?? null,
          block_id: block?.id ?? null,
          block_word_index: blockWordIndex,
          source: block?.source && typeof block.source === 'object'
            ? { ...block.source }
            : null
        }
      });
    });
  }
}

/**
 * Builds the authoritative ordered word provenance sequence for one turn.
 *
 * @param {Object<string, *>} turn - Canonical presentation turn.
 * @returns {Array<Object<string, *>>} Ordered provenance descriptors.
 */
function turnWordProvenance(turn) {
  const output = [];
  for (const child of turn?.children ?? []) {
    appendWordProvenance(child, output);
  }
  return output;
}

/**
 * Attaches verified canonical provenance to annotated word records.
 *
 * @param {Array<Object<string, *>>} words - Annotated unit word records.
 * @param {Array<Object<string, *>>} expected - Core provenance descriptors.
 * @param {string} unitId - Canonical unit identity for invariant errors.
 * @returns {Array<Object<string, *>>} Word records carrying provenance.
 */
function wordsWithVerifiedProvenance(words, expected, unitId) {
  if (words.length !== expected.length) {
    throw new TypeError(
      `Canonical word provenance count mismatch in unit ${unitId}: ` +
      `${words.length} rendered words versus ${expected.length} block words.`
    );
  }
  return words.map((word, index) => {
    const descriptor = expected[index];
    if (word?.text !== descriptor?.text) {
      throw new TypeError(
        `Canonical word provenance mismatch in unit ${unitId} at word ` +
        `${index}: rendered ${JSON.stringify(word?.text)} versus block ` +
        `${JSON.stringify(descriptor?.text)}.`
      );
    }
    return {
      ...word,
      separator_before: descriptor.separator_before,
      provenance: descriptor.provenance
    };
  });
}

/**
 * Renders canonical HTML as ordered complete-turn units while retaining
 * historical revision turns and assigning one transcript-global word identity.
 *
 * Word IDs are allocated once across the complete ordered unit sequence. The
 * returned `speech_words` are the same identities embedded in each unit's HTML;
 * consumers never have to retokenize or align rendered text independently.
 * Each canonical word leaves Core as one DOM element, with inline Markdown
 * formatting restructured inside that element when necessary.
 *
 * @param {Array<Object<string, *>>} events - Complete canonical event inventory.
 * @param {Object<string, *>} options - Projection options.
 * @returns {Array<Object<string, *>>} Ordered canonical HTML units.
 */
export function renderCanonicalHtmlUnits(events, options = {}) {
  if (!Array.isArray(events)) {
    throw new TypeError('Canonical events must be an array.');
  }
  const projectedEvents = projectRevisionVisibility(events, options);
  const presentation = buildCanonicalPresentation(projectedEvents);
  const eventsById = new Map(projectedEvents.map(event => [event?.id, event]));
  const turnsById = new Map((presentation.turns ?? []).map(turn => [
    String(turn?.id ?? ''),
    turnRevisionProjection(turn, eventsById)
  ]));
  const wordState = createCanonicalWordState();
  const provenanceByTurnId = new Map((presentation.turns ?? []).map(turn => [
    String(turn?.id ?? ''),
    turnWordProvenance(turn)
  ]));

  return renderBaseHtmlUnits(projectedEvents).map(unit => {
    const revisionHtml = applyTurnRevisionAttributes(unit.html, turnsById);
    const annotated = annotateCanonicalHtmlWords(revisionHtml, wordState);
    const provenance = provenanceByTurnId.get(String(unit?.id ?? '')) ?? [];
    const words = wordsWithVerifiedProvenance(
      annotated.words,
      provenance,
      String(unit?.id ?? '')
    );
    return {
      ...unit,
      html: collapseCanonicalWordFragments(annotated.html),
      speech_words: words
    };
  });
}

/**
 * Locates one authoritative canonical word and its containing rendered unit.
 *
 * The numeric word ID is the only lookup key. Core renders the same canonical
 * unit/word projection used by HTML and speech, then resolves the exact identity
 * from that projection. Visible text is never searched or used as a fallback, so
 * duplicate text in another unit cannot impersonate the requested handle.
 *
 * @param {Array<Object<string, *>>} events - Complete canonical event inventory.
 * @param {number} wordId - Positive safe canonical global word ID.
 * @param {Object<string, *>} options - Projection options.
 * @returns {Object<string, *>|null} Canonical word plus containing rendered unit, or null when the ID is absent.
 */
export function locateCanonicalWord(events, wordId, options = {}) {
  if (!Number.isSafeInteger(wordId) || wordId < 1) {
    throw new TypeError('Canonical word ID must be a positive safe integer.');
  }

  const units = renderCanonicalHtmlUnits(events, options);
  for (const unit of units) {
    const word = (unit.speech_words ?? []).find(item => item?.id === wordId);
    if (word) return { word, unit };
  }
  return null;
}

/**
 * Projects the authoritative global word handles used by HTML and speech/UI.
 *
 * This projection is derived from the same annotated unit render returned by
 * `renderCanonicalHtmlUnits`, so there is no second tokenization/alignment path.
 * Internal word-to-turn/unit lookup remains Core-owned and is exposed through a
 * separate high-level lookup API rather than copied into each word record.
 *
 * @param {Array<Object<string, *>>} events - Complete canonical event inventory.
 * @param {Object<string, *>} options - Projection options.
 * @returns {Object<string, Array<Object<string, *>>>} Ordered canonical words.
 */
export function projectCanonicalWords(events, options = {}) {
  const units = renderCanonicalHtmlUnits(events, options);
  return {
    words: units.flatMap(unit => unit.speech_words)
  };
}

/**
 * Renders canonical HTML while retaining historical revision turns in the DOM.
 *
 * Historical turns always remain serialized with stable presentation IDs and
 * semantic revision attributes. The selected projection controls only their
 * `hidden` state, allowing an interactive consumer to show/hide the already
 * materialized DOM without reparsing provider records or changing identities.
 *
 * @param {Array<Object<string, *>>} events - Complete canonical event inventory.
 * @param {Object<string, *>} options - Projection options.
 * @returns {string} Canonical structural HTML containing all revision turns.
 */
export function renderCanonicalHtml(events, options = {}) {
  return renderCanonicalHtmlUnits(events, options)
    .map(unit => unit.html)
    .join('');
}
