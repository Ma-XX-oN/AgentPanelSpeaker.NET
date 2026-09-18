import { renderCanonicalMarkdown as renderRevisionMarkdown } from './markdown-revisions.js';
import { projectRevisionVisibility } from './revision-visibility.js';

/**
 * Renders canonical Markdown after applying projection-time revision visibility.
 *
 * Markdown is a serialization rather than an interactive retained DOM, so events
 * that are not effectively visible are omitted from this output.  Canonical
 * normalization itself still retains them.
 *
 * @param {Array<Object<string, *>>} events - Complete canonical event inventory.
 * @param {Object<string, *>} options - Projection options.
 * @returns {string} Canonical Markdown for the selected visibility projection.
 */
export function renderCanonicalMarkdown(events, options = {}) {
  const projected = projectRevisionVisibility(events, options);
  const visible = projected.filter(event => event?.projection?.visible !== false);
  return renderRevisionMarkdown(visible);
}
