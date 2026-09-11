# AIConversationCore source layout

## Incremental provider-adapter slices

`src/adapters/chatgpt.js` exposes the current fixture-backed ChatGPT adapter.  The
previously extracted ChatGPT semantics are retained in `chatgpt-base.js`, while
the image slice composes ordered multimodal image normalization over that existing
adapter rather than rewriting the already-proven citation/file/tool behaviour.
Claude and Codex tool slices exist alongside it so equivalent call/result
semantics can share one canonical shape without pretending that the rest of those
provider adapters is already complete.

For ordinary visible ChatGPT message records, each source record becomes one
canonical `message` event in the same source-array order. The adapter does not
regroup, sort, or associate records by `turn_exchange_id` or `working_turn_id`;
those values are preserved only as relationships/metadata.

For an Assistant record whose provider channel is `commentary`, the ChatGPT
adapter emits a canonical `commentary` event rather than flattening that record
into a final/ordinary message event. Commentary preserves the same stable source
identity, source index, channel, visibility, text blocks, and block provenance as
other mapped records.

For an Assistant record whose source `content_type` is `thoughts`, the ChatGPT
adapter emits a canonical `reasoning_summary` event. Each source thought is
retained as a structured `reasoning_summary` block with its `summary`, `content`,
`chunks`, `finished` state, stable block ID, source record index, and thought
index. Normalizing a source reasoning record does not by itself assert that the
material is eligible for public/export presentation; visibility/export policy
remains a separate projection concern.

## ChatGPT citations

The ChatGPT adapter now exposes canonical citation metadata on `event.citations`
for the concrete citation/reference forms established by the checked-in rich
fixture and subsequent verified evidence. Citation normalization follows
`NORMALIZATION_RULES.md`: equivalent semantics share a small base, while
citation-kind-specific information remains in kind-specific substructures rather
than being forced into one rigid schema.

The shared citation base contains:

- stable canonical citation ID;
- `type: "citation"`;
- `citation_kind`;
- provider/source record/reference-index provenance; and
- source marker text and a canonical text-block range when the evidenced citation
  form is tied to an inline marker.

The currently evidenced kinds are:

- `file` — keeps source file ID, name, source, and snippet in `file`;
- `retrieved_file` — resolves an evidenced retrieval marker to same-conversation
  retrieval metadata when an exact retrieval turn/file identity exists, keeping a
  boolean `resolved` plus the resolved title/URL and provenance;
- `web` — keeps web source title, URL, attribution, snippet, safe URLs, and
  supporting-source relationships in `web`; same-record search-result evidence is
  used to enrich a supporting source only when its normalized URL matches;
- `memory` — keeps the evidenced conversation-context citation sources in
  `memory`, including citation UUID, title, URL, snippet, attribution, category,
  deletion flag, and retrieval origin; and
- `sources_footnote` — keeps the independently evidenced user-visible source list
  in `sources_footnote.sources`, currently preserving only source `title`, `url`,
  and `attribution`. It remains distinct from `grouped_webpages` and
  `search_result_groups`; no inline marker/range or selection relationship is
  invented when the evidence does not establish one.

The rich fixture also contains an `alt_text` entity reference.  It is deliberately
not forced into the citation schema because the checked-in evidence does not make
it a citation semantic merely because it lives in `content_references`.

Canonical citation consumers do not need to inspect ChatGPT-native fields such as
`content_references`, `search_result_groups`, or
`conversation_context_citation_metadata`.  If future rendering requires source
information that is not represented by the canonical citation object, the adapter
must be extended from evidence rather than teaching the shared renderer to read
provider-native JSON.

Citation tooltip presentation remains a renderer contract.  The established
canonical golden protects a blank line between tooltip title and blurb/snippet so
readability is preserved, not merely data presence.

## ChatGPT files and generated artifacts

File-like resources are exposed on `event.resources`.  This slice normalizes only
the concrete forms established by checked-in evidence:

- a ChatGPT `file` citation becomes a canonical `type: "file"` resource with
  `resource_kind: "attachment"`, source file identity/name/source/snippet, and
  source-reference provenance;
- an exactly resolved hidden tool/file citation becomes a distinct
  `resource_kind: "retrieved_file"` resource instead of being forced into the
  uploaded-attachment shape; and
- an Assistant Markdown link whose destination is an evidenced `sandbox:/...`
  pointer becomes `type: "artifact"`, `resource_kind: "generated_file"`.

Citation objects that correspond to canonical file resources expose a
`resource_id` relationship to the resource rather than requiring downstream
consumers to reconstruct that relationship from ChatGPT metadata.

Generated-file artifacts preserve both forms of location that are useful for
different purposes:

- `source_pointer` and normalized `path` preserve the original ChatGPT sandbox
  identity/provenance; and
- `download_url` contains the deterministic ChatGPT HTTPS interpreter-download URL
  when the required conversation ID and Assistant message ID are available.

The conversation ID and Assistant message ID also remain in `resolution_context`
so the derivation is explicit and traceable.  Consumers therefore do not have to
call another provider-specific mapping layer or inspect the original ChatGPT JSON
to turn a `sandbox:` pointer into the shared HTTPS transport location.  Actual
authenticated retrieval of that URL remains outside the core.

The exported `chatgpt_conversation_metadata` JSONL wrapper supplies conversation
context and is not emitted as a conversation event.  Original JSONL source indices
are still retained for the actual conversation records, so provenance remains
traceable through the wrapper line.

No `available` flag or `resolved_url` is invented for sandbox artifacts merely
because a source pointer or deterministic download URL exists.  Those facts do not
prove that the host can currently retrieve the resource.  Browser-credential-
dependent access remains outside the core.

The sandbox-link scanner follows the evidence-backed balanced-parenthesis rule, so
a path such as `fixture(phase2).txt` remains one complete Markdown destination.
The `download_url` percent-encodes only characters needed to preserve URL/query
structure; safe characters such as the filename parentheses remain literal rather
than copying incidental over-encoding from the Python reference.

## ChatGPT images

ChatGPT `image_asset_pointer` parts are canonical ordered content.  They become
`type: "image"` blocks at their original provider `part_index`, with each block
referencing a canonical `type: "image"`, `resource_kind: "conversation_image"`
resource through `resource_id`.  Text parts around an image retain their actual
source positions, so `text -> image -> text` stays in that order rather than being
flattened into text plus attachment metadata.

For the verified ChatGPT image form, pointer identity is taken from
`metadata.asset_pointer_link`, then `asset_pointer_link`, then `asset_pointer`.
Evidenced `size_bytes`, `width`, and `height` are retained when present.  A missing
pointer is `status: "missing"`; an inline `data:image/...` pointer is already usable
and is `status: "available"`.  Other pointers do not receive a guessed status
before resolution evidence exists.

The verified image state domain also includes `unavailable` when a resolution
attempt has a resource identity but cannot obtain usable image data for a reason
other than established absence.  That multi-state distinction is intentional and
is documented in `NORMALIZATION_RULES.md`; it is not reduced to a boolean.

For `sediment://file_...`, the core may expose the deterministic ChatGPT
`https://chatgpt.com/backend-api/files/download/...` transport URL while retaining
the original source pointer.  The existence of that URL does not establish
availability.  Credential-bound fetching and browser recovery remain host
responsibilities, and a host may enrich the canonical image resource with the
resolved state/data without teaching downstream renderers ChatGPT-native fields.

Because earlier ChatGPT extraction counted only text parts, image normalization
also remaps citation/resource text ranges to the real provider `part_index` when
images precede referenced text.  This keeps canonical blocks, ranges, and source
provenance internally consistent.

## ChatGPT parent relationships

An explicit non-empty `metadata.parent_id` is preserved as
`relationships.parent_record_id`.  When that target record exists in the same input
dataset, the adapter also exposes its deterministic canonical event identity as
`relationships.parent_event_id`; otherwise the source parent ID remains preserved
and the canonical target is null.

These fields record source graph identity only.  The adapter does not derive a
parent from adjacency, chronology, role, `turn_exchange_id`, or `working_turn_id`,
and parent metadata does not reorder records or alter derived User/Assistant
turns.  Reverse child/branch structures can be derived later from the explicit
relationship if a consumer needs them.

## Tool calls and results

The canonical tool slice uses `tool_call` and `tool_result` events with structured
blocks.  The shared fields are:

- `call_id` when the provider supplies an explicit correlation identity;
- tool `name` when known;
- raw provider input or output payload without rendering it to Markdown first;
- provider-specific input/output format metadata where needed;
- source record/block provenance and source index; and
- `relationships.tool_call_id` when explicit source evidence supplies it.

Provider differences are preserved:

- ChatGPT code records addressed to a tool normalize as `tool_call`; tool-role
  execution-output and multimodal-result records normalize as `tool_result`.
  The established rich ChatGPT fixture does not expose an explicit call/result ID,
  so the adapter leaves `call_id` and `relationships.tool_call_id` null rather than
  inferring linkage from adjacency.
- `adaptClaudeToolEvents()` extracts `tool_use` and `tool_result` blocks. Claude's
  `tool_use.id` / `tool_result.tool_use_id` provide explicit correlation. Special
  tool names such as `Agent`, `AskUserQuestion`, and `ExitPlanMode` and their raw
  inputs remain intact so downstream projections can preserve their established
  special rendering.
- `adaptCodexToolEvents()` extracts `function_call`, `function_call_output`,
  `custom_tool_call`, and `custom_tool_call_output` response items. Codex
  `call_id` supplies explicit correlation. Tool-specific semantics such as
  `request_user_input` and `apply_patch` remain represented by their tool name and
  raw payload instead of being flattened into generic transcript text.

The Claude and Codex exports are deliberately named `adapt*ToolEvents`: these are
narrow migration slices, not claims that every provider record type has already
been normalized. Unknown/non-tool records must not be silently reclassified just
to make those adapters look complete.

Tool events do not create new derived User/Assistant turns merely because they are
present. Turn grouping remains a separately derived semantic and is changed only
when established provider behaviour requires it.

Each mapped ChatGPT event currently records:

- canonical `id` derived from the stable ChatGPT message ID;
- `provider`, `source_record_id`, and `source_index`;
- `kind`, `role`, `channel`, `visibility`, and source `content_type`;
- ordered structured content `blocks` with stable block IDs and source provenance,
  including image blocks at their original multimodal part positions;
- canonical `citations` where the source record contains an evidenced citation
  form;
- canonical `resources` for evidenced file/retrieved-file/sandbox-artifact/image
  forms;
- observed `turn_exchange_id`, `working_turn_id`, and explicit parent relationship
  values; and
- an explicit tool-call relationship field that remains null when the source has
  no explicit correlation ID.

`src/derive/turns.js` derives independently addressable turns from the ordered
canonical event stream. Ordinary message events start turns. Assistant commentary
joins the current Assistant turn when one already exists; if commentary appears
before the final Assistant message, it starts the Assistant turn and the later
Assistant message completes that same turn. A pre-final Assistant
`reasoning_summary` likewise starts or joins the still-open Assistant turn,
preserving source event order before commentary/final content. A reasoning event
observed after a completed Assistant message is not collapsed backward into that
completed turn. These are turn/event relationships, not User/Assistant pairing.

The duplicate-identity fixture protects two separate invariants:

1. provider relationship metadata does not change canonical event order; and
2. four distinct visible message events derive into four distinct turns.

It is not a User/Assistant-pair (UAP) association test. Any exchange or pairing
relationship is a later derived relationship and is not used to establish
canonical ordering or turn identity.

The rich ChatGPT fixture `tests/fixtures/chatgpt/chatgpt-direct.jsonl` is imported
from `AI-General-Memory/scripts/fixtures/chatgpt-direct.jsonl` at upstream blob SHA
`1631c0b4fe7b059759d3546f9c8ab54d6ae22c92`. The canonical rich ChatGPT golden is
recorded separately in `tests/canonical-golden-manifest.json`. Claude and Codex
tool tests use the checked-in privacy-safe fixtures whose real-evidence provenance
is pinned in the provider canonical/regression manifests.

This is intentionally not the complete provider schema. Additional hidden/system
records, branches, and additional content types must be added incrementally
against fixture/baseline evidence. Unsupported semantics must not be invented
merely to make the schema look complete.

The chronology invariant is explicit: source ordering is preserved unless real
provider evidence establishes a different required rule and that change is
separately tracked and tested.
