from pathlib import Path


def read(path):
  return Path(path).read_text(encoding="utf-8")


def write(path, text):
  Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_exact(text, old, new, expected, label):
  count = text.count(old)
  if count != expected:
    raise RuntimeError(f"{label}: expected {expected} occurrences, found {count}")
  return text.replace(old, new)


# Issue #24: preserve the structural-unit assertion without inventing a
# node-word-zero requirement.  Search the secondary record directly by its
# record-local coordinates, then retain the production DOM identity assertion.
path = "AgentPanelSpeaker/Issue24SpeechOrdinalRegressionTestRunner.cs"
text = read(path)
old = '''    Require(searchIndex.TryResolveVoiceOrigin(
        202,
        0,
        out int secondaryRecordNumber,
        out int secondaryRecordWordIndex) &&
        secondaryRecordNumber == 2 &&
        secondaryRecordWordIndex >= 0,
      "Secondary structural-unit record lost its direct speech mapping.");
'''
new = '''    IReadOnlyList<TranscriptSearchMatch> secondaryMatches = searchIndex
      .SearchAsync(
        new TranscriptSearchRequest(
          1,
          "Nested numbered item",
          CaseSensitive: false,
          WholeWord: false,
          Regex: false,
          VoicedOnly: false),
        CancellationToken.None)
      .GetAwaiter()
      .GetResult();
    Require(secondaryMatches.Any(match =>
        match.RecordNumber == 2 &&
        match.StartWordIndex >= 0 &&
        match.EndWordIndex >= match.StartWordIndex),
      "Secondary structural-unit record lost its direct record-local search mapping.");
'''
text = replace_exact(text, old, new, 1, "issue24 secondary direct mapping")
write(path, text)

# Issue #37: private reflection tests must call the deliberately simplified
# record-number-only signatures, rather than preserving the removed source-id
# parameter as a compatibility shim.
path = "AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs"
text = read(path)
old = '''      Task shiftTask = InvokeTask(
      view,
      "RenderWindowForIndexAsync",
      0,
      "scroll-up",
      null,
      string.Empty,
      null);'''
new = '''      Task shiftTask = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        0,
        "scroll-up",
        null,
        null);'''
text = replace_exact(text, old, new, 1, "issue37 initial scroll reflection")
old = '''      Task measuredMiddleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        document.Count / 2,
        "test-measured-refinement",
        null,
        string.Empty,
        null);'''
new = '''      Task measuredMiddleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        document.Count / 2,
        "test-measured-refinement",
        null,
        null);'''
text = replace_exact(text, old, new, 1, "issue37 measured reflection")
old = '''          physicalWindow,
          false,
          null,
          null,
          null,
          null,
          null,
          null,
          null
'''
new = '''          physicalWindow,
          false,
          null,
          null,
          null,
          null,
          null,
          null
'''
text = replace_exact(text, old, new, 1, "issue37 window-script reflection")
write(path, text)

# Real-session regressions: update the same private call contract and remove
# diagnostic fields that belonged solely to the deleted global word-map path.
path = "AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs"
text = read(path)
old = '''        middleIndex,
        "test-precondition",
        null,
        string.Empty,
        null);'''
new = '''        middleIndex,
        "test-precondition",
        null,
        null);'''
text = replace_exact(text, old, new, 2, "issue54 precondition reflections")
old = '''        middleIndex,
        "instrumentation-contract",
        null,
        string.Empty,
        null);'''
new = '''        middleIndex,
        "instrumentation-contract",
        null,
        null);'''
text = replace_exact(text, old, new, 1, "issue54 instrumentation reflection")
text = replace_exact(text, '        "wordMapCount",\n', '', 1,
                     "issue54 word-map diagnostic field")
text = replace_exact(text, '        "stableWordScopesMilliseconds",\n', '', 1,
                     "issue54 stable-word diagnostic field")
text = replace_exact(text, "    anchorSourceId:anchor.dataset.sourceId || '',\n", '', 1,
                     "issue54 obsolete anchor source message")
write(path, text)

# Issue #60: the invariant is deferred index construction, not attachment of
# the deleted stable-word-ID table.  Verify the initial browser window remains
# materialized and unchanged while the completed C# index can execute a search.
path = "AgentPanelSpeaker/Issue60StartupPerformanceRegressionTestRunner.cs"
text = read(path)
text = replace_exact(
  text,
  '''  /// The visible initial window must be allowed to appear before the full-file
  /// search corpus is ready. Search-index construction can continue after that
  /// point, and its stable word map must subsequently attach to the already
  /// rendered browser window without replacing that window.
''',
  '''  /// The visible initial window must be allowed to appear before the full-file
  /// search corpus is ready. Search-index construction can continue afterward
  /// without replacing the already-rendered window, and the completed C# index
  /// must remain immediately usable without installing per-word browser maps.
''',
  1,
  "issue60 comment")
old = '''      PumpUntil(
        () => ReadNullableField(view, "_searchIndex") is not null,
        "deferred search index to complete",
        timeoutMilliseconds: 60000);
      PumpUntil(
        () => ReadScriptInt(
          webView,
          "document.querySelectorAll('.word[data-word-id]').length") > 0,
        "deferred stable word maps to attach to the visible window",
        timeoutMilliseconds: 30000);
'''
new = '''      int initialWindowStart = ReadField<int>(view, "_windowStartIndex");
      int initialWindowEnd = ReadField<int>(view, "_windowEndIndex");
      int initialWordCount = ReadScriptInt(
        webView,
        "document.querySelectorAll('.word').length");
      Require(initialWordCount > 0,
        "Initial visible transcript window contained no materialized words.");

      PumpUntil(
        () => ReadNullableField(view, "_searchIndex") is not null,
        "deferred search index to complete",
        timeoutMilliseconds: 60000);
      Require(
        ReadField<int>(view, "_windowStartIndex") == initialWindowStart &&
        ReadField<int>(view, "_windowEndIndex") == initialWindowEnd,
        "Deferred search-index completion replaced the already-visible window.");
      Require(ReadScriptInt(
          webView,
          "document.querySelectorAll('.word').length") == initialWordCount,
        "Deferred search-index completion rematerialized the visible word DOM.");

      TranscriptSearchIndex searchIndex =
        (TranscriptSearchIndex?)ReadNullableField(view, "_searchIndex") ??
        throw new InvalidOperationException(
          "Deferred search index disappeared after completion.");
      IReadOnlyList<TranscriptSearchMatch> matches = searchIndex.SearchAsync(
          new TranscriptSearchRequest(
            1,
            "issue60-search-token-100",
            CaseSensitive: false,
            WholeWord: false,
            Regex: false,
            VoicedOnly: false),
          CancellationToken.None)
        .GetAwaiter()
        .GetResult();
      Require(matches.Count > 0,
        "Deferred C# search index was not usable after visible rendering.");
'''
text = replace_exact(text, old, new, 1, "issue60 stable-map wait")
write(path, text)

print("Migrated full-suite regressions to the issue #65 record-local contract.")
