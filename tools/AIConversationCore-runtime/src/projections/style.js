/** Stable semantic style-role names exposed to projection consumers. */
export const STYLE_ROLES = Object.freeze({
  USER_HEADING: 'user-heading',
  ASSISTANT_HEADING: 'assistant-heading',
  TIMESTAMP: 'timestamp',
  RECORD_NUMBER: 'record-number',
  TURN_ID: 'turn-id'
});

/** Immutable default projection theme used as the reset and merge baseline. */
const DEFAULT_THEME = Object.freeze({
  ansi: Object.freeze({
    [STYLE_ROLES.USER_HEADING]: '\u001b[33m',
    [STYLE_ROLES.ASSISTANT_HEADING]: '\u001b[32m',
    [STYLE_ROLES.TIMESTAMP]: '\u001b[36m',
    [STYLE_ROLES.RECORD_NUMBER]: '\u001b[2m',
    [STYLE_ROLES.TURN_ID]: '\u001b[35m',
    reset: '\u001b[0m'
  }),
  html: Object.freeze({
    [STYLE_ROLES.USER_HEADING]: 'transcript-user-heading',
    [STYLE_ROLES.ASSISTANT_HEADING]: 'transcript-assistant-heading',
    [STYLE_ROLES.TIMESTAMP]: 'transcript-timestamp',
    [STYLE_ROLES.RECORD_NUMBER]: 'transcript-record-number',
    [STYLE_ROLES.TURN_ID]: 'transcript-turn-id'
  })
});

/** Mutable process-wide projection theme produced by applying consumer overrides to the default. */
let configuredTheme = cloneTheme(DEFAULT_THEME);

/**
 * Handles clone theme.
 *
 * @param {Object<string, *>} theme - The projection theme containing ANSI and HTML style-role mappings.
 * @returns {Object<string, *>} A detached projection-theme object containing copied ANSI and HTML role maps.
 */
function cloneTheme(theme) {
  return {
    ansi: { ...(theme?.ansi ?? {}) },
    html: { ...(theme?.html ?? {}) }
  };
}

/**
 * Handles merge theme.
 *
 * @param {Object<string, *>} base - The base projection theme on which overrides are applied.
 * @param {Object<string, *>|null} overrides - Optional projection-theme role overrides to merge with the current/base theme.
 * @returns {Object<string, *>} A new projection theme formed by overlaying the supplied role maps on the base theme.
 */
function mergeTheme(base, overrides) {
  return {
    ansi: { ...base.ansi, ...(overrides?.ansi ?? {}) },
    html: { ...base.html, ...(overrides?.html ?? {}) }
  };
}

/**
 * Gets default projection theme.
 *
 * @returns {Object<string, *>} A detached copy of the currently configured projection theme.
 */
export function getDefaultProjectionTheme() {
  return cloneTheme(configuredTheme);
}

/**
 * Configures projection theme.
 *
 * @param {Object<string, *>} overrides - Optional projection-theme role overrides to merge with the current/base theme.
 * @returns {Object<string, *>} A detached copy of the newly configured projection theme.
 */
export function configureProjectionTheme(overrides = {}) {
  configuredTheme = mergeTheme(configuredTheme, overrides);
  return getDefaultProjectionTheme();
}

/**
 * Resets projection theme.
 *
 * @returns {Object<string, *>} A detached copy of the restored built-in projection theme.
 */
export function resetProjectionTheme() {
  configuredTheme = cloneTheme(DEFAULT_THEME);
  return getDefaultProjectionTheme();
}

/**
 * Handles resolve projection theme.
 *
 * @param {Object<string, *>|null} overrides - Optional projection-theme role overrides to merge with the current/base theme.
 * @returns {Object<string, *>} A new effective projection theme combining the configured theme with optional per-call overrides.
 */
export function resolveProjectionTheme(overrides = null) {
  return mergeTheme(configuredTheme, overrides);
}
