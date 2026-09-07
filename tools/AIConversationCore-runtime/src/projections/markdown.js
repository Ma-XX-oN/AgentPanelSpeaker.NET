/**
 * Escapes text for safe insertion into generated HTML fragments.
 *
 * @param {string} value - The input value to process.
 * @returns {string} The HTML-escaped form of the supplied text.
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
 * Quotes Markdown.
 *
 * @param {string} text - The text value to process.
 * @returns {string} The supplied text rendered as Markdown blockquote lines.
 */
function quoteMarkdown(text) {
  return String(text).split('\n').map(line => line ? `> ${line}` : '>').join('\n');
}

/**
 * Returns the human-readable transcript speaker label for a canonical provider.
 *
 * @param {string} provider - The canonical provider identifier whose display label is requested.
 * @returns {string} The human-readable provider name used in transcript headings.
 */
function providerLabel(provider) {
  if (provider === 'claude') return 'Claude';
  if (provider === 'codex') return 'Codex';
  return 'ChatGPT';
}


/**
 * Renders the optional metadata suffix for a transcript heading.
 *
 * @param {Object<string, *>} event - The canonical event whose source projection metadata is being used.
 * @returns {string} The consumer-specific heading metadata suffix.
 */
function projectedHeadingMetadataSuffix(event) {
  const projection = event?.projection ?? {};
  const metadata = projection.heading_metadata ?? {};
  const colors = projection.colors ?? {};
  const reset = colors.reset ?? '';
  /**
   * Applies one configured ANSI colour to heading metadata.
   *
   * @param {string} text - Metadata text to style.
   * @param {string} colorName - Projection colour field name.
   * @returns {string} Styled text, or the original text when no colour is configured.
   */
  const styled = (text, colorName) => {
    const color = colors[colorName] ?? '';
    return color ? `${color}${text}${reset}` : text;
  };
  const fields = [];
  if (metadata.timestamp != null) {
    fields.push(styled(`[${metadata.timestamp}]:`, 'timestamp'));
  }
  if (metadata.record_number != null) {
    fields.push(styled(`${metadata.record_number}:`, 'record_number'));
  }
  const turnId = metadata.turn_id ?? event?.source_record_id;
  if (metadata.show_turn_id && turnId != null) {
    fields.push(`turn_id=${turnId}`);
  }
  const metadataSuffix = fields.length ? ` ${fields.join(' ')}` : '';
  return `${metadataSuffix}${projection.heading_suffix ?? ''}`;
}

/**
 * Renders a transcript heading with optional consumer projection styling.
 *
 * @param {Object<string, *>} event - The canonical event being headed.
 * @param {string} label - Canonical Markdown heading label.
 * @returns {string} The consumer-decorated transcript heading.
 */
function projectedHeading(event, label) {
  const projection = event?.projection ?? {};
  const colors = projection.colors ?? {};
  const color = label === '## User' ? colors.user : (label.includes(' Sub-agent ') ? '' : colors.ai);
  const reset = colors.reset ?? '';
  const heading = color ? `${color}${label}${reset}` : label;
  return `${heading}${projectedHeadingMetadataSuffix(event)}`;
}

/**
 * Renders a numbered thought heading with optional consumer projection metadata.
 *
 * @param {Object<string, *>} event - The canonical reasoning/tool event being headed.
 * @param {number} number - The one-based thought number within the current Assistant section.
 * @returns {string} The consumer-decorated thought heading.
 */
function projectedThoughtHeading(event, number) {
  const projection = event?.projection ?? {};
  const colors = projection.colors ?? {};
  const label = `### Thought ${number}`;
  const heading = colors.thought ? `${colors.thought}${label}${colors.reset ?? ''}` : label;
  return `${heading}${projectedHeadingMetadataSuffix(event)}`;
}

/**
 * Returns the optional source-record debug provenance comment for an event.
 *
 * Debug provenance is formatted centrally so every renderer uses the same
 * `record_id` and `record_index` field names. `record_id` is the native
 * provider/source record identity retained as `source_record_id`; it is
 * deliberately distinct from first-class turn identity.
 *
 * @param {Object<string, *>} event - The canonical event whose source provenance is being rendered.
 * @param {boolean} quoted - Whether the comment must remain inside an existing Markdown blockquote.
 * @returns {string} The provenance comment in plain or blockquoted form, or an empty string when debugging is disabled.
 */
function projectedComment(event, quoted = false) {
  const projection = event?.projection ?? {};
  if (!projection.debug_provenance) return '';
  const fields = [];
  if (event?.source_record_id != null) fields.push(`record_id=${event.source_record_id}`);
  if (Number.isInteger(event?.source_index)) fields.push(`record_index=${event.source_index}`);
  if (!fields.length) return '';
  const comment = `<!-- ${fields.join(' ')} -->`;
  return quoted ? quoteMarkdown(comment) : comment;
}

/**
 * Appends source debug provenance to the first renderer-generated structural line.
 *
 * @param {Object<string, *>} event - The canonical event whose provenance identifies the generated structure.
 * @param {string} section - The already-rendered Markdown section whose first line is renderer-generated structure.
 * @returns {string} The section with optional provenance appended to its first structural line.
 */
function projectedSection(event, section) {
  const comment = projectedComment(event);
  if (!comment) return section;
  const newline = section.indexOf('\n');
  if (newline < 0) return `${section} ${comment}`;
  return `${section.slice(0, newline)} ${comment}${section.slice(newline)}`;
}


/**
 * Returns an event-shaped projection view for a related source record.
 *
 * Primary canonical event provenance is preserved unchanged. This view is used
 * only when a renderer-generated structure has separately evidenced provenance.
 *
 * @param {Object<string, *>} event - The canonical event whose related source is being projected.
 * @param {string} relationshipName - The relationship/source role to project.
 * @returns {Object<string, *>} An event-shaped view using the related source and projection, or the original event when unavailable.
 */
function relatedProjectionEvent(event, relationshipName) {
  const source = event?.relationships?.[relationshipName];
  if (!source || typeof source !== 'object') return event;
  const relatedProjection = event?.projection?.related_sources?.[relationshipName];
  return {
    ...event,
    source_record_id: source.record_id ?? null,
    source_index: Number.isInteger(source.record_index) ? source.record_index : null,
    projection: relatedProjection ?? event?.projection ?? {}
  };
}

/**
 * Finds one canonical event resource by resource ID.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {string} resourceId - The resource id.
 * @returns {Object<string, *>|null} The canonical resource with the requested ID, or null when the event has no matching resource.
 */
function resourceById(event, resourceId) {
  return event.resources?.find(resource => resource.id === resourceId) ?? null;
}

/**
 * Renders image block.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @returns {string} Markdown for the canonical image resource, including available/missing/unavailable state.
 */
function renderImageBlock(event, block) {
  const resource = resourceById(event, block.resource_id);
  if (!resource || resource.status === 'missing') return '[image missing]';
  if (resource.status === 'available') {
    const source = resource.data_url ?? resource.download_url ?? resource.source_pointer;
    return source ? `![image](${source})` : '[image available]';
  }
  if (resource.source_pointer) return `[image not available](${resource.source_pointer})`;
  return '[image not available]';
}

/**
 * Returns the origin used for a citation-source favicon lookup.
 *
 * @param {string} url - The URL value to process.
 * @returns {string} The hostname used for favicon lookup, or an empty string when the URL cannot be parsed.
 */
function faviconDomain(url) {
  if (typeof url !== 'string' || !url) return '';
  try {
    const parsed = new URL(url);
    return `${parsed.protocol}//${parsed.host}`;
  } catch {
    return url;
  }
}

/**
 * Builds citation-source tooltip text from the source title and snippet.
 *
 * @param {Object<string, *>} source - The source descriptor or provider pointer being normalized or rendered.
 * @returns {string} Tooltip text assembled from the source title/snippet metadata.
 */
function sourceTooltip(source) {
  const title = source?.title ?? '';
  const snippet = source?.snippet ?? '';
  if (title && snippet) return `${title}\n\n${snippet}`;
  return title || snippet;
}

/**
 * Returns the preferred visible label for a citation source.
 *
 * @param {Object<string, *>} source - The source descriptor or provider pointer being normalized or rendered.
 * @param {string} fallback - Fallback display label used when the source supplies no suitable label.
 * @returns {string} The preferred visible source label, falling back to the supplied label/hostname.
 */
function sourceLabel(source, fallback = '') {
  return source?.attribution || source?.title || fallback;
}

/**
 * Renders source anchor.
 *
 * @param {Object<string, *>} source - The source descriptor or provider pointer being normalized or rendered.
 * @param {string} fallbackLabel - Fallback link label used when the source supplies no suitable title/attribution.
 * @param {boolean} preferTitle - Whether an available source title should be preferred over attribution/hostname.
 * @returns {string} The HTML anchor (with optional favicon/tooltip) for the canonical web source.
 */
function renderSourceAnchor(source, fallbackLabel = '', preferTitle = false) {
  const url = source?.url ?? '';
  const tooltip = sourceTooltip(source);
  const label = preferTitle ? (source?.title || source?.attribution || fallbackLabel)
    : sourceLabel(source, fallbackLabel);
  const favicon = `https://www.google.com/s2/favicons?domain=${faviconDomain(url)}&sz=32`;
  const escapedTooltip = htmlEscape(tooltip).replaceAll('\n', '&#10;');
  return `<a href="${htmlEscape(url)}" title="${escapedTooltip}" style="display:inline-block;white-space:nowrap;"><img alt="" src="${htmlEscape(favicon)}" width="15" height="15" title="${escapedTooltip}" style="width:0.97em;height:0.97em;vertical-align:-0.13em;margin-right:0.22em;border-radius:2px;">${htmlEscape(label)}</a>`;
}

/**
 * Renders web citation.
 *
 * @param {Object<string, *>} citation - The canonical citation being rendered or remapped.
 * @returns {string} Markdown/HTML rendering of the canonical web citation and its supporting sources.
 */
function renderWebCitation(citation) {
  const rendered = [];
  for (const source of citation.web?.sources ?? []) {
    rendered.push(renderSourceAnchor(source));
    for (const supporting of source.supporting_sources ?? []) rendered.push(renderSourceAnchor(supporting));
  }
  return `**(cite: ${rendered.join(', ')})**`;
}

/**
 * Renders memory citation.
 *
 * @param {Object<string, *>} citation - The canonical citation being rendered or remapped.
 * @returns {string} Markdown/HTML rendering of the canonical memory citation sources.
 */
function renderMemoryCitation(citation) {
  const rendered = (citation.memory?.sources ?? [])
    .map(source => renderSourceAnchor(source, source?.title ?? 'memory', true));
  return `**(memory: ${rendered.join(', ')})**`;
}

/**
 * Extracts and normalizes a retrieved-file line-range label from citation marker text.
 *
 * @param {Object<string, *>} citation - The canonical citation being rendered or remapped.
 * @returns {string} Human-readable line/range label for a retrieved-file citation, or an empty string when no line metadata exists.
 */
function retrievedLineLabel(citation) {
  const matched = citation?.matched_text;
  if (typeof matched !== 'string') return '';
  const match = matched.match(/(L\d+(?:-L?\d+)?)$/);
  return match ? ` ${match[1].replace(/-(?=\d)/, '-L')}` : '';
}

/**
 * Renders citation.
 *
 * @param {Object<string, *>} citation - The canonical citation being rendered or remapped.
 * @returns {string} Rendered Markdown for the supported canonical citation kind.
 */
function renderCitation(citation) {
  if (citation.citation_kind === 'file') return `\`${citation.file?.name ?? 'file'}\``;
  if (citation.citation_kind === 'retrieved_file') {
    const file = citation.retrieved_file ?? {};
    if (!file.resolved || !file.url) return file.title ?? citation.matched_text ?? '';
    return `<a href="${htmlEscape(file.url)}">${htmlEscape(file.title ?? 'file')}${retrievedLineLabel(citation)}</a>`;
  }
  if (citation.citation_kind === 'web') return renderWebCitation(citation);
  if (citation.citation_kind === 'memory') return renderMemoryCitation(citation);
  return citation.matched_text ?? '';
}

/**
 * Collects display replacements, citations, and generated-file links that apply to one text part.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {number} partIndex - The zero-based content-part index.
 * @returns {Array<Object<string, *>>} Text replacements for the requested part, sorted from highest to lowest character offset for safe in-place rewriting.
 */
function textReplacements(event, partIndex) {
  const replacements = [];
  for (const replacement of event.display_replacements ?? []) {
    if (replacement?.text_range?.part_index !== partIndex) continue;
    replacements.push({ start: replacement.text_range.start, end: replacement.text_range.end, text: replacement.display_text ?? '' });
  }
  for (const citation of event.citations ?? []) {
    if (citation?.text_range?.part_index !== partIndex) continue;
    replacements.push({ start: citation.text_range.start, end: citation.text_range.end, text: renderCitation(citation) });
  }
  for (const resource of event.resources ?? []) {
  if (resource?.type !== 'artifact' || resource?.resource_kind !== 'generated_file') continue;
  if (resource?.text_range?.part_index !== partIndex) continue;
  const destination = resource.download_url ?? resource.source_pointer;
  if (!destination) continue;
  const label = resource.label ?? resource.name ?? 'Download';
  replacements.push({
    start: resource.text_range.start,
    end: resource.text_range.end,
    text: `[${label}](${destination})`
  });
}
  return replacements.sort((a, b) => b.start - a.start || b.end - a.end);
}

/**
 * Renders text block.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @param {number} partIndex - The zero-based content-part index.
 * @returns {string} Canonical text block after applying ordered display/citation replacements.
 */
function renderTextBlock(event, block, partIndex) {
  let text = block.text ?? '';
  for (const replacement of textReplacements(event, partIndex)) {
    text = text.slice(0, replacement.start) + replacement.text + text.slice(replacement.end);
  }
  return text;
}

/**
 * Renders message blocks.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {string} Visible canonical message/image blocks rendered as Markdown text in block order.
 */
function renderMessageBlocks(event) {
  return (event.blocks ?? []).map((block, blockIndex) => {
    if (block.type === 'text') {
      const partIndex = Number.isInteger(block?.source?.part_index) ? block.source.part_index : blockIndex;
      return renderTextBlock(event, block, partIndex);
    }
    if (block.type === 'image') return renderImageBlock(event, block);
    return '';
  }).filter(text => text !== '').join('\n\n');
}

/**
 * Escapes bare HTML starts on Claude blockquoted thinking lines outside code fences.
 *
 * Claude thinking may itself contain Markdown blockquote lines. After the
 * renderer adds its outer blockquote, an unescaped `<tag>` on one of those
 * source lines can be interpreted as HTML rather than literal reasoning text.
 * Fenced code is left unchanged because the fence already protects its body.
 *
 * @param {string} text - Raw Claude thinking content.
 * @returns {string} Thinking content with `<` escaped only on source blockquote lines outside matching backtick fences.
 */
function escapeClaudeThinking(text) {
  const lines = String(text ?? '').split('\n');
  let fence = null;
  return lines.map(line => {
    const marker = line.match(/^((?:> )*)(`{3,})(?!`)/);
    if (marker) {
      const key = `${marker[1]}${marker[2]}`;
      if (fence === null) fence = key;
      else if (key === fence) fence = null;
      return line;
    }
    if (fence === null && line.startsWith('>')) return line.replaceAll('<', '&lt;');
    return line;
  }).join('\n');
}

/**
 * Builds the Markdown body for canonical reasoning-summary blocks in one event.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {string} Visible reasoning-summary text joined from the event reasoning blocks.
 */
function reasoningBody(event) {
  return (event.blocks ?? []).map(block => {
    if (block.type !== 'reasoning_summary') return '';
    const parts = [];
    if (block.summary) parts.push(`**${block.summary}**`);
    if (block.content) parts.push(event.provider === 'claude' ? escapeClaudeThinking(block.content) : block.content);
    return parts.join('\n\n');
  }).filter(Boolean).join('\n\n');
}

/**
 * Wraps a summary and body in the HTML `details` structure used by Markdown output.
 *
 * @param {string} summary - The summary label shown for the collapsible details block.
 * @param {string} body - The body text placed inside the generated details block.
 * @returns {string} The HTML details/summary Markdown fragment containing the supplied body.
 */
function details(summary, body) {
  return `<details>\n<summary>${summary}</summary>\n\n${body}\n\n</details>`;
}

/**
 * Renders a details group with source debug provenance for every grouped event.
 *
 * @param {string} summary - The visible summary label for the details group.
 * @param {string} body - The Markdown body inside the details group.
 * @param {Array<Object<string, *>>} sourceEvents - Ordered canonical events represented by the generated group.
 * @param {boolean} quoted - Whether provenance lines must be Markdown-blockquoted.
 * @param {boolean} inlineOpening - Whether `<details>` and `<summary>` share the opening line.
 * @returns {string} The details group with optional per-source provenance on the summary and following lines.
 */
function projectedDetails(summary, body, sourceEvents, quoted = false, inlineOpening = false) {
  const events = Array.isArray(sourceEvents) ? sourceEvents : [];
  const comments = events.map(event => projectedComment(event, quoted)).filter(Boolean);
  const first = comments.shift() ?? '';
  const summaryLine = `<summary>${summary}</summary>${first ? ` ${first.replace(/^> /, '')}` : ''}`;
  const opening = inlineOpening ? `<details>${summaryLine}` : `<details>\n${summaryLine}`;
  const extra = comments.length ? `\n${comments.join('\n')}` : '';
  return `${opening}${extra}\n\n${body}\n\n</details>`;
}

/**
 * Returns the singular/plural human-readable summary for a count of thoughts.
 *
 * @param {number} count - The number of reasoning/thought items represented by the summary label.
 * @returns {string} The singular/plural summary label for the requested number of thought items.
 */
function thoughtSummary(count) {
  return count === 1 ? 'Having a thought' : `Having ${count} thoughts`;
}

/**
 * Wraps literal content in an adaptive Markdown code fence that cannot collide with backtick runs in the payload.
 *
 * @param {string} content - The provider/canonical content being converted to display text.
 * @param {string} language - The source or canonical language identifier.
 * @returns {string} The Markdown code-fence representation of the literal payload.
 */
function fencedCode(content, language = '') {
  const text = String(content ?? '');
  const runs = text.match(/`+/g) ?? [];
  const longest = runs.reduce((max, run) => Math.max(max, run.length), 0);
  const fence = '`'.repeat(Math.max(3, longest + 1));
  return `${fence}${language}\n${text}\n${fence}`;
}

/**
 * Selects the Markdown fence language from canonical tool-call semantics.
 *
 * A normalized non-`unknown` canonical language is emitted unchanged; the historical `container.exec` fallback emits `bash` only when no stronger normalized language is present.
 *
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @returns {string} The Markdown fence language selected from canonical tool semantics, or an empty string when no language is justified.
 */
function inferredToolLanguage(block) {
  const language = typeof block.language === 'string' ? block.language.trim() : '';
  if (language && language !== 'unknown') return language;
  if (block.name === 'container.exec') return 'bash';
  return '';
}

/**
 * Handles related retrieved file.
 *
 * @param {Array<Object<string, *>>} events - The ordered canonical events to process.
 * @param {string} sourceRecordId - The stable provider/source record identifier.
 * @returns {Object<string, *>|null} Resolved retrieved-file citation metadata associated with the source record, or null when none is related.
 */
function relatedRetrievedFile(events, sourceRecordId) {
  for (const event of events) {
    for (const citation of event.citations ?? []) {
      if (citation.citation_kind === 'retrieved_file' && citation.retrieved_file?.source_record_id === sourceRecordId && citation.retrieved_file?.resolved) return citation.retrieved_file;
    }
  }
  return null;
}

/**
 * Renders multimodal tool output.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @param {Array<Object<string, *>>} events - The ordered canonical events to process.
 * @returns {string} Rendered Markdown for a multimodal tool-result block and any related retrieved file.
 */
function renderMultimodalToolOutput(event, block, events) {
  const values = Array.isArray(block.output) ? block.output : [];
  const visible = values.filter(value => typeof value === 'string' && !value.startsWith('Make sure to include ')).map(value => value.trim());
  const retrieved = relatedRetrievedFile(events, event.source_record_id);
  if (retrieved?.url) {
    for (let index = 0; index < visible.length; index += 1) {
      if (visible[index].startsWith('Citation Marker:')) visible[index] = `Citation Marker: <a href="${htmlEscape(retrieved.url)}">${htmlEscape(retrieved.title ?? 'file')}</a>`;
    }
  }
  return visible.join('\n\n');
}

/**
 * Renders a canonical ChatGPT tool block into the Markdown details/fence representation.
 *
 * The renderer consumes canonical `input`, `language`, `output`, and
 * `output_format`; provider-native presentation is not reinterpreted here.
 *
 * @param {Object<string, *>} event - The canonical event represented by the tool structure.
 * @param {Object<string, *>} block - The canonical tool call/result block being rendered.
 * @param {Array<Object<string, *>>} events - The ordered canonical events used for related-resource resolution.
 * @returns {string} Rendered Markdown details block for one ChatGPT tool call or result block.
 */
function renderChatGPTToolBlock(event, block, events) {
  if (block.type === 'tool_call') {
    const language = inferredToolLanguage(block);
    return projectedDetails(`${block.name ?? 'tool'} code`, fencedCode(block.input ?? '', language), [event]);
  }
  if (block.type === 'tool_result') {
    let output = block.output ?? '';
    if (block.output_format === 'multimodal_text') output = renderMultimodalToolOutput(event, block, events);
    else if (block.output_format === 'tether_browsing_display') output = [block.output?.summary, block.output?.result].filter(Boolean).join('\n\n');
    return projectedDetails(`${block.name ?? 'tool'} output`, fencedCode(output), [event]);
  }
  return '';
}

/**
 * Renders all canonical tool blocks belonging to one ChatGPT tool event.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @param {Array<Object<string, *>>} events - The ordered canonical events to process.
 * @returns {string} Rendered Markdown for all tool blocks in the canonical ChatGPT tool event.
 */
function renderChatGPTToolEvent(event, events) {
  return (event.blocks ?? []).map(block => renderChatGPTToolBlock(event, block, events)).filter(Boolean).join('\n\n');
}

/**
 * Renders one semantic User-context block as a blockquoted details disclosure.
 *
 * @param {Object<string, *>} block - Canonical User-context block.
 * @returns {string} Blockquoted Markdown/HTML details fragment.
 */
function renderUserContextBlock(block) {
  const summary = htmlEscape(block?.summary ?? '# Context from my IDE setup:');
  const body = String(block?.text ?? '');
  return quoteMarkdown(`<details><summary>${summary}</summary>\n\n${body}\n\n</details>`);
}

/**
 * Renders user.
 *
 * Semantic User context is presented before the actual prompt in a blockquoted
 * details disclosure. The prompt remains outside that blockquote so consumers
 * can assign context and prompt distinct User speech voices without reparsing
 * provider-native text. User events without semantic context retain the existing
 * fully-blockquoted rendering contract.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {string} The complete User transcript section for the canonical event.
 */
function renderUser(event) {
  const blocks = Array.isArray(event?.blocks) ? event.blocks : [];
  const contexts = blocks.filter(block => block?.type === 'user_context');
  if (!contexts.length) {
    return projectedSection(event, `${projectedHeading(event, '## User')}\n\n${quoteMarkdown(renderMessageBlocks(event))}`);
  }

  const promptEvent = {
    ...event,
    blocks: blocks.filter(block => block?.type !== 'user_context')
  };
  const parts = contexts.map(renderUserContextBlock);
  const prompt = renderMessageBlocks(promptEvent);
  if (prompt) parts.push(prompt);
  return projectedSection(event, `${projectedHeading(event, '## User')}\n\n${parts.join('\n\n')}`);
}

/**
 * Renders the ChatGPT activity inside one response while preserving source order.
 *
 * Consecutive reasoning records define the thought count. Tool call/result
 * structures may occur within the same thought run but do not increment `N`.
 * Commentary flushes the current run, receives its own
 * `### ChatGPT Commentary` heading, and breaks thought consecutiveness.
 *
 * @param {Array<Object<string, *>>} segment - Ordered canonical events in one ChatGPT response.
 * @param {Array<Object<string, *>>} events - Full ordered canonical event sequence for resource resolution.
 * @returns {Array<string>} Ordered thought/tool/commentary structures rendered inside the enclosing ChatGPT response.
 */
function renderChatGPTCommentarySegment(segment, events) {
  const body = [];
  let run = [];
  let thoughtEvents = [];

  /**
   * Flushes the current ChatGPT reasoning/tool run without counting tools as thoughts.
   *
   * @returns {void} No value is returned.
   */
  const flushRun = () => {
    if (!run.length) return;
    if (thoughtEvents.length) {
      const rendered = run.map(item => item.text).join('\n\n');
      body.push(projectedDetails(
        thoughtSummary(thoughtEvents.length),
        rendered,
        thoughtEvents,
        false,
        true
      ));
    } else {
      body.push(...run.map(item => item.text));
    }
    run = [];
    thoughtEvents = [];
  };

  for (const event of segment) {
    if (event.kind === 'reasoning_summary') {
      const text = reasoningBody(event);
      if (text) {
        run.push({ event, text });
        thoughtEvents.push(event);
      }
      continue;
    }
    if (event.kind === 'tool_call' || event.kind === 'tool_result') {
      const text = renderChatGPTToolEvent(event, events);
      if (text) run.push({ event, text });
      continue;
    }
    if (event.kind === 'commentary') {
      flushRun();
      const text = renderMessageBlocks(event);
      if (text) body.push(projectedSection(event, `${projectedHeading(event, '### ChatGPT Commentary')}\n\n${quoteMarkdown(text)}`));
    }
  }
  flushRun();
  return body;
}

/**
 * Renders one canonical ChatGPT response with exactly one leading `## ChatGPT` heading.
 *
 * Reasoning/tool activity is grouped into consecutive thought groups; commentary
 * is rendered at level three and breaks thought-group consecutiveness. Final
 * Assistant messages remain inside the same response section.
 *
 * @param {Array<Object<string, *>>} segment - Ordered canonical events forming one ChatGPT response.
 * @param {Array<Object<string, *>>} events - Full ordered canonical event sequence for resource resolution.
 * @returns {Array<string>} Zero or one complete ChatGPT Markdown response sections.
 */
function renderChatGPTAssistantSegment(segment, events) {
  const body = renderChatGPTCommentarySegment(segment, events);
  const messages = segment.filter(event => event.kind === 'message' && event.role === 'assistant');
  for (const event of messages) {
    const text = renderMessageBlocks(event);
    if (text) body.push(quoteMarkdown(text));
  }
  if (!body.length) return [];
  const headingEvent = segment[0];
  // Consumer response-heading metadata may differ from the first activity event's own heading metadata.
  const responseHeadingEvent = headingEvent?.projection?.response_heading_suffix != null
    ? {
        ...headingEvent,
        projection: {
          ...headingEvent.projection,
          heading_suffix: headingEvent.projection.response_heading_suffix
        }
      }
    : headingEvent;
  return [projectedSection(responseHeadingEvent, `${projectedHeading(responseHeadingEvent, '## ChatGPT')}\n\n${body.join('\n\n')}`)];
}

/**
 * Handles tool call ID.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {string|null} The canonical tool-call correlation ID, or null when the event carries no call ID.
 */
function toolCallId(event) {
  return event?.relationships?.tool_call_id ?? event?.blocks?.[0]?.call_id ?? null;
}

/**
 * Handles tool result by call ID.
 *
 * @param {Array<Object<string, *>>} segment - The ordered canonical events that form one Assistant activity segment.
 * @returns {Map<string, Object<string, *>>} Tool-result events indexed by their non-null call IDs.
 */
function toolResultByCallId(segment) {
  // Maps tool-call IDs to their canonical result events for paired rendering.
  const results = new Map();
  for (const event of segment) if (event.kind === 'tool_result' && toolCallId(event)) results.set(toolCallId(event), event);
  return results;
}

/**
 * Handles tool output.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {string} Tool-result output flattened to displayable text.
 */
function toolOutput(event) {
  const block = event?.blocks?.find(item => item.type === 'tool_result');
  if (!block) return '';
  if (typeof block.output === 'string') return block.output;
  if (Array.isArray(block.output)) return block.output.filter(item => item?.type === 'text' && typeof item.text === 'string').map(item => item.text).join('\n');
  return String(block.output ?? '');
}

/**
 * Renders Claude tool thought.
 *
 * @param {Object<string, *>} callEvent - The canonical tool-call event being paired/rendered.
 * @param {Object<string, *>|null} resultEvent - The matching canonical tool-result event, or null when no result is available.
 * @returns {string} Collapsed Markdown representation of a Claude tool call and its optional result.
 */
function renderClaudeToolThought(callEvent, resultEvent) {
  const block = callEvent?.blocks?.find(item => item.type === 'tool_call');
  if (!block || block.name !== 'Bash') return '';
  const command = typeof block.input?.command === 'string' ? block.input.command : '';
  const summary = typeof block.input?.description === 'string' && block.input.description ? block.input.description : 'Bash';
  const output = resultEvent ? toolOutput(resultEvent) : '';
  const sources = resultEvent ? [callEvent, resultEvent] : [callEvent];
  return projectedDetails(summary, [fencedCode(command, 'bash'), `**OUT**\n\n${fencedCode(output)}`].join('\n\n'), sources);
}

/**
 * Renders a Claude subagent section with invocation and completion provenance.
 *
 * The heading represents the originating Agent invocation when that related
 * source is available. The completion remains the primary event source and is
 * emitted as a second debug provenance line so neither source is lost.
 *
 * @param {Object<string, *>} event - The canonical subagent completion event being rendered.
 * @returns {string} Markdown representation of the Claude subagent completion event.
 */
function renderSubagentEvent(event) {
  const block = event?.blocks?.find(item => item.type === 'subagent');
  if (!block?.agent_id) return '';
  const headingEvent = relatedProjectionEvent(event, 'invocation_source');
  const body = [];
  const completionComment = headingEvent === event ? '' : projectedComment(event);
  if (block.description) body.push(quoteMarkdown(`**${block.description}**`));
  if (block.output) body.push(quoteMarkdown(block.output));
  const label = `## ${providerLabel(event.provider)} Sub-agent ${block.agent_id}`;
  const secondaryProvenance = completionComment ? `\n${completionComment}` : '';
  const renderedBody = body.length ? `\n\n${body.join('\n\n')}` : '';
  return projectedSection(
    headingEvent,
    `${projectedHeading(headingEvent, label)}${secondaryProvenance}${renderedBody}`
  );
}
/**
 * Renders Claude AskUserQuestion headings/options with source debug provenance.
 *
 * @param {Object<string, *>} event - The canonical tool-call event represented by the generated question headings.
 * @param {Object<string, *>} block - The normalized Claude AskUserQuestion block.
 * @returns {string} Markdown question/options blocks in provider order.
 */
function renderClaudeQuestionBlock(event, block) {
  const questions = block?.ask_user_question?.questions ?? [];
  const chunks = [];
  questions.forEach((question, index) => {
    const lines = [];
    if (question.question) lines.push(`**${question.question}**`);
    for (const option of question.options ?? []) {
      if (!option?.label) continue;
      lines.push(`- ${option.label}${option.description ? ` - ${option.description}` : ''}`);
    }
    chunks.push(projectedSection(event, `### Question ${index + 1}\n\n${quoteMarkdown(lines.join('\n'))}`));
  });
  return chunks.join('\n\n');
}

/**
 * Renders a Claude ExitPlanMode plan heading with source debug provenance.
 *
 * @param {Object<string, *>} event - The canonical tool-call event represented by the generated plan heading.
 * @param {Object<string, *>} block - The normalized Claude ExitPlanMode block.
 * @returns {string} Blockquoted Markdown plan section, or an empty string when no plan is present.
 */
function renderClaudePlanBlock(event, block) {
  const plan = block?.exit_plan?.plan;
  if (typeof plan !== 'string' || !plan.trim()) return '';
  const comment = projectedComment(event);
  const heading = `### Plan${comment ? ` ${comment}` : ''}`;
  return quoteMarkdown(`${heading}\n\n${quoteMarkdown(plan.trim())}`);
}

/**
 * Renders Claude plan approval.
 *
 * @param {Object<string, *>} event - The canonical tool-result event supplying source projection metadata.
 * @param {Object<string, *>} block - The canonical/provider content block being inspected or rendered.
 * @returns {string} Markdown User response section for a Claude exit-plan approval result.
 */
function renderClaudePlanApproval(event, block) {
  const response = block?.exit_plan_response;
  if (!response) return '';
  const parts = [];
  if (response.intro) parts.push(quoteMarkdown(response.intro));
  if (response.approved_plan) parts.push(quoteMarkdown(projectedDetails('Approved Plan', response.approved_plan, [event])));
  return projectedSection(event, `${projectedHeading(event, '## User')}\n\n${parts.join('\n\n')}`);
}

/**
 * Renders Claude assistant segment.
 *
 * @param {Array<Object<string, *>>} segment - The ordered canonical events that form one Assistant activity segment.
 * @returns {Array<string>} Claude/User/subagent Markdown sections produced from the Assistant segment.
 */
function renderClaudeAssistantSegment(segment) {
  const sections = [];
  const subagents = segment.filter(event => event.kind === 'subagent');
  const results = toolResultByCallId(segment);
  // Tracks tool-result event IDs already rendered with a call so they are not emitted twice.
  const consumedResults = new Set();
  let body = [];
  let thoughts = [];
  const headingEvent = segment[0];

  /**
   * Implements `flushThoughts`.
   *
   * @returns {void} No value is returned.
   */
  const flushThoughts = () => {
    if (!thoughts.length) return;
    const separate = Boolean(headingEvent?.projection?.separate_thoughts);
    const renderedThoughts = separate
      ? thoughts.map((item, index) => `${projectedThoughtHeading(item.event, index + 1)}\n\n${item.text}`).join('\n\n***\n\n')
      : thoughts.map(item => item.text).join('\n\n***\n\n');
    body.push(quoteMarkdown(projectedDetails(thoughtSummary(thoughts.length), renderedThoughts, thoughts.map(item => item.event))));
    thoughts = [];
  };
  /**
   * Implements `flushClaude`.
   *
   * @returns {void} No value is returned.
   */
  const flushClaude = () => {
    flushThoughts();
    if (!body.length) return;
    sections.push(projectedSection(headingEvent, `${projectedHeading(headingEvent, '## Claude')}\n\n${body.join('\n\n')}`));
    body = [];
  };

  for (const event of segment) {
    if (event.kind === 'subagent') continue;
    if (event.kind === 'reasoning_summary') {
      const text = reasoningBody(event);
      if (text) thoughts.push({ event, text });
      continue;
    }
    if (event.kind === 'tool_call') {
      const block = event.blocks?.find(item => item.type === 'tool_call');
      const result = toolCallId(event) ? results.get(toolCallId(event)) : null;
      if (block?.name === 'Bash') {
        const rendered = renderClaudeToolThought(event, result);
        if (rendered) thoughts.push({ event, text: rendered });
        if (result) consumedResults.add(result.id);
      } else if (block?.name === 'AskUserQuestion') {
        flushThoughts();
        const rendered = renderClaudeQuestionBlock(event, block);
        if (rendered) body.push(rendered);
      } else if (block?.name === 'ExitPlanMode') {
        flushThoughts();
        const rendered = renderClaudePlanBlock(event, block);
        if (rendered) body.push(rendered);
      }
      continue;
    }
    if (event.kind === 'tool_result') {
      if (consumedResults.has(event.id)) continue;
      const block = event.blocks?.find(item => item.type === 'tool_result');
      if (block?.name === 'AskUserQuestion') {
        flushClaude();
        const text = block.ask_user_question_response?.text ?? toolOutput(event);
        if (text) sections.push(projectedSection(event, `${projectedHeading(event, '## User')}\n\n${quoteMarkdown(text)}`));
      } else if (block?.name === 'ExitPlanMode') {
        flushClaude();
        const rendered = renderClaudePlanApproval(event, block);
        if (rendered) sections.push(rendered);
      }
      continue;
    }
    if (event.kind === 'message' && event.role === 'assistant') {
      flushThoughts();
      const text = renderMessageBlocks(event);
      if (text) {
        if (event?.projection?.separate_thoughts) {
          const inner = `${quoteMarkdown(projectedHeading(event, '## Claude'))}\n>\n${quoteMarkdown(text)}`;
          const comment = projectedComment(event);
          body.push(comment ? `${comment}\n\n${inner}` : inner);
        } else {
          body.push(quoteMarkdown(text));
        }
      }
    }
  }
  flushClaude();
  for (const event of subagents) {
    const rendered = renderSubagentEvent(event);
    if (rendered) sections.push(rendered);
  }
  return sections;
}

/**
 * Handles codex request block.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {Object<string, *>|undefined} The request_user_input tool-call block, or undefined when the event has no such block.
 */
function codexRequestBlock(event) {
  return event?.blocks?.find(block => block.type === 'tool_call' && block.name === 'request_user_input');
}

/**
 * Handles codex response block.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {Object<string, *>|undefined} The request_user_input tool-result block, or undefined when the event has no such block.
 */
function codexResponseBlock(event) {
  return event?.blocks?.find(block => block.type === 'tool_result' && block.request_user_input_response);
}

/**
 * Renders Codex request sections.
 *
 * @param {Object<string, *>} callEvent - The canonical tool-call event being paired/rendered.
 * @param {Object<string, *>|null} resultEvent - The matching canonical tool-result event, or null when no result is available.
 * @param {Object<string, number>} state - Per-render mutable state used for numbering and other projection-local counters.
 * @returns {Array<string>} Codex question Markdown and, when answers exist, the corresponding User answer section.
 */
function renderCodexRequestSections(callEvent, resultEvent, state) {
  const call = codexRequestBlock(callEvent);
  if (!call) return [];
  const response = codexResponseBlock(resultEvent);
  const questions = call.request_user_input?.questions ?? [];
  if (!questions.length) return [];
  const questionParts = [];
  const answerLines = [];
  for (const question of questions) {
    state.codexQuestionNumber += 1;
    const lines = [];
    if (question.question) lines.push(`**${question.question}**`);
    for (const option of question.options ?? []) if (option?.label) lines.push(`- ${option.label}${option.description ? ` - ${option.description}` : ''}`);
    questionParts.push(projectedSection(callEvent, `### Question ${state.codexQuestionNumber}\n\n${quoteMarkdown(lines.join('\n'))}`));
    const selected = response?.request_user_input_response?.answers?.[question.id] ?? [];
    if (question.question && selected.length) answerLines.push(`**${question.question}** → ${selected.map(value => `"${value}"`).join(', ')}`);
  }
  const sections = [projectedSection(callEvent, `${projectedHeading(callEvent, '## Codex')}\n\n${questionParts.join('\n\n')}`)];
  if (answerLines.length) sections.push(projectedSection(resultEvent, `${projectedHeading(resultEvent, '## User')}\n\n${quoteMarkdown(answerLines.join('\n'))}`));
  return sections;
}

/**
 * Renders Codex apply_patch changes as one provenance-traceable details group.
 *
 * @param {Array<Object<string, *>>} segment - Ordered canonical events forming the Codex response.
 * @returns {string|null} Collapsed Codex file-change details section, or null when no apply_patch changes exist.
 */
function renderCodexFileChanges(segment) {
  const patches = [];
  for (const event of segment) {
    if (event.kind !== 'tool_call') continue;
    const block = event.blocks?.find(item => item.type === 'tool_call' && item.name === 'apply_patch' && item.file_change?.patch);
    if (block) patches.push({ event, patch: block.file_change.patch });
  }
  if (!patches.length) return null;
  const fileCount = patches.reduce((count, item) => count + (item.patch.match(/^\*\*\* (?:Update|Add|Delete) File:/gm)?.length ?? 0), 0);
  const n = fileCount || patches.length;
  const body = patches.map(item => quoteMarkdown(`\`\`\`diff\n${item.patch}\n\`\`\``)).join('\n\n');
  return projectedDetails(`${n} file change${n === 1 ? '' : 's'}`, body, patches.map(item => item.event));
}

/**
 * Renders the main Codex response with provenance on generated response/thought structures.
 *
 * @param {Array<Object<string, *>>} segment - Ordered canonical events forming the Codex response.
 * @returns {string|null} The main Codex transcript section, or null when no visible response exists.
 */
function renderCodexMainResponse(segment) {
  const thoughts = [];
  const finals = [];
  for (const event of segment) {
    if (event.kind === 'reasoning_summary') {
      const text = reasoningBody(event);
      if (text) thoughts.push({ event, text });
    } else if (event.kind === 'commentary') {
      const text = renderMessageBlocks(event);
      if (text) thoughts.push({ event, text });
    } else if (event.kind === 'message' && event.role === 'assistant') {
      const text = renderMessageBlocks(event);
      if (text) finals.push({ event, text });
    }
  }
  if (!thoughts.length && !finals.length) return null;
  const body = [];
  if (thoughts.length) {
    const thoughtBody = thoughts.map(item => item.text).join('\n\n***\n\n');
    body.push(quoteMarkdown(projectedDetails(thoughtSummary(thoughts.length), thoughtBody, thoughts.map(item => item.event))));
  }
  for (const item of finals) body.push(quoteMarkdown(item.text));
  const headingEvent = segment[0];
  return projectedSection(headingEvent, `${projectedHeading(headingEvent, '## Codex')}\n\n${body.join('\n\n')}`);
}

/**
 * Renders Codex assistant segment.
 *
 * @param {Array<Object<string, *>>} segment - The ordered canonical events that form one Assistant activity segment.
 * @param {Object<string, number>} state - Per-render mutable state used for numbering and other projection-local counters.
 * @returns {Array<string>} Ordered Markdown sections for Codex request/answer, main response, and file-change content.
 */
function renderCodexAssistantSegment(segment, state) {
  const sections = [];
  const results = toolResultByCallId(segment);
  // Tracks Codex request/result event IDs rendered in request sections and excluded from the main response.
  const requestIds = new Set();
  for (const event of segment) {
    if (!codexRequestBlock(event)) continue;
    const result = toolCallId(event) ? results.get(toolCallId(event)) : null;
    sections.push(...renderCodexRequestSections(event, result, state));
    requestIds.add(event.id);
    if (result) requestIds.add(result.id);
  }
  const mainSegment = segment.filter(event => !requestIds.has(event.id));
  const main = renderCodexMainResponse(mainSegment);
  if (main) sections.push(main);
  const changes = renderCodexFileChanges(mainSegment);
  if (changes) sections.push(changes);
  return sections;
}

/**
 * Renders assistant segment.
 *
 * @param {Array<Object<string, *>>} segment - The ordered canonical events that form one Assistant activity segment.
 * @param {Array<Object<string, *>>} events - The ordered canonical events to process.
 * @param {Object<string, number>} state - Per-render mutable state used for numbering and other projection-local counters.
 * @returns {Array<string>} Provider-specific Markdown sections produced from one ordered Assistant activity segment.
 */
function renderAssistantSegment(segment, events, state) {
  const provider = segment.find(event => event?.provider)?.provider ?? 'chatgpt';
  if (provider === 'claude') return renderClaudeAssistantSegment(segment);
  if (provider === 'codex') return renderCodexAssistantSegment(segment, state);
  return renderChatGPTAssistantSegment(segment, events);
}

/**
 * Renders notice.
 *
 * @param {Object<string, *>} event - The canonical event being inspected, normalized, or rendered.
 * @returns {string} Blockquoted system-notice Markdown, or an empty string when the notice has no visible text.
 */
function renderNotice(event) {
  const text = renderMessageBlocks(event);
  if (!text) return '';
  const rendered = `> *(system: ${text})*`;
  return projectedSection(event, rendered);
}

/**
 * Renders canonical Markdown.
 *
 * @param {Array<Object>} events - The ordered canonical events to process.
 * @returns {string} The complete canonical Markdown transcript projection.
 */
export function renderCanonicalMarkdown(events) {
  if (!Array.isArray(events)) throw new TypeError('Canonical events must be an array.');
  const sections = [];
  // Per-render mutable numbering state for Codex question sections; it is not shared across render calls.
  const state = { codexQuestionNumber: 0 };
  let assistantSegment = [];
  /**
   * Implements `flushAssistant`.
   *
   * @returns {void} No value is returned.
   */
  const flushAssistant = () => {
    if (!assistantSegment.length) return;
    sections.push(...renderAssistantSegment(assistantSegment, events, state));
    assistantSegment = [];
  };

  for (const event of events) {
    if (event?.visibility === 'hidden' || event?.kind === 'system_context') continue;
    if (event?.kind === 'notice') {
      flushAssistant();
      const rendered = renderNotice(event);
      if (rendered) sections.push(rendered);
      continue;
    }
    if (event?.role === 'user' && event?.kind === 'message') {
      flushAssistant();
      sections.push(renderUser(event));
      continue;
    }
    const isAssistantActivity = event?.role === 'assistant' || event?.kind === 'tool_call' || event?.kind === 'tool_result' || event?.kind === 'subagent';
    if (!isAssistantActivity) continue;
    assistantSegment.push(event);
    if (event?.role === 'assistant' && event?.kind === 'message' && event?.provider !== 'claude') flushAssistant();
  }
  flushAssistant();
  return sections.join('\n\n') + '\n\n';
}
