using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Permanent regressions for issue #73 Ctrl+click voice-pointer navigation.
/// </summary>
internal static class Issue73CtrlClickVoicePointerRegressionTestRunner
{
  /// <summary>
  /// Runs the issue #73 Ctrl+click voice-pointer regression suite.
  /// </summary>
  /// <returns>Zero when every regression passes.</returns>
  public static int Run()
  {
    var tests = new (string Name, Action Body)[]
    {
      ("ctrl-click-voice-pointer/current-speech-eligibility-is-authoritative",
        TestCurrentSpeechEligibilityIsAuthoritative),
      ("ctrl-click-voice-pointer/browser-affordance-and-click-contract",
        TestBrowserAffordanceAndClickContract),
      ("ctrl-click-voice-pointer/punctuation-rich-browser-coordinate-matches-speech-history",
        TestPunctuationRichBrowserCoordinateMatchesSpeechHistory),
      ("ctrl-click-voice-pointer/host-ctrl-routing-does-not-require-webview-focus",
        TestHostCtrlRoutingDoesNotRequireWebViewFocus),
      ("ctrl-click-voice-pointer/owned-popup-focus-preserves-held-ctrl",
        TestOwnedPopupFocusPreservesHeldCtrl),
      ("ctrl-click-voice-pointer/owned-popup-root-routes-ctrl-messages",
        TestOwnedPopupRootRoutesCtrlMessages),
      ("ctrl-click-voice-pointer/spoken-md-markup-split-token-is-selectable",
        TestSpokenMarkdownMarkupSplitTokenIsSelectable),
      ("ctrl-click-voice-pointer/spoken-quoted-markup-split-token-is-selectable",
        TestSpokenQuotedMarkupSplitTokenIsSelectable)
    };

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine(
      $"Issue #73 Ctrl+click voice-pointer regression suite: {tests.Length} tests");
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
      ? $"PASS: {tests.Length}/{tests.Length} issue #73 Ctrl+click tests passed."
      : $"FAIL: {failures}/{tests.Length} issue #73 Ctrl+click tests failed.");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// Selectability is current playback eligibility, not merely DOM/node
  /// mapping. Node-word coordinates remain stable when earlier fragments are
  /// disabled, so enabling a category or fence never renumbers later words.
  /// </summary>
  private static void TestCurrentSpeechEligibilityIsAuthoritative()
  {
    bool reasoningSpoken = false;
    var spokenFenceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "cpp"
    };
    using var speech = new SpeechService();
    speech.SetPolicyProviders(
      category => category == ContentCategory.Reasoning && !reasoningSpoken
        ? new SpeechProfileSettings(SpeechProfileSettings.NotSpoken, 0, 0)
        : new SpeechProfileSettings("Test voice", 0, 0),
      fenceType => spokenFenceTypes.Contains(fenceType),
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);

    SpeechFragment[] fragments =
    {
      new(
        42,
        ContentCategory.Assistant,
        SpeechFragmentKind.Prose,
        "alpha beta",
        TranscriptWords: new[]
        {
          new SpeechFragmentWord(1001, "alpha", 0, 5),
          new SpeechFragmentWord(1002, "beta", 6, 4)
        }),
      new(
        42,
        ContentCategory.Reasoning,
        SpeechFragmentKind.Prose,
        "hidden",
        TranscriptWords: new[]
        {
          new SpeechFragmentWord(1003, "hidden", 0, 6)
        }),
      new(
        42,
        ContentCategory.Assistant,
        SpeechFragmentKind.FencedCodeLine,
        "gamma",
        FenceType: "python",
        TranscriptWords: new[]
        {
          new SpeechFragmentWord(1004, "gamma", 0, 5)
        }),
      new(
        42,
        ContentCategory.Assistant,
        SpeechFragmentKind.FencedCodeLine,
        "delta",
        FenceType: "cpp",
        TranscriptWords: new[]
        {
          new SpeechFragmentWord(1005, "delta", 0, 5)
        })
    };
    speech.LoadHistory(
      fragments,
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    MethodInfo getRanges = typeof(SpeechService).GetMethod(
      "GetSeekableTranscriptWordRanges",
      BindingFlags.Instance | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "SpeechService.GetSeekableTranscriptWordRanges() is missing.");

    TranscriptRangeProbe[] initial = ReadRanges(getRanges.Invoke(speech, null));
    RequireRanges(
      initial,
      new TranscriptRangeProbe(1001, 2),
      new TranscriptRangeProbe(1005, 1));

    Require(!speech.TrySeekToTranscriptWord(1003, out _),
      "A currently Not Spoken reasoning word was seekable.");
    Require(!speech.TrySeekToTranscriptWord(1004, out _),
      "A currently disabled fenced-code word was seekable.");
    Require(speech.TrySeekToTranscriptWord(1005, out string delta) &&
        delta == "delta",
      "Core word 1005 did not seek the eligible later word.");

    reasoningSpoken = true;
    TranscriptRangeProbe[] reasoningEnabled = ReadRanges(
      getRanges.Invoke(speech, null));
    RequireRanges(
      reasoningEnabled,
      new TranscriptRangeProbe(1001, 3),
      new TranscriptRangeProbe(1005, 1));
    Require(speech.TrySeekToTranscriptWord(1003, out string hidden) &&
        hidden == "hidden",
      "Enabling reasoning did not make its existing Core word ID seekable.");

    spokenFenceTypes.Add("python");
    TranscriptRangeProbe[] allEnabled = ReadRanges(getRanges.Invoke(speech, null));
    RequireRanges(allEnabled, new TranscriptRangeProbe(1001, 5));
    Require(speech.TrySeekToTranscriptWord(1004, out string gamma) &&
        gamma == "gamma",
      "Enabling the fence type did not make its Core word ID seekable.");
  }

  /// <summary>
  /// Browser node-global coordinates must use the same speech-token ordinal
  /// space as SpeechService.  Punctuation-rich path/file-name fragments from
  /// the real node-308 failure must not make a click in the later (5).md entry
  /// seek backward into the preceding (6).md fragment.
  /// </summary>
  private static void TestPunctuationRichBrowserCoordinateMatchesSpeechHistory()
  {
    const long NodeId = 308;
    const long FirstWordId = 2001;
    const long SecondWordId = 2002;
    const string first =
      "Phase 2 Classification Update (6).md: c:\\Users\\adria\\Downloads\\" +
      "Download Conversation - 3.";
    const string second =
      "Phase 2 Classification Update (5).md: c:\\Users\\adria\\Downloads\\" +
      "Download Conversation - 3.";

    using var speech = new SpeechService();
    speech.SetPolicyProviders(
      _ => new SpeechProfileSettings("Test voice", 0, 0),
      _ => true,
      () => Array.Empty<string>(),
      () => PronunciationRuleSet.Parse(string.Empty),
      () => AudioWakeSettings.Default);
    speech.LoadHistory(
      new[]
      {
        new SpeechFragment(
          NodeId,
          ContentCategory.UserContext,
          SpeechFragmentKind.Prose,
          first,
          TranscriptWords: new[]
          {
            new SpeechFragmentWord(FirstWordId, "6", 31, 1)
          }),
        new SpeechFragment(
          NodeId,
          ContentCategory.UserContext,
          SpeechFragmentKind.Prose,
          second,
          TranscriptWords: new[]
          {
            new SpeechFragmentWord(SecondWordId, "5", 31, 1)
          })
      },
      Array.Empty<TurnCompletion>(),
      Array.Empty<BackgroundWorkEvent>(),
      PlaybackStartMode.Beginning);

    using BrowserFixture fixture = BrowserFixture.Create();
    var seekMessage = new TaskCompletionSource<JsonElement>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.WebView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
    {
      using JsonDocument document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
      JsonElement root = document.RootElement;
      if (root.TryGetProperty("type", out JsonElement typeElement) &&
          string.Equals(typeElement.GetString(), "find-seek", StringComparison.Ordinal) &&
          root.TryGetProperty("source", out JsonElement sourceElement) &&
          string.Equals(sourceElement.GetString(), "ctrl-click", StringComparison.Ordinal))
      {
        seekMessage.TrySetResult(root.Clone());
      }
    };

    ExecuteScript(
      fixture.WebView,
      """
(() => {
  replaceTranscript(
    '<span class="record-anchor" data-jsonl-record="1"></span>' +
      '<p>Phase 2 Classification Update (<span id="word-2001">6</span>).md: ' +
      'c:\\Users\\adria\\Downloads\\Download Conversation - 3. ' +
      'Phase 2 Classification Update (<span id="word-2002">5</span>).md: ' +
      'c:\\Users\\adria\\Downloads\\Download Conversation - 3.</p>',
    false,
    [{NodeId:308, RecordNumber:1, Segments:[]}]);
  setVoicePolicy([{StartWordId:2001, WordCount:2}]);
  setVoicePointerSelectMode(true);
  const target = document.getElementById('word-2002');
  if (!target || !isVoiceWordEligible(target)) {
    throw new Error('Canonical later 5 is not currently eligible.');
  }
  target.dispatchEvent(new MouseEvent('click', {
    button:0, ctrlKey:true, bubbles:true, cancelable:true
  }));
})()
""");
    PumpUntilCompleted(
      seekMessage.Task,
      "issue #73 punctuation-rich Core word-ID Ctrl+click seek message");
    JsonElement seek = seekMessage.Task.Result;
    Require(seek.GetProperty("wordId").GetInt64() == SecondWordId,
      "Punctuation-rich Ctrl+click did not post the exact later Core word ID.");

    TranscriptPlaybackPosition? position = null;
    speech.PlaybackPositionChanged += value => position = value;
    Require(
      speech.TrySeekToTranscriptWord(SecondWordId, out string sought) &&
        string.Equals(sought, second, StringComparison.Ordinal),
      "The Core word ID did not resolve to the later (5).md speech fragment.");
    Require(position is not null &&
        position.State == TranscriptPlaybackState.Paused &&
        position.WordId == SecondWordId &&
        string.Equals(position.Word, "5", StringComparison.Ordinal),
      "The Core word ID did not place the paused voice cursor on the clicked 5.");
  }

  /// <summary>
  /// Ctrl is a momentary discoverability affordance: only currently eligible
  /// mapped words are boxed and clickable, replacement inherits held state,
  /// ordinary click is untouched, and key release or blur removes the boxes.
  /// </summary>
  private static void TestBrowserAffordanceAndClickContract()
  {
    using BrowserFixture fixture = BrowserFixture.Create();
    var seekMessage = new TaskCompletionSource<JsonElement>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    int seekCount = 0;

    fixture.WebView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
    {
      using JsonDocument document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
      JsonElement root = document.RootElement;
      if (root.TryGetProperty("type", out JsonElement typeElement) &&
          string.Equals(typeElement.GetString(), "find-seek", StringComparison.Ordinal) &&
          root.TryGetProperty("source", out JsonElement sourceElement) &&
          string.Equals(sourceElement.GetString(), "ctrl-click", StringComparison.Ordinal))
      {
        ++seekCount;
        seekMessage.TrySetResult(root.Clone());
      }
    };

    JsonElement initial = ExecuteJsonProbe(
      fixture.WebView,
      """
(() => {
  replaceTranscript(
    '<span class="record-anchor" data-jsonl-record="1"></span>' +
      '<p><span id="word-3001">alpha</span> ' +
      '<span id="word-3002">beta</span> ' +
      '<span id="word-3003">gamma</span></p>',
    false,
    [{NodeId:42, RecordNumber:1, Segments:[]}]);
  setVoicePolicy([{StartWordId:3001, WordCount:2}]);
  const owners = [3001,3002,3003].map(id => document.getElementById('word-' + id));
  const classesBeforeCtrl = owners.map(word => word.className);
  window.dispatchEvent(new KeyboardEvent('keydown', {
    key:'Control', code:'ControlLeft', ctrlKey:true, bubbles:true
  }));
  const classesAfterCtrl = owners.map(word => word.className);
  return JSON.stringify({
    mode:document.body.classList.contains('voice-pointer-select-mode'),
    ctrlChangedWordClasses:JSON.stringify(classesBeforeCtrl) !==
      JSON.stringify(classesAfterCtrl),
    words:owners.map(word => ({
      text:word.textContent,
      wordId:canonicalWordId(word),
      eligible:isVoiceWordEligible(word),
      cursor:getComputedStyle(word).cursor
    }))
  });
})()
""");

    Require(initial.GetProperty("mode").GetBoolean(),
      "Holding Ctrl did not enable the voice-pointer selection affordance.");
    Require(!initial.GetProperty("ctrlChangedWordClasses").GetBoolean(),
      "Ctrl-down mutated individual canonical word classes.");
    JsonElement words = initial.GetProperty("words");
    Require(words.GetArrayLength() == 3,
      "Browser fixture did not retain all three Core word owners.");
    Require(words[0].GetProperty("wordId").GetInt64() == 3001 &&
        words[0].GetProperty("eligible").GetBoolean() &&
        words[0].GetProperty("cursor").GetString() == "pointer",
      "Eligible alpha did not receive the centralized Ctrl affordance.");
    Require(words[1].GetProperty("wordId").GetInt64() == 3002 &&
        words[1].GetProperty("eligible").GetBoolean() &&
        words[1].GetProperty("cursor").GetString() == "pointer",
      "Eligible beta did not receive the centralized Ctrl affordance.");
    Require(words[2].GetProperty("wordId").GetInt64() == 3003 &&
        !words[2].GetProperty("eligible").GetBoolean() &&
        words[2].GetProperty("cursor").GetString() != "pointer",
      "Ineligible gamma incorrectly received the Ctrl affordance.");

    ExecuteScript(
      fixture.WebView,
      """
(() => {
  document.getElementById('word-3001').dispatchEvent(new MouseEvent('click', {
    button:0, ctrlKey:false, bubbles:true, cancelable:true
  }));
  document.getElementById('word-3003').dispatchEvent(new MouseEvent('click', {
    button:0, ctrlKey:true, bubbles:true, cancelable:true
  }));
})()
""");
    PumpFor(150);
    Require(seekCount == 0,
      "Ordinary click or Ctrl+click on an ineligible Core word requested a seek.");

    ExecuteScript(
      fixture.WebView,
      """
(() => {
  document.getElementById('word-3002').dispatchEvent(new MouseEvent('click', {
    button:0, ctrlKey:true, bubbles:true, cancelable:true
  }));
})()
""");
    PumpUntilCompleted(seekMessage.Task, "issue #73 Core word-ID Ctrl+click seek message");
    JsonElement seek = seekMessage.Task.Result;
    Require(seekCount == 1 && seek.GetProperty("wordId").GetInt64() == 3002,
      "Ctrl+click did not post the exact eligible Core word ID.");

    JsonElement replacement = ExecuteJsonProbe(
      fixture.WebView,
      """
(() => {
  setVoicePolicy([{StartWordId:4001, WordCount:2}]);
  replaceTranscript(
    '<span class="record-anchor" data-jsonl-record="2"></span>' +
      '<p><span id="word-4001">delta</span> ' +
      '<span id="word-4002">epsilon</span> ' +
      '<span id="word-4003">tail</span></p>',
    false,
    [{NodeId:99, RecordNumber:2, Segments:[]}]);
  const delta = document.getElementById('word-4001');
  const epsilon = document.getElementById('word-4002');
  const tail = document.getElementById('word-4003');
  const heldThroughReplacement =
    isVoiceWordEligible(delta) && isVoiceWordEligible(epsilon) &&
    !isVoiceWordEligible(tail) &&
    getComputedStyle(delta).cursor === 'pointer' &&
    getComputedStyle(epsilon).cursor === 'pointer' &&
    getComputedStyle(tail).cursor !== 'pointer';
  window.dispatchEvent(new KeyboardEvent('keyup', {
    key:'Control', code:'ControlLeft', ctrlKey:false, bubbles:true
  }));
  const afterUp = document.body.classList.contains('voice-pointer-select-mode');
  window.dispatchEvent(new KeyboardEvent('keydown', {
    key:'Control', code:'ControlLeft', ctrlKey:true, bubbles:true
  }));
  window.dispatchEvent(new Event('blur'));
  const afterBrowserBlur = document.body.classList.contains(
    'voice-pointer-select-mode');
  setVoicePointerSelectMode(false);
  const afterHostDeactivate = document.body.classList.contains(
    'voice-pointer-select-mode');
  return JSON.stringify({
    heldThroughReplacement,
    afterUp,
    afterBrowserBlur,
    afterHostDeactivate
  });
})()
""");

    Require(replacement.GetProperty("heldThroughReplacement").GetBoolean(),
      "Newly materialized eligible Core words did not inherit held-Ctrl affordances.");
    Require(!replacement.GetProperty("afterUp").GetBoolean(),
      "Ctrl-up did not remove selectable-word rectangles.");
    Require(replacement.GetProperty("afterBrowserBlur").GetBoolean(),
      "WebView focus loss incorrectly impersonated Ctrl-up while the app remained active.");
    Require(!replacement.GetProperty("afterHostDeactivate").GetBoolean(),
      "Host deactivation did not clear Ctrl selection mode.");
  }

  /// <summary>
  /// Ctrl selection mode must be driven by the containing WinForms window as
  /// well as browser key events so focus on another app control still exposes
  /// the currently seekable transcript words.  Key release is likewise routed
  /// by the host without consuming the original key message.
  /// </summary>
  private static void TestHostCtrlRoutingDoesNotRequireWebViewFocus()
  {
    MethodInfo setMode = typeof(TranscriptView).GetMethod(
      "SetVoicePointerSelectMode",
      BindingFlags.Instance | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "TranscriptView.SetVoicePointerSelectMode(bool) is missing.");
    Require(setMode.GetParameters() is [{ ParameterType: var parameterType }] &&
        parameterType == typeof(bool),
      "TranscriptView.SetVoicePointerSelectMode has the wrong contract.");

    MethodInfo resolver = typeof(MainForm).GetMethod(
      "TryGetVoicePointerSelectModeMessage",
      BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "MainForm host Ctrl-message resolver is missing.");

    object?[] down = { 0x0100, (IntPtr)(int)Keys.ControlKey, false };
    Require((bool)(resolver.Invoke(null, down) ?? false) &&
        down[2] is true,
      "Host WM_KEYDOWN for Ctrl did not enable voice-pointer selection mode.");

    object?[] up = { 0x0101, (IntPtr)(int)Keys.ControlKey, true };
    Require((bool)(resolver.Invoke(null, up) ?? false) &&
        up[2] is false,
      "Host WM_KEYUP for Ctrl did not disable voice-pointer selection mode.");

    object?[] ordinary = { 0x0100, (IntPtr)(int)Keys.A, false };
    Require(!(bool)(resolver.Invoke(null, ordinary) ?? true),
      "An ordinary host key was incorrectly treated as Ctrl selection state.");
  }

  /// <summary>
  /// Moving focus from MainForm to an owned popup keeps the application in the
  /// foreground.  That same-process deactivation must therefore preserve a
  /// physically held Ctrl affordance; only external application deactivation
  /// may clear it.
  /// </summary>
  private static void TestOwnedPopupFocusPreservesHeldCtrl()
  {
    MethodInfo resolver = typeof(MainForm).GetMethod(
      "ResolveVoicePointerSelectModeForActivation",
      BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "MainForm activation-aware Ctrl-mode resolver is missing.");

    bool Resolve(bool controlHeld, bool foregroundIsCurrentProcess)
    {
      object? result = resolver.Invoke(
        null,
        new object?[] { controlHeld, foregroundIsCurrentProcess });
      return result is bool value
        ? value
        : throw new InvalidOperationException(
          "MainForm activation-aware Ctrl-mode resolver returned no bool.");
    }

    Require(Resolve(controlHeld: true, foregroundIsCurrentProcess: true),
      "Held Ctrl was cleared when focus moved to an owned same-process popup.");
    Require(!Resolve(controlHeld: true, foregroundIsCurrentProcess: false),
      "Held Ctrl remained exposed after the foreground moved outside the app.");
    Require(!Resolve(controlHeld: false, foregroundIsCurrentProcess: true),
      "Ctrl selection mode remained enabled after physical Ctrl release.");
  }

  /// <summary>
  /// The real SV1 failure: an enabled md fence visibly renders Markdown source
  /// where inline backticks split one spoken token (turn_ids) into DOM pieces.
  /// Every visible lexical piece belonging to that spoken token must advertise
  /// the same authoritative speech coordinate.
  /// </summary>
  private static void TestSpokenMarkdownMarkupSplitTokenIsSelectable()
  {
    TestMarkupSplitTokenMapping(
      recordNumber: 1,
      nodeId: 501,
      html: "<pre><code class=\"language-md\">&gt; Are `turn_id`s the id guids with author.role user?</code></pre>",
      description: "spoken md fence");
  }

  /// <summary>
  /// The real SV2 failure: canonical HTML can render the same spoken token
  /// across an inline code element and adjacent text. DOM element boundaries
  /// must not make voiced text unselectable.
  /// </summary>
  private static void TestSpokenQuotedMarkupSplitTokenIsSelectable()
  {
    TestMarkupSplitTokenMapping(
      recordNumber: 2,
      nodeId: 502,
      html: "<blockquote><p>Are <code>turn_id</code>s the id guids with author.role user?</p></blockquote>",
      description: "quoted User Context markup");
  }

  private static void TestMarkupSplitTokenMapping(
    int recordNumber,
    long nodeId,
    string html,
    string description)
  {
    long wordId = 5000 + recordNumber;
    string canonical = html.Replace(
      "turn_id</code>s",
      $"<span id=\"word-{wordId}\"><code>turn_id</code>s</span>",
      StringComparison.Ordinal).Replace(
      "`turn_id`s",
      $"<span id=\"word-{wordId}\"><code>turn_id</code>s</span>",
      StringComparison.Ordinal);
    using BrowserFixture fixture = BrowserFixture.Create();
    string script = "(() => {" +
      "replaceTranscript(" + JsonSerializer.Serialize(
        $"<span class=\"record-anchor\" data-jsonl-record=\"{recordNumber}\"></span>{canonical}") +
      ",false,[{NodeId:" + nodeId + ",RecordNumber:" + recordNumber +
      ",Segments:[]}]);" +
      "setVoicePolicy([{StartWordId:" + wordId + ",WordCount:1}]);" +
      "setVoicePointerSelectMode(true);" +
      "const owner=document.getElementById('word-" + wordId + "');" +
      "const inner=owner?.querySelector('code')||owner;" +
      "return JSON.stringify({" +
        "ownerId:canonicalWordId(owner),innerId:canonicalWordId(inner)," +
        "eligible:isVoiceWordEligible(inner),cursor:getComputedStyle(owner).cursor});" +
      "})()";
    JsonElement result = ExecuteJsonProbe(fixture.WebView, script);
    Require(result.GetProperty("ownerId").GetInt64() == wordId &&
        result.GetProperty("innerId").GetInt64() == wordId,
      $"{description} did not preserve one canonical word identity across markup.");
    Require(result.GetProperty("eligible").GetBoolean() &&
        result.GetProperty("cursor").GetString() == "pointer",
      $"{description} canonical word owner is not Ctrl-selectable.");
  }

  /// <summary>
  /// A top-level owned popup is not the MainForm root, but it is still part of
  /// this process and its Ctrl key messages must reach the transcript modifier
  /// router. External-process roots must remain excluded.
  /// </summary>
  private static void TestOwnedPopupRootRoutesCtrlMessages()
  {
    MethodInfo resolver = typeof(MainForm).GetMethod(
      "ShouldRouteVoicePointerMessageSource",
      BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) ??
      throw new InvalidOperationException(
        "MainForm popup-root voice-pointer routing resolver is missing.");

    bool Resolve(bool isMainFormRoot, bool rootIsCurrentProcess)
    {
      object? result = resolver.Invoke(
        null,
        new object?[] { isMainFormRoot, rootIsCurrentProcess });
      return result is bool value
        ? value
        : throw new InvalidOperationException(
          "MainForm popup-root routing resolver returned no bool.");
    }

    Require(Resolve(isMainFormRoot: true, rootIsCurrentProcess: true),
      "MainForm-root Ctrl messages were rejected.");
    Require(Resolve(isMainFormRoot: false, rootIsCurrentProcess: true),
      "Owned same-process popup Ctrl messages were rejected.");
    Require(!Resolve(isMainFormRoot: false, rootIsCurrentProcess: false),
      "External-process messages were accepted as transcript Ctrl input.");
  }

  private static TranscriptRangeProbe[] ReadRanges(object? value)
  {
    if (value is not IEnumerable ranges)
    {
      throw new InvalidOperationException(
        "GetSeekableTranscriptWordRanges() returned no enumerable result.");
    }

    var result = new List<TranscriptRangeProbe>();
    foreach (object? item in ranges)
    {
      if (item is null)
      {
        continue;
      }
      Type type = item.GetType();
      result.Add(new TranscriptRangeProbe(
        ReadInt64Property(type, item, "StartWordId"),
        ReadInt32Property(type, item, "WordCount")));
    }
    return result.ToArray();
  }

  private static long ReadInt64Property(Type type, object instance, string name)
  {
    return type.GetProperty(name)?.GetValue(instance) is long value
      ? value
      : throw new InvalidOperationException($"Range property {name} is missing.");
  }

  private static int ReadInt32Property(Type type, object instance, string name)
  {
    return type.GetProperty(name)?.GetValue(instance) is int value
      ? value
      : throw new InvalidOperationException($"Range property {name} is missing.");
  }

  private static void RequireRanges(
    IReadOnlyList<TranscriptRangeProbe> actual,
    params TranscriptRangeProbe[] expected)
  {
    Require(actual.Count == expected.Length,
      $"Expected {expected.Length} seekable range(s), found {actual.Count}.");
    for (int index = 0; index < expected.Length; ++index)
    {
      Require(actual[index] == expected[index],
        $"Seekable range {index} was {actual[index]}, expected {expected[index]}.");
    }
  }

  private static void RequireWord(
    JsonElement word,
    string text,
    long nodeId,
    int nodeWordIndex,
    bool selectable,
    bool excluded)
  {
    Require(string.Equals(
        word.GetProperty("text").GetString(),
        text,
        StringComparison.Ordinal),
      $"Expected rendered word {text}.");
    Require(word.GetProperty("nodeId").GetInt64() == nodeId,
      $"Rendered word {text} has the wrong node id.");
    Require(word.GetProperty("nodeWordIndex").GetInt32() == nodeWordIndex,
      $"Rendered word {text} has the wrong node-global word index.");
    Require(word.GetProperty("selectable").GetBoolean() == selectable,
      $"Rendered word {text} has the wrong structural selectable class.");
    Require(word.GetProperty("excluded").GetBoolean() == excluded,
      $"Rendered word {text} has the wrong current exclusion class.");
  }

  private static JsonElement ExecuteJsonProbe(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #73 browser probe");
    string encoded = JsonSerializer.Deserialize<string>(task.Result) ??
      throw new InvalidOperationException("Browser probe returned no JSON string.");
    using JsonDocument document = JsonDocument.Parse(encoded);
    return document.RootElement.Clone();
  }

  private static void ExecuteScript(WebView2 webView, string script)
  {
    Task<string> task = webView.CoreWebView2.ExecuteScriptAsync(script);
    PumpUntilCompleted(task, "issue #73 browser script");
  }

  private static void PumpFor(int milliseconds)
  {
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(10);
    }
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

  private readonly record struct TranscriptRangeProbe(
    long StartWordId,
    int WordCount);

  private sealed class BrowserFixture : IDisposable
  {
    private BrowserFixture(Form host, WebView2 webView)
    {
      Host = host;
      WebView = webView;
    }

    public Form Host { get; }
    public WebView2 WebView { get; }

    public static BrowserFixture Create()
    {
      var host = new Form
      {
        Width = 800,
        Height = 600,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-30000, -30000)
      };
      var webView = new WebView2 { Dock = DockStyle.Fill };
      host.Controls.Add(webView);
      host.Show();
      Application.DoEvents();

      PumpUntilCompleted(
        webView.EnsureCoreWebView2Async(),
        "issue #73 WebView initialization");
      MethodInfo shellMethod = typeof(TranscriptView).GetMethod(
        "BuildShellHtml",
        BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException(
          "TranscriptView.BuildShellHtml() was not found.");
      string shell = shellMethod.Invoke(null, null) as string ??
        throw new InvalidOperationException("Transcript shell was empty.");
      var navigated = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      webView.NavigationCompleted += (_, eventArgs) =>
      {
        if (eventArgs.IsSuccess)
        {
          navigated.TrySetResult(true);
        }
        else
        {
          navigated.TrySetException(new InvalidOperationException(
            $"WebView navigation failed: {eventArgs.WebErrorStatus}."));
        }
      };
      webView.CoreWebView2.NavigateToString(shell);
      PumpUntilCompleted(navigated.Task, "issue #73 transcript shell navigation");
      return new BrowserFixture(host, webView);
    }

    public void Dispose()
    {
      WebView.Dispose();
      Host.Dispose();
    }
  }
}
