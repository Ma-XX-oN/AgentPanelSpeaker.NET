from pathlib import Path


path = Path("AgentPanelSpeaker/Issue26UserContextSpeechRegressionTestRunner.cs")
text = path.read_text(encoding="utf-8")
start_marker = "  private static void TestActiveCheckboxCancelsIneligibleUtterance()\n"
end_marker = "  /// <summary>\n  /// Creates project options by the production record constructor while keeping\n"
start = text.find(start_marker)
if start < 0:
  raise RuntimeError("Active User Context RED method was not found.")
summary = text.rfind("  /// <summary>\n", 0, start)
if summary < 0:
  raise RuntimeError("Active User Context RED summary was not found.")
end = text.find(end_marker, start)
if end < 0:
  raise RuntimeError("Active User Context RED end marker was not found.")

method = r'''  /// <summary>
  /// Reproduces the real-machine acceptance failure where disabling the actual
  /// Speak User Context checkbox while one retained User Context fragment is
  /// actively speaking lets that now-ineligible utterance keep emitting word
  /// boundaries before playback advances to the next eligible User fragment.
  /// </summary>
  private static void TestActiveCheckboxCancelsIneligibleUtterance()
  {
    MainForm? form = null;
    try
    {
      form = new MainForm();
      _ = form.Handle;
      SpeechService speech = GetField<SpeechService>(form, "_speech");
      InstalledSpeechVoice voice = speech.GetInstalledVoices().FirstOrDefault() ??
        throw new InvalidOperationException(
          "No installed speech voice is available for active-toggle acceptance.");
      var profile = new SpeechProfileSettings(voice.Name, -10, 0)
      {
        Volume = 1
      };
      speech.SetPolicyProviders(
        category =>
          category == ContentCategory.UserContext &&
          !GetField<bool>(form, "_speakUserContext")
            ? profile with { VoiceName = SpeechProfileSettings.NotSpoken }
            : profile,
        _ => true,
        () => Array.Empty<string>(),
        () => PronunciationRuleSet.Parse(string.Empty),
        () => AudioWakeSettings.Default with { Enabled = false });

      TranscriptSettingsPopup popup =
        GetField<TranscriptSettingsPopup>(form, "_transcriptSettingsPopup");
      CheckBox speakContextCheckBox =
        GetField<CheckBox>(popup, "_speakUserContextCheckBox");
      speakContextCheckBox.Checked = true;
      Application.DoEvents();
      Require(GetField<bool>(form, "_speakUserContext"),
        "Actual Speak User Context checkbox did not enable playback policy.");

      string contextText = string.Join(
        ' ',
        Enumerable.Repeat("active user context source", 80));
      const string userText = "Visible user destination.";
      speech.LoadHistory(
        new[]
        {
          new SpeechFragment(
            9001,
            ContentCategory.UserContext,
            SpeechFragmentKind.Prose,
            contextText),
          new SpeechFragment(
            9002,
            ContentCategory.User,
            SpeechFragmentKind.Prose,
            userText)
        },
        Array.Empty<TurnCompletion>(),
        Array.Empty<BackgroundWorkEvent>(),
        PlaybackStartMode.Beginning);

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
            return positions.Count(position =>
              position.State == TranscriptPlaybackState.Speaking &&
              position.NodeId == 9001) >= 4;
          }
        },
        "User Context did not emit enough active boundaries for the toggle.");

      int transitionStart;
      lock (positions)
      {
        transitionStart = positions.Count;
      }
      speakContextCheckBox.Checked = false;
      Application.DoEvents();
      Require(!GetField<bool>(form, "_speakUserContext"),
        "Actual Speak User Context checkbox did not disable playback policy.");

      WaitUntil(
        () =>
        {
          lock (positions)
          {
            return positions.Skip(transitionStart).Any(position =>
              position.State == TranscriptPlaybackState.Speaking &&
              position.NodeId == 9002);
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
          position.NodeId == 9001),
        "User Context continued emitting speech boundaries after its actual " +
        "checkbox made the active utterance ineligible.");
    }
    finally
    {
      if (form is not null)
      {
        Application.RemoveMessageFilter(form);
        form.Dispose();
      }
    }
  }

'''

path.write_text(text[:summary] + method + text[end:], encoding="utf-8")
