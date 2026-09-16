import {
  STYLE_ROLES,
  resolveProjectionTheme
} from './style.js';

/** Human-readable provider labels used when rendering turn-header provenance. */
const PROVIDER_LABELS = Object.freeze({
  chatgpt: 'ChatGPT',
  claude: 'Claude',
  codex: 'Codex'
});

/**
 * Escapes text for safe insertion into generated HTML fragments.
 *
 * @param {string} text - The text value to process.
 * @returns {string} The HTML-escaped form of the supplied text.
 */
function htmlEscape(text) {
  return String(text)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/**
 * Handles heading label.
 *
 * @param {Object<string, *>} turn - The derived canonical turn whose identity or header is being projected.
 * @returns {string} The User label or human-readable Assistant provider label for the canonical turn.
 */
function headingLabel(turn) {
  if (turn?.role === 'user') return 'User';
  const provider = turn?.source?.provider ?? turn?.provider ?? null;
  return PROVIDER_LABELS[provider] ?? 'Assistant';
}

/**
 * Returns the first provider/source turn identity retained by a derived turn.
 *
 * Source turn identity is deliberately distinct from the canonical derived
 * `turn:<event>` identity.  ChatGPT and Claude preserve native source-record
 * IDs here; Codex currently has no suitable per-rendered-record UUID-like ID
 * and therefore returns null.
 *
 * @param {Object<string, *>} turn - The derived canonical turn to inspect.
 * @returns {string|null} The provider/source turn identity, or null when absent.
 */
function sourceTurnId(turn) {
  const records = Array.isArray(turn?.source?.records) ? turn.source.records : [];
  for (const record of records) {
    if (typeof record?.turn_id === 'string' && record.turn_id) return record.turn_id;
  }
  return null;
}

/**
 * Builds turn header components.
 *
 * @param {Object<string, *>} turn - The derived canonical turn whose identity or header is being projected.
 * @param {Object<string, *>} options - Turn-header rendering options such as timestamp, record number, turn ID visibility, format, and theme.
 * @returns {Array<Object<string, string>>} Ordered speaker/timestamp/record-number/turn-ID components used to render the turn header.
 */
export function buildTurnHeaderComponents(turn, options = {}) {
  if (!turn?.id) throw new Error('Turn header projection requires a canonical turn id.');

  const components = [{
    type: 'speaker',
    styleRole: turn.role === 'user'
      ? STYLE_ROLES.USER_HEADING
      : STYLE_ROLES.ASSISTANT_HEADING,
    text: `## ${headingLabel(turn)}`
  }];

  if (options.timestamp != null) {
    components.push({
      type: 'timestamp',
      styleRole: STYLE_ROLES.TIMESTAMP,
      text: `[${options.timestamp}]:`
    });
  }

  if (options.recordNumber != null) {
    components.push({
      type: 'record-number',
      styleRole: STYLE_ROLES.RECORD_NUMBER,
      text: `${options.recordNumber}:`
    });
  }

  if (options.showTurnId) {
    const turnId = options.turnId ?? sourceTurnId(turn);
    if (turnId != null && String(turnId).length > 0) {
      components.push({
        type: 'turn-id',
        styleRole: STYLE_ROLES.TURN_ID,
        text: `turn_id=${turnId}`
      });
    }
  }

  return components;
}

/**
 * Renders plain.
 *
 * @param {Array<Object<string, string>>} components - The ordered turn-header components to render.
 * @returns {string} Plain-text turn header assembled from the ordered components.
 */
function renderPlain(components) {
  return components.map(component => component.text).join(' ');
}

/**
 * Renders ANSI.
 *
 * @param {Array<Object<string, string>>} components - The ordered turn-header components to render.
 * @param {Object<string, *>} theme - The projection theme containing ANSI and HTML style-role mappings.
 * @returns {string} ANSI-styled turn header assembled from the ordered components and theme.
 */
function renderAnsi(components, theme) {
  const reset = theme.ansi.reset ?? '\u001b[0m';
  return components.map(component => {
    const prefix = theme.ansi[component.styleRole] ?? '';
    return prefix ? `${prefix}${component.text}${reset}` : component.text;
  }).join(' ');
}

/**
 * Renders HTML.
 *
 * @param {Array<Object<string, string>>} components - The ordered turn-header components to render.
 * @param {Object<string, *>} theme - The projection theme containing ANSI and HTML style-role mappings.
 * @returns {string} HTML h2 turn header with role-specific classes from the projection theme.
 */
function renderHtml(components, theme) {
  return `<h2>${components.map(component => {
    const className = theme.html[component.styleRole];
    const text = htmlEscape(component.text.replace(/^## /, ''));
    return className
      ? `<span class="${htmlEscape(className)}">${text}</span>`
      : `<span>${text}</span>`;
  }).join(' ')}</h2>`;
}

/**
 * Renders turn header.
 *
 * @param {Object<string, *>} turn - The derived canonical turn whose identity or header is being projected.
 * @param {Object<string, *>} options - Turn-header rendering options such as timestamp, record number, turn ID visibility, format, and theme.
 * @returns {string|Array<Object<string, string>>} The requested plain/ANSI/HTML header string, or the component array when format is components.
 */
export function renderTurnHeader(turn, options = {}) {
  const components = buildTurnHeaderComponents(turn, options);
  const format = options.format ?? 'plain';
  const theme = resolveProjectionTheme(options.theme);

  if (format === 'plain') return renderPlain(components);
  if (format === 'ansi') return renderAnsi(components, theme);
  if (format === 'html') return renderHtml(components, theme);
  if (format === 'components') return components;
  throw new Error(`Unsupported turn header format: ${format}`);
}
