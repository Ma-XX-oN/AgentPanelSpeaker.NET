using System.Reflection;
using System.Runtime.CompilerServices;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #65 transcript identity simplification.
/// </summary>
internal static class Issue65RedundancyRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #65 redundancy/performance regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("redundancy/window-replacement-does-not-embed-search-word-maps",
        TestWindowReplacementDoesNotEmbedSearchWordMaps),
      ("redundancy/downstream-record-identity-is-record-number-only",
        TestDownstreamRecordIdentityIsRecordNumberOnly),
      ("redundancy/search-contract-has-no-global-word-id-layer",
        TestSearchContractHasNoGlobalWordIdLayer),
      ("redundancy/browser-record-address-is-record-number-only",
        TestBrowserRecordAddressIsRecordNumberOnly),
      ("redundancy/find-result-retains-direct-coordinates",
        TestFindResultRetainsDirectCoordinates)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #65 transcript-redundancy suite: {tests.Length} tests");
    foreach ((string name, Action body) in tests)
    {
      try
      {
        body();
        Console.WriteLine($"PASS  {name}");
      }
      catch (Exception exception)
      {
        ++failures;
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine(
          $"      {exception.GetType().Name}: {exception.Message}");
      }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
      ? $"PASS: {tests.Length}/{tests.Length} issue #65 regressions passed."
      : $"FAIL: {failures}/{tests.Length} issue #65 regressions failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Ordinary virtual-window rendering must not serialize search-owned word
  /// maps into JavaScript.  Search results are requested separately and carry
  /// only the coordinates needed for the requested match.
  /// </summary>
  private static void TestWindowReplacementDoesNotEmbedSearchWordMaps()
  {
    const string html =
      "<span class=\"record-anchor\" data-jsonl-record=\"1\" " +
      "data-source-id=\"source-1\"></span><p>alpha beta gamma</p>";
    TranscriptSearchIndex index = TranscriptSearchIndex.Build(
      html,
      Array.Empty<TranscriptNodeIdentity>(),
      CancellationToken.None);
    TranscriptWindow window = TranscriptVirtualDocument.Build(html)
      .CreateFullWindow();

    object view = RuntimeHelpers.GetUninitializedObject(typeof(TranscriptView));
    SetField(view, "_searchIndex", index);
    SetField(
      view,
      "_identities",
      (IReadOnlyList<TranscriptNodeIdentity>)Array.Empty<TranscriptNodeIdentity>());

    MethodInfo method = typeof(TranscriptView).GetMethod(
      "BuildReplaceWindowScript",
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "TranscriptView.BuildReplaceWindowScript was not found.");
    object?[] arguments = new object?[method.GetParameters().Length];
    arguments[0] = window;
    arguments[1] = false;
    string script = method.Invoke(view, arguments) as string ??
      throw new InvalidOperationException(
        "BuildReplaceWindowScript did not return JavaScript.");

    Require(
      !script.Contains("\"WordId\"", StringComparison.Ordinal) &&
      !script.Contains("\"Words\"", StringComparison.Ordinal),
      "Virtual-window replacement still embeds search-owned per-word maps " +
      "into the C# -> JavaScript ExecuteScript payload.");
  }

  /// <summary>
  /// Every transcript source record has a record number.  Optional provider
  /// provenance must not be duplicated into downstream record identity types.
  /// </summary>
  private static void TestDownstreamRecordIdentityIsRecordNumberOnly()
  {
    RequireNoProperty(typeof(TranscriptNodeIdentity), "SourceId");
    RequireNoProperty(typeof(TranscriptVirtualIdentity), "SourceId");
    RequireNoProperty(typeof(TranscriptVirtualRecord), "SourceId");
    RequireNoProperty(typeof(TranscriptSearchMatch), "SourceId");
  }

  /// <summary>
  /// Find does not need a second global word-ID namespace.  Match coordinates
  /// and authoritative speech node/word coordinates are already sufficient.
  /// </summary>
  private static void TestSearchContractHasNoGlobalWordIdLayer()
  {
    RequireNoProperty(typeof(TranscriptSearchMatch), "WordIds");
    RequireNoProperty(typeof(TranscriptSearchMatch), "SeekWordId");
    Require(
      typeof(TranscriptSearchIndex).GetMethod(
        "GetWordMaps",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null,
      "TranscriptSearchIndex still exposes GetWordMaps for redundant DOM maps.");
    Require(
      typeof(TranscriptSearchIndex).GetMethod(
        "TryResolveSpeechWord",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null,
      "TranscriptSearchIndex still exposes the redundant WordId seek fallback.");
    Require(
      typeof(TranscriptSearchIndex).GetField(
        "_wordsById",
        BindingFlags.Instance | BindingFlags.NonPublic) is null,
      "TranscriptSearchIndex still stores the redundant global WordId map.");
  }

  /// <summary>
  /// Browser-side rendered-word addressing must use record number plus the
  /// record-relative word index, not provider source IDs.
  /// </summary>
  private static void TestBrowserRecordAddressIsRecordNumberOnly()
  {
    MethodInfo method = typeof(TranscriptView).GetMethod(
      "BuildShellHtml",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("TranscriptView.BuildShellHtml was not found.");
    string shell = method.Invoke(null, null) as string ??
      throw new InvalidOperationException("BuildShellHtml did not return HTML.");

    Require(
      !shell.Contains("dataset.sourceId", StringComparison.Ordinal) &&
      !shell.Contains("data-source-id", StringComparison.Ordinal) &&
      !shell.Contains("makeRecordKey(recordNumber, sourceId)", StringComparison.Ordinal),
      "Browser-side record/word addressing still depends on sourceId.");
  }

  /// <summary>
  /// The simplified Find result must retain the direct record-local highlight
  /// coordinates and authoritative speech coordinates needed by the UI.
  /// </summary>
  private static void TestFindResultRetainsDirectCoordinates()
  {
    Type type = typeof(TranscriptSearchMatch);
    RequireProperty(type, "RecordNumber");
    RequireProperty(type, "StartWordIndex");
    RequireProperty(type, "EndWordIndex");
    RequireProperty(type, "NodeId");
    RequireProperty(type, "NodeWordIndex");
  }

  private static void SetField(object target, string fieldName, object? value)
  {
    FieldInfo field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field '{fieldName}' was not found on {target.GetType().Name}.");
    field.SetValue(target, value);
  }

  private static void RequireNoProperty(Type type, string propertyName)
  {
    Require(
      type.GetProperty(
        propertyName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null,
      $"{type.Name} still exposes redundant {propertyName}.");
  }

  private static void RequireProperty(Type type, string propertyName)
  {
    Require(
      type.GetProperty(
        propertyName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null,
      $"{type.Name} no longer exposes required {propertyName}.");
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
