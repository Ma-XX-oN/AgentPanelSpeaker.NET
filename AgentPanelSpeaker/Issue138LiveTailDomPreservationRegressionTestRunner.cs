using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Regression coverage for issue #138 live-tail DOM preservation.
/// </summary>
internal static class Issue138LiveTailDomPreservationRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #138 browser-lifetime acceptance suite.
  /// </summary>
  /// <returns>Zero when every acceptance oracle passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("live-tail-dom/unchanged-playback-node-retains-identity",
        TestUnchangedPlaybackNodeRetainsIdentity),
      ("live-tail-dom/changed-record-is-replaced-and-tail-remains-unique",
        TestChangedRecordIsReplacedAndTailRemainsUnique),
      ("live-tail-dom/legacy-window-retains-full-replacement-path",
        TestLegacyWindowRetainsFullReplacementPath),
      ("live-tail-dom/playback-message-continues-during-refresh",
        TestPlaybackMessageContinuesDuringRefresh)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #138 live-tail DOM acceptance suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #138 tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #138 tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Adding a live tail record must not replace an unchanged Core unit that
  /// owns the active canonical playback word.  DOM object identity is the
  /// invariant; merely recreating equivalent markup is insufficient.
  /// </summary>
  private static void TestUnchangedPlaybackNodeRetainsIdentity()
  {
    using var host = CreateOffscreenHost();
    using var view = CreateInitializedView(host);
    WebView2 webView = ReadField<WebView2>(view, "_webView");

    JsonElement result = ExecuteJsonProbe(webView, """
(() => {
  const stable =
    '<section class="virtual-record" data-virtual-index="10">' +
    '<span hidden class="aicore-structural-unit" data-aicore-unit-id="turn:stable" ' +
    'data-aicore-source-record-ids="10"></span>' +
    '<span class="record-anchor" data-jsonl-record="10"></span>' +
    '<p><span id="word-100" data-word-id="100">stable</span> text</p>' +
    '</section>';
  const tail =
    '<section class="virtual-record" data-virtual-index="11">' +
    '<span hidden class="aicore-structural-unit" data-aicore-unit-id="turn:tail" ' +
    'data-aicore-source-record-ids="11"></span>' +
    '<span class="record-anchor" data-jsonl-record="11"></span>' +
    '<p>new tail</p></section>';

  replaceTranscriptWindow(stable, false, [], 10, 10, 100, 100);
  const beforeRecord = document.querySelector(
    '.virtual-record[data-virtual-index="10"]');
  const beforeWord = document.getElementById('word-100');
  retainedPlayback = {
    state:'speaking', fragmentText:'', wordIndex:0, wordText:'stable',
    nodeId:0, wordId:100, wordIds:null, fragmentId:null,
    highlightMode:'word'
  };
  setPlayback('speaking', '', 0, 'stable', 0, true, 100, null, null, 'word');
  const activeBefore = beforeWord?.classList.contains('active') === true;

  replaceTranscriptWindow(stable + tail, true, [], 10, 11, 100, 50);
  const afterRecord = document.querySelector(
    '.virtual-record[data-virtual-index="10"]');
  const afterWord = document.getElementById('word-100');
  return JSON.stringify({
    activeBefore,
    sameRecord: beforeRecord === afterRecord,
    sameWord: beforeWord === afterWord,
    activeAfter: afterWord?.classList.contains('active') === true,
    tailCount: document.querySelectorAll(
      '[data-aicore-unit-id="turn:tail"]').length
  });
})()
""");

    Require(result.GetProperty("activeBefore").GetBoolean(),
      "Issue #138 fixture did not establish active canonical playback.");
    Require(result.GetProperty("sameRecord").GetBoolean(),
      "Unchanged live-tail Core record was replaced instead of retaining DOM identity.");
    Require(result.GetProperty("sameWord").GetBoolean(),
      "Active canonical word DOM node was replaced during live-tail growth.");
    Require(result.GetProperty("activeAfter").GetBoolean(),
      "Active canonical word lost its playback marker during live-tail growth.");
    Require(result.GetProperty("tailCount").GetInt32() == 1,
      "Live-tail growth did not add exactly one new tail unit.");
  }

  /// <summary>
  /// Reconciliation may preserve only source-equivalent units.  A changed unit
  /// must be replaced, while an immediately repeated identical update must then
  /// retain that new node and must not duplicate the appended tail.
  /// </summary>
  private static void TestChangedRecordIsReplacedAndTailRemainsUnique()
  {
    using var host = CreateOffscreenHost();
    using var view = CreateInitializedView(host);
    WebView2 webView = ReadField<WebView2>(view, "_webView");

    JsonElement result = ExecuteJsonProbe(webView, """
(() => {
  const first =
    '<section class="virtual-record" data-virtual-index="5">' +
    '<span hidden class="aicore-structural-unit" data-aicore-unit-id="turn:mutable" ' +
    'data-aicore-source-record-ids="5"></span>' +
    '<span class="record-anchor" data-jsonl-record="5"></span>' +
    '<p id="mutable-value">before</p></section>';
  const changed =
    '<section class="virtual-record" data-virtual-index="5">' +
    '<span hidden class="aicore-structural-unit" data-aicore-unit-id="turn:mutable" ' +
    'data-aicore-source-record-ids="5"></span>' +
    '<span class="record-anchor" data-jsonl-record="5"></span>' +
    '<p id="mutable-value">after</p></section>';
  const tail =
    '<section class="virtual-record" data-virtual-index="6">' +
    '<span hidden class="aicore-structural-unit" data-aicore-unit-id="turn:once" ' +
    'data-aicore-source-record-ids="6"></span>' +
    '<span class="record-anchor" data-jsonl-record="6"></span>' +
    '<p>tail once</p></section>';

  replaceTranscriptWindow(first, false, [], 5, 5, 100, 100);
  const before = document.querySelector('.virtual-record[data-virtual-index="5"]');
  replaceTranscriptWindow(changed + tail, true, [], 5, 6, 100, 50);
  const after = document.querySelector('.virtual-record[data-virtual-index="5"]');
  const textAfterChange = document.getElementById('mutable-value')?.textContent || '';
  replaceTranscriptWindow(changed + tail, true, [], 5, 6, 100, 50);
  const afterRepeat = document.querySelector('.virtual-record[data-virtual-index="5"]');
  return JSON.stringify({
    changedWasReplaced: before !== after,
    textAfterChange,
    repeatedIdentityStable: after === afterRepeat,
    tailCount: document.querySelectorAll(
      '[data-aicore-unit-id="turn:once"]').length
  });
})()
""");

    Require(result.GetProperty("changedWasReplaced").GetBoolean(),
      "Changed live-tail unit was incorrectly preserved.");
    Require(string.Equals(
        result.GetProperty("textAfterChange").GetString(),
        "after",
        StringComparison.Ordinal),
      "Changed live-tail unit did not install its new content.");
    Require(result.GetProperty("repeatedIdentityStable").GetBoolean(),
      "Repeated identical live-tail update replaced an unchanged unit.");
    Require(result.GetProperty("tailCount").GetInt32() == 1,
      "Repeated live-tail update duplicated the appended tail unit.");
  }

  /// <summary>
  /// Virtual windows without Core unit identity retain the established full
  /// replacement path.  They must render correctly without inventing a keyed
  /// reconciliation identity from record numbers or visible text.
  /// </summary>
  private static void TestLegacyWindowRetainsFullReplacementPath()
  {
    using var host = CreateOffscreenHost();
    using var view = CreateInitializedView(host);
    WebView2 webView = ReadField<WebView2>(view, "_webView");

    JsonElement result = ExecuteJsonProbe(webView, """
(() => {
  const first =
    '<section class="virtual-record" data-virtual-index="2">' +
    '<span class="record-anchor" data-jsonl-record="3"></span>' +
    '<p id="legacy-value">before</p></section>';
  const changed =
    '<section class="virtual-record" data-virtual-index="2">' +
    '<span class="record-anchor" data-jsonl-record="3"></span>' +
    '<p id="legacy-value">after</p></section>';

  replaceTranscriptWindow(first, false, [], 2, 2, 100, 100);
  const before = document.querySelector(
    '.virtual-record[data-virtual-index="2"]');
  replaceTranscriptWindow(changed, false, [], 2, 2, 100, 100);
  const after = document.querySelector(
    '.virtual-record[data-virtual-index="2"]');
  return JSON.stringify({
    firstRendered: before !== null,
    changedRendered: after !== null,
    changedWasReplaced: before !== after,
    textAfterChange: document.getElementById('legacy-value')?.textContent || ''
  });
})()
""");

    Require(result.GetProperty("firstRendered").GetBoolean(),
      "Legacy virtual record was not installed by the compatibility path.");
    Require(result.GetProperty("changedRendered").GetBoolean(),
      "Changed legacy virtual record disappeared during full replacement.");
    Require(result.GetProperty("changedWasReplaced").GetBoolean(),
      "Legacy virtual record was incorrectly treated as Core-keyed content.");
    Require(string.Equals(
        result.GetProperty("textAfterChange").GetString(),
        "after",
        StringComparison.Ordinal),
      "Legacy full replacement did not install changed content.");
  }

  /// <summary>
  /// Canonical word-boundary updates are independent of transcript preparation.
  /// A refresh may take seconds; suppressing playback messages for that entire
  /// interval leaves the visible cursor stale even before DOM reconciliation.
  /// </summary>
  private static void TestPlaybackMessageContinuesDuringRefresh()
  {
    using var host = CreateOffscreenHost();
    using var view = CreateInitializedView(host);
    WebView2 webView = ReadField<WebView2>(view, "_webView");

    ExecuteVoidScript(webView, """
replaceTranscriptWindow(
  '<section class="virtual-record" data-virtual-index="0">' +
  '<span hidden class="aicore-structural-unit" data-aicore-unit-id="turn:playback" ' +
  'data-aicore-source-record-ids="1"></span>' +
  '<span class="record-anchor" data-jsonl-record="1"></span>' +
  '<p><span id="word-100" data-word-id="100">stable</span></p>' +
  '</section>',
  false, [], 0, 0, 0, 0);
""");
    long before = ReadBrowserInt64(webView, "latestPlaybackSequence");
    SetField(view, "_refreshInProgress", true);
    try
    {
      view.ShowPlaybackPosition(new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Speaking,
        "stable",
        0,
        "stable",
        0,
        0,
        6,
        Stopwatch.GetTimestamp(),
        WordId: 100));
      PumpUntil(
        () => ReadBrowserInt64(webView, "latestPlaybackSequence") > before,
        "playback message while transcript refresh is in progress",
        timeoutMilliseconds: 1500);
    }
    finally
    {
      SetField(view, "_refreshInProgress", false);
    }
  }

  private static TranscriptView CreateInitializedView(Form host)
  {
    var view = new TranscriptView { Dock = DockStyle.Fill };
    host.Controls.Add(view);
    host.Show();
    _ = host.Handle;
    _ = view.Handle;
    PumpUntil(
      () => ReadField<bool>(view, "_initialized"),
      "TranscriptView shell initialization");
    return view;
  }

  private static Form CreateOffscreenHost()
  {
    return new Form
    {
      Width = 900,
      Height = 700,
      ShowInTaskbar = false,
      StartPosition = FormStartPosition.Manual,
      Location = new Point(-30000, -30000)
    };
  }

  private static JsonElement ExecuteJsonProbe(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #138 browser probe");
    string encoded = JsonSerializer.Deserialize<string>(task.Result) ??
      throw new InvalidOperationException(
        "Issue #138 browser probe returned no JSON string.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static long ReadBrowserInt64(WebView2 webView, string expression)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(
      "Number(" + expression + ")");
    PumpUntilCompleted(task, "issue #138 browser integer probe");
    return JsonSerializer.Deserialize<long>(task.Result);
  }

  private static void ExecuteVoidScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #138 browser action");
  }

  private static T ReadField<T>(object target, string fieldName)
  {
    FieldInfo field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field '{fieldName}' was not found on {target.GetType().Name}.");
    object? value = field.GetValue(target);
    return value is T typed
      ? typed
      : throw new InvalidOperationException(
        $"Field '{fieldName}' had an unexpected value/type.");
  }

  private static void SetField<T>(object target, string fieldName, T value)
  {
    FieldInfo field = target.GetType().GetField(
      fieldName,
      BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        $"Field '{fieldName}' was not found on {target.GetType().Name}.");
    field.SetValue(target, value);
  }

  private static void PumpUntil(
    Func<bool> predicate,
    string description,
    int timeoutMilliseconds = 30000)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (!predicate() && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(predicate(), $"Timed out waiting for {description}.");
  }

  private static void PumpUntilCompleted(
    Task task,
    string description,
    int timeoutMilliseconds = 30000)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
    while (!task.IsCompleted && DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
    Require(task.IsCompleted, $"Timed out waiting for {description}.");
    task.GetAwaiter().GetResult();
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }
}
