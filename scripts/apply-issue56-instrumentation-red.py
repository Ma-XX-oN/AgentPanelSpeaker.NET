from pathlib import Path

path = Path('AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs')
text = path.read_text(encoding='utf-8')

old_tests = '''      ("real-session/stale-window-shift-is-coalesced",\n        TestStaleWindowShiftIsCoalesced),\n      ("real-session/directional-shift-keeps-prefetch-headroom",\n        TestDirectionalShiftKeepsPrefetchHeadroom),'''
new_tests = '''      ("real-session/stale-window-shift-is-coalesced",\n        TestStaleWindowShiftIsCoalesced),\n      ("real-session/window-replacement-emits-transaction-diagnostics",\n        TestWindowReplacementInstrumentationContract),\n      ("real-session/directional-shift-keeps-prefetch-headroom",\n        TestDirectionalShiftKeepsPrefetchHeadroom),'''
if text.count(old_tests) != 1:
  raise SystemExit('Expected test-list anchor exactly once.')
text = text.replace(old_tests, new_tests, 1)

marker = '''  /// <summary>\n  /// Reproduces the remaining issue #56 scroll-ahead deficit. Once a downward\n'''
method = r'''  /// <summary>
  /// Locks the issue #56 diagnostic contract used to distinguish C# window
  /// construction, browser mapping, DOM/layout, and anchor-restoration cost.
  /// Instrumentation must report one correlated transaction without changing
  /// the virtual-window navigation result.
  /// </summary>
  private static void TestWindowReplacementInstrumentationContract()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue56-instrumentation-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue56-instrumentation.jsonl");
    WriteFixture(path);

    try
    {
      using var host = CreateOffscreenHost();
      using var view = new TranscriptView { Dock = DockStyle.Fill };
      host.Controls.Add(view);
      host.Show();
      _ = host.Handle;
      _ = view.Handle;
      WaitForViewInitialization(view);
      view.ApplySettings(
        TranscriptSettings.Default with { FollowSpeech = false },
        dark: false);

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      string? diagnosticJson = null;
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (rootElement.TryGetProperty("type", out JsonElement typeElement) &&
              typeElement.ValueKind == JsonValueKind.String &&
              typeElement.GetString() == "window-transaction-diagnostic")
          {
            diagnosticJson = eventArgs.WebMessageAsJson;
          }
        }
        catch (JsonException)
        {
        }
      };

      view.SelectSession(
        path,
        AgentSource.Codex,
        "Issue 56 instrumentation fixture");
      WaitForTranscriptRender(view);
      PumpUntil(
        () => ReadNullableField<TranscriptSearchIndex>(view, "_searchIndex") is not null,
        "search index for issue #56 instrumentation fixture");

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int middleIndex = document.Count / 2;
      int beforeStart = ReadField<int>(view, "_windowStartIndex");
      int beforeEnd = ReadField<int>(view, "_windowEndIndex");
      Require(
        middleIndex < beforeStart || middleIndex > beforeEnd,
        "Issue #56 instrumentation fixture did not leave its middle unit unloaded.");

      Task replacement = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "instrumentation-contract",
        null,
        string.Empty,
        null);
      PumpUntilCompleted(replacement, "instrumented issue #56 replacement");
      PumpUntil(
        () => diagnosticJson is not null,
        "window replacement transaction diagnostic",
        timeoutMilliseconds: 5000);

      using JsonDocument diagnostic = JsonDocument.Parse(diagnosticJson!);
      JsonElement rootElement = diagnostic.RootElement;
      string[] requiredNumericFields =
      {
        "transactionId",
        "requestSequence",
        "startIndex",
        "endIndex",
        "recordCount",
        "nodeCount",
        "wordMapCount",
        "wordCount",
        "beforeScrollY",
        "beforeViewportHeight",
        "beforeDocumentHeight",
        "beforeTopSpacerHeight",
        "beforeBottomSpacerHeight",
        "beforeVisibleStartIndex",
        "beforeVisibleEndIndex",
        "afterScrollY",
        "afterViewportHeight",
        "afterDocumentHeight",
        "afterTopSpacerHeight",
        "afterBottomSpacerHeight",
        "afterVisibleStartIndex",
        "afterVisibleEndIndex",
        "innerHtmlMilliseconds",
        "wrapWordsMilliseconds",
        "recordScopesMilliseconds",
        "nodeScopesMilliseconds",
        "stableWordScopesMilliseconds",
        "mappingSummaryMilliseconds",
        "measurementMilliseconds",
        "anchorRestoreMilliseconds",
        "totalMilliseconds"
      };
      foreach (string field in requiredNumericFields)
      {
        Require(
          rootElement.TryGetProperty(field, out JsonElement value) &&
          value.ValueKind == JsonValueKind.Number,
          $"Instrumentation diagnostic omitted numeric field '{field}'.");
      }
      Require(
        rootElement.GetProperty("transactionId").GetInt64() > 0,
        "Instrumentation diagnostic did not carry a positive transaction ID.");
      Require(
        rootElement.TryGetProperty("reason", out JsonElement reasonElement) &&
        reasonElement.GetString() == "instrumentation-contract",
        "Instrumentation diagnostic lost the replacement reason.");
      Require(
        rootElement.TryGetProperty("searchIndexAvailable", out JsonElement searchElement) &&
        searchElement.ValueKind == JsonValueKind.True,
        "Instrumentation diagnostic did not report the completed search index.");
      Require(
        rootElement.GetProperty("startIndex").GetInt32() <= middleIndex &&
        rootElement.GetProperty("endIndex").GetInt32() >= middleIndex,
        "Instrumentation changed the requested virtual-window navigation result.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

'''
if text.count(marker) != 1:
  raise SystemExit('Expected method insertion anchor exactly once.')
text = text.replace(marker, method + marker, 1)

helper_marker = '''  private static T ReadField<T>(object target, string fieldName)\n  {\n'''
helper = '''  private static T? ReadNullableField<T>(object target, string fieldName)\n    where T : class\n  {\n    FieldInfo field = target.GetType().GetField(\n      fieldName,\n      BindingFlags.Instance | BindingFlags.NonPublic) ??\n      throw new InvalidOperationException(\n        $"Field '{fieldName}' was not found on {target.GetType().Name}.");\n    return field.GetValue(target) as T;\n  }\n\n'''
if text.count(helper_marker) != 1:
  raise SystemExit('Expected helper insertion anchor exactly once.')
text = text.replace(helper_marker, helper + helper_marker, 1)

path.write_text(text, encoding='utf-8')
