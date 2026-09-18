import { adaptChatGPTRecords as adaptBaseChatGPTRecords } from './chatgpt-base.js';

/**
 * Returns the first usable image asset pointer exposed by a ChatGPT multimodal part.
 *
 * @param {Object<string, *>} part - The ChatGPT multimodal source part being normalized.
 * @returns {string|null} The first usable image asset pointer exposed by the multimodal part, or null when none is present.
 */
function imagePointerSource(part) {
  if (!part || typeof part !== 'object') return null;
  const metadata = part?.metadata && typeof part.metadata === 'object' ? part.metadata : {};
  for (const value of [metadata.asset_pointer_link, part.asset_pointer_link, part.asset_pointer]) {
    if (typeof value === 'string' && value.trim()) return value.trim();
  }
  return null;
}

/**
 * Converts a ChatGPT `sediment://file_*` pointer into its authenticated download URL.
 *
 * @param {string} source - The source descriptor or provider pointer being normalized or rendered.
 * @returns {string|null} The authenticated ChatGPT file-download URL for a sediment file pointer, or null for another pointer form.
 */
function sedimentDownloadUrl(source) {
  if (typeof source !== 'string' || !source.startsWith('sediment://')) return null;
  const assetId = source.slice('sediment://'.length).split(/[?#]/, 1)[0];
  if (!assetId.startsWith('file_')) return null;
  return `https://chatgpt.com/backend-api/files/download/${encodeURIComponent(assetId)}`;
}

/**
 * Builds the canonical conversation-image resource for one ChatGPT image-pointer part.
 *
 * @param {Object<string, *>} part - The ChatGPT multimodal source part being normalized.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @param {number} sourceIndex - The zero-based index of the source record.
 * @param {number} partIndex - The zero-based content-part index.
 * @returns {Object<string, *>} The canonical conversation-image resource corresponding to the source image-pointer part.
 */
function imageResource(part, sourceRecordId, sourceIndex, partIndex) {
  const sourcePointer = imagePointerSource(part);
  const resource = {
    id: `${sourceRecordId}:resource:image:${partIndex}`,
    type: 'image',
    resource_kind: 'conversation_image',
    source: {
      provider: 'chatgpt',
      record_id: sourceRecordId,
      record_index: sourceIndex,
      part_index: partIndex
    }
  };

  if (sourcePointer) resource.source_pointer = sourcePointer;
  if (Number.isFinite(part?.size_bytes)) resource.size_bytes = part.size_bytes;
  if (Number.isFinite(part?.width)) resource.width = part.width;
  if (Number.isFinite(part?.height)) resource.height = part.height;

  if (!sourcePointer) {
    resource.status = 'missing';
    return resource;
  }

  if (sourcePointer.startsWith('data:image/')) {
    resource.status = 'available';
    resource.data_url = sourcePointer;
    return resource;
  }

  const downloadUrl = sedimentDownloadUrl(sourcePointer);
  if (downloadUrl) resource.download_url = downloadUrl;
  return resource;
}

/**
 * Remaps text range.
 *
 * @param {Object<string, *>|null} range - The located text range, or `null` when the reference text was not found.
 * @param {Map<number, number>} textOrdinalToPartIndex - The zero-based text ordinal to part index.
 * @returns {Object<string, *>|null} The supplied text range with its text-only ordinal remapped to the original multimodal part index, or the original null/range when no remap applies.
 */
function remapTextRange(range, textOrdinalToPartIndex) {
  if (!range || !Number.isInteger(range.part_index)) return range;
  const partIndex = textOrdinalToPartIndex.get(range.part_index);
  if (!Number.isInteger(partIndex)) return range;
  return { ...range, part_index: partIndex };
}

/**
 * Remaps citation.
 *
 * @param {Object<string, *>} citation - The canonical citation being rendered or remapped.
 * @param {Map<number, number>} textOrdinalToPartIndex - The zero-based text ordinal to part index.
 * @returns {Object<string, *>} A copy of the canonical citation whose text range uses the original multimodal part index.
 */
function remapCitation(citation, textOrdinalToPartIndex) {
  if (!citation || typeof citation !== 'object') return citation;
  return {
    ...citation,
    text_range: remapTextRange(citation.text_range, textOrdinalToPartIndex)
  };
}

/**
 * Remaps display replacement.
 *
 * @param {Object<string, *>} replacement - The canonical display-replacement object whose part indexes are being remapped.
 * @param {Map<number, number>} textOrdinalToPartIndex - The zero-based text ordinal to part index.
 * @returns {Object<string, *>} A copy of the display replacement whose text range uses the original multimodal part index.
 */
function remapDisplayReplacement(replacement, textOrdinalToPartIndex) {
  if (!replacement || typeof replacement !== 'object') return replacement;
  return {
    ...replacement,
    text_range: remapTextRange(replacement.text_range, textOrdinalToPartIndex)
  };
}

/**
 * Remaps existing resource.
 *
 * @param {Object<string, *>} resource - The canonical resource whose source/text part indexes are being remapped.
 * @param {Map<number, number>} textOrdinalToPartIndex - The zero-based text ordinal to part index.
 * @returns {Object<string, *>} A copy of the canonical resource with text/source part indexes remapped to original multimodal indexes when applicable.
 */
function remapExistingResource(resource, textOrdinalToPartIndex) {
  if (!resource || typeof resource !== 'object') return resource;
  const remapped = { ...resource };
  if (resource.text_range) {
    remapped.text_range = remapTextRange(resource.text_range, textOrdinalToPartIndex);
  }
  if (resource.source && Number.isInteger(resource.source.part_index)) {
    const partIndex = textOrdinalToPartIndex.get(resource.source.part_index);
    if (Number.isInteger(partIndex)) {
      remapped.source = { ...resource.source, part_index: partIndex };
    }
  }
  return remapped;
}

/**
 * Normalizes multimodal images.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Object<string, *>} The canonical event with source-order image blocks/resources inserted, or the original event when no image pointers are present.
 */
function normalizeMultimodalImages(event, record) {
  const parts = record?.content?.parts;
  if (!Array.isArray(parts)) return event;
  if (!parts.some(part => part?.content_type === 'image_asset_pointer')) return event;

  const blocks = [];
  const images = [];
  // Maps text-only ordinals used by ChatGPT references back to original multimodal part indexes.
  const textOrdinalToPartIndex = new Map();
  // Counts only textual parts while walking multimodal source content.
  let textOrdinal = 0;

  parts.forEach((part, partIndex) => {
    if (typeof part === 'string') {
      textOrdinalToPartIndex.set(textOrdinal, partIndex);
      textOrdinal += 1;
      blocks.push({
        id: `${event.source_record_id}:part:${partIndex}`,
        type: 'text',
        text: part,
        source: {
          provider: 'chatgpt',
          record_id: event.source_record_id,
          record_index: event.source_index,
          part_index: partIndex
        }
      });
      return;
    }

    if (part?.content_type !== 'image_asset_pointer') return;
    const resource = imageResource(part, event.source_record_id, event.source_index, partIndex);
    images.push(resource);
    blocks.push({
      id: `${event.source_record_id}:part:${partIndex}`,
      type: 'image',
      resource_id: resource.id,
      source: {
        provider: 'chatgpt',
        record_id: event.source_record_id,
        record_index: event.source_index,
        part_index: partIndex
      }
    });
  });

  return {
    ...event,
    blocks,
    citations: event.citations.map(citation => remapCitation(citation, textOrdinalToPartIndex)),
    display_replacements: (event.display_replacements ?? [])
      .map(replacement => remapDisplayReplacement(replacement, textOrdinalToPartIndex)),
    resources: [
      ...event.resources.map(resource => remapExistingResource(resource, textOrdinalToPartIndex)),
      ...images
    ]
  };
}

/**
 * Builds canonical ChatGPT source provenance for a derived event/block object.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} extra - Additional source-provenance fields to merge with the event source object.
 * @returns {Object<string, *>} Canonical ChatGPT source provenance extended with any supplied block/reference fields.
 */
function sourceFor(event, extra = {}) {
  return {
    provider: 'chatgpt',
    record_id: event.source_record_id,
    record_index: event.source_index,
    ...extra
  };
}

/**
 * Locates text range.
 *
 * @param {Array<Object<string, *>>} blocks - The ordered canonical content blocks to process.
 * @param {string} matchedText - The literal source text associated with the reference.
 * @param {number} startPartIndex - The content-part index at which searching begins.
 * @param {number} startOffset - The character offset at which searching begins.
 * @returns {Object<string, number>|null} The located text range within canonical blocks, or null when the literal text is absent.
 */
function locateTextRange(blocks, matchedText, startPartIndex = 0, startOffset = 0) {
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
 * Normalizes display replacements.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Object<string, *>} The canonical event with normalized alt-text display replacements appended, or the original event when none apply.
 */
function normalizeDisplayReplacements(event, record) {
  const references = record?.metadata?.content_references;
  if (!Array.isArray(references)) return event;

  const replacements = [];
  // Canonical text-block cursor used to place display replacements in source order.
  let partIndex = 0;
  // Character offset within the current canonical text block for the next reference match.
  let offset = 0;

  references.forEach((reference, referenceIndex) => {
    if (reference?.type !== 'alt_text') return;
    const matchedText = typeof reference?.matched_text === 'string'
      ? reference.matched_text
      : null;
    const displayText = typeof reference?.alt === 'string'
      ? reference.alt
      : null;
    if (!matchedText || displayText == null) return;

    const range = locateTextRange(event.blocks, matchedText, partIndex, offset);
    if (range) {
      partIndex = range.part_index;
      offset = range.end;
    }

    replacements.push({
      id: `${event.source_record_id}:display_replacement:${referenceIndex}`,
      type: 'display_replacement',
      replacement_kind: 'alt_text',
      matched_text: matchedText,
      display_text: displayText,
      prompt_text: typeof reference?.prompt_text === 'string' ? reference.prompt_text : null,
      text_range: range,
      source: sourceFor(event, { reference_index: referenceIndex })
    });
  });

  if (!replacements.length) return event;
  return {
    ...event,
    display_replacements: [
      ...(Array.isArray(event.display_replacements) ? event.display_replacements : []),
      ...replacements
    ]
  };
}

/**
 * Normalizes tether assets.
 *
 * @param {Object<string, *>|Array<Object<string, *>>|null} assets - The provider tether-browsing asset object/array, or null when absent.
 * @returns {Array<Object<string, *>>|null} Normalized tether asset descriptors in source order, or null when the source assets field is absent.
 */
function normalizedTetherAssets(assets) {
  if (assets == null) return null;
  const values = Array.isArray(assets) ? assets : [assets];
  return values.filter(asset => asset && typeof asset === 'object').map((asset, assetIndex) => {
    const normalized = { asset_index: assetIndex };
    for (const key of ['title', 'text', 'alt', 'caption', 'url']) {
      if (typeof asset[key] === 'string') normalized[key] = asset[key];
    }
    return normalized;
  });
}

/**
 * Normalizes tether browsing display.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Object<string, *>} A canonical tool-result event for tether browsing display content, or the original event when the source shape does not match.
 */
function normalizeTetherBrowsingDisplay(event, record) {
  if (record?.author?.role !== 'tool' ||
      record?.content?.content_type !== 'tether_browsing_display') return event;

  const output = {
    summary: typeof record.content.summary === 'string' ? record.content.summary : null,
    result: typeof record.content.result === 'string' ? record.content.result : null,
    assets: normalizedTetherAssets(record.content.assets),
    tether_id: typeof record.content.tether_id === 'string' ? record.content.tether_id : null
  };

  return {
    ...event,
    kind: 'tool_result',
    blocks: [{
      id: `${event.source_record_id}:tool_result:0`,
      type: 'tool_result',
      call_id: null,
      name: record?.author?.name ?? null,
      output,
      output_format: 'tether_browsing_display',
      source: sourceFor(event)
    }]
  };
}

/**
 * Normalizes reasoning recap.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Object<string, *>} A canonical reasoning-summary event for reasoning recap content, or the original event when the source shape does not match.
 */
function normalizeReasoningRecap(event, record) {
  if (record?.author?.role !== 'assistant' ||
      record?.content?.content_type !== 'reasoning_recap') return event;

  const content = typeof record.content.content === 'string' ? record.content.content : null;
  return {
    ...event,
    kind: 'reasoning_summary',
    blocks: [{
      id: `${event.source_record_id}:reasoning_recap:0`,
      type: 'reasoning_summary',
      summary: null,
      content,
      chunks: null,
      finished: null,
      source: sourceFor(event)
    }]
  };
}

/**
 * Normalizes model editable context.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Object<string, *>} A canonical system-context event for model editable context content, or the original event when the source shape does not match.
 */
function normalizeModelEditableContext(event, record) {
  if (record?.author?.role !== 'assistant' ||
      record?.content?.content_type !== 'model_editable_context') return event;

  const blocks = [];
  for (const key of ['model_set_context', 'repo_summary']) {
    const value = record.content[key];
    if (typeof value !== 'string' || !value.trim()) continue;
    blocks.push({
      id: `${event.source_record_id}:context:${key}`,
      type: 'text',
      text: value,
      context_kind: key,
      source: sourceFor(event, { context_key: key })
    });
  }

  return {
    ...event,
    kind: 'system_context',
    blocks
  };
}

/**
 * Normalizes non parts content.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Object<string, *>} The canonical event after applying supported non-parts ChatGPT content normalizers.
 */
function normalizeNonPartsContent(event, record) {
  let normalized = normalizeTetherBrowsingDisplay(event, record);
  normalized = normalizeReasoningRecap(normalized, record);
  return normalizeModelEditableContext(normalized, record);
}

/**
 * Normalizes source footnotes.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Object<string, *>} The canonical event with sources-footnote citations appended, or the original event when none are present.
 */
function normalizeSourceFootnotes(event, record) {
  const references = record?.metadata?.content_references;
  if (!Array.isArray(references)) return event;

  const footnotes = [];
  references.forEach((reference, referenceIndex) => {
    if (reference?.type !== 'sources_footnote') return;

    footnotes.push({
      id: `${event.source_record_id}:citation:${referenceIndex}`,
      type: 'citation',
      citation_kind: 'sources_footnote',
      matched_text: typeof reference?.matched_text === 'string' ? reference.matched_text : null,
      text_range: null,
      sources_footnote: {
        sources: Array.isArray(reference?.sources)
          ? reference.sources.map(source => ({
              title: typeof source?.title === 'string' ? source.title : null,
              url: typeof source?.url === 'string' ? source.url : null,
              attribution: typeof source?.attribution === 'string' ? source.attribution : null
            }))
          : []
      },
      source: sourceFor(event, { reference_index: referenceIndex })
    });
  });

  if (!footnotes.length) return event;
  return {
    ...event,
    citations: [...event.citations, ...footnotes]
  };
}

/**
 * Normalizes parent relationship.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} record - The provider/source record to process.
 * @param {Set<string>} knownRecordIds - Set of stable source record IDs used to validate parent-event linkage.
 * @returns {Object<string, *>} The canonical event with parent source/event relationship fields derived from provider metadata.
 */
function normalizeParentRelationship(event, record, knownRecordIds) {
  const parentRecordId = typeof record?.metadata?.parent_id === 'string' &&
      record.metadata.parent_id.trim()
    ? record.metadata.parent_id.trim()
    : null;

  return {
    ...event,
    relationships: {
      ...event.relationships,
      parent_record_id: parentRecordId,
      parent_event_id: parentRecordId && knownRecordIds.has(parentRecordId)
        ? `chatgpt:${parentRecordId}`
        : null
    }
  };
}

/**
 * Normalizes source provenance.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} record - The provider/source record to process.
 * @returns {Object<string, *>} The canonical event with stable ChatGPT record identity, indexes, timestamps, and turn-linkage provenance.
 */
function normalizeSourceProvenance(event, record) {
  const sourceIndex = Number.isInteger(event.source_index) ? event.source_index : null;
  const recordId = typeof record?.id === 'string' ? record.id : event.source_record_id;
  const metadata = record?.metadata && typeof record.metadata === 'object' ? record.metadata : {};

  return {
    ...event,
    source: {
      ...event.source,
      record_id: recordId,
      record_index: sourceIndex,
      turn_id: recordId,
      create_time: record?.create_time ?? null,
      update_time: record?.update_time ?? null,
      turn_exchange_id: typeof metadata.turn_exchange_id === 'string'
        ? metadata.turn_exchange_id
        : null,
      working_turn_id: typeof metadata.working_turn_id === 'string'
        ? metadata.working_turn_id
        : null
    }
  };
}

/**
 * Adapts ordered ChatGPT provider records into ordered canonical events while preserving source identity and provenance.
 *
 * @param {Array<Object>} records - The ordered provider/source records to process.
 * @returns {Array<Object>} The ordered canonical events derived from the ordered ChatGPT source records.
 */
export function adaptChatGPTRecords(records) {
  const events = adaptBaseChatGPTRecords(records);
  // Set of stable provider record IDs used to validate parent relationships without inventing links.
  const knownRecordIds = new Set(records
    .map(record => record?.id)
    .filter(id => typeof id === 'string' && id));

  return events.map(event => {
    const record = records[event.source_index];
    const withReplacements = normalizeDisplayReplacements(event, record);
    const withImages = normalizeMultimodalImages(withReplacements, record);
    const withNonParts = normalizeNonPartsContent(withImages, record);
    const withFootnotes = normalizeSourceFootnotes(withNonParts, record);
    const withParent = normalizeParentRelationship(withFootnotes, record, knownRecordIds);
    return normalizeSourceProvenance(withParent, record);
  });
}
