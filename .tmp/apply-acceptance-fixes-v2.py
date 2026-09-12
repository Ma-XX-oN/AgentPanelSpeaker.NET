from pathlib import Path

ROOT = Path("AgentPanelSpeaker")


def read(path: Path) -> str:
  return path.read_text(encoding="utf-8")


def write(path: Path, text: str) -> None:
  path.write_text(text, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one anchor, found {count}.")
  return text.replace(old, new, 1)


def replace_documented_method(
  text: str,
  signature: str,
  replacement: str,
  label: str,
) -> str:
  signature_at = text.find(signature)
  if signature_at < 0:
    raise RuntimeError(f"{label}: signature was not found.")
  start = text.rfind("  /// <summary>\n", 0, signature_at)
  end = text.find("  /// <summary>\n", signature_at + len(signature))
  if start < 0 or end < 0:
    raise RuntimeError(f"{label}: documentation boundaries were not found.")
  return text[:start] + replacement + text[end:]


# SpeechService: policy revalidation must cancel an active history utterance
# that became ineligible, using the same serialized cancellation transition as
# rolled-back-history policy changes.
speech_path = ROOT / "SpeechService.cs"
speech = read(speech_path)
speech_method = r'''  /// <summary>
  /// Revalidates retained history after a speech-eligibility policy change.
  /// A paused cursor moves to the next eligible fragment. An actively speaking
  /// history fragment that became ineligible is cancelled and playback resumes
  /// at the next eligible fragment without rebuilding canonical history.
  /// </summary>
  public void RevalidateHistoryEligibility(string reason)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(reason);
    lock (_sync)
    {
      ThrowIfDisposed();
      if (_history.Count == 0)
      {
        return;
      }

      int anchor = _activeHistoryIndex >= 0
        ? _activeHistoryIndex
        : _pendingHistoryIndex ?? _nextHistoryIndex;
      if (anchor < 0 || anchor >= _history.Count ||
          TryGetEligibleProfileLocked(_history[anchor], out _, out _))
      {
        return;
      }

      int candidate = FindNextEligibleLocked(anchor + 1);
      DiagnosticLog.Write("speech.eligibility_revalidated", new
      {
        reason,
        anchor,
        anchorNodeId = GetHistoryNodeIdLocked(anchor),
        candidate,
        candidateNodeId = GetHistoryNodeIdLocked(candidate),
        activeKind = _activeKind.ToString(),
        activeHistoryIndex = _activeHistoryIndex,
        pendingHistoryIndex = _pendingHistoryIndex,
        pendingHistoryWordIndex = _pendingHistoryWordIndex,
        nextHistoryIndex = _nextHistoryIndex,
        historyCount = _history.Count,
        isPaused = _isPaused
      });

      if (_activeKind == ActiveSpeechKind.History)
      {
        if (_isPaused)
        {
          if (candidate >= 0)
          {
            RestartHistoryLocked(candidate);
          }
          else
          {
            MoveToPausedLiveEndLocked();
          }
          return;
        }

        _pendingHistoryIndex = candidate >= 0 ? candidate : null;
        _pendingHistoryWordIndex = 0;
        _nextHistoryIndex = candidate >= 0 ? candidate : _history.Count;
        _pauseBeforeNextHistory = candidate >= 0;
        _lastFenceActivity = null;
        _engine.Cancel();
        return;
      }

      if (!_isPaused)
      {
        return;
      }

      if (candidate >= 0)
      {
        _pendingHistoryIndex = candidate;
        _pendingHistoryWordIndex = 0;
        _nextHistoryIndex = candidate;
        _lastFenceActivity = null;
        SetPausedNavigationPositionLocked(candidate);
      }
      else
      {
        MoveToPausedLiveEndLocked();
      }
    }
  }

'''
speech = replace_documented_method(
  speech,
  "  public void RevalidatePausedNavigationEligibility(string reason)\n",
  speech_method,
  "SpeechService eligibility revalidation",
)
write(speech_path, speech)


# Canonical prose segmentation: attached punctuation is not a sentence break.
monitor_path = ROOT / "JsonlSessionMonitor.cs"
monitor = read(monitor_path)
old_punctuation = '''      int end = index + 1;
      while (end < words.Count &&
             words[end].SeparatorBefore.Length == 0 &&
             words[end].Text is "\\\"" or "'" or ")" or "]" or "}")
      {
        ++end;
      }

      AddCanonicalProsePart(
'''
new_punctuation = '''      int end = index + 1;
      while (end < words.Count &&
             words[end].SeparatorBefore.Length == 0 &&
             words[end].Text is "\\\"" or "'" or ")" or "]" or "}")
      {
        ++end;
      }
      if (end < words.Count && words[end].SeparatorBefore.Length == 0)
      {
        continue;
      }

      AddCanonicalProsePart(
'''
monitor = replace_once(
  monitor,
  old_punctuation,
  new_punctuation,
  "canonical attached punctuation",
)
write(monitor_path, monitor)


# InputDiagnosticTracker: WebView wheel events enter the existing physical input
# ID stream before the browser emits any Follow state transition.
input_path = ROOT / "InputDiagnosticTracker.cs"
input_text = read(input_path)
input_marker = '''  /// <summary>\n  /// Returns the most recent physical identity for one key.\n  /// </summary>\n'''
input_method = r'''  /// <summary>
  /// Records one vertical-wheel input consumed inside WebView2 and returns its
  /// identity in the same physical-input timeline used by native WinForms input.
  /// </summary>
  public long ObserveWebViewWheel(
    int delta,
    Keys modifiers,
    bool followSpeech,
    bool speaking,
    bool paused,
    string targetTag,
    string targetId)
  {
    long inputId = NextInputId();
    _recentMouseInputId = inputId;
    _recentInputId = inputId;
    DiagnosticLog.Write("input.physical", new
    {
      inputId,
      kind = "mouse",
      phase = "wheel",
      code = "vertical-wheel",
      modifiers = modifiers.ToString(),
      repeat = false,
      delta,
      route = "webview",
      nativeMessage = (string?)null,
      targetHwnd = (string?)null,
      targetType = string.IsNullOrWhiteSpace(targetTag)
        ? "WebView2.DOM"
        : $"WebView2.DOM.{targetTag}",
      targetName = string.IsNullOrWhiteSpace(targetId) ? null : targetId,
      targetPath = "MainForm/TranscriptView/WebView2",
      followSpeech,
      speaking,
      paused,
      timestamp = Stopwatch.GetTimestamp()
    });
    return inputId;
  }

'''
input_text = replace_once(
  input_text,
  input_marker,
  input_method + input_marker,
  "WebView wheel input tracker",
)
write(input_path, input_text)


# TranscriptView: report WebView wheel input and explicitly continue manual
# materialization after a completed manual-scroll replacement until geometry has
# enough adjacent canonical content.
view_path = ROOT / "TranscriptView.cs"
view = read(view_path)
view = replace_once(
  view,
  "  public event Action<bool, string>? FollowSpeechChanged;\n",
  "  public event Action<bool, string>? FollowSpeechChanged;\n\n"
  "  /// <summary>\n"
  "  /// Raised for a vertical-wheel input received inside WebView2.\n"
  "  /// </summary>\n"
  "  public event Action<int, Keys, string, string>? PhysicalWheelInput;\n",
  "TranscriptView physical-wheel event",
)

follow_anchor = '''      if (type == "follow-changed")\n      {\n'''
wheel_branch = r'''      if (type == "physical-wheel")
      {
        int delta = (int)Math.Round(ReadOptionalDouble(root, "delta") ?? 0);
        Keys modifiers = Keys.None;
        if (ReadOptionalBoolean(root, "ctrlKey") == true)
        {
          modifiers |= Keys.Control;
        }
        if (ReadOptionalBoolean(root, "altKey") == true)
        {
          modifiers |= Keys.Alt;
        }
        if (ReadOptionalBoolean(root, "shiftKey") == true)
        {
          modifiers |= Keys.Shift;
        }
        if (ReadOptionalBoolean(root, "metaKey") == true)
        {
          modifiers |= Keys.LWin;
        }
        PhysicalWheelInput?.Invoke(
          delta,
          modifiers,
          ReadOptionalString(root, "targetTag"),
          ReadOptionalString(root, "targetId"));
        return;
      }
'''
view = replace_once(
  view,
  follow_anchor,
  wheel_branch + follow_anchor,
  "TranscriptView physical-wheel WebMessage",
)

old_wheel = r'''window.addEventListener('wheel', event => {
  markUserScrollIntent(event.deltaY);
}, {
  passive:true,
  capture:true
});
'''
new_wheel = r'''window.addEventListener('wheel', event => {
  const target = event.target instanceof Element ? event.target : null;
  chrome.webview.postMessage({
    type:'physical-wheel',
    delta:event.deltaY,
    ctrlKey:event.ctrlKey,
    altKey:event.altKey,
    shiftKey:event.shiftKey,
    metaKey:event.metaKey,
    targetTag:target?.tagName ?? '',
    targetId:target?.id ?? ''
  });
  markUserScrollIntent(event.deltaY);
}, {
  passive:true,
  capture:true
});
'''
view = replace_once(view, old_wheel, new_wheel, "browser wheel reporting")

manual_direction_anchor = r'''    programmaticScrollUntil = 0;
  }

  if (followSpeech) setFollowSpeech(false, true, 'manual-scroll');
'''
manual_direction_replacement = r'''    programmaticScrollUntil = 0;
  }

  // Physical input establishes manual-scroll ownership. Geometry, not the
  // short attribution timer, drives subsequent materialization convergence.
  userScrollIntentDirection = direction;
  if (followSpeech) setFollowSpeech(false, true, 'manual-scroll');
'''
view = replace_once(
  view,
  manual_direction_anchor,
  manual_direction_replacement,
  "manual-scroll direction retention",
)

function_start = view.find("function replaceTranscriptWindow(")
if function_start < 0:
  raise RuntimeError("replaceTranscriptWindow was not found.")
function_end = view.find("\nfunction ", function_start + 1)
if function_end < 0:
  raise RuntimeError("replaceTranscriptWindow end marker was not found.")
function_text = view[function_start:function_end]
old_pending = "  virtualShiftPending = false;\n"
if function_text.count(old_pending) != 1:
  raise RuntimeError(
    "replaceTranscriptWindow must have exactly one pending-reset anchor.")
new_pending = r'''  virtualShiftPending = false;
  if (renderReason === 'scroll-up' || renderReason === 'scroll-down') {
    const fallbackDirection = renderReason === 'scroll-up' ? -1 : 1;
    // Replacement and anchor restoration are programmatic. They never establish
    // user intent. After layout settles, explicitly remeasure coverage in the
    // latest physical direction and request only the next adjacent interval.
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        const convergenceDirection = userScrollIntentDirection !== 0
          ? userScrollIntentDirection
          : fallbackDirection;
        maybeRequestManualVirtualShift(convergenceDirection);
      });
    });
  }
'''
function_text = function_text.replace(old_pending, new_pending, 1)
view = view[:function_start] + function_text + view[function_end:]
write(view_path, view)


# MainForm: point bare-key ownership at the actual native message target, feed
# WebView wheel input into the shared diagnostic tracker, and use the broadened
# eligibility revalidation entry point.
main_path = ROOT / "MainForm.cs"
main = read(main_path)
old_name = "RevalidatePausedNavigationEligibility"
if main.count(old_name) != 2:
  raise RuntimeError(
    f"MainForm: expected two {old_name} calls, found {main.count(old_name)}.")
main = main.replace(old_name, "RevalidateHistoryEligibility")

main = replace_once(
  main,
  "    _transcriptView.FollowSpeechChanged += TranscriptFollowSpeechChanged;\n",
  "    _transcriptView.FollowSpeechChanged += TranscriptFollowSpeechChanged;\n"
  "    _transcriptView.PhysicalWheelInput += TranscriptPhysicalWheelInput;\n",
  "MainForm WebView wheel subscription",
)

follow_method_marker = "  private void TranscriptFollowSpeechChanged(bool enabled, string reason)\n"
wheel_method = r'''  /// <summary>
  /// Adds a WebView-consumed wheel event to the shared physical-input timeline
  /// before browser scroll handling can change Follow state.
  /// </summary>
  private void TranscriptPhysicalWheelInput(
    int delta,
    Keys modifiers,
    string targetTag,
    string targetId)
  {
    _inputDiagnostics.ObserveWebViewWheel(
      delta,
      modifiers,
      _transcriptSettingsPopup.Settings.FollowSpeech,
      _speech.IsSpeaking,
      _speech.IsPaused,
      targetTag,
      targetId);
  }

'''
main = replace_once(
  main,
  follow_method_marker,
  wheel_method + follow_method_marker,
  "MainForm WebView wheel handler",
)

focus_signature = "  private bool IsTransportShortcutBlockedByFocusedControl()\n"
focus_start = main.find(focus_signature)
if focus_start < 0:
  raise RuntimeError("MainForm focus-blocking method was not found.")
focus_end = main.find("  /// <summary>\n", focus_start + len(focus_signature))
if focus_end < 0:
  raise RuntimeError("MainForm focus-blocking method end marker was not found.")
focus_method = main[focus_start:focus_end]
focus_method = replace_once(
  focus_method,
  "  private bool IsTransportShortcutBlockedByFocusedControl()\n"
  "  {\n"
  "    Control? focused = this;\n",
  "  private bool IsTransportShortcutBlockedByFocusedControl(\n"
  "    IntPtr messageTarget = default)\n"
  "  {\n"
  "    Control? focused = messageTarget != IntPtr.Zero\n"
  "      ? Control.FromChildHandle(messageTarget)\n"
  "      : this;\n"
  "    focused ??= this;\n",
  "MainForm focused-control root",
)
main = main[:focus_start] + focus_method + main[focus_end:]

message_filter_block = r'''    if (hasNoModifiers && IsTransportShortcutBlockedByFocusedControl())
    {
      return Finish(false, "message-filter");
    }
'''
message_filter_fixed = r'''    if (hasNoModifiers &&
        IsTransportShortcutBlockedByFocusedControl(message.HWnd))
    {
      return Finish(false, "message-filter");
    }
'''
main = replace_once(
  main,
  message_filter_block,
  message_filter_fixed,
  "owned-editor message-filter routing",
)
write(main_path, main)


# README: the old claim that every physical input enters at the Win32 boundary
# is false for WebView2-consumed wheel input.
readme_path = Path("README.md")
readme = read(readme_path)
old_contract = '''Every keyboard key-down/key-up and mouse button,\ndouble-click, or wheel message delivered to Agent Panel Speaker is recorded as\n`input.physical` at the Win32 message-filter boundary. A physical input keeps one\n`inputId` across its down/up pair; repeated routing observations are not recorded\nas separate physical presses. The log records key/button identity, modifiers,\nmanaged target identity/path, and playback/Follow state, but does not copy the\ncontents of editable controls.\n'''
new_contract = '''Every keyboard key-down/key-up and mouse button, double-click, or wheel input\ndelivered to Agent Panel Speaker is recorded as `input.physical`. WinForms-owned\ninput enters the shared `InputDiagnosticTracker` at the Win32 message-filter\nboundary. Wheel input consumed inside WebView2 is reported by the browser and\nenters that same tracker with `route=webview` before its scroll can change Follow\nstate. A physical input keeps one `inputId` across its down/up pair; repeated\nrouting observations are not recorded as separate physical presses. The log\nrecords key/button identity, modifiers, managed or DOM target context, and\nplayback/Follow state, but does not copy the contents of editable controls.\n'''
readme = replace_once(readme, old_contract, new_contract, "README input contract")
write(readme_path, readme)

print("Applied acceptance production repairs v2.")
