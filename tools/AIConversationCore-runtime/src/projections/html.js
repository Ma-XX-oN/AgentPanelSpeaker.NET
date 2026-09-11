import { marked } from 'marked';

import { buildCanonicalPresentation } from './presentation-revisions.js';

/**
 * Escapes text for safe insertion into generated structural HTML.
 *
 * @param {*} value - Value to escape.
 * @returns {string} HTML-escaped text.
 */
function htmlEscape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/**
 * Adds an explicit resolved ordinal to every ordered-list item in a generated
 * Markdown HTML fragment.
 *
 * HTML list markers are visual presentation and are not part of an item's text.
 * Exposing the resolved ordinal as stable semantic metadata lets speech and
 * accessibility integrations consume Core output without reparsing Markdown or
 * recreating ordered-list numbering. Nested lists maintain independent ordinal
 * counters and unordered lists remain unchanged.
 *
 * @param {string} html - Marked-generated HTML fragment.
 * @returns {string} HTML with `data-list-ordinal` on ordered-list items.
 */
function annotateOrderedListOrdinals(html) {
  const lists = [];
  return String(html).replace(
    /<(\/?)(ol|ul|li)\b([^>]*)>/gi,
    (match, closing, rawTag, rawAttributes) => {
      const tag = rawTag.toLowerCase();
      const attributes = rawAttributes ?? '';

      if (closing) {
        if (tag === 'ol' || tag === 'ul') lists.pop();
        return match;
      }

      if (tag === 'ol') {
        const startMatch = attributes.match(
          /\bstart\s*=\s*(?:"(-?\d+)"|'(-?\d+)'|(-?\d+))/i
        );
        const start = Number.parseInt(
          startMatch?.[1] ?? startMatch?.[2] ?? startMatch?.[3] ?? '1',
          10
        );
        lists.push({ ordered: true, next: Number.isFinite(start) ? start : 1 });
        return match;
      }

      if (tag === 'ul') {
        lists.push({ ordered: false, next: null });
        return match;
      }

      const list = lists.at(-1);
      if (!list?.ordered) return match;
      const ordinal = list.next;
      list.next += 1;
      if (/\bdata-list-ordinal\s*=/i.test(attributes)) return match;
      return `<li${attributes} data-list-ordinal="${ordinal}">`;
    }
  );
}

/**
 * Renders one canonical Markdown leaf to HTML inside Core.
 *
 * @param {string} markdown - Canonical Markdown leaf content.
 * @returns {string} HTML generated from the canonical Markdown content.
 */
function renderMarkdown(markdown) {
  const html = String(marked.parse(String(markdown ?? ''), {
    async: false,
    breaks: false,
    gfm: true
  }));
  return annotateOrderedListOrdinals(html);
}

/**
 * Converts one canonical block to Markdown leaf text without interpreting
 * provider-native syntax.
 *
 * @param {Object<string, *>} block - Canonical content block.
 * @returns {string} Markdown leaf text represented by the block.
 */
function blockMarkdown(block) {
  if (!block || typeof block !== 'object') return '';
  if (block.type === 'text') return block.text ?? '';
  if (block.type === 'reasoning_summary') {
    const content = block.content ?? '';
    if (content) return content;
    return block.summary ?? '';
  }
  if (block.type === 'code') {
    const language = block.language ?? '';
    const code = block.code ?? block.text ?? '';
    return `\`\`\`${language}\n${code}\n\`\`\``;
  }
  return block.text ?? '';
}

/**
 * Converts the Markdown-bearing blocks on one presentation node to one leaf
 * string in canonical block order.
 *
 * @param {Object<string, *>} node - Canonical presentation node.
 * @returns {string} Markdown leaf content.
 */
function nodeMarkdown(node) {
  return (node?.blocks ?? [])
    .map(blockMarkdown)
    .filter(text => text !== '')
    .join('\n\n');
}

/**
 * Renders source anchors retained by the canonical presentation tree.
 *
 * @param {Object<string, *>} node - Canonical presentation node.
 * @param {Set<number>} emittedSourceIndexes - Source indexes already emitted.
 * @returns {string} Stable source-anchor HTML.
 */
function renderSourceAnchors(node, emittedSourceIndexes) {
  const output = [];
  for (const source of node?.source ?? []) {
    const index = source?.record_index;
    if (!Number.isInteger(index) || emittedSourceIndexes.has(index)) continue;
    emittedSourceIndexes.add(index);
    const id = source?.record_id ?? String(index + 1);
    output.push(
      `<span class="record-anchor" data-jsonl-record="${index + 1}" ` +
      `data-source-id="${htmlEscape(id)}"></span>`
    );
  }
  return output.join('');
}

/**
 * Renders a Markdown-bearing canonical presentation node.
 *
 * @param {Object<string, *>} node - Canonical presentation node.
 * @param {Set<number>} emittedSourceIndexes - Source indexes already emitted.
 * @param {string} className - Semantic CSS class for the content wrapper.
 * @returns {string} Canonical HTML for the node.
 */
function renderMarkdownNode(node, emittedSourceIndexes, className = 'presentation-content') {
  const anchors = renderSourceAnchors(node, emittedSourceIndexes);
  const markdown = nodeMarkdown(node);
  const body = markdown ? renderMarkdown(markdown) : '';
  return `<div class="${className}" data-presentation-id="${htmlEscape(node?.id ?? '')}">${anchors}${body}</div>`;
}

/**
 * Returns the stable reasoning-group summary label.
 *
 * @param {number} count - Canonical thought count.
 * @returns {string} Human-readable reasoning summary.
 */
function thoughtSummary(count) {
  if (count === 1) return 'Having a thought';
  if (count > 1) return `Having ${count} thoughts`;
  return 'Thought and tool activity';
}

/**
 * Returns a stable summary for one canonical tool presentation node.
 *
 * @param {Object<string, *>} node - Canonical tool or interaction node.
 * @returns {string} Human-readable tool summary.
 */
function toolSummary(node) {
  const description = node?.call?.input?.description;
  if (typeof description === 'string' && description.trim()) return description;
  return node?.name || 'Tool';
}

/**
 * Renders one canonical tool or interaction node.
 *
 * @param {Object<string, *>} node - Canonical tool presentation node.
 * @param {Set<number>} emittedSourceIndexes - Source indexes already emitted.
 * @returns {string} Canonical structural HTML for the tool.
 */
function renderTool(node, emittedSourceIndexes) {
  const anchors = renderSourceAnchors(node, emittedSourceIndexes);
  const payload = [node?.call, node?.result]
    .filter(value => value != null)
    .map(value => JSON.stringify(value))
    .join('\n');
  return `<details class="tool" data-presentation-id="${htmlEscape(node?.id ?? '')}">` +
    `<summary>${htmlEscape(toolSummary(node))}</summary>${anchors}` +
    `<pre><code>${htmlEscape(payload)}</code></pre></details>`;
}

/**
 * Resolves a canonical attachment/image source from node resources.
 *
 * @param {Object<string, *>} node - Canonical attachment node.
 * @param {Object<string, *>} block - Canonical attachment/image block.
 * @returns {string} Renderable resource source or an empty string.
 */
function attachmentSource(node, block) {
  if (block?.url) return block.url;
  if (block?.source_pointer) return block.source_pointer;
  const resource = (node?.resources ?? []).find(item => item?.id === block?.resource_id);
  return resource?.data_url ?? resource?.download_url ?? resource?.source_pointer ?? '';
}

/**
 * Renders canonical User attachments.
 *
 * @param {Object<string, *>} node - Canonical attachments node.
 * @param {Set<number>} emittedSourceIndexes - Source indexes already emitted.
 * @returns {string} Canonical attachment-area HTML.
 */
function renderAttachments(node, emittedSourceIndexes) {
  const anchors = renderSourceAnchors(node, emittedSourceIndexes);
  const images = (node?.blocks ?? []).map(block => {
    const source = attachmentSource(node, block);
    if (!source) return '';
    return `<img src="${htmlEscape(source)}" alt="">`;
  }).join('');
  return `<div class="attachments" data-presentation-id="${htmlEscape(node?.id ?? '')}">${anchors}${images}</div>`;
}

/**
 * Renders semantic User context as a nested blockquote/details disclosure.
 *
 * @param {Object<string, *>} node - Canonical User-context presentation node.
 * @param {Set<number>} emittedSourceIndexes - Source indexes already emitted.
 * @returns {string} Canonical User-context HTML.
 */
function renderUserContext(node, emittedSourceIndexes) {
  const block = (node?.blocks ?? []).find(item => item?.type === 'user_context') ?? {};
  const summary = block.summary ?? '# Context from my IDE setup:';
  const anchors = renderSourceAnchors(node, emittedSourceIndexes);
  const body = block.text ? renderMarkdown(String(block.text)) : '';
  return `<blockquote class="user-context"><details class="user-context-details" ` +
    `data-presentation-id="${htmlEscape(node?.id ?? '')}">` +
    `<summary>${htmlEscape(summary)}</summary>${anchors}${body}</details></blockquote>`;
}

/**
 * Renders one canonical subagent content node.
 *
 * @param {Object<string, *>} node - Canonical subagent content node.
 * @param {Set<number>} emittedSourceIndexes - Source indexes already emitted.
 * @returns {string} Canonical subagent HTML.
 */
function renderSubagentContent(node, emittedSourceIndexes) {
  const anchors = renderSourceAnchors(node, emittedSourceIndexes);
  const block = node?.block ?? {};
  const markdown = block.output ?? block.text ?? block.description ?? '';
  const body = markdown ? renderMarkdown(String(markdown)) : '';
  return `<div class="subagent-content" data-presentation-id="${htmlEscape(node?.id ?? '')}">${anchors}${body}</div>`;
}

/**
 * Renders one reasoning group and its ordered canonical children.
 *
 * @param {Object<string, *>} node - Canonical reasoning-group node.
 * @param {Set<number>} emittedSourceIndexes - Source indexes already emitted.
 * @returns {string} Canonical reasoning-group HTML.
 */
function renderReasoningGroup(node, emittedSourceIndexes) {
  const children = (node?.children ?? []).map(child => {
    if (child?.kind === 'reasoning') {
      return renderMarkdownNode(child, emittedSourceIndexes, 'thought');
    }
    return renderNode(child, emittedSourceIndexes);
  }).join('');
  return `<details class="reasoning" data-presentation-id="${htmlEscape(node?.id ?? '')}">` +
    `<summary>${htmlEscape(thoughtSummary(node?.thought_count ?? 0))}</summary>` +
    `<div class="reasoning-body">${children}</div></details>`;
}

/**
 * Renders one canonical presentation node according to its normalized kind.
 *
 * @param {Object<string, *>} node - Canonical presentation node.
 * @param {Set<number>} emittedSourceIndexes - Source indexes already emitted.
 * @returns {string} Canonical structural HTML for the node.
 */
function renderNode(node, emittedSourceIndexes) {
  switch (node?.kind) {
    case 'reasoning_group':
      return renderReasoningGroup(node, emittedSourceIndexes);
    case 'tool':
    case 'interaction':
      return renderTool(node, emittedSourceIndexes);
    case 'attachments':
      return renderAttachments(node, emittedSourceIndexes);
    case 'user_context':
      return renderUserContext(node, emittedSourceIndexes);
    case 'reasoning':
      return renderMarkdownNode(node, emittedSourceIndexes, 'thought');
    case 'markdown':
      return renderMarkdownNode(node, emittedSourceIndexes, 'presentation-content');
    case 'commentary':
      return renderMarkdownNode(node, emittedSourceIndexes, 'presentation-content commentary');
    case 'notice':
      return renderMarkdownNode(node, emittedSourceIndexes, 'presentation-content notice');
    case 'subagent_content':
      return renderSubagentContent(node, emittedSourceIndexes);
    default:
      throw new TypeError(`Unsupported canonical presentation node kind: ${node?.kind ?? '<missing>'}`);
  }
}

/**
 * Renders one canonical turn from the provider-independent presentation tree.
 *
 * @param {Object<string, *>} turn - Canonical presentation turn.
 * @param {Set<number>} emittedSourceIndexes - Source indexes already emitted.
 * @returns {string} Canonical turn HTML.
 */
function renderTurn(turn, emittedSourceIndexes) {
  const label = turn?.actor?.label || (turn?.actor?.role === 'user' ? 'User' : 'Agent');
  const children = (turn?.children ?? [])
    .map(node => renderNode(node, emittedSourceIndexes))
    .join('');
  return `<section class="transcript-turn" data-presentation-id="${htmlEscape(turn?.id ?? '')}">` +
    `<h2>${htmlEscape(label)}</h2>` +
    `<blockquote class="transcript-turn-body">${children}</blockquote></section>`;
}

/**
 * Renders complete canonical HTML directly from normalized canonical events.
 *
 * All semantic structure and Markdown-to-HTML conversion are owned by
 * AIConversationCore. Downstream consumers integrate this completed HTML rather
 * than interpreting presentation nodes or provider-specific markers.
 *
 * @param {Array<Object<string, *>>} events - Ordered normalized canonical events.
 * @returns {string} Complete canonical HTML transcript.
 */
export function renderCanonicalHtml(events) {
  if (!Array.isArray(events)) throw new TypeError('Canonical events must be an array.');
  const presentation = buildCanonicalPresentation(events);
  const emittedSourceIndexes = new Set();
  return (presentation.turns ?? [])
    .map(turn => renderTurn(turn, emittedSourceIndexes))
    .join('');
}
