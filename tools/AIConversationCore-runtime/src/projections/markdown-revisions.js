import { renderCanonicalMarkdown as renderBaseMarkdown } from './markdown.js';

/**
 * Returns the visible revision/execution suffix for one canonical User event.
 *
 * @param {Object<string, *>} event - Canonical User event.
 * @returns {string} Parenthesized status suffix or an empty string.
 */
function userStatusSuffix(event) {
  const statuses = [];
  if (event?.revision_status && event.revision_status !== 'normal') {
    statuses.push(event.revision_status);
  }
  if (event?.execution_status === 'aborted') statuses.push('aborted');
  return statuses.length ? ` (${statuses.join(', ')})` : '';
}

/**
 * Moves a legacy heading suffix immediately after the User label so revision
 * state composes as `## User (edited) [timestamp]: record:` rather than being
 * appended after consumer metadata.
 *
 * @param {string} markdown - Base canonical Markdown.
 * @param {Array<Object<string, *>>} events - Ordered canonical events.
 * @returns {string} Revision-aware Markdown.
 */
function positionUserStatuses(markdown, events) {
  const statuses = events
    .filter(event => event?.visibility !== 'hidden' && event?.role === 'user' &&
      event?.kind === 'message')
    .map(userStatusSuffix)
    .filter(Boolean);
  if (!statuses.length) return markdown;

  let statusIndex = 0;
  return markdown.split('\n').map(line => {
    if (statusIndex >= statuses.length || !line.includes('## User')) return line;
    const status = statuses[statusIndex];
    if (!line.endsWith(status)) return line;
    const labelIndex = line.indexOf('## User');
    if (labelIndex < 0) return line;
    const afterLabel = labelIndex + '## User'.length;
    let resetEnd = afterLabel;
    const resetMatch = line.slice(afterLabel).match(/^(\x1b\[[0-9;]*m)/);
    if (resetMatch) resetEnd += resetMatch[1].length;
    statusIndex += 1;
    return `${line.slice(0, resetEnd)}${status}${line.slice(resetEnd, -status.length)}`;
  }).join('\n');
}

/**
 * Renders canonical Markdown with revision/execution status positioned as part
 * of the User heading label before timestamp/record-number metadata.
 *
 * @param {Array<Object<string, *>>} events - Ordered canonical event stream.
 * @returns {string} Canonical transcript Markdown.
 */
export function renderCanonicalMarkdown(events) {
  return positionUserStatuses(renderBaseMarkdown(events), events);
}
