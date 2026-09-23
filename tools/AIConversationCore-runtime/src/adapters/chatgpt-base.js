/**
 * Returns the string-valued text parts from a ChatGPT source record in source order.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Array<string>} String-valued ChatGPT content parts in their source order.
 */
function textParts(record) {
  const parts = record?.content?.parts;
  if (!Array.isArray(parts)) return [];
  return parts.filter(part => typeof part === 'string');
}

/**
 * Builds canonical reasoning-summary blocks from a ChatGPT `thoughts` record.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {Array<Object<string, *>>} Canonical reasoning-summary blocks derived from the ChatGPT thoughts record.
 */
function reasoningBlocks(record, sourceRecordId, sourceIndex) {
  const thoughts = record?.content?.thoughts;
  if (!Array.isArray(thoughts)) return [];

  return thoughts.map((thought, thoughtIndex) => ({
    id: `${sourceRecordId}:thought:${thoughtIndex}`,
    type: 'reasoning_summary',
    summary: thought?.summary ?? null,
    content: thought?.content ?? null,
    chunks: Array.isArray(thought?.chunks) ? [...thought.chunks] : null,
    finished: thought?.finished ?? null,
    source: {
      provider: 'chatgpt',
      record_id: sourceRecordId,
      record_index: sourceIndex,
      thought_index: thoughtIndex
    }
  }));
}

/**
 * Parses a JSON string when valid, otherwise returns no parsed value.
 *
 * @param {string} value - The input value to process.
 * @returns {Object|Array<unknown>|string|number|boolean|null} The parsed JSON value, or `null` when the input is empty or invalid JSON.
 */
function parsedJson(value) {
  if (typeof value !== 'string' || !value.trim()) return null;
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

/**
 * Extracts the normalized executable/launcher token from the start of a persisted command string.
 *
 * @param {string} value - The input value to process.
 * @returns {string|null} The normalized launcher executable name, or `null` when no launcher token can be extracted.
 */
function launcherToken(value) {
  if (typeof value !== 'string') return null;
  const match = value.trimStart().match(/^([A-Za-z0-9_./+-]+)/);
  return match?.[1]?.split('/').at(-1)?.toLowerCase() ?? null;
}

/**
 * Normalizes ChatGPT tool-call presentation without discarding the persisted source form.
 *
 * Source -> canonical transformations:
 * - `api_tool.*` + provider `language: python3` + JSON object text -> unchanged JSON input, `input_format: json`, `language: json`.
 * - `container.exec` + provider `language: unknown` + `bash`/`sh` launcher -> original command text with `bash`/`sh` language.
 * - `container.exec` + provider `language: unknown` + flattened Python `-c` command -> preserve the full persisted command in `source_input`, render only the Python program in `input`, and set `language: python`.
 * The source language is always retained separately as `source_language`.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Object<string, *>} Normalized output-facing tool-call input/format/language together with the exact persisted source input and source language.
 */
function normalizedToolCallPresentation(record) {
  const name = record?.recipient ?? null;
  const sourceInput = record?.content?.text ?? null;
  const sourceLanguage = record?.content?.language ?? null;
  // Canonical tool input starts as the exact provider payload and is replaced only by an evidenced normalization.
  let input = sourceInput;
  // Canonical input format remains unspecified unless the provider payload can be classified safely.
  let inputFormat = 'code';
  // Canonical display language starts from provider metadata and may be corrected from stronger tool semantics.
  let language = sourceLanguage;

  // ChatGPT currently labels api_tool call arguments as python3 even when the
  // payload is a serialized JSON object.  The recipient plus successful JSON
  // parse is stronger semantic evidence than that provider presentation label.
  if (typeof name === 'string' && name.startsWith('api_tool.') && parsedJson(sourceInput) !== null) {
    inputFormat = 'json';
    language = 'json';
  } else if (name === 'container.exec' && typeof sourceInput === 'string') {
    const launcher = launcherToken(sourceInput);
    if (launcher === 'bash') language = 'bash';
    else if (launcher === 'sh') language = 'sh';
    else if (['python', 'python3', 'py'].includes(launcher)) {
      const command = sourceInput.trimStart();
      const pythonCommand = command.match(/^[A-Za-z0-9_./+-]+\s+-c(?:\s+|$)/);
      if (pythonCommand) {
        // The persisted ChatGPT record has already flattened the argv boundary
        // around Python's -c program.  Do not invent shell quoting.  Preserve
        // the source string separately and render the program itself as Python.
        input = command.slice(pythonCommand[0].length);
        language = 'python';
      }
    }
  }

  return { input, inputFormat, language, sourceInput, sourceLanguage };
}

/**
 * Projects a persisted ChatGPT assistant tool-call record into one canonical `tool_call` block.
 *
 * The block carries both the normalized input/language used for output and the original persisted input/language for provenance.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {Array<Object<string, *>>} The single canonical tool-call block array for the source record.
 */
function toolCallBlocks(record, sourceRecordId, sourceIndex) {
  const presentation = normalizedToolCallPresentation(record);
  return [{
    id: `${sourceRecordId}:tool_call:0`,
    type: 'tool_call',
    call_id: null,
    name: record?.recipient ?? null,
    input: presentation.input,
    input_format: presentation.inputFormat,
    language: presentation.language,
    source_input: presentation.sourceInput,
    source_language: presentation.sourceLanguage,
    source: {
      provider: 'chatgpt',
      record_id: sourceRecordId,
      record_index: sourceIndex
    }
  }];
}

/**
 * Projects a persisted ChatGPT tool-role record into one canonical `tool_result` block.
 *
 * Source -> canonical transformations:
 * - `execution_output`/`code` -> text output from the source text/content field.
 * - `text` -> string parts joined in source order with blank lines.
 * - `multimodal_text` -> source parts preserved as an ordered array.
 * The original ChatGPT content type is retained as `output_format`.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {Array<Object<string, *>>} The single canonical tool-result block array for the source record.
 */
function toolResultBlocks(record, sourceRecordId, sourceIndex) {
  const contentType = record?.content?.content_type ?? null;
  let output = null;
  if (contentType === 'execution_output' || contentType === 'code') {
    output = record?.content?.text ?? record?.content?.content ?? null;
  }
  if (contentType === 'text') {
    output = Array.isArray(record?.content?.parts)
      ? record.content.parts.filter(part => typeof part === 'string').join('\n\n')
      : record?.content?.text ?? null;
  }
  if (contentType === 'multimodal_text') output = Array.isArray(record?.content?.parts)
    ? [...record.content.parts]
    : null;

  return [{
    id: `${sourceRecordId}:tool_result:0`,
    type: 'tool_result',
    call_id: null,
    name: record?.author?.name ?? null,
    output,
    output_format: contentType,
    source: {
      provider: 'chatgpt',
      record_id: sourceRecordId,
      record_index: sourceIndex
    }
  }];
}

/**
 * Returns whether a ChatGPT source record is canonically visible or hidden.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {string} The canonical visibility value, `visible` or `hidden`.
 */
function eventVisibility(record) {
  return record?.metadata?.is_visually_hidden_from_conversation ? 'hidden' : 'visible';
}

/**
 * Checks whether tool call.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {boolean} Whether the source record represents a supported ChatGPT tool call.
 */
function isToolCall(record) {
  return record?.author?.role === 'assistant' &&
    record?.content?.content_type === 'code' &&
    typeof record?.recipient === 'string' &&
    record.recipient !== 'all';
}

/**
 * Checks whether tool result.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {boolean} Whether the source record represents a supported ChatGPT tool result.
 */
function isToolResult(record) {
  if (record?.author?.role !== 'tool') return false;
  return ['execution_output', 'multimodal_text', 'text', 'code']
    .includes(record?.content?.content_type);
}

/**
 * Classifies a ChatGPT source record into its canonical event kind.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {string} The canonical event-kind classification for the source record.
 */
function eventKind(record) {
  if (isToolCall(record)) return 'tool_call';
  if (isToolResult(record)) return 'tool_result';
  if (record?.author?.role === 'assistant' && record?.content?.content_type === 'thoughts') {
    return 'reasoning_summary';
  }
  if (record?.author?.role === 'assistant' && record?.channel === 'commentary') return 'commentary';
  return 'message';
}

/**
 * Builds the canonical content blocks for one classified ChatGPT source record.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {string} kind - The canonical kind/category being processed.
 * @returns {Array<Object<string, *>>} Canonical content blocks for the classified ChatGPT source record.
 */
function eventBlocks(record, sourceRecordId, sourceIndex, kind) {
  if (kind === 'reasoning_summary') return reasoningBlocks(record, sourceRecordId, sourceIndex);
  if (kind === 'tool_call') return toolCallBlocks(record, sourceRecordId, sourceIndex);
  if (kind === 'tool_result') return toolResultBlocks(record, sourceRecordId, sourceIndex);

  return textParts(record).map((text, partIndex) => ({
    id: `${sourceRecordId}:part:${partIndex}`,
    type: 'text',
    text,
    source: {
      provider: 'chatgpt',
      record_id: sourceRecordId,
      record_index: sourceIndex,
      part_index: partIndex
    }
  }));
}

/**
 * Normalizes a URL for stable citation/search-result lookup.
 *
 * @param {string} value - The input value to process.
 * @returns {string|null} The normalized URL string, or `null` when the input is not a string.
 */
function normalizedUrl(value) {
  if (typeof value !== 'string') return null;
  try {
    const url = new URL(value);
    url.searchParams.delete('utm_source');
    url.hash = '';
    return url.toString();
  } catch {
    return value;
  }
}

/**
 * Handles search result lookup.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Map<string, Object<string, *>>} Search-result entries indexed by normalized URL.
 */
function searchResultLookup(record) {
  // Lookup maps preserve source reference identity while citations/resources are normalized.
  const lookup = new Map();
  const groups = record?.metadata?.search_result_groups;
  if (!Array.isArray(groups)) return lookup;

  for (const group of groups) {
    if (!Array.isArray(group?.entries)) continue;
    for (const entry of group.entries) {
      const key = normalizedUrl(entry?.url);
      if (key) lookup.set(key, entry);
    }
  }
  return lookup;
}

/**
 * Handles retrieved file lookup.
 *
 * @param {Array<Object<string, *>>} records - The ordered provider/source records to process.
 * @returns {Map<string, Object<string, *>>} Retrieved-file citation metadata indexed by ChatGPT retrieval marker.
 */
function retrievedFileLookup(records) {
  // Lookup maps preserve source reference identity while citations/resources are normalized.
  const lookup = new Map();
  records.forEach((record, recordIndex) => {
    const turn = record?.metadata?.retrieval_turn_number;
    const file = record?.metadata?.retrieval_file_index;
    const citation = record?.metadata?.citation_metadata;
    if (!Number.isInteger(turn) || !Number.isInteger(file) || !citation) return;
    lookup.set(`turn${turn}file${file}`, {
      title: citation?.title ?? null,
      url: citation?.url ?? null,
      source_record_id: record?.id ?? null,
      source_index: recordIndex
    });
  });
  return lookup;
}

/**
 * Handles file marker key.
 *
 * @param {string} matchedText - The literal source text associated with the reference.
 * @returns {string|null} The normalized ChatGPT retrieved-file marker key, or `null` when no marker is present.
 */
function fileMarkerKey(matchedText) {
  if (typeof matchedText !== 'string') return null;
  const match = matchedText.match(/turn(\d+)file(\d+)/);
  return match ? `turn${match[1]}file${match[2]}` : null;
}

/**
 * Locates reference.
 *
 * @param {Array<Object<string, *>>} blocks - The ordered canonical content blocks to process.
 * @param {string} matchedText - The literal source text associated with the reference.
 * @param {number} startPartIndex - The content-part index at which searching begins.
 * @param {number} startOffset - The character offset at which searching begins.
 * @returns {Object<string, number>|null} The matching canonical text range, or null when the literal reference text is absent.
 */
function locateReference(blocks, matchedText, startPartIndex = 0, startOffset = 0) {
  if (typeof matchedText !== 'string' || !matchedText) return null;
  for (let partIndex = startPartIndex; partIndex < blocks.length; partIndex += 1) {
    const block = blocks[partIndex];
    if (block?.type !== 'text' || typeof block.text !== 'string') continue;
    const from = partIndex === startPartIndex ? startOffset : 0;
    const index = block.text.indexOf(matchedText, from);
    if (index >= 0) {
      return {
        part_index: partIndex,
        start: index,
        end: index + matchedText.length
      };
    }
  }
  return null;
}

/**
 * Handles citation base.
 *
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {number} referenceIndex - The zero-based index of the content reference.
 * @param {Object<string, *>} reference - The provider reference object to process.
 * @param {Object<string, number>|null} range - The located text range, or `null` when the reference text was not found.
 * @param {string} kind - The canonical kind/category being processed.
 * @returns {Object<string, *>} Common canonical citation fields and source provenance.
 */
function citationBase(sourceRecordId, sourceIndex, referenceIndex, reference, range, kind) {
  return {
    id: `${sourceRecordId}:citation:${referenceIndex}`,
    type: 'citation',
    citation_kind: kind,
    matched_text: reference?.matched_text ?? null,
    text_range: range,
    source: {
      provider: 'chatgpt',
      record_id: sourceRecordId,
      record_index: sourceIndex,
      reference_index: referenceIndex
    }
  };
}

/**
 * Handles web source.
 *
 * @param {Object<string, *>} item - The provider/search-result item being converted to a canonical source descriptor.
 * @param {Map<string, Object<string, *>>} lookup - The lookup table used to resolve related source data.
 * @returns {Object<string, *>} A canonical web-source descriptor with any normalized supporting-source evidence.
 */
function webSource(item, lookup) {
  const source = {
    url: item?.url ?? null,
    title: item?.title ?? null,
    attribution: item?.attribution ?? null,
    snippet: item?.snippet ?? null,
    supporting_sources: []
  };

  if (Array.isArray(item?.supporting_websites)) {
    source.supporting_sources = item.supporting_websites.map(site => {
      const evidence = lookup.get(normalizedUrl(site?.url));
      return {
        url: site?.url ?? null,
        title: evidence?.title ?? null,
        attribution: evidence?.attribution ?? null,
        snippet: evidence?.snippet ?? null
      };
    });
  }

  return source;
}

/**
 * Normalizes citation.
 *
 * @param {Object<string, *>} reference - The provider reference object to process.
 * @param {Object<string, *>} context - The contextual source/provenance values required by the operation.
 * @returns {Object<string, *>|null} The canonical citation for a supported provider reference, or null for an unsupported reference shape.
 */
function normalizeCitation(reference, context) {
  const {
    sourceRecordId,
    sourceIndex,
    referenceIndex,
    range,
    record,
    retrievedFiles
  } = context;

  if (reference?.type === 'file') {
    return {
      ...citationBase(sourceRecordId, sourceIndex, referenceIndex, reference, range, 'file'),
      file: {
        id: reference?.id ?? null,
        name: reference?.name ?? null,
        source: reference?.source ?? null,
        snippet: reference?.snippet ?? null
      }
    };
  }

  if (reference?.type === 'grouped_webpages') {
    const lookup = searchResultLookup(record);
    return {
      ...citationBase(sourceRecordId, sourceIndex, referenceIndex, reference, range, 'web'),
      web: {
        sources: Array.isArray(reference?.items)
          ? reference.items.map(item => webSource(item, lookup))
          : [],
        safe_urls: Array.isArray(reference?.safe_urls) ? [...reference.safe_urls] : []
      }
    };
  }

  if (reference?.type === 'hidden' && reference?.invalid === false &&
      reference?.matched_text === 'memcite') {
    const metadata = record?.metadata?.conversation_context_citation_metadata;
    return {
      ...citationBase(sourceRecordId, sourceIndex, referenceIndex, reference, range, 'memory'),
      memory: {
        sources: Array.isArray(metadata) ? metadata.map(entry => ({
          citation_uuid: entry?.citation_uuid ?? null,
          deleted: entry?.deleted ?? null,
          retrieval_origin: entry?.retrieval_origin ?? null,
          title: entry?.citation?.title ?? null,
          url: entry?.citation?.url ?? null,
          snippet: entry?.citation?.snippet ?? null,
          attribution: entry?.citation?.attribution ?? null,
          category: entry?.citation?.category ?? null
        })) : []
      }
    };
  }

  if (reference?.type === 'hidden' && reference?.invalid === true) {
    const key = fileMarkerKey(reference?.matched_text);
    const resolved = key ? retrievedFiles.get(key) : null;
    return {
      ...citationBase(sourceRecordId, sourceIndex, referenceIndex, reference, range, 'retrieved_file'),
      retrieved_file: {
        resolved: Boolean(resolved),
        title: resolved?.title ?? null,
        url: resolved?.url ?? null,
        source_record_id: resolved?.source_record_id ?? null,
        source_index: resolved?.source_index ?? null
      }
    };
  }

  return null;
}

/**
 * Handles event citations.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {Array<Object<string, *>>} blocks - The ordered canonical content blocks to process.
 * @param {Map<string, Object<string, *>>} retrievedFiles - The retrieved-file lookup keyed by ChatGPT file marker.
 * @returns {Array<Object<string, *>>} Canonical citations associated with the source record in source-reference order.
 */
function eventCitations(record, sourceRecordId, sourceIndex, blocks, retrievedFiles) {
  const references = record?.metadata?.content_references;
  if (!Array.isArray(references)) return [];

  // Canonical citations are accumulated in source-reference order.
  const citations = [];
  // Source text-part cursor used to continue citation matching after the previous reference.
  let partIndex = 0;
  // Character offset within the current source text part for the next citation search.
  let offset = 0;

  references.forEach((reference, referenceIndex) => {
    const range = locateReference(blocks, reference?.matched_text, partIndex, offset);
    if (range) {
      partIndex = range.part_index;
      offset = range.end;
    }

    const citation = normalizeCitation(reference, {
      sourceRecordId,
      sourceIndex,
      referenceIndex,
      range,
      record,
      retrievedFiles
    });
    if (citation) citations.push(citation);
  });

  return citations;
}

/**
 * Handles conversation ID.
 *
 * @param {Array<Object<string, *>>} records - The ordered provider/source records to process.
 * @returns {string|null} The ChatGPT conversation identifier from the metadata record, or null when metadata is absent.
 */
function conversationId(records) {
  const metadata = records.find(record =>
    record?.record_type === 'chatgpt_conversation_metadata' &&
    typeof record?.conversation_id === 'string'
  );
  return metadata?.conversation_id ?? null;
}

/**
 * Checks whether conversation metadata.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {boolean} Whether the isConversationMetadata condition is satisfied.
 */
function isConversationMetadata(record) {
  return record?.record_type === 'chatgpt_conversation_metadata';
}

/**
 * Handles basename.
 *
 * @param {string} path - The provider/sandbox path being reduced to its final path component.
 * @returns {string|null} The last non-empty path component, or null when the path has no component.
 */
function basename(path) {
  if (typeof path !== 'string') return null;
  const pieces = path.split('/').filter(Boolean);
  return pieces.length ? pieces[pieces.length - 1] : null;
}

/**
 * Handles sandbox path.
 *
 * @param {string} pointer - The provider sandbox pointer being converted to a /mnt/data-style path.
 * @returns {string|null} The /mnt/data-style path represented by a supported sandbox pointer, or null for unsupported pointer forms.
 */
function sandboxPath(pointer) {
  if (typeof pointer !== 'string') return null;
  const value = pointer.trim();
  if (!value.startsWith('sandbox:/') || value.startsWith('sandbox://')) return null;
  return value.slice('sandbox:'.length);
}

/**
 * Handles sandbox download URL.
 *
 * @param {string} path - The provider/sandbox path being reduced to its final path component.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {string} chatgptConversationId - The chatgpt conversation id.
 * @returns {string|null} The authenticated ChatGPT generated-file download URL, or null when required identity is unavailable.
 */
function sandboxDownloadUrl(path, sourceRecordId, chatgptConversationId) {
  if (!path || !sourceRecordId || !chatgptConversationId) return null;
  const conversation = encodeURIComponent(chatgptConversationId);
  const message = encodeURIComponent(sourceRecordId);
  const sandbox = encodeURIComponent(path);
  return (
    `https://chatgpt.com/backend-api/conversation/${conversation}/` +
    `interpreter/download?message_id=${message}&sandbox_path=${sandbox}` +
    '&download_intent=true'
  );
}

/**
 * Handles sandbox links.
 *
 * @param {string} text - The text value to process.
 * @returns {Array<Object<string, *>>} Generated sandbox-link descriptors in source-text order, including label, pointer, and character range.
 */
function sandboxLinks(text) {
  if (typeof text !== 'string' || !text) return [];
  const links = [];
  // Offset of the next source character not yet copied while rewriting generated sandbox links.
  let cursor = 0;

  while (cursor < text.length) {
    const destinationMarker = text.indexOf('](', cursor);
    if (destinationMarker < 0) break;

    const labelStart = text.lastIndexOf('[', destinationMarker);
    if (labelStart < 0) {
      cursor = destinationMarker + 2;
      continue;
    }

    const destinationStart = destinationMarker + 2;
    if (!text.startsWith('sandbox:/', destinationStart) ||
        text.startsWith('sandbox://', destinationStart)) {
      cursor = destinationStart;
      continue;
    }

    let depth = 0;
    let destinationEnd = -1;
    for (let index = destinationStart; index < text.length; index += 1) {
      const char = text[index];
      if (char === '(') {
        depth += 1;
      } else if (char === ')') {
        if (depth === 0) {
          destinationEnd = index;
          break;
        }
        depth -= 1;
      }
    }

    if (destinationEnd < 0) break;

    links.push({
      label: text.slice(labelStart + 1, destinationMarker),
      source_pointer: text.slice(destinationStart, destinationEnd),
      start: labelStart,
      end: destinationEnd + 1
    });
    cursor = destinationEnd + 1;
  }

  return links;
}

/**
 * Handles citation resources.
 *
 * @param {Array<Object<string, *>>} citations - The canonical citations associated with the event, in source order.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @returns {Array<Object<string, *>>} Canonical file resources derived from supported citation objects in citation order.
 */
function citationResources(citations, sourceRecordId, sourceIndex) {
  // Canonical resources are accumulated without changing source encounter order.
  const resources = [];

  for (const citation of citations) {
    if (citation?.citation_kind === 'file') {
      const resourceId = `${sourceRecordId}:resource:citation:${citation.source.reference_index}`;
      citation.resource_id = resourceId;
      resources.push({
        id: resourceId,
        type: 'file',
        resource_kind: 'attachment',
        name: citation.file?.name ?? null,
        provider_file_id: citation.file?.id ?? null,
        provider_source: citation.file?.source ?? null,
        snippet: citation.file?.snippet ?? null,
        source: {
          provider: 'chatgpt',
          record_id: sourceRecordId,
          record_index: sourceIndex,
          reference_index: citation.source.reference_index
        }
      });
    }

    if (citation?.citation_kind === 'retrieved_file' && citation.retrieved_file?.resolved) {
      const resourceId = `${sourceRecordId}:resource:citation:${citation.source.reference_index}`;
      citation.resource_id = resourceId;
      resources.push({
        id: resourceId,
        type: 'file',
        resource_kind: 'retrieved_file',
        name: citation.retrieved_file?.title ?? null,
        source_url: citation.retrieved_file?.url ?? null,
        source_record_id: citation.retrieved_file?.source_record_id ?? null,
        source: {
          provider: 'chatgpt',
          record_id: sourceRecordId,
          record_index: sourceIndex,
          reference_index: citation.source.reference_index
        }
      });
    }
  }

  return resources;
}

/**
 * Handles sandbox resources.
 *
 * @param {Array<Object<string, *>>} blocks - The ordered canonical content blocks to process.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {string} chatgptConversationId - The chatgpt conversation id.
 * @returns {Array<Object<string, *>>} Canonical generated-file resources derived from sandbox links in block/source order.
 */
function sandboxResources(blocks, sourceRecordId, sourceIndex, chatgptConversationId) {
  // Canonical resources are accumulated without changing source encounter order.
  const resources = [];
  // Stable per-event resource ordinal used to construct canonical resource identifiers.
  let resourceIndex = 0;

  blocks.forEach((block, partIndex) => {
    if (block?.type !== 'text') return;
    for (const link of sandboxLinks(block.text)) {
      const path = sandboxPath(link.source_pointer);
      if (!path) continue;
      resources.push({
        id: `${sourceRecordId}:resource:sandbox:${resourceIndex}`,
        type: 'artifact',
        resource_kind: 'generated_file',
        name: basename(path),
        label: link.label,
        source_pointer: link.source_pointer,
        path,
        download_url: sandboxDownloadUrl(path, sourceRecordId, chatgptConversationId),
        text_range: {
          part_index: partIndex,
          start: link.start,
          end: link.end
        },
        resolution_context: {
          provider: 'chatgpt',
          conversation_id: chatgptConversationId,
          message_id: sourceRecordId
        },
        source: {
          provider: 'chatgpt',
          record_id: sourceRecordId,
          record_index: sourceIndex,
          part_index: partIndex
        }
      });
      resourceIndex += 1;
    }
  });

  return resources;
}

/**
 * Handles event resources.
 *
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {Array<Object<string, *>>} blocks - The ordered canonical content blocks to process.
 * @param {Array<Object<string, *>>} citations - The canonical citations associated with the event, in source order.
 * @param {string} chatgptConversationId - The chatgpt conversation id.
 * @returns {Array<Object<string, *>>} Canonical citation and generated-file resources for the source event.
 */
function eventResources(record, sourceRecordId, sourceIndex, blocks, citations,
                        chatgptConversationId) {
  return [
    ...citationResources(citations, sourceRecordId, sourceIndex),
    ...sandboxResources(blocks, sourceRecordId, sourceIndex, chatgptConversationId)
  ];
}

/**
 * Adapts ordered ChatGPT provider records into ordered canonical events while preserving source identity and provenance.
 *
 * @param {Array<Object<string, *>>} records - The ordered provider/source records to process.
 * @returns {Array<Object<string, *>>} Canonical events derived from ChatGPT source records while preserving source order and provenance.
 */
export function adaptChatGPTRecords(records) {
  if (!Array.isArray(records)) throw new TypeError('ChatGPT records must be an array.');

  const chatgptConversationId = conversationId(records);
  const retrievedFiles = retrievedFileLookup(records);
  // Metadata records are excluded here so source_index continues to refer only to provider conversation records.
  const sourceRecords = records
    .map((record, sourceIndex) => ({ record, sourceIndex }))
    .filter(({ record }) => !isConversationMetadata(record));

  return sourceRecords.map(({ record, sourceIndex }) => {
    const sourceRecordId = typeof record?.id === 'string' ? record.id : null;
    if (!sourceRecordId) throw new Error(`ChatGPT source record at index ${sourceIndex} is missing id.`);

    const role = record?.author?.role ?? null;
    const channel = record?.channel ?? null;
    const contentType = record?.content?.content_type ?? null;
    const kind = eventKind(record);
    const blocks = eventBlocks(record, sourceRecordId, sourceIndex, kind);
    const citations = eventCitations(record, sourceRecordId, sourceIndex, blocks, retrievedFiles);
    const resources = eventResources(
      record,
      sourceRecordId,
      sourceIndex,
      blocks,
      citations,
      chatgptConversationId
    );

    return {
      id: `chatgpt:${sourceRecordId}`,
      provider: 'chatgpt',
      source_record_id: sourceRecordId,
      source_index: sourceIndex,
      kind,
      role,
      channel,
      visibility: eventVisibility(record),
      content_type: contentType,
      blocks,
      citations,
      resources,
      relationships: {
        turn_exchange_id: record?.metadata?.turn_exchange_id ?? null,
        working_turn_id: record?.metadata?.working_turn_id ?? null,
        tool_call_id: null
      },
      source: {
        provider: 'chatgpt',
        record_id: sourceRecordId,
        record_index: sourceIndex
      }
    };
  });
}
