/** HTML elements that never contain a matching closing tag. */
const WORD_ELEMENT_VOID_TAGS = new Set([
  'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta',
  'param', 'source', 'track', 'wbr'
]);

/**
 * Finds the exclusive end of one Core-generated HTML tag or comment.
 *
 * @param {string} html - Complete canonical HTML fragment.
 * @param {number} start - Offset of the opening `<`.
 * @returns {number} Exclusive end offset of the tag or comment.
 */
function tagEnd(html, start) {
  if (html.startsWith('<!--', start)) {
    const end = html.indexOf('-->', start + 4);
    if (end < 0) throw new TypeError('Canonical HTML contains an unterminated comment.');
    return end + 3;
  }

  let quote = '';
  for (let index = start + 1; index < html.length; ++index) {
    const character = html[index];
    if (quote) {
      if (character === quote) quote = '';
      continue;
    }
    if (character === '"' || character === "'") {
      quote = character;
      continue;
    }
    if (character === '>') return index + 1;
  }
  throw new TypeError('Canonical HTML contains an unterminated tag.');
}

/**
 * Parses the structural fields needed from one Core-generated HTML tag.
 *
 * @param {string} raw - Complete raw HTML tag.
 * @returns {Object<string, *>} Parsed tag metadata.
 */
function parseTag(raw) {
  if (raw.startsWith('<!--') || raw.startsWith('<!') || raw.startsWith('<?')) {
    return { name: '', closing: false, selfClosing: true };
  }
  const match = raw.match(/^<\s*(\/?)\s*([A-Za-z][\w:-]*)\b([\s\S]*?)>$/);
  if (!match) throw new TypeError(`Unsupported canonical HTML tag: ${raw.slice(0, 80)}`);
  const name = match[2].toLowerCase();
  const attributes = match[3] ?? '';
  return {
    name,
    closing: match[1] === '/',
    selfClosing: WORD_ELEMENT_VOID_TAGS.has(name) || /\/\s*$/.test(attributes)
  };
}

/**
 * Returns a canonical word-fragment ID carried by one opening span tag.
 *
 * @param {string} raw - Raw opening tag.
 * @returns {number|null} Numeric word ID, or null when the span is not a fragment.
 */
function fragmentWordId(raw) {
  if (!/^<\s*span\b/i.test(raw)) return null;
  const match = raw.match(/\b(?:id="word-|data-word-id=")([0-9]+)"/i);
  if (!match) return null;
  const id = Number.parseInt(match[1], 10);
  return Number.isSafeInteger(id) && id > 0 ? id : null;
}

/**
 * Parses Core-generated HTML into the minimal tree needed for word restructuring.
 *
 * @param {string} html - Annotated canonical HTML.
 * @returns {Object<string, *>} Root tree node preserving original tag spellings.
 */
function parseHtml(html) {
  const root = { type: 'root', children: [] };
  const stack = [root];
  let cursor = 0;

  while (cursor < html.length) {
    if (html[cursor] !== '<') {
      const next = html.indexOf('<', cursor);
      const end = next < 0 ? html.length : next;
      stack.at(-1).children.push({ type: 'text', raw: html.slice(cursor, end) });
      cursor = end;
      continue;
    }

    const end = tagEnd(html, cursor);
    const raw = html.slice(cursor, end);
    const tag = parseTag(raw);
    if (!tag.name) {
      stack.at(-1).children.push({ type: 'raw', raw });
      cursor = end;
      continue;
    }

    if (tag.closing) {
      const current = stack.at(-1);
      if (current?.type !== 'element' || current.name !== tag.name) {
        throw new TypeError(`Canonical HTML closes unexpected <${tag.name}>.`);
      }
      current.closeRaw = raw;
      stack.pop();
      cursor = end;
      continue;
    }

    if (tag.selfClosing) {
      stack.at(-1).children.push({ type: 'raw', raw });
      cursor = end;
      continue;
    }

    const element = {
      type: 'element',
      name: tag.name,
      openRaw: raw,
      closeRaw: '',
      wordFragmentId: fragmentWordId(raw),
      children: []
    };
    stack.at(-1).children.push(element);
    stack.push(element);
    cursor = end;
  }

  if (stack.length !== 1) {
    throw new TypeError(`Canonical HTML leaves <${stack.at(-1).name}> unclosed.`);
  }
  return root;
}

/**
 * Serializes a parsed canonical HTML node without changing untouched markup.
 *
 * @param {Object<string, *>} node - Parsed HTML node.
 * @returns {string} Serialized HTML.
 */
function serialize(node) {
  if (node.type === 'text' || node.type === 'raw') return node.raw;
  if (node.type === 'root') return node.children.map(serialize).join('');
  return node.openRaw + node.children.map(serialize).join('') + node.closeRaw;
}

/**
 * Counts the current fragment wrappers for each canonical word ID.
 *
 * @param {Object<string, *>} node - Parsed HTML subtree.
 * @param {Map<number, number>} counts - Mutable fragment counts by word ID.
 * @returns {void} Counts are accumulated into `counts`.
 */
function countFragments(node, counts) {
  if (node.type === 'element' && node.wordFragmentId != null) {
    counts.set(node.wordFragmentId, (counts.get(node.wordFragmentId) ?? 0) + 1);
  }
  for (const child of node.children ?? []) countFragments(child, counts);
}

/**
 * Finds root-to-fragment paths for one canonical word ID.
 *
 * @param {Object<string, *>} node - Current parsed HTML node.
 * @param {number} wordId - Canonical word ID to locate.
 * @param {Array<Object<string, *>>} path - Current root-to-node path.
 * @param {Array<Array<Object<string, *>>>} matches - Mutable matching paths.
 * @returns {void} Matching paths are appended to `matches`.
 */
function findFragmentPaths(node, wordId, path, matches) {
  const nextPath = [...path, node];
  if (node.type === 'element' && node.wordFragmentId === wordId) {
    matches.push(nextPath);
  }
  for (const child of node.children ?? []) {
    findFragmentPaths(child, wordId, nextPath, matches);
  }
}

/**
 * Returns the lowest common ancestor shared by all supplied node paths.
 *
 * @param {Array<Array<Object<string, *>>>} paths - Root-to-node paths.
 * @returns {Object<string, *>} Lowest common ancestor node.
 */
function commonAncestor(paths) {
  if (!paths.length) throw new TypeError('Canonical word has no HTML fragments.');
  let ancestor = paths[0][0];
  const limit = Math.min(...paths.map(path => path.length));
  for (let index = 0; index < limit; ++index) {
    const candidate = paths[0][index];
    if (!paths.every(path => path[index] === candidate)) break;
    ancestor = candidate;
  }
  return ancestor;
}

/**
 * Returns whether a subtree contains a fragment for one canonical word ID.
 *
 * @param {Object<string, *>} node - Parsed HTML subtree.
 * @param {number} wordId - Canonical word ID to locate.
 * @returns {boolean} Whether the subtree contains the requested fragment.
 */
function containsFragment(node, wordId) {
  if (node.type === 'element' && node.wordFragmentId === wordId) return true;
  return (node.children ?? []).some(child => containsFragment(child, wordId));
}

/**
 * Clones one formatting/container element around a selected child slice.
 *
 * Fragment spans for the target word are stripped before this function is used;
 * other elements preserve their exact original opening and closing tags.
 *
 * @param {Object<string, *>} node - Original element node.
 * @param {Array<Object<string, *>>} children - Child slice to wrap.
 * @returns {Object<string, *>} Cloned element containing `children`.
 */
function cloneElement(node, children) {
  return {
    ...node,
    children
  };
}

/**
 * Splits one element around the contiguous fragments of a canonical word.
 *
 * Formatting ancestors are cloned only where the selected word cuts through
 * them. This lets Core move one word span outside its inline formatting while
 * preserving formatting on neighbouring text.
 *
 * @param {Object<string, *>} node - Element containing a target fragment.
 * @param {number} wordId - Canonical word ID being restructured.
 * @returns {Object<string, Array<Object<string, *>>>} Before/selected/after slices.
 */
function splitElement(node, wordId) {
  if (node.wordFragmentId === wordId) {
    return { before: [], selected: node.children, after: [] };
  }

  const children = node.children ?? [];
  const targetIndexes = [];
  for (let index = 0; index < children.length; ++index) {
    if (containsFragment(children[index], wordId)) targetIndexes.push(index);
  }
  if (!targetIndexes.length) {
    throw new TypeError(`Canonical word ${wordId} is absent from requested subtree.`);
  }

  const first = targetIndexes[0];
  const last = targetIndexes.at(-1);
  const beforeChildren = [...children.slice(0, first)];
  const selectedChildren = [];
  const afterChildren = [...children.slice(last + 1)];

  for (let index = first; index <= last; ++index) {
    const child = children[index];
    if (!containsFragment(child, wordId)) {
      selectedChildren.push(child);
      continue;
    }
    if (child.type !== 'element') {
      throw new TypeError(`Canonical word ${wordId} fragment is not an element.`);
    }
    const split = splitElement(child, wordId);
    if (index === first) beforeChildren.push(...split.before);
    else if (split.before.length) {
      throw new TypeError(`Canonical word ${wordId} has noncontiguous rendered content.`);
    }
    selectedChildren.push(...split.selected);
    if (index === last) afterChildren.unshift(...split.after);
    else if (split.after.length) {
      throw new TypeError(`Canonical word ${wordId} has noncontiguous rendered content.`);
    }
  }

  const before = beforeChildren.length ? [cloneElement(node, beforeChildren)] : [];
  const selected = selectedChildren.length ? [cloneElement(node, selectedChildren)] : [];
  const after = afterChildren.length ? [cloneElement(node, afterChildren)] : [];
  return { before, selected, after };
}

/**
 * Replaces fragmented wrappers for one word with one enclosing canonical span.
 *
 * @param {Object<string, *>} root - Parsed canonical HTML root.
 * @param {number} wordId - Canonical word ID to collapse.
 * @returns {void} The parsed tree is restructured in place.
 */
function collapseWord(root, wordId) {
  const paths = [];
  findFragmentPaths(root, wordId, [], paths);
  if (paths.length < 2) return;
  const ancestor = commonAncestor(paths);
  if (!Array.isArray(ancestor.children)) {
    throw new TypeError(`Canonical word ${wordId} has no common container.`);
  }

  const targetIndexes = [];
  for (let index = 0; index < ancestor.children.length; ++index) {
    if (containsFragment(ancestor.children[index], wordId)) targetIndexes.push(index);
  }
  const first = targetIndexes[0];
  const last = targetIndexes.at(-1);
  const before = [...ancestor.children.slice(0, first)];
  const selected = [];
  const after = [...ancestor.children.slice(last + 1)];

  for (let index = first; index <= last; ++index) {
    const child = ancestor.children[index];
    if (!containsFragment(child, wordId)) {
      selected.push(child);
      continue;
    }
    if (child.type !== 'element') {
      throw new TypeError(`Canonical word ${wordId} fragment is not an element.`);
    }
    const split = splitElement(child, wordId);
    if (index === first) before.push(...split.before);
    else if (split.before.length) {
      throw new TypeError(`Canonical word ${wordId} has noncontiguous rendered content.`);
    }
    selected.push(...split.selected);
    if (index === last) after.unshift(...split.after);
    else if (split.after.length) {
      throw new TypeError(`Canonical word ${wordId} has noncontiguous rendered content.`);
    }
  }

  const wordSpan = {
    type: 'element',
    name: 'span',
    openRaw: `<span id="word-${wordId}">`,
    closeRaw: '</span>',
    wordFragmentId: wordId,
    children: selected
  };
  ancestor.children = [...before, wordSpan, ...after];
}

/**
 * Converts internal per-text-piece word markers into one DOM element per word.
 *
 * `annotateCanonicalHtmlWords()` identifies canonical token text exactly once and
 * may temporarily mark multiple raw text pieces when inline Markdown separates
 * them. This Core-owned restructuring step removes that serialization detail from
 * the public HTML contract: every canonical word leaves Core as exactly one DOM
 * word element. Ordinary textual words use `<span id="word-N">...</span>`; an
 * ordered-list ordinal uses its canonical `<li id="word-N">` because that
 * structural element is the visible/highlightable word object. Applicable inline
 * formatting remains nested inside ordinary word spans. Consumers never repair or
 * reconstruct word identity.
 *
 * @param {string} html - Core-annotated canonical HTML.
 * @returns {string} Canonical HTML with one DOM element per canonical word.
 */
export function collapseCanonicalWordFragments(html) {
  const root = parseHtml(String(html ?? ''));
  const counts = new Map();
  countFragments(root, counts);
  const fragmentedIds = [...counts.entries()]
    .filter(([, count]) => count > 1)
    .map(([wordId]) => wordId)
    .sort((left, right) => left - right);
  for (const wordId of fragmentedIds) collapseWord(root, wordId);
  return serialize(root);
}
