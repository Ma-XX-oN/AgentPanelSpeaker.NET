from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
  p = Path(path)
  text = p.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{path}: expected one replacement target, found {count}")
  p.write_text(text.replace(old, new, 1), encoding="utf-8")


issue26 = "AgentPanelSpeaker/Issue26UserContextSpeechRegressionTestRunner.cs"
replace_once(
  issue26,
  '''      ("user-context-speech/live-monitor-toggle",
        TestLiveMonitorToggle)
''',
  '''      ("user-context-speech/live-monitor-toggle",
        TestLiveMonitorToggle),
      ("user-context-speech/active-checkbox-cancels-ineligible-utterance",
        TestActiveCheckboxCancelsIneligibleUtterance)
''')

issue26_method = r'''
  /// <summary>
  /// Reproduces the real-machine acceptance failure where disabling the actual
  /// Speak User Context checkbox while User Context is actively speaking lets
  /// that now-ineligible utterance run to completion before playback advances.
  /// </summary>
  private static void TestActiveCheckboxCancelsIneligibleUtterance()
  {
    const string prompt = "Continue with the actual user prompt.";
    string contextPayload = string.Join(
      ' ',
      Enumerable.Repeat(
        "active user context payload remains deliberately long",
        120));
    string sourceMessage =
      "# Context from my IDE setup:\n\n" +
      "## Active file: sessions/active-context.jsonl\n\n" +
      contextPayload + "\n\n" +
      "## My request for Codex:\n" + prompt;
    string record = JsonSerializer.Serialize(new
    {
      type = "event_msg",
      timestamp = "2026-09-12T17:11:00Z",
      payload = new { type = "user_message", message = sourceMessage }
    });

    string directory = Path.Combine(
      Path.GetTempPath(),
      "AgentPanelSpeaker-Issue26-Active-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, "rollout-active-context.jsonl");
    File.WriteAllText(path, record + Environment.NewLine);

    MainForm? form = null;
    try
    {
      form = new MainForm();
      _ = form.Handle;

      SetField(form, "_loadingSettings", true);
      GetField<ComboBox>(form, "_sourceComboBox").SelectedItem =
        AgentSource.Codex;
      GetField<TextBox>(form, "_sessionPathTextBox").Text = path;
      GetField<CheckBox>(form, "_followLatestCheckBox").Checked = false;
      GetField<CheckBox>(form, "_speakExistingCheckBox").Checked = false;
      SetField(form, "_pathIsManual", true);
      TranscriptSettingsPopup popup =
        GetField<TranscriptSettingsPopup>(form, "_transcriptSettingsPopup");
      popup.SetSettings(
        popup.Settings with { SpeakUserContext = false },
        dark: false);
      SetField(form, "_speakUserContext", false);
      SetField(form, "_loadingSettings", false);

      InvokeTask(form, "StartMonitoringAsync").GetAwaiter().GetResult();
      JsonlSessionMonitor monitor =
        GetField<JsonlSessionMonitor>(form, "_monitor");
      SpeechService speech = GetField<SpeechService>(form, "_speech");
      WaitUntil(
        () =>
        {
          IReadOnlyList<ContentCategory> categories =
            ReadSpeechHistoryCategories(speech);
          return monitor.IsRunning &&
            categories.Contains(ContentCategory.UserContext) &&
            categories.Contains(ContentCategory.User);
        },
        "Active User Context fixture was not retained in speech history.");

      SpeechFragment[] history = ReadSpeechHistoryFragments(speech);
      SpeechFragment context = history.First(fragment =>
        fragment.Category == ContentCategory.UserContext);
      SpeechFragment user = history.First(fragment =>
        fragment.Category == ContentCategory.User);
      long contextWordId = context.WordIds?.FirstOrDefault() ??
        throw new InvalidOperationException(
          "Active User Context fragment has no Core word identity.");

      InstalledSpeechVoice voice = speech.GetInstalledVoices().FirstOrDefault() ??
        throw new InvalidOperationException(
          "No installed speech voice is available for active-toggle acceptance.");
      var slowProfile = new SpeechProfileSettings(voice.Name, -10, 0)
      {
        Volume = 100
      };
      speech.SetPolicyProviders(
        category =>
          category == ContentCategory.UserContext &&
          !GetField<bool>(form, "_speakUserContext")
            ? slowProfile with { VoiceName = SpeechProfileSettings.NotSpoken }
            : slowProfile,
        _ => true,
        () => Array.Empty<string>(),
        () => PronunciationRuleSet.Parse(string.Empty),
        () => AudioWakeSettings.Default with { Enabled = false });

      CheckBox speakContextCheckBox =
        GetField<CheckBox>(popup, "_speakUserContextCheckBox");
      speakContextCheckBox.Checked = true;
      Application.DoEvents();
      Require(GetField<bool>(form, "_speakUserContext"),
        "Actual Speak User Context checkbox did not enable playback policy.");
      Require(speech.TrySeekToTranscriptWord(contextWordId, out _),
        "Enabled User Context was not seekable before active acceptance.");

      var positions = new List<TranscriptPlaybackPosition>();
      speech.PlaybackPositionChanged += position =>
      {
        lock (positions)
        {
          positions.Add(position);
        }
      };
      Require(speech.TogglePause() == PauseToggleResult.Resumed,
        "Could not start the active User Context utterance.");
      WaitUntil(
        () =>
        {
          lock (positions)
          {
            return speech.IsSpeaking && positions.Any(position =>
              position.State == TranscriptPlaybackState.Speaking &&
              position.NodeId == context.NodeId);
          }
        },
        "User Context never entered active speech.");

      int transitionStart;
      lock (positions)
      {
        transitionStart = positions.Count;
      }
      speakContextCheckBox.Checked = false;
      Application.DoEvents();

      WaitUntil(
        () =>
        {
          lock (positions)
          {
            return positions.Skip(transitionStart).Any(position =>
              position.State == TranscriptPlaybackState.Speaking &&
              position.NodeId == user.NodeId);
          }
        },
        "Disabling the actual Speak User Context checkbox did not promptly " +
        "resume at the next eligible User fragment.");

      TranscriptPlaybackPosition[] transition;
      lock (positions)
      {
        transition = positions.Skip(transitionStart).ToArray();
      }
      Require(!transition.Any(position =>
          position.State == TranscriptPlaybackState.Speaking &&
          position.NodeId == context.NodeId),
        "User Context continued emitting speech boundaries after its actual " +
        "checkbox made the active utterance ineligible.");
    }
    finally
    {
      if (form is not null)
      {
        GetField<JsonlSessionMonitor>(form, "_monitor").Stop(
          "issue26-active-regression-cleanup");
        form.Dispose();
      }
      try { Directory.Delete(directory, recursive: true); } catch { }
    }
  }
'''
replace_once(
  issue26,
  '''  /// <summary>
  /// Creates project options by the production record constructor while keeping
''',
  issue26_method + '''
  /// <summary>
  /// Creates project options by the production record constructor while keeping
''')

issue75 = "AgentPanelSpeaker/Issue75CoreWordIdMigrationRegressionTestRunner.cs"
replace_once(
  issue75,
  '''      ("core-word-id/ordered-list-history-preserves-core-word-boundaries",
        TestOrderedListHistoryPreservesCoreWordBoundaries),
      ("core-word-id/policy-is-centralized-without-per-word-eligibility-mutation",
''',
  '''      ("core-word-id/ordered-list-history-preserves-core-word-boundaries",
        TestOrderedListHistoryPreservesCoreWordBoundaries),
      ("core-word-id/attached-identifier-period-preserves-one-speech-fragment",
        TestAttachedIdentifierPeriodPreservesOneSpeechFragment),
      ("core-word-id/attached-identifier-period-synthesizes-dot",
        TestAttachedIdentifierPeriodSynthesizesDot),
      ("core-word-id/policy-is-centralized-without-per-word-eligibility-mutation",
''')

issue75_methods = r'''
  /// <summary>
  /// A Core-attached period inside an identifier is not a sentence boundary.
  /// The production Core-to-history path must retain the original adjacency.
  /// </summary>
  private static void TestAttachedIdentifierPeriodPreservesOneSpeechFragment()
  {
    SpeechFragment[] fragments = LoadAttachedIdentifierHistory();
    Require(fragments.Length == 1,
      "Canonical attached identifier was split into " +
      $"{fragments.Length} speech fragments: " +
      string.Join(" | ", fragments.Select(fragment => fragment.Text)));
    Require(
      fragments[0].Text.Contains(
        "scripts/AI-transcript.py",
        StringComparison.Ordinal),
      $"Production speech changed attached identifier text to: {fragments[0].Text}");
    Require(
      !fragments[0].Text.Contains("AI-transcript. py", StringComparison.Ordinal),
      "Production speech inserted whitespace after an attached identifier period.");
  }

  /// <summary>
  /// Windows.Media bookmark synthesis keeps the display token `.` but voices
  /// an attached identifier period as "dot". The production history must keep
  /// `py` adjacent so that exact synthesis rule remains applicable.
  /// </summary>
  private static void TestAttachedIdentifierPeriodSynthesizesDot()
  {
    SpeechFragment[] fragments = LoadAttachedIdentifierHistory();
    SpeechFragment fragment = fragments.FirstOrDefault(item =>
      item.Text.Contains("AI-transcript", StringComparison.Ordinal)) ??
      throw new InvalidOperationException(
        "Production history omitted the attached identifier fragment.");
    MatchCollection tokens = SpeechTokenization.Matches(fragment.Text);
    int dotIndex = -1;
    for (int index = 1; index < tokens.Count; ++index)
    {
      if (tokens[index].Value == "." &&
          tokens[index - 1].Value.Contains(
            "AI-transcript",
            StringComparison.Ordinal))
      {
        dotIndex = index;
        break;
      }
    }
    Require(
      dotIndex > 0 && dotIndex < tokens.Count,
      $"Could not locate the identifier period in fragment: {fragment.Text}");

    MethodInfo synthesis = typeof(SapiSpeechEngine).GetMethod(
      "GetBookmarkedSynthesisText",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException(
        "Windows.Media bookmark synthesis token transform was not found.");
    string spoken = synthesis.Invoke(
      null,
      new object[] { tokens, dotIndex }) as string ?? string.Empty;
    Require(
      string.Equals(spoken, "dot", StringComparison.Ordinal),
      $"Attached display period synthesized as '{spoken}' instead of 'dot'.");
  }

  private static SpeechFragment[] LoadAttachedIdentifierHistory()
  {
    string record = ClaudeRecord(
      "user",
      "Open [scripts/AI-transcript.py]() now.",
      1,
      null);
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue75-attached-dot-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
      string sessionPath = Path.Combine(root, "session.jsonl");
      File.WriteAllText(sessionPath, record + Environment.NewLine);
      LocatedSession session = SessionLocator.FromPath(
        sessionPath,
        AgentSource.Claude);
      using var monitor = new JsonlSessionMonitor();
      SpeechHistorySnapshot history = monitor.LoadHistoryPreview(
        session,
        speakExistingLatestTurn: false);
      return history.Fragments
        .Where(fragment => fragment.Category == ContentCategory.User)
        .ToArray();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }
'''
replace_once(
  issue75,
  '''  /// <summary>
  /// Policy changes must be centralized. Static Core word elements may not be
''',
  issue75_methods + '''
  /// <summary>
  /// Policy changes must be centralized. Static Core word elements may not be
''')

issue54 = "AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs"
replace_once(
  issue54,
  '''      ("real-session/directional-shift-keeps-prefetch-headroom",
        TestDirectionalShiftKeepsPrefetchHeadroom),
      ("real-session/live-end-window-retains-preceding-unit",
''',
  '''      ("real-session/directional-shift-keeps-prefetch-headroom",
        TestDirectionalShiftKeepsPrefetchHeadroom),
      ("real-session/pending-manual-scroll-converges-after-replacement",
        TestPendingManualScrollConvergesAfterReplacement),
      ("real-session/live-end-window-retains-preceding-unit",
''')
replace_once(
  issue54,
  '''      ("real-session/follow-opens-canonical-disclosure-only-when-enabled",
        TestFollowOpensCanonicalDisclosureOnlyWhenEnabled),
      ("real-session/input-diagnostics-capture-key-mouse-and-follow-state",
''',
  '''      ("real-session/follow-opens-canonical-disclosure-only-when-enabled",
        TestFollowOpensCanonicalDisclosureOnlyWhenEnabled),
      ("real-session/owned-editor-retains-bare-transport-keys",
        TestOwnedEditorRetainsBareTransportKeys),
      ("real-session/input-diagnostics-capture-key-mouse-and-follow-state",
''')

replace_once(
  issue54,
  '''    Require(
      shifted.EndIndex - 12 >= 4,
      "Directional shift left fewer than four viewport-heights of measured " +
      "materialized headroom beyond the visible range.");
  }

  /// <summary>
  /// Reproduces issue #58 without splitting Core atomic units.
''',
  '''    Require(
      shifted.EndIndex - 12 >= 4,
      "Directional shift left fewer than four viewport-heights of measured " +
      "materialized headroom beyond the visible range.");
    int expectedCount = shifted.EndIndex - shifted.StartIndex + 1;
    Require(
      shifted.Records.Count == expectedCount,
      $"Shifted window {shifted.StartIndex}..{shifted.EndIndex} contains " +
      $"{shifted.Records.Count} records instead of contiguous count {expectedCount}.");
    int[] expectedRecordNumbers = document.Records
      .Skip(shifted.StartIndex)
      .Take(expectedCount)
      .Select(record => record.RecordNumber)
      .ToArray();
    Require(
      shifted.Records.Select(record => record.RecordNumber)
        .SequenceEqual(expectedRecordNumbers),
      "Directional shift skipped or reordered canonical records.");
  }

  /// <summary>
  /// Reproduces the rapid-scroll stall: physical same-direction demand arrives
  /// while a virtual shift is pending, its short intent timer expires, and the
  /// completed replacement still leaves the viewport at an unloaded edge.
  /// Completion must continue materialization without requiring another
  /// physical input and without treating programmatic scroll as new user input.
  /// </summary>
  private static void TestPendingManualScrollConvergesAfterReplacement()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-scroll-convergence-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "scroll-convergence.jsonl");
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
      view.SelectSession(path, AgentSource.Codex, "Scroll convergence fixture");
      WaitForTranscriptRender(view);

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int middleIndex = document.Count / 2;
      Task middleWindow = InvokeTask(
        view,
        "RenderWindowForIndexAsync",
        middleIndex,
        "convergence-precondition",
        null,
        null);
      PumpUntilCompleted(middleWindow, "middle convergence window");
      int currentStart = ReadField<int>(view, "_windowStartIndex");
      Require(currentStart > 2,
        "Convergence fixture did not leave canonical content above the window.");

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      var reasons = new List<string>();
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (rootElement.TryGetProperty("type", out JsonElement typeElement) &&
              typeElement.GetString() == "window-shift" &&
              rootElement.TryGetProperty("reason", out JsonElement reasonElement))
          {
            reasons.Add(reasonElement.GetString() ?? string.Empty);
          }
        }
        catch (JsonException)
        {
        }
      };

      ExecuteVoidScript(
        webView,
        """
(() => {
  virtualShiftPending = true;
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY:-240,
    bubbles:true,
    cancelable:true
  }));
  window.scrollBy(0, -240);
})()
""");
      PumpMessages(1400);
      int beforeReplacement = reasons.Count;

      ExecuteVoidScript(
        webView,
        """
(() => {
  const start = Math.max(3, Number(windowStartIndex));
  const end = start + 2;
  const html = [start, start + 1, end].map(index =>
    '<div class="virtual-record" data-virtual-index="' + index + '">' +
    '<span class="record-anchor" data-jsonl-record="' + (index + 1) + '"></span>' +
    '<p>convergence record ' + index + '</p></div>').join('');
  replaceTranscriptWindow(
    html,
    false,
    [],
    start,
    end,
    0,
    5000,
    null,
    null,
    null,
    null,
    [],
    '',
    9001,
    'scroll-up',
    77,
    false);
})()
""");
      PumpMessages(1000);

      Require(
        reasons.Skip(beforeReplacement).Any(reason => reason == "scroll-up"),
        "A completed manual-scroll replacement remained at the unloaded upper " +
        "edge after the physical-intent timer expired and did not request the " +
        "next adjacent canonical window.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces issue #58 without splitting Core atomic units.
''')

owned_editor_method = r'''
  /// <summary>
  /// Bare transport keys belong to a writable editor in an owned top-level
  /// window. MainForm's global filter must decline the key and let the native
  /// edit control process it normally; it must never synthesize or redirect it.
  /// </summary>
  private static void TestOwnedEditorRetainsBareTransportKeys()
  {
    using var form = new MainForm();
    form.Show();
    _ = form.Handle;
+    PronunciationDialog? dialog = null;
    try
    {
      SpeechService speech = ReadField<SpeechService>(form, "_speech");
      InstalledSpeechVoice voice = speech.GetInstalledVoices().FirstOrDefault() ??
        throw new InvalidOperationException(
          "No installed speech voice is available for editor routing acceptance.");
      var profile = new SpeechProfileSettings(voice.Name, 0, 0)
      {
        Volume = 100
      };
-      using var dialog = new PronunciationDialog(
+      dialog = new PronunciationDialog(
        string.Empty,
        string.Empty,
        speech,
        () => profile,
        () => AudioWakeSettings.Default with { Enabled = false },
        _ => { },
        default);
      dialog.Show(form);
      PumpMessages(150);

      RichTextBox editor =
        ReadField<RichTextBox>(dialog, "_pronunciationsTextBox");
      TabPage page = FindAncestor<TabPage>(editor) ??
        throw new InvalidOperationException(
          "Pronunciation RichTextBox is not hosted by a TabPage.");
      if (page.Parent is not TabControl tabs)
      {
        throw new InvalidOperationException(
          "Pronunciation RichTextBox TabPage has no TabControl parent.");
      }
      tabs.SelectedTab = page;
      editor.Focus();
      PumpMessages(100);
      Require(editor.Focused,
        "Pronunciation RichTextBox did not acquire focus for routing acceptance.");

      Message keyDown = Message.Create(
        editor.Handle,
        0x0100,
        new IntPtr((int)Keys.H),
        IntPtr.Zero);
      bool handled = form.PreFilterMessage(ref keyDown);
      Require(
        !handled,
        "MainForm consumed bare H as transport navigation while the owned " +
        "Pronunciation RichTextBox had focus.");

      editor.Clear();
      _ = SendMessageForEditorAcceptance(
        editor.Handle,
        0x0102,
        new IntPtr('h'),
        IntPtr.Zero);
      PumpMessages(50);
      Require(
        string.Equals(editor.Text, "h", StringComparison.Ordinal),
        "Normal native editor handling did not receive the unconsumed character.");
    }
    finally
    {
+      dialog?.Dispose();
      Application.RemoveMessageFilter(form);
    }
  }
'''.replace('+    ', '    ').replace('-      ', '      ').replace('+      ', '      ')
replace_once(
  issue54,
  '''  /// <summary>
  /// Reproduces issue #77. The structured JSONL log must contain the physical
''',
  owned_editor_method + '''
  /// <summary>
  /// Reproduces issue #77. The structured JSONL log must contain the physical
''')

helpers = r'''
  private static T? FindAncestor<T>(Control control)
    where T : Control
  {
    for (Control? current = control; current is not null; current = current.Parent)
    {
      if (current is T typed)
      {
        return typed;
      }
    }
    return null;
  }

  [System.Runtime.InteropServices.DllImport(
    "user32.dll",
    CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  private static extern IntPtr SendMessageForEditorAcceptance(
    IntPtr window,
    int message,
    IntPtr wordParameter,
    IntPtr longParameter);
'''
replace_once(
  issue54,
  '''  private static long FirstCanonicalWordId(string html)
''',
  helpers + '''
  private static long FirstCanonicalWordId(string html)
''')

print("Applied acceptance RED edits.")
