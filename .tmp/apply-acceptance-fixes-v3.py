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


def documented_method_bounds(text: str, signature: str, label: str) -> tuple[int, int]:
  signature_at = text.find(signature)
  if signature_at < 0:
    raise RuntimeError(f"{label}: signature was not found.")
  start = text.rfind("  /// <summary>\n", 0, signature_at)
  end = text.find("  /// <summary>\n", signature_at + len(signature))
  if start < 0 or end < 0:
    raise RuntimeError(f"{label}: documentation boundaries were not found.")
  return start, end


# ---------------------------------------------------------------------------
# Speech policy: revalidate active as well as paused retained history.
# ---------------------------------------------------------------------------
speech_path = ROOT / "SpeechService.cs"
speech = read(speech_path)
speech_signature = "  public void RevalidatePausedNavigationEligibility(string reason)\n"
start, end = documented_method_bounds(
  speech,
  speech_signature,
  "SpeechService eligibility revalidation",
)
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
speech = speech[:start] + speech_method + speech[end:]
write(speech_path, speech)


# ---------------------------------------------------------------------------
# Canonical sentence segmentation: do not split an attached identifier period.
# ---------------------------------------------------------------------------
monitor_path = ROOT / "JsonlSessionMonitor.cs"
monitor = read(monitor_path)
monitor_signature = "  private static void AddCanonicalProseParts(\n"
method_at = monitor.find(monitor_signature)
if method_at < 0:
  raise RuntimeError("AddCanonicalProseParts was not found.")
method_end = monitor.find("  /// <summary>\n", method_at + len(monitor_signature))
if method_end < 0:
  raise RuntimeError("AddCanonicalProseParts end marker was not found.")
method = monitor[method_at:method_end]
call_anchor = '''      AddCanonicalProsePart(
        words,
        start,
        end,
'''
attached_guard = '''      if (end < words.Count && words[end].SeparatorBefore.Length == 0)
      {
        continue;
      }

'''
method = replace_once(
  method,
  call_anchor,
  attached_guard + call_anchor,
  "canonical attached punctuation insertion",
)
monitor = monitor[:method_at] + method + monitor[method_end:]
write(monitor_path, monitor)


# ---------------------------------------------------------------------------
# WebView wheel input joins the same physical-input diagnostic ID timeline.
# ---------------------------------------------------------------------------
input_path = ROOT / "InputDiagnosticTracker.cs"
input_text = read(input_path)
input_marker = '''  /// <summary>
  /// Returns the most recent physical identity for one key.
  /// </summary>
'''
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


# ---------------------------------------------------------------------------
# TranscriptView: physical wheel reporting + explicit geometry convergence.
# ---------------------------------------------------------------------------
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

follow_anchor = '''      if (type == "follow-changed")
      {
'''
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

manual_anchor = r'''    programmaticScrollUntil = 0;
  }

  if (followSpeech) setFollowSpeech(false, true, 'manual-scroll');
'''
manual_replacement = r'''    programmaticScrollUntil = 0;
  }

  // Physical input establishes manual-scroll ownership. Geometry, not the
  // short attribution timer, drives subsequent materialization convergence.
  userScrollIntentDirection = direction;
  if (followSpeech) setFollowSpeech(false, true, 'manual-scroll');
'''
view = replace_once(
  view,
  manual_anchor,
  manual_replacement,
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
    // Replacement/anchor restoration is programmatic and never establishes
    // user intent. After layout settles, remeasure the latest physical
    // direction and request only the next adjacent canonical interval.
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


# ---------------------------------------------------------------------------
# MainForm: active policy revalidation, real owned-editor target, wheel bridge.
# ---------------------------------------------------------------------------
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

follow_method = "  private void TranscriptFollowSpeechChanged(bool enabled, string reason)\n"
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
  follow_method,
  wheel_method + follow_method,
  "MainForm WebView wheel handler",
)

focus_signature = "  private bool IsTransportShortcutBlockedByFocusedControl()\n"
focus_at = main.find(focus_signature)
if focus_at < 0:
  raise RuntimeError("MainForm focus-blocking method was not found.")
focus_end = main.find("  /// <summary>\n", focus_at + len(focus_signature))
if focus_end < 0:
  raise RuntimeError("MainForm focus-blocking method end marker was not found.")
focus = main[focus_at:focus_end]
focus = replace_once(
  focus,
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
main = main[:focus_at] + focus + main[focus_end:]

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


# The broadened API has one implementation path; no old production call remains.
remaining_old = []
for path in ROOT.glob("*.cs"):
  if old_name in read(path):
    remaining_old.append(str(path))
if remaining_old:
  raise RuntimeError(
    "Obsolete paused-only eligibility API remains in: " +
    ", ".join(remaining_old))


# ---------------------------------------------------------------------------
# README: document the two real physical-input ingress routes truthfully.
# ---------------------------------------------------------------------------
readme_path = Path("README.md")
readme = read(readme_path)
paragraph_start = readme.find(
  "Every keyboard key-down/key-up and mouse button,\n"
  "double-click, or wheel message delivered to Agent Panel Speaker is recorded as\n")
if paragraph_start < 0:
  raise RuntimeError("README diagnostic contract start was not found.")
paragraph_end_marker = "contents of editable controls.\n"
paragraph_end = readme.find(paragraph_end_marker, paragraph_start)
if paragraph_end < 0:
  raise RuntimeError("README diagnostic contract end was not found.")
paragraph_end += len(paragraph_end_marker)
new_contract = '''Every keyboard key-down/key-up and mouse button, double-click, or wheel input
delivered to Agent Panel Speaker is recorded as `input.physical`. WinForms-owned
input enters the shared `InputDiagnosticTracker` at the Win32 message-filter
boundary. Wheel input consumed inside WebView2 is reported by the browser and
enters that same tracker with `route=webview` before its scroll can change Follow
state. A physical input keeps one `inputId` across its down/up pair; repeated
routing observations are not recorded as separate physical presses. The log
records key/button identity, modifiers, managed or DOM target context, and
playback/Follow state, but does not copy the contents of editable controls.
'''
readme = readme[:paragraph_start] + new_contract + readme[paragraph_end:]
write(readme_path, readme)

print("Applied acceptance production repairs v3.")
