from pathlib import Path


def read(path):
  return Path(path).read_text(encoding="utf-8")


def write(path, text):
  Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(text, old, new, label):
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one occurrence, found {count}")
  return text.replace(old, new, 1)


# AdditionalRegressionTestRunner.cs
path = "AgentPanelSpeaker/AdditionalRegressionTestRunner.cs"
text = read(path)
text = replace_once(text,
  '("mapping/word-id-round-trip", TestWordIdRoundTrip),\n'
  '      ("mapping/unknown-word-id-rejected", TestUnknownWordId),',
  '("mapping/direct-speech-coordinate-round-trip", TestDirectSpeechCoordinateRoundTrip),\n'
  '      ("mapping/unknown-speech-coordinate-rejected", TestUnknownSpeechCoordinate),',
  "rename obsolete word-id tests")
text = text.replace('new(101, 1, "a", new[] { "same text" })',
                    'new(101, 1, new[] { "same text" })')
text = text.replace('new(202, 2, "b", new[] { "same text" })',
                    'new(202, 2, new[] { "same text" })')
text = replace_once(text,
  '    Require(index.TryResolveVoiceOrigin(101, 0, out int firstRecord, out string firstSource, out _),\n'
  '      "First repeated speech text did not resolve.");\n'
  '    Require(index.TryResolveVoiceOrigin(202, 0, out int secondRecord, out string secondSource, out _),\n'
  '      "Second repeated speech text did not resolve.");\n'
  '    Require(firstRecord == 1 && firstSource == "a",\n'
  '      "First repeated text mapped to the wrong record.");\n'
  '    Require(secondRecord == 2 && secondSource == "b",\n'
  '      "Second repeated text mapped to the wrong record.");',
  '    Require(index.TryResolveVoiceOrigin(101, 0, out int firstRecord, out int firstWord),\n'
  '      "First repeated speech text did not resolve.");\n'
  '    Require(index.TryResolveVoiceOrigin(202, 0, out int secondRecord, out int secondWord),\n'
  '      "Second repeated speech text did not resolve.");\n'
  '    Require(firstRecord == 1 && firstWord == 0,\n'
  '      "First repeated text mapped to the wrong record-local coordinate.");\n'
  '    Require(secondRecord == 2 && secondWord == 0,\n'
  '      "Second repeated text mapped to the wrong record-local coordinate.");',
  "migrate repeated text mapping")
start = text.index("  private static void TestWordIdRoundTrip()")
end = text.index("\n  private static void TestTranscriptSettingsPlacement()", start)
replacement = '''  private static void TestDirectSpeechCoordinateRoundTrip()
  {
    const string html =
      "<span class=\\"record-anchor\\" data-jsonl-record=\\"1\\" data-source-id=\\"a\\"></span><p>alpha beta</p>";
    TranscriptNodeIdentity[] identities =
    {
      new(77, 1, new[] { "alpha beta" })
    };
    TranscriptSearchIndex index = BuildSearchIndex(html, identities);
    for (int nodeWordIndex = 0; nodeWordIndex < 2; ++nodeWordIndex)
    {
      Require(index.TryResolveVoiceOrigin(
          77,
          nodeWordIndex,
          out int recordNumber,
          out int recordWordIndex),
        $"Speech coordinate 77:{nodeWordIndex} did not resolve.");
      Require(recordNumber == 1 && recordWordIndex == nodeWordIndex,
        $"Speech coordinate 77:{nodeWordIndex} mapped to the wrong record-local coordinate.");
    }
  }

  private static void TestUnknownSpeechCoordinate()
  {
    TranscriptSearchIndex index = BuildSearchIndex(
      "<p>alpha</p>",
      Array.Empty<TranscriptNodeIdentity>());
    Require(!index.TryResolveVoiceOrigin(
        long.MaxValue,
        0,
        out int recordNumber,
        out int recordWordIndex),
      "Unknown speech coordinate unexpectedly resolved.");
    Require(recordNumber == 0 && recordWordIndex == -1,
      "Unknown speech coordinate returned non-sentinel record coordinates.");
  }
'''
text = text[:start] + replacement + text[end:]
write(path, text)

# ExtendedRegressionTestRunner.cs
path = "AgentPanelSpeaker/ExtendedRegressionTestRunner.cs"
text = read(path)
text = text.replace('document.TryGetIndex(1, "id-1", out int first)',
                    'document.TryGetIndex(1, out int first)')
text = text.replace('document.TryGetIndex(100, "id-100", out int last)',
                    'document.TryGetIndex(100, out int last)')
write(path, text)

# Issue24ProductionPathRegressionTestRunner.cs
path = "AgentPanelSpeaker/Issue24ProductionPathRegressionTestRunner.cs"
text = read(path)
text = text.replace('new(201, 1, "one", new[] { "Primary thought." })',
                    'new(201, 1, new[] { "Primary thought." })')
text = text.replace('new(202, 2, "two", new[]',
                    'new(202, 2, new[]')
text = replace_once(text,
  '      window,\n      false,\n      null,\n      null,\n      null,\n      null,\n      null,\n      null,\n      null\n',
  '      window,\n      false,\n      null,\n      null,\n      null,\n      null,\n      null,\n      null\n',
  "migrate production window reflection args")
write(path, text)

# Issue24SpeechOrdinalRegressionTestRunner.cs
path = "AgentPanelSpeaker/Issue24SpeechOrdinalRegressionTestRunner.cs"
text = read(path)
text = replace_once(text,
  '        Require(searchIndex.TryResolveVoiceOrigin(\n'
  '            identity!.NodeId,\n'
  '            0,\n'
  '            out int recordNumber,\n'
  '            out string sourceId,\n'
  '            out int recordWordIndex),\n'
  '          $"Rendered transcript could not map speech segment \'{expectedSegment}\'.");\n'
  '        Require(recordNumber == identity.RecordNumber,\n'
  '          $"Speech segment \'{expectedSegment}\' mapped to record {recordNumber}, " +\n'
  '          $"expected {identity.RecordNumber}.");\n'
  '        Require(string.Equals(sourceId, identity.SourceId, StringComparison.Ordinal),\n'
  '          $"Speech segment \'{expectedSegment}\' mapped to source \'{sourceId}\', " +\n'
  '          $"expected \'{identity.SourceId}\'.");\n'
  '        Require(recordWordIndex >= 0,',
  '        Require(searchIndex.TryResolveVoiceOrigin(\n'
  '            identity!.NodeId,\n'
  '            0,\n'
  '            out int recordNumber,\n'
  '            out int recordWordIndex),\n'
  '          $"Rendered transcript could not map speech segment \'{expectedSegment}\'.");\n'
  '        Require(recordNumber == identity.RecordNumber,\n'
  '          $"Speech segment \'{expectedSegment}\' mapped to record {recordNumber}, " +\n'
  '          $"expected {identity.RecordNumber}.");\n'
  '        Require(recordWordIndex >= 0,',
  "migrate ordinal voice origin")
text = text.replace('new(201, 1, "one", new[] { "Primary thought." })',
                    'new(201, 1, new[] { "Primary thought." })')
text = text.replace('new(202, 2, "two", new[]',
                    'new(202, 2, new[]')
old = '''    IReadOnlyList<TranscriptRecordWordMap> maps =
      searchIndex.GetWordMaps(document.Records);
    Require(maps.Any(map => map.RecordNumber == 1 && map.SourceId == "one"),
      "Primary structural-unit record lost its stable word map.");
    Require(maps.Any(map => map.RecordNumber == 2 && map.SourceId == "two"),
      "Secondary structural-unit record was omitted from stable word maps.");
'''
new = '''    Require(searchIndex.TryResolveVoiceOrigin(
        202,
        0,
        out int secondaryRecordNumber,
        out int secondaryRecordWordIndex) &&
        secondaryRecordNumber == 2 &&
        secondaryRecordWordIndex >= 0,
      "Secondary structural-unit record lost its direct speech mapping.");
'''
text = replace_once(text, old, new, "replace structural stable-word-map assertion")
text = replace_once(text,
  '    Require(script.Contains("\\\"RecordNumber\\\":2", StringComparison.Ordinal) &&\n'
  '        script.Contains("\\\"SourceId\\\":\\\"two\\\"", StringComparison.Ordinal),\n'
  '      "Secondary structural-unit stable word map was omitted from the production DOM payload.");',
  '    Require(script.Contains("\\\"RecordNumber\\\":2", StringComparison.Ordinal),\n'
  '      "Secondary structural-unit record identity was omitted from the production DOM payload.");',
  "migrate structural DOM payload assertion")
text = text.replace("    SourceId: 'ordinal-test',\n", "")
write(path, text)

# Issue37VirtualWindowRegressionTestRunner.cs
path = "AgentPanelSpeaker/Issue37VirtualWindowRegressionTestRunner.cs"
text = read(path)
text = text.replace('document.TryGetIndex(11, "context", out int contextIndex)',
                    'document.TryGetIndex(11, out int contextIndex)')
text = text.replace(
  '        document.TryGetIndex(\n          playbackIdentity.RecordNumber,\n          playbackIdentity.SourceId,\n          out int playbackVirtualIndex),',
  '        document.TryGetIndex(\n          playbackIdentity.RecordNumber,\n          out int playbackVirtualIndex),')
text = text.replace(
  '        document.TryGetIndex(\n          tallIdentity.RecordNumber,\n          tallIdentity.SourceId,\n          out int tallIndex),',
  '        document.TryGetIndex(\n          tallIdentity.RecordNumber,\n          out int tallIndex),')
text = text.replace(
  '        "test-precondition",\n        null,\n        string.Empty,\n        null);',
  '        "test-precondition",\n        null,\n        null);')
# Other reflection invocations of RenderWindowForIndexAsync may have the same obsolete anchor source slot.
text = text.replace(
  '        reason,\n        anchorRecordNumber,\n        anchorSourceId,\n        anchorOffset)',
  '        reason,\n        anchorRecordNumber,\n        anchorOffset)')
write(path, text)

# Issue37WordMaterializationRegressionTestRunner.cs
path = "AgentPanelSpeaker/Issue37WordMaterializationRegressionTestRunner.cs"
text = read(path)
text = text.replace(
  '      \'.word[data-record-number="{{BulkRecordNumber}}"]\' +\n'
  '      \'[data-source-id="{{BulkSourceId}}"]\').length,',
  '      \'.word[data-record-number="{{BulkRecordNumber}}"]\').length,')
text = text.replace(
  '      \'.word[data-record-number="{{SpeechRecordNumber}}"]\' +\n'
  '      \'[data-source-id="{{SpeechSourceId}}"]\').length,',
  '      \'.word[data-record-number="{{SpeechRecordNumber}}"]\').length,')
text = text.replace(
  '      \'.word[data-record-number="{{BulkRecordNumber}}"]\' +\n'
  '      \'[data-source-id="{{BulkSourceId}}"]\').length,',
  '      \'.word[data-record-number="{{BulkRecordNumber}}"]\').length,')
text = text.replace(
  '        SpeechNodeId,\n        SpeechRecordNumber,\n        SpeechSourceId,\n        new[] { SpeechFragment })',
  '        SpeechNodeId,\n        SpeechRecordNumber,\n        new[] { SpeechFragment })')
text = text.replace(
  '    Require(document.TryGetIndex(\n        SpeechRecordNumber,\n        SpeechSourceId,\n        out int speechIndex),',
  '    Require(document.TryGetIndex(\n        SpeechRecordNumber,\n        out int speechIndex),')
text = text.replace(
  '    Require(window.Records.Any(record => record.Identities.Any(identity =>\n'
  '        identity.RecordNumber == BulkRecordNumber &&\n'
  '        string.Equals(identity.SourceId, BulkSourceId, StringComparison.Ordinal))),',
  '    Require(window.Records.Any(record => record.Identities.Any(identity =>\n'
  '        identity.RecordNumber == BulkRecordNumber)),')
text = text.replace(
  '  /// an unvoiced record must lazily materialize that record\'s stable word spans\n',
  '  /// an unvoiced record must lazily materialize that record\'s word spans\n')
text = replace_once(text,
  '      window,\n      false,\n      null,\n      null,\n      null,\n      null,\n      null,\n      null,\n      null\n',
  '      window,\n      false,\n      null,\n      null,\n      null,\n      null,\n      null,\n      null\n',
  "migrate word-materialization reflection args")
write(path, text)

# Issue44IndependentOutputOracleRegressionTestRunner.cs
path = "AgentPanelSpeaker/Issue44IndependentOutputOracleRegressionTestRunner.cs"
text = read(path)
text = text.replace('document.TryGetIndex(11, "context", out int contextIndex)',
                    'document.TryGetIndex(11, out int contextIndex)')
write(path, text)

# RegressionTestRunner.cs: keep Core provenance HTML assertions, but downstream lookup is record-only.
path = "AgentPanelSpeaker/RegressionTestRunner.cs"
text = read(path)
for record, source in [(2, "thought-one"), (3, "thought-two"), (1, "regression-user"), (4, "regression-final")]:
  text = text.replace(f'document.TryGetIndex({record}, "{source}", out ',
                      f'document.TryGetIndex({record}, out ')
text = text.replace('document.TryGetIndex(999, "missing", out _)',
                    'document.TryGetIndex(999, out _)')
write(path, text)

print("Issue #65 legacy regressions migrated to the simplified identity contract.")
