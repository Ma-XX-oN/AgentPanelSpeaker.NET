from pathlib import Path

p = Path('AgentPanelSpeaker/Issue24SpeechOrdinalRegressionTestRunner.cs')
s = p.read_text(encoding='utf-8')

old = '''      ("speech-ordinals/whole-list-item-highlight", TestWholeListItemHighlight),
      ("speech-ordinals/nested-production-highlight", TestNestedProductionHighlight)
'''
new = '''      ("speech-ordinals/whole-list-item-highlight", TestWholeListItemHighlight),
      ("speech-ordinals/nested-production-highlight", TestNestedProductionHighlight),
      ("speech-ordinals/structural-unit-playback-mapping", TestStructuralUnitPlaybackMapping)
'''
if s.count(old) != 1:
  raise SystemExit(f'test registration target count={s.count(old)}')
s = s.replace(old, new, 1)

marker = '''  /// <summary>
  /// Pumps the Windows message queue until one WebView task completes.
  /// </summary>
'''
method = r'''  /// <summary>
  /// Verifies that a virtual structural unit containing multiple canonical
  /// record anchors carries every contained record's node identities and stable
  /// word map into the exact production replaceTranscriptDom payload.  The
  /// user's nested-list failure occurs when a secondary identity is omitted:
  /// punctuation-heavy nested Markdown then misses the lexical node mapping and
  /// playback falls back to a stale outer-list range.
  /// </summary>
  private static void TestStructuralUnitPlaybackMapping()
  {
    const string html = """
<details>
  <summary>Having 2 thoughts</summary>
  <span class="record-anchor" data-jsonl-record="1" data-source-id="one"></span>
  <p>Primary thought.</p>
  <span class="record-anchor" data-jsonl-record="2" data-source-id="two"></span>
  <ol>
    <li data-list-ordinal="1"><span class="speech-ordinal-map" data-list-ordinal="1">1. </span><em>Nested numbered item with a table inside it</em>
      <table><thead><tr><th>Column</th><th>Value</th><th>Style</th></tr></thead>
      <tbody><tr><td>Alpha</td><td>1</td><td>bold</td></tr></tbody></table>
    </li>
  </ol>
</details>
""";

    TranscriptVirtualDocument document = TranscriptVirtualDocument.Build(html);
    Require(document.Records.Count == 1,
      "Fixture did not create one atomic multi-record structural unit.");
    Require(document.Records[0].Identities.Count == 2,
      "Fixture did not retain both canonical record identities in the structural unit.");

    TranscriptNodeIdentity[] identities =
    {
      new(201, 1, "one", new[] { "Primary thought." }),
      new(202, 2, "two", new[]
      {
        "1. *Nested numbered item with a table inside it*",
        "| Column | Value | Style |"
      })
    };
    TranscriptSearchIndex searchIndex = TranscriptSearchIndex.Build(
      html,
      identities,
      CancellationToken.None);

    IReadOnlyList<TranscriptRecordWordMap> maps =
      searchIndex.GetWordMaps(document.Records);
    Require(maps.Any(map => map.RecordNumber == 1 && map.SourceId == "one"),
      "Primary structural-unit record lost its stable word map.");
    Require(maps.Any(map => map.RecordNumber == 2 && map.SourceId == "two"),
      "Secondary structural-unit record was omitted from stable word maps.");

    using var view = new TranscriptView();
    FieldInfo? identitiesField = typeof(TranscriptView).GetField(
      "_identities",
      BindingFlags.NonPublic | BindingFlags.Instance);
    FieldInfo? searchIndexField = typeof(TranscriptView).GetField(
      "_searchIndex",
      BindingFlags.NonPublic | BindingFlags.Instance);
    MethodInfo? buildScript = typeof(TranscriptView).GetMethod(
      "BuildReplaceDomScript",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Require(identitiesField is not null && searchIndexField is not null &&
        buildScript is not null,
      "Production transcript payload members could not be located.");
    identitiesField!.SetValue(view, identities);
    searchIndexField!.SetValue(view, searchIndex);

    string? script = buildScript!.Invoke(view, new object?[]
    {
      document.CreateFullWindow(),
      Array.Empty<TranscriptDomNode>(),
      false,
      null,
      null
    }) as string;
    Require(!string.IsNullOrWhiteSpace(script),
      "Production BuildReplaceDomScript returned no script.");
    Require(script!.Contains("\"NodeId\":202", StringComparison.Ordinal),
      "Secondary structural-unit node identity was omitted from the production DOM payload.");
    Require(script.Contains("\"RecordNumber\":2", StringComparison.Ordinal) &&
        script.Contains("\"SourceId\":\"two\"", StringComparison.Ordinal),
      "Secondary structural-unit stable word map was omitted from the production DOM payload.");
  }

'''
if s.count(marker) != 1:
  raise SystemExit(f'method insertion marker count={s.count(marker)}')
s = s.replace(marker, method + marker, 1)
p.write_text(s, encoding='utf-8', newline='\n')
