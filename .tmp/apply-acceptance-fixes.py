from pathlib import Path


def read(path: str) -> str:
  return Path(path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
  Path(path).write_text(text, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{label}: expected one anchor, found {count}")
  return text.replace(old, new, 1)


# Active playback eligibility revalidation.
path = "AgentPanelSpeaker/SpeechService.cs"
text = read(path)
start = text.index(
  "  /// <summary>\n"
  "  /// Revalidates a paused history cursor after a speech-eligibility policy\n")
end = text.index(
  "  /// <summary>\n"
  "  /// Gets enabled installed voices and their descriptive labels.\n",
  start)
replacement = r'''  /// <summary>
  /// Revalidates retained history after a speech-eligibility policy change.
  /// A paused cursor moves to the next eligible fragment. An actively speaking
  /// history fragment that became ineligible is cancelled and playback resumes
  /// at the next eligible fragment without rebuilding canonical history.
  /// </summary>
  public void RevalidatePausedNavigationEligibility(string reason)
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
text = text[:start] + replacement + text[end:]
write(path, text)


# Canonical sentence boundaries must preserve punctuation attached inside words.
path = "AgentPanelSpeaker/JsonlSessionMonitor.cs"
text = read(path)
old = '''      int end = index + 1;
      while (end < words.Count &&
             words[end].SeparatorBefore.Length == 0 &&
             words[end].Text is "\\\"" or "'" or ")" or "]" or "}")
      {
        ++end;
      }

      AddCanonicalProsePart(
'''
new = '''      int end = index + 1;
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
text = replace_once(text, old, new, "canonical punctuation adjacency")
write(path, text)


# Route bare shortcuts according to the actual message target, including owned
# top-level editors. Normal WinForms delivery remains authoritative.
path = "AgentPanelSpeaker/MainForm.cs"
text = read(path)
old_call = "if (hasNoModifiers && IsTransportShortcutBlockedByFocusedControl())"
count = text.count(old_call)
if count != 2:
  raise RuntimeError(f"editor blocker calls: expected two, found {count}")
text = text.replace(
  old_call,
  "if (hasNoModifiers && "
  "IsTransportShortcutBlockedByFocusedControl(message.HWnd))")

start = text.index(
  "  /// <summary>\n"
  "  /// Returns whether a text-entry control should retain one bare shortcut.\n"
  "  /// </summary>\n"
  "  private bool IsTransportShortcutBlockedByFocusedControl()\n")
end = text.index(
  "  /// <summary>\n"
  "  /// Cancels all pending automatic voice-setting previews.\n",
  start)
blocker = r'''  /// <summary>
  /// Returns whether the actual native message target belongs to a writable
  /// text-entry control that must retain one bare shortcut.
  /// </summary>
  private bool IsTransportShortcutBlockedByFocusedControl(IntPtr messageWindow)
  {
    Control? focused = messageWindow == IntPtr.Zero
      ? null
      : Control.FromChildHandle(messageWindow);
    Form? activeForm = Form.ActiveForm;
    if (activeForm is not null && activeForm.ContainsFocus)
    {
      focused = FindFocusedDescendant(activeForm) ?? focused;
    }
    focused ??= FindFocusedDescendant(this) ?? this;

    var ancestry = new List<Control>();
    for (Control? control = focused; control is not null;
         control = control.Parent)
    {
      ancestry.Add(control);
    }

    if (ancestry.Any(control => control is UpDownBase))
    {
      return false;
    }

    foreach (Control control in ancestry)
    {
      if (control is TextBoxBase textBox && !textBox.ReadOnly)
      {
        return true;
      }
      if (control is ComboBox comboBox &&
          comboBox.DropDownStyle != ComboBoxStyle.DropDownList)
      {
        return true;
      }
    }
    return false;
  }

'''
text = text[:start] + blocker + text[end:]

old = '''    _transcriptView.FindSeekEndRequested += TranscriptFindSeekEndRequested;
    _transcriptView.FollowSpeechChanged += TranscriptFollowSpeechChanged;
'''
new = '''    _transcriptView.FindSeekEndRequested += TranscriptFindSeekEndRequested;
    _transcriptView.PhysicalInputReceived += TranscriptPhysicalInputReceived;
    _transcriptView.FollowSpeechChanged += TranscriptFollowSpeechChanged;
'''
text = replace_once(text, old, new, "WebView physical-input subscription")

anchor = '''  private void TranscriptFollowSpeechChanged(bool enabled, string reason)
'''
handler = r'''  private void TranscriptPhysicalInputReceived(WebViewPhysicalInput input)
  {
    _inputDiagnostics.ObserveWebViewInput(
      input,
      _transcriptSettingsPopup.Settings.FollowSpeech,
      _speech.IsSpeaking,
      _speech.IsPaused);
  }

'''
text = replace_once(text, anchor, handler + anchor, "WebView input handler")
write(path, text)


# Extend the canonical input timeline to physical input consumed inside WebView2.
path = "AgentPanelSpeaker/InputDiagnosticTracker.cs"
text = read(path)
anchor = '''  /// <summary>
  /// Returns the most recent physical identity for one key.
'''
method = r'''  /// <summary>
  /// Logs one physical keyboard or mouse input observed inside WebView2 and
  /// returns the same canonical identity model used by the Win32 route.
  /// </summary>
  public long ObserveWebViewInput(
    WebViewPhysicalInput input,
    bool followSpeech,
    bool speaking,
    bool paused)
  {
    long inputId;
    bool repeat = input.Repeat;
    Keys modifiers = Keys.None;
    if (input.Alt) modifiers |= Keys.Alt;
    if (input.Control) modifiers |= Keys.Control;
    if (input.Shift) modifiers |= Keys.Shift;

    if (string.Equals(input.Kind, "keyboard", StringComparison.Ordinal))
    {
      int virtualKey = (int)(input.KeyCode & Keys.KeyCode);
      if (string.Equals(input.Phase, "down", StringComparison.Ordinal))
      {
        if (!_pressedKeys.TryGetValue(virtualKey, out inputId))
        {
          inputId = NextInputId();
          _pressedKeys[virtualKey] = inputId;
        }
        else
        {
          repeat = true;
        }
        _recentKeyIds[virtualKey] = inputId;
      }
      else if (string.Equals(input.Phase, "up", StringComparison.Ordinal) &&
               _pressedKeys.Remove(virtualKey, out long pressedId))
      {
        inputId = pressedId;
        _recentKeyIds[virtualKey] = inputId;
      }
      else
      {
        inputId = NextInputId();
        _recentKeyIds[virtualKey] = inputId;
      }
    }
    else
    {
      string identityKey = input.Code;
      if (string.Equals(input.Phase, "down", StringComparison.Ordinal) ||
          string.Equals(input.Phase, "double-click", StringComparison.Ordinal))
      {
        inputId = NextInputId();
        _pressedMouseButtons[identityKey] = inputId;
      }
      else if (string.Equals(input.Phase, "up", StringComparison.Ordinal) &&
               _pressedMouseButtons.Remove(identityKey, out long pressedId))
      {
        inputId = pressedId;
      }
      else
      {
        inputId = NextInputId();
      }
      _recentMouseInputId = inputId;
    }

    _recentInputId = inputId;
    DiagnosticLog.Write("input.physical", new
    {
      inputId,
      kind = input.Kind,
      phase = input.Phase,
      code = input.Code,
      modifiers = modifiers.ToString(),
      repeat,
      delta = input.DeltaY,
      deltaX = input.DeltaX,
      route = "webview",
      targetType = "WebView2.DOM",
      targetPath = input.Target,
      input.Editable,
      input.Meta,
      input.Button,
      input.Buttons,
      followSpeech,
      speaking,
      paused,
      timestamp = Stopwatch.GetTimestamp()
    });
    return inputId;
  }

'''
text = replace_once(text, anchor, method + anchor, "WebView input tracker method")
write(path, text)


# Browser-side physical input capture plus geometry-driven virtual-window
# convergence after a legitimate manual shift.
path = "AgentPanelSpeaker/TranscriptView.cs"
text = read(path)
class_anchor = '''/// <summary>
/// Renders the selected JSONL session as Markdown-derived HTML and tracks the
/// current speech position.
/// </summary>
internal sealed class TranscriptView : UserControl
'''
record_and_class = r'''/// <summary>
/// Describes one physical keyboard or mouse event observed inside WebView2.
/// </summary>
internal sealed record WebViewPhysicalInput(
  string Kind,
  string Phase,
  string Code,
  Keys KeyCode,
  bool Alt,
  bool Control,
  bool Shift,
  bool Meta,
  bool Repeat,
  double DeltaX,
  double DeltaY,
  int Button,
  int Buttons,
  bool Editable,
  string Target);

/// <summary>
/// Renders the selected JSONL session as Markdown-derived HTML and tracks the
/// current speech position.
/// </summary>
internal sealed class TranscriptView : UserControl
'''
text = replace_once(text, class_anchor, record_and_class, "WebView input record")

old = '''  /// <summary>
  /// Raised when the transcript overlay or manual scrolling changes follow mode.
  /// </summary>
  public event Action<bool, string>? FollowSpeechChanged;
'''
new = '''  /// <summary>
  /// Raised for physical keyboard or mouse input observed inside WebView2.
  /// </summary>
  public event Action<WebViewPhysicalInput>? PhysicalInputReceived;

  /// <summary>
  /// Raised when the transcript overlay or manual scrolling changes follow mode.
  /// </summary>
  public event Action<bool, string>? FollowSpeechChanged;
'''
text = replace_once(text, old, new, "WebView input event")

old = '''      string type = typeElement.GetString() ?? string.Empty;
      if (type == "playback-applied")
'''
new = '''      string type = typeElement.GetString() ?? string.Empty;
      if (type == "physical-input")
      {
        Keys keyCode = KeyNameToKeys(ReadOptionalString(root, "key"));
        PhysicalInputReceived?.Invoke(new WebViewPhysicalInput(
          ReadOptionalString(root, "kind"),
          ReadOptionalString(root, "phase"),
          ReadOptionalString(root, "code"),
          keyCode,
          ReadOptionalBoolean(root, "alt") == true,
          ReadOptionalBoolean(root, "control") == true,
          ReadOptionalBoolean(root, "shift") == true,
          ReadOptionalBoolean(root, "meta") == true,
          ReadOptionalBoolean(root, "repeat") == true,
          ReadOptionalDouble(root, "deltaX") ?? 0.0,
          ReadOptionalDouble(root, "deltaY") ?? 0.0,
          ReadOptionalInt32(root, "button") ?? -1,
          ReadOptionalInt32(root, "buttons") ?? 0,
          ReadOptionalBoolean(root, "editable") == true,
          ReadOptionalString(root, "target")));
        return;
      }
      if (type == "playback-applied")
'''
text = replace_once(text, old, new, "physical-input WebMessage branch")

old = '''function isEditableScrollTarget(target) {
  return target instanceof Element &&
    (target.matches('input,textarea,select') || target.isContentEditable);
}

window.addEventListener('wheel', event => {
  markUserScrollIntent(event.deltaY);
}, {
'''
new = r'''function isEditableScrollTarget(target) {
  return target instanceof Element &&
    (target.matches('input,textarea,select') || target.isContentEditable);
}

function physicalInputTarget(target) {
  if (!(target instanceof Element)) return '';
  const id = target.id ? '#' + target.id : '';
  const classes = typeof target.className === 'string' && target.className
    ? '.' + target.className.trim().split(/\s+/).slice(0, 3).join('.')
    : '';
  return target.tagName.toLowerCase() + id + classes;
}

function mouseButtonCode(button) {
  if (button === 0) return 'left';
  if (button === 1) return 'middle';
  if (button === 2) return 'right';
  return 'button-' + String(button);
}

function postPhysicalInput(kind, phase, event, code) {
  chrome.webview.postMessage({
    type:'physical-input',
    kind,
    phase,
    key:kind === 'keyboard' ? String(event.key || '') : '',
    code:String(code || ''),
    alt:!!event.altKey,
    control:!!event.ctrlKey,
    shift:!!event.shiftKey,
    meta:!!event.metaKey,
    repeat:!!event.repeat,
    deltaX:Number(event.deltaX || 0),
    deltaY:Number(event.deltaY || 0),
    button:Number.isFinite(Number(event.button)) ? Number(event.button) : -1,
    buttons:Number.isFinite(Number(event.buttons)) ? Number(event.buttons) : 0,
    editable:isEditableScrollTarget(event.target),
    target:physicalInputTarget(event.target),
    trusted:!!event.isTrusted
  });
}

window.addEventListener('keydown', event => {
  postPhysicalInput('keyboard', 'down', event, event.key);
}, {capture:true});
window.addEventListener('keyup', event => {
  postPhysicalInput('keyboard', 'up', event, event.key);
}, {capture:true});
window.addEventListener('wheel', event => {
  postPhysicalInput('mouse', 'wheel', event, 'vertical-wheel');
  markUserScrollIntent(event.deltaY);
}, {
'''
text = replace_once(text, old, new, "browser physical input helpers")

old = '''window.addEventListener('pointerdown', event => {
  if (event.button !== 0) return;
'''
new = '''window.addEventListener('pointerdown', event => {
  if (event.pointerType === 'mouse') {
    postPhysicalInput('mouse', 'down', event, mouseButtonCode(event.button));
  }
  if (event.button !== 0) return;
'''
text = replace_once(text, old, new, "pointer-down logging")

old = '''window.addEventListener('pointerup', () => {
  scrollbarPointerActive = false;
}, {capture:true});
window.addEventListener('pointercancel', () => {
  scrollbarPointerActive = false;
}, {capture:true});
'''
new = '''window.addEventListener('pointerup', event => {
  if (event.pointerType === 'mouse') {
    postPhysicalInput('mouse', 'up', event, mouseButtonCode(event.button));
  }
  scrollbarPointerActive = false;
}, {capture:true});
window.addEventListener('pointercancel', event => {
  if (event.pointerType === 'mouse') {
    postPhysicalInput('mouse', 'cancel', event, mouseButtonCode(event.button));
  }
  scrollbarPointerActive = false;
}, {capture:true});
window.addEventListener('dblclick', event => {
  postPhysicalInput('mouse', 'double-click', event, mouseButtonCode(event.button));
}, {capture:true});
'''
text = replace_once(text, old, new, "pointer-up logging")

old = '''  lastWindowTransactionDiagnostic = {
    transactionId:diagnostic.transactionId,
    requestSequence:diagnostic.requestSequence,
    reason:diagnostic.reason,
    completedAt:performance.now(),
    afterVisibleStartIndex:diagnostic.afterVisibleStartIndex,
    afterVisibleEndIndex:diagnostic.afterVisibleEndIndex
  };
}
'''
new = '''  lastWindowTransactionDiagnostic = {
    transactionId:diagnostic.transactionId,
    requestSequence:diagnostic.requestSequence,
    reason:diagnostic.reason,
    completedAt:performance.now(),
    afterVisibleStartIndex:diagnostic.afterVisibleStartIndex,
    afterVisibleEndIndex:diagnostic.afterVisibleEndIndex
  };
  const convergenceDirection = diagnostic.reason === 'scroll-up'
    ? -1
    : diagnostic.reason === 'scroll-down'
      ? 1
      : 0;
  if (convergenceDirection !== 0) {
    requestAnimationFrame(() =>
      maybeRequestManualVirtualShift(convergenceDirection));
  }
}
'''
text = replace_once(text, old, new, "manual-scroll convergence")
write(path, text)

print("Applied production acceptance fixes.")
