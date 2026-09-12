from pathlib import Path
import re

PATH = Path('AgentPanelSpeaker/Issue73CtrlClickVoicePointerRegressionTestRunner.cs')


def replace_method(text: str, name: str, following_summary: str, body: str) -> str:
  pattern = (
    rf'  private static void {re.escape(name)}\(\)\n  \{{.*?\n  \}}\n\n'
    rf'  /// <summary>\n  /// {re.escape(following_summary)}'
  )
  replacement = body + '\n\n  /// <summary>\n  /// ' + following_summary
  updated, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
  if count != 1:
    raise RuntimeError(f'Expected one method {name}, found {count}')
  return updated


text = PATH.read_text(encoding='utf-8')

text = replace_method(
  text,
  'TestCurrentSpeechEligibilityIsAuthoritative',
  'Browser node-global coordinates must use the same speech-token ordinal',
  '''  private static void TestCurrentSpeechEligibilityIsAuthoritative()
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
  }''')

text = replace_method(
  text,
  'TestPunctuationRichBrowserCoordinateMatchesSpeechHistory',
  'Ctrl is a momentary discoverability affordance: only currently eligible',
  '''  private static void TestPunctuationRichBrowserCoordinateMatchesSpeechHistory()
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
  }''')

text = replace_method(
  text,
  'TestBrowserAffordanceAndClickContract',
  'Ctrl selection mode must be driven by the containing WinForms window as',
  '''  private static void TestBrowserAffordanceAndClickContract()
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
  }''')

# Replace markup-split helper with the Core contract: one authoritative word-N
# owner can contain inline markup, and clicking any descendant resolves that ID.
pattern = r'''  private static void TestMarkupSplitTokenMapping\(.*?\n  \}\n\n  /// <summary>\n  /// A top-level owned popup'''
replacement = '''  private static void TestMarkupSplitTokenMapping(
    int recordNumber,
    long nodeId,
    string html,
    string description)
  {
    long wordId = 5000 + recordNumber;
    string canonical = html.Replace(
      "turn_id</code>s",
      $"<span id=\\\"word-{wordId}\\\"><code>turn_id</code>s</span>",
      StringComparison.Ordinal).Replace(
      "`turn_id`s",
      $"<span id=\\\"word-{wordId}\\\"><code>turn_id</code>s</span>",
      StringComparison.Ordinal);
    using BrowserFixture fixture = BrowserFixture.Create();
    string script = "(() => {" +
      "replaceTranscript(" + JsonSerializer.Serialize(
        $"<span class=\\\"record-anchor\\\" data-jsonl-record=\\\"{recordNumber}\\\"></span>{canonical}") +
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
  /// A top-level owned popup'''
text, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
if count != 1:
  raise RuntimeError(f'Expected one markup helper replacement, found {count}')

text = text.replace(
  '''      result.Add(new TranscriptRangeProbe(\n        ReadInt64Property(type, item, "NodeId"),\n        ReadInt32Property(type, item, "StartNodeWordIndex"),\n        ReadInt32Property(type, item, "WordCount")));\n''',
  '''      result.Add(new TranscriptRangeProbe(\n        ReadInt64Property(type, item, "StartWordId"),\n        ReadInt32Property(type, item, "WordCount")));\n''')
text = text.replace(
  '''  private readonly record struct TranscriptRangeProbe(\n    long NodeId,\n    int StartNodeWordIndex,\n    int WordCount);\n''',
  '''  private readonly record struct TranscriptRangeProbe(\n    long StartWordId,\n    int WordCount);\n''')

PATH.write_text(text, encoding='utf-8', newline='\n')
