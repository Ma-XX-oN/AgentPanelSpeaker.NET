from __future__ import annotations

import argparse
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
  return (ROOT / path).read_text(encoding="utf-8-sig")


def write(path: str, text: str) -> None:
  (ROOT / path).write_text(text, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
  count = text.count(old)
  if count != 1:
    raise SystemExit(f"{label}: expected one anchor, found {count}")
  return text.replace(old, new, 1)


def regex_once(text: str, pattern: str, replacement: str, label: str) -> str:
  updated, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
  if count != 1:
    raise SystemExit(f"{label}: expected one regex anchor, found {count}")
  return updated


def apply_tests() -> None:
  path = "AgentPanelSpeaker/Issue54RealSessionRegressionTestRunner.cs"
  text = read(path)
  if "real-session/follow-enable-reattaches-retained-core-word" in text:
    return

  text = replace_once(
    text,
    "using Microsoft.Web.WebView2.WinForms;\nusing System.Reflection;",
    "using Microsoft.Web.WebView2.WinForms;\nusing System.Diagnostics;\nusing System.Reflection;",
    "issue54 diagnostics using")

  old_tail = '''      ("real-session/stale-find-invalidated-after-install-stays-visible",\n        TestStaleFindInvalidatedAfterInstallStaysVisible)\n'''
  new_tail = '''      ("real-session/stale-find-invalidated-after-install-stays-visible",\n        TestStaleFindInvalidatedAfterInstallStaysVisible),\n      ("real-session/follow-enable-reattaches-retained-core-word",\n        TestFollowEnableReattachesRetainedCoreWord),\n      ("real-session/programmatic-scroll-does-not-disable-follow",\n        TestProgrammaticScrollDoesNotDisableFollow),\n      ("real-session/follow-opens-canonical-disclosure-only-when-enabled",\n        TestFollowOpensCanonicalDisclosureOnlyWhenEnabled),\n      ("real-session/input-diagnostics-capture-key-mouse-and-follow-state",\n        TestInputDiagnosticsCaptureKeyMouseAndFollowState)\n'''
  text = replace_once(text, old_tail, new_tail, "issue54 test list")

  insertion = r'''  /// <summary>
  /// Reproduces issue #57 on the current Core-word path. Follow OFF may retain
  /// a canonical paused cursor outside the materialized window. Turning Follow
  /// ON must replay that retained Core word, materialize its Core unit, and
  /// apply the marker without requiring another speech callback.
  /// </summary>
  private static void TestFollowEnableReattachesRetainedCoreWord()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue57-core-follow-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue57-core-follow.jsonl");
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

      TranscriptSettings followOff =
        TranscriptSettings.Default with { FollowSpeech = false };
      TranscriptSettings followOn =
        TranscriptSettings.Default with { FollowSpeech = true };
      view.ApplySettings(followOff, dark: false);
      view.SelectSession(path, AgentSource.Codex, "Issue 57 Core follow fixture");
      WaitForTranscriptRender(view);

      TranscriptVirtualDocument document =
        ReadField<TranscriptVirtualDocument>(view, "_virtualDocument");
      int initialStart = ReadField<int>(view, "_windowStartIndex");
      Require(initialStart > 0,
        "Issue #57 Core fixture did not leave earlier units unloaded.");

      int targetIndex = -1;
      long targetWordId = 0;
      for (int index = 0; index < initialStart; ++index)
      {
        long candidate = FirstCanonicalWordId(document.Records[index].Html);
        if (candidate <= 0)
        {
          continue;
        }
        targetIndex = index;
        targetWordId = candidate;
        break;
      }
      Require(targetIndex >= 0 && targetWordId > 0,
        "Issue #57 Core fixture exposed no off-window canonical word.");

      WebView2 webView = ReadField<WebView2>(view, "_webView");
      int exactPlaybackApplied = 0;
      webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
      {
        try
        {
          using JsonDocument message = JsonDocument.Parse(
            eventArgs.WebMessageAsJson);
          JsonElement rootElement = message.RootElement;
          if (rootElement.TryGetProperty("type", out JsonElement typeElement) &&
              typeElement.GetString() == "playback-applied" &&
              rootElement.TryGetProperty("wordId", out JsonElement wordElement) &&
              wordElement.TryGetInt64(out long appliedWordId) &&
              appliedWordId == targetWordId)
          {
            ++exactPlaybackApplied;
          }
        }
        catch (JsonException)
        {
        }
      };

      var position = new TranscriptPlaybackPosition(
        TranscriptPlaybackState.Paused,
        "retained canonical cursor",
        0,
        "retained",
        0,
        0,
        8,
        Stopwatch.GetTimestamp(),
        targetWordId);
      view.ShowPlaybackPosition(position);
      PumpMessages(300);
      Require(
        targetIndex < ReadField<int>(view, "_windowStartIndex") ||
        targetIndex > ReadField<int>(view, "_windowEndIndex"),
        "Follow OFF unexpectedly materialized the retained Core word.");

      // Remove only the browser-side retained projection. C# still owns the
      // authoritative current/last playback position. This makes the test prove
      // that the OFF->ON settings transition itself replays the cursor.
      ExecuteVoidScript(webView, "resetRetainedPlayback();");
      int appliedBeforeEnable = exactPlaybackApplied;

      view.ApplySettings(followOn, dark: false);
      PumpUntil(
        () =>
          targetIndex >= ReadField<int>(view, "_windowStartIndex") &&
          targetIndex <= ReadField<int>(view, "_windowEndIndex") &&
          exactPlaybackApplied > appliedBeforeEnable,
        "Follow ON to reattach the retained canonical cursor",
        timeoutMilliseconds: 8000);

      int appliedAfterEnable = exactPlaybackApplied;
      int startAfterEnable = ReadField<int>(view, "_windowStartIndex");
      int endAfterEnable = ReadField<int>(view, "_windowEndIndex");
      view.ApplySettings(followOn, dark: true);
      PumpMessages(500);
      Require(exactPlaybackApplied == appliedAfterEnable,
        "Reapplying settings while Follow was already ON replayed the cursor.");
      Require(
        ReadField<int>(view, "_windowStartIndex") == startAfterEnable &&
        ReadField<int>(view, "_windowEndIndex") == endAfterEnable,
        "Reapplying settings while Follow was already ON moved the window.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces issue #78. A scroll event with no physical user-scroll intent
  /// is programmatic and may not silently turn Follow off. A real wheel gesture
  /// followed by scrolling must still turn Follow off.
  /// </summary>
  private static void TestProgrammaticScrollDoesNotDisableFollow()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue78-scroll-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue78-scroll.jsonl");
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
        TranscriptSettings.Default with { FollowSpeech = true },
        dark: false);
      view.SelectSession(path, AgentSource.Codex, "Issue 78 scroll fixture");
      WaitForTranscriptRender(view);
      WebView2 webView = ReadField<WebView2>(view, "_webView");

      ExecuteVoidScript(
        webView,
        """
(() => {
  document.body.style.minHeight = '7000px';
  setFollowSpeech(true, false);
  userScrollIntentUntil = 0;
  userScrollIntentDirection = 0;
  programmaticScrollUntil = 0;
  lastManualScrollY = 0;
  window.scrollTo(0, 700);
  window.dispatchEvent(new Event('scroll'));
})()
""");
      PumpMessages(250);
      Require(
        ExecuteIntScript(webView, "followSpeech ? 1 : 0") == 1,
        "A programmatic scroll with no user input silently disabled Follow.");

      ExecuteVoidScript(
        webView,
        """
(() => {
  setFollowSpeech(true, false);
  window.dispatchEvent(new WheelEvent('wheel', {
    deltaY: 160,
    bubbles: true,
    cancelable: true
  }));
  const next = window.scrollY + 220;
  window.scrollTo(0, next);
  window.dispatchEvent(new Event('scroll'));
})()
""");
      PumpMessages(250);
      Require(
        ExecuteIntScript(webView, "followSpeech ? 1 : 0") == 0,
        "A physical wheel-scroll intent did not disable Follow.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Locks the documented Follow disclosure contract on the canonical WordId
  /// path: Follow OFF does not chase speech into a collapsed disclosure, while
  /// Follow ON opens the required disclosure when restoring retained playback.
  /// </summary>
  private static void TestFollowOpensCanonicalDisclosureOnlyWhenEnabled()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      $"AgentPanelSpeaker-issue78-details-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "issue78-details.jsonl");
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
      view.SelectSession(path, AgentSource.Codex, "Issue 78 disclosure fixture");
      WaitForTranscriptRender(view);
      WebView2 webView = ReadField<WebView2>(view, "_webView");

      const long probeWordId = 900000001;
      ExecuteVoidScript(
        webView,
        $$"""
(() => {
  const details = document.createElement('details');
  details.id = 'issue78-follow-details';
  details.innerHTML = '<summary>Thought probe</summary>' +
    '<p><span id="word-{{probeWordId}}">probe</span></p>';
  transcript.append(details);
  setFollowSpeech(false, false);
  setCanonicalPlayback('speaking', {{probeWordId}});
})()
""");
      Require(
        ExecuteIntScript(
          webView,
          "document.getElementById('issue78-follow-details').open ? 1 : 0") == 0,
        "Follow OFF opened a collapsed canonical disclosure.");

      ExecuteVoidScript(
        webView,
        $$"""
(() => {
  retireCanonicalPlayback(false);
  const details = document.getElementById('issue78-follow-details');
  details.open = false;
  retainedPlayback = {
    state:'speaking',
    fragmentText:'probe',
    wordIndex:0,
    wordText:'probe',
    nodeId:0,
    wordId:{{probeWordId}}
  };
  setFollowSpeech(true, false);
  restoreRetainedPlaybackProjection();
})()
""");
      Require(
        ExecuteIntScript(
          webView,
          "document.getElementById('issue78-follow-details').open ? 1 : 0") == 1,
        "Follow ON did not open the disclosure containing retained playback.");
      Require(
        ExecuteIntScript(webView, "followSpeech ? 1 : 0") == 1,
        "Retained-playback restoration changed Follow from ON to OFF.");
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  /// <summary>
  /// Reproduces issue #77. The structured JSONL log must contain the physical
  /// key/mouse timeline plus the recognized Follow command and explicit state
  /// transition. Activity-text output and settings dirty-state are insufficient.
  /// </summary>
  private static void TestInputDiagnosticsCaptureKeyMouseAndFollowState()
  {
    DiagnosticLog.Initialize();
    string logPath = DiagnosticLog.FilePath;
    int before = File.Exists(logPath) ? File.ReadLines(logPath).Count() : 0;

    using var form = new MainForm();
    _ = form.Handle;

    Message keyDown = Message.Create(
      form.Handle,
      0x0100,
      new IntPtr((int)Keys.Oemplus),
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref keyDown);
    Message keyUp = Message.Create(
      form.Handle,
      0x0101,
      new IntPtr((int)Keys.Oemplus),
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref keyUp);
    Message mouseDown = Message.Create(
      form.Handle,
      0x0201,
      IntPtr.Zero,
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref mouseDown);
    Message mouseUp = Message.Create(
      form.Handle,
      0x0202,
      IntPtr.Zero,
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref mouseUp);
    Message wheel = Message.Create(
      form.Handle,
      0x020A,
      new IntPtr(120L << 16),
      IntPtr.Zero);
    _ = form.PreFilterMessage(ref wheel);
    PumpMessages(150);

    JsonElement[] events = File.ReadLines(logPath)
      .Skip(before)
      .Select(line => JsonDocument.Parse(line).RootElement.Clone())
      .ToArray();

    bool HasInputPhase(string kind, string phase) => events.Any(record =>
      record.TryGetProperty("Event", out JsonElement eventElement) &&
      eventElement.GetString() == "input.physical" &&
      record.TryGetProperty("Data", out JsonElement data) &&
      data.TryGetProperty("kind", out JsonElement kindElement) &&
      kindElement.GetString() == kind &&
      data.TryGetProperty("phase", out JsonElement phaseElement) &&
      phaseElement.GetString() == phase);

    Require(HasInputPhase("keyboard", "down"),
      "Structured diagnostics omitted physical key-down.");
    Require(HasInputPhase("keyboard", "up"),
      "Structured diagnostics omitted physical key-up.");
    Require(HasInputPhase("mouse", "down"),
      "Structured diagnostics omitted physical mouse-down.");
    Require(HasInputPhase("mouse", "up"),
      "Structured diagnostics omitted physical mouse-up.");
    Require(HasInputPhase("mouse", "wheel"),
      "Structured diagnostics omitted physical mouse wheel input.");

    Require(events.Any(record =>
      record.TryGetProperty("Event", out JsonElement eventElement) &&
      eventElement.GetString() == "input.command" &&
      record.TryGetProperty("Data", out JsonElement data) &&
      data.TryGetProperty("command", out JsonElement commandElement) &&
      commandElement.GetString() == "ToggleFollow"),
      "Structured diagnostics omitted the recognized ToggleFollow command.");

    Require(events.Any(record =>
      record.TryGetProperty("Event", out JsonElement eventElement) &&
      eventElement.GetString() == "follow.changed" &&
      record.TryGetProperty("Data", out JsonElement data) &&
      data.TryGetProperty("oldValue", out JsonElement oldElement) &&
      data.TryGetProperty("newValue", out JsonElement newElement) &&
      oldElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
      newElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
      oldElement.GetBoolean() != newElement.GetBoolean()),
      "Structured diagnostics omitted explicit old/new Follow state.");
  }

  private static long FirstCanonicalWordId(string html)
  {
    const string prefix = "id=\"word-";
    int start = html.IndexOf(prefix, StringComparison.Ordinal);
    if (start < 0)
    {
      return 0;
    }
    start += prefix.Length;
    int end = html.IndexOf('"', start);
    if (end <= start)
    {
      return 0;
    }
    return long.TryParse(
      html.AsSpan(start, end - start),
      System.Globalization.NumberStyles.None,
      System.Globalization.CultureInfo.InvariantCulture,
      out long wordId)
        ? wordId
        : 0;
  }

'''
  text = replace_once(
    text,
    "  private static CanonicalHtmlUnitProjection CreateUnit(\n",
    insertion + "  private static CanonicalHtmlUnitProjection CreateUnit(\n",
    "issue54 test methods")
  write(path, text)


def input_tracker_source() -> str:
  return r'''using System.Diagnostics;

namespace AgentPanelSpeaker;

/// <summary>
/// Records one canonical physical-input timeline at the Win32 message-filter
/// boundary and correlates later command/state diagnostics to those inputs.
/// </summary>
internal sealed class InputDiagnosticTracker
{
  private readonly Dictionary<int, long> _pressedKeys = new();
  private readonly Dictionary<int, long> _recentKeyIds = new();
  private readonly Dictionary<string, long> _pressedMouseButtons = new();
  private long _nextInputId;
  private long _recentMouseInputId;
  private long _recentInputId;

  /// <summary>
  /// Logs one keyboard/mouse message and returns its physical input identity.
  /// Key-up shares the key-down identity. Repeated key-down messages share the
  /// held-key identity and are marked as repeats.
  /// </summary>
  public long? ObserveNativeMessage(
    Message message,
    Keys modifiers,
    bool followSpeech,
    bool speaking,
    bool paused)
  {
    int nativeMessage = message.Msg;
    if (nativeMessage is 0x0100 or 0x0104 or 0x0101 or 0x0105)
    {
      bool down = nativeMessage is 0x0100 or 0x0104;
      int virtualKey = (int)message.WParam & 0xFFFF;
      long inputId;
      bool repeat = false;
      if (down)
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
      else if (_pressedKeys.Remove(virtualKey, out long pressedId))
      {
        inputId = pressedId;
        _recentKeyIds[virtualKey] = inputId;
      }
      else
      {
        inputId = NextInputId();
        _recentKeyIds[virtualKey] = inputId;
      }
      _recentInputId = inputId;
      LogPhysical(
        inputId,
        "keyboard",
        down ? "down" : "up",
        ((Keys)virtualKey & Keys.KeyCode).ToString(),
        modifiers,
        repeat,
        message,
        followSpeech,
        speaking,
        paused,
        delta: 0);
      return inputId;
    }

    if (!TryDescribeMouseMessage(
          nativeMessage,
          message.WParam,
          out string phase,
          out string button,
          out int delta))
    {
      return null;
    }

    string identityKey = button;
    long mouseId;
    if (phase == "down" || phase == "double-click")
    {
      mouseId = NextInputId();
      _pressedMouseButtons[identityKey] = mouseId;
    }
    else if (phase == "up" &&
             _pressedMouseButtons.Remove(identityKey, out long pressedId))
    {
      mouseId = pressedId;
    }
    else
    {
      mouseId = NextInputId();
    }
    _recentMouseInputId = mouseId;
    _recentInputId = mouseId;
    LogPhysical(
      mouseId,
      "mouse",
      phase,
      button,
      modifiers,
      repeat: false,
      message,
      followSpeech,
      speaking,
      paused,
      delta);
    return mouseId;
  }

  /// <summary>
  /// Returns the most recent physical identity for one key.
  /// </summary>
  public long? GetRecentKeyInputId(Keys keyCode)
  {
    int value = (int)(keyCode & Keys.KeyCode);
    return _recentKeyIds.TryGetValue(value, out long inputId)
      ? inputId
      : null;
  }

  /// <summary>
  /// Returns the most recent mouse input identity.
  /// </summary>
  public long? RecentMouseInputId => _recentMouseInputId > 0
    ? _recentMouseInputId
    : null;

  /// <summary>
  /// Returns the most recent physical input identity of any kind.
  /// </summary>
  public long? RecentInputId => _recentInputId > 0 ? _recentInputId : null;

  /// <summary>
  /// Records whether one application input route consumed the physical event.
  /// </summary>
  public void LogDispatch(long? inputId, string route, bool handled)
  {
    if (inputId is null)
    {
      return;
    }
    DiagnosticLog.Write("input.dispatch", new
    {
      physicalInputId = inputId,
      route,
      handled
    });
  }

  /// <summary>
  /// Records one application command recognized from physical input.
  /// </summary>
  public void LogCommand(
    long? inputId,
    string command,
    string route,
    string shortcut,
    bool followSpeech,
    bool speaking,
    bool paused)
  {
    DiagnosticLog.Write("input.command", new
    {
      physicalInputId = inputId,
      command,
      route,
      shortcut,
      followSpeech,
      speaking,
      paused
    });
  }

  private long NextInputId() => Interlocked.Increment(ref _nextInputId);

  private static void LogPhysical(
    long inputId,
    string kind,
    string phase,
    string code,
    Keys modifiers,
    bool repeat,
    Message message,
    bool followSpeech,
    bool speaking,
    bool paused,
    int delta)
  {
    Control? target = Control.FromChildHandle(message.HWnd);
    DiagnosticLog.Write("input.physical", new
    {
      inputId,
      kind,
      phase,
      code,
      modifiers = modifiers.ToString(),
      repeat,
      delta,
      route = "win32-message-filter",
      nativeMessage = $"0x{message.Msg:X4}",
      targetHwnd = $"0x{message.HWnd.ToInt64():X}",
      targetType = target?.GetType().FullName,
      targetName = target?.Name,
      targetPath = GetControlPath(target),
      followSpeech,
      speaking,
      paused,
      timestamp = Stopwatch.GetTimestamp()
    });
  }

  private static string? GetControlPath(Control? control)
  {
    if (control is null)
    {
      return null;
    }
    var parts = new Stack<string>();
    for (Control? current = control; current is not null; current = current.Parent)
    {
      parts.Push(string.IsNullOrWhiteSpace(current.Name)
        ? current.GetType().Name
        : current.Name);
    }
    return string.Join("/", parts);
  }

  private static bool TryDescribeMouseMessage(
    int message,
    IntPtr wordParameter,
    out string phase,
    out string button,
    out int delta)
  {
    phase = string.Empty;
    button = string.Empty;
    delta = 0;
    switch (message)
    {
      case 0x0201: phase = "down"; button = "left"; return true;
      case 0x0202: phase = "up"; button = "left"; return true;
      case 0x0203: phase = "double-click"; button = "left"; return true;
      case 0x0204: phase = "down"; button = "right"; return true;
      case 0x0205: phase = "up"; button = "right"; return true;
      case 0x0206: phase = "double-click"; button = "right"; return true;
      case 0x0207: phase = "down"; button = "middle"; return true;
      case 0x0208: phase = "up"; button = "middle"; return true;
      case 0x0209: phase = "double-click"; button = "middle"; return true;
      case 0x020A:
        phase = "wheel";
        button = "vertical-wheel";
        delta = (short)((wordParameter.ToInt64() >> 16) & 0xFFFF);
        return true;
      case 0x020B:
      case 0x020C:
      case 0x020D:
        phase = message == 0x020B
          ? "down"
          : message == 0x020C ? "up" : "double-click";
        button = ((wordParameter.ToInt64() >> 16) & 0xFFFF) == 2
          ? "x2"
          : "x1";
        return true;
      case 0x020E:
        phase = "wheel";
        button = "horizontal-wheel";
        delta = (short)((wordParameter.ToInt64() >> 16) & 0xFFFF);
        return true;
      case 0x00A1: phase = "down"; button = "nc-left"; return true;
      case 0x00A2: phase = "up"; button = "nc-left"; return true;
      case 0x00A3: phase = "double-click"; button = "nc-left"; return true;
      case 0x00A4: phase = "down"; button = "nc-right"; return true;
      case 0x00A5: phase = "up"; button = "nc-right"; return true;
      case 0x00A6: phase = "double-click"; button = "nc-right"; return true;
      case 0x00A7: phase = "down"; button = "nc-middle"; return true;
      case 0x00A8: phase = "up"; button = "nc-middle"; return true;
      case 0x00A9: phase = "double-click"; button = "nc-middle"; return true;
      case 0x00AB:
      case 0x00AC:
      case 0x00AD:
        phase = message == 0x00AB
          ? "down"
          : message == 0x00AC ? "up" : "double-click";
        button = ((wordParameter.ToInt64() >> 16) & 0xFFFF) == 2
          ? "nc-x2"
          : "nc-x1";
        return true;
      default:
        return false;
    }
  }
}
'''


def apply_production() -> None:
  tracker_path = ROOT / "AgentPanelSpeaker/InputDiagnosticTracker.cs"
  if not tracker_path.exists():
    tracker_path.write_text(input_tracker_source(), encoding="utf-8")

  main_path = "AgentPanelSpeaker/MainForm.cs"
  main = read(main_path)
  if "private readonly InputDiagnosticTracker _inputDiagnostics" not in main:
    main = replace_once(
      main,
      "  private readonly JsonlSessionMonitor _monitor = new();\n",
      "  private readonly JsonlSessionMonitor _monitor = new();\n  private readonly InputDiagnosticTracker _inputDiagnostics = new();\n",
      "MainForm input tracker field")

  prefilter = r'''  public bool PreFilterMessage(ref Message message)
  {
    if (message.Msg == WmActivateApp)
    {
      bool foregroundIsCurrentProcess = IsWindowFromCurrentProcess(
        GetForegroundWindowForTabDiagnostics());
      _transcriptView.SetVoicePointerSelectMode(
        ResolveVoicePointerSelectModeForActivation(
          (Control.ModifierKeys & Keys.Control) != 0,
          foregroundIsCurrentProcess));
      return false;
    }

    IntPtr messageRoot = GetAncestor(message.HWnd, GaRoot);
    bool isMainFormRoot = messageRoot == Handle;
    bool rootIsCurrentProcess = IsWindowFromCurrentProcess(messageRoot);
    if (!ShouldRouteVoicePointerMessageSource(
          isMainFormRoot,
          rootIsCurrentProcess))
    {
      return false;
    }

    long? physicalInputId = _inputDiagnostics.ObserveNativeMessage(
      message,
      Control.ModifierKeys & Keys.Modifiers,
      _transcriptSettingsPopup.Settings.FollowSpeech,
      _speech.IsSpeaking,
      _speech.IsPaused);

    bool Finish(bool handled, string route)
    {
      _inputDiagnostics.LogDispatch(physicalInputId, route, handled);
      return handled;
    }

    if (message.Msg is WmLButtonDown or WmRButtonDown or
        WmMButtonDown or WmXButtonDown or
        WmNcLButtonDown or WmNcRButtonDown or
        WmNcMButtonDown or WmNcXButtonDown)
    {
      HoverPopupController.HandleGlobalPointerDown(
        Control.FromChildHandle(message.HWnd));
      return Finish(false, "popup-pointer-filter");
    }

    if (TryGetVoicePointerSelectModeMessage(
          message.Msg,
          message.WParam,
          out bool voicePointerSelectMode))
    {
      _transcriptView.SetVoicePointerSelectMode(voicePointerSelectMode);
    }

    if (message.Msg is not (WmKeyDown or WmSystemKeyDown))
    {
      return Finish(false, "message-filter");
    }

    Keys keyCode = (Keys)(int)message.WParam & Keys.KeyCode;
    Keys modifiers = Control.ModifierKeys & Keys.Modifiers;
    if (keyCode == Keys.F && modifiers == Keys.Control)
    {
      _inputDiagnostics.LogCommand(
        physicalInputId,
        "OpenFind",
        "message-filter",
        "Ctrl+F",
        _transcriptSettingsPopup.Settings.FollowSpeech,
        _speech.IsSpeaking,
        _speech.IsPaused);
      _transcriptView.OpenFind();
      return Finish(true, "message-filter");
    }
    bool hasAltOnly = modifiers == Keys.Alt;
    bool hasNoModifiers = modifiers == Keys.None;
    if (!hasAltOnly && !hasNoModifiers)
    {
      return Finish(false, "message-filter");
    }
    if (hasNoModifiers && IsTransportShortcutBlockedByFocusedControl())
    {
      return Finish(false, "message-filter");
    }

    string shortcut = hasAltOnly
      ? $"Alt+{FormatTransportKey(keyCode)}"
      : FormatTransportKey(keyCode);
    bool handled = ActivateTransportShortcut(
      keyCode,
      shortcut,
      "message-filter");
    return Finish(handled, "message-filter");
  }

'''
  main = regex_once(
    main,
    r'  public bool PreFilterMessage\(ref Message message\)\n  \{.*?\n  \}\n\n(?=  /// <summary>\n  /// Handles profile-editor dismissal)',
    prefilter,
    "MainForm PreFilterMessage")

  main = main.replace(
    "_ = ActivateTransportShortcut(eventArgs.KeyCode, shortcut);",
    "_ = ActivateTransportShortcut(eventArgs.KeyCode, shortcut, \"profile-control\");")
  main = main.replace(
    "_ = ActivateTransportShortcut(keyCode, shortcut);\n  }\n\n  private void SetDiagnosticsMaximized",
    "_ = ActivateTransportShortcut(keyCode, shortcut, \"webview-bridge\");\n  }\n\n  private void SetDiagnosticsMaximized")
  main = main.replace(
    "bool result = ActivateTransportShortcut(keyCode, shortcut) ||\n      base.ProcessCmdKey(ref message, keyData);",
    "bool result = ActivateTransportShortcut(\n      keyCode,\n      shortcut,\n      \"process-cmd-key\") ||\n      base.ProcessCmdKey(ref message, keyData);")

  activate = r'''  private bool ActivateTransportShortcut(
    Keys keyCode,
    string shortcut,
    string route = "native")
  {
    long? physicalInputId = _inputDiagnostics.GetRecentKeyInputId(keyCode);
    if (keyCode == Keys.Oemplus)
    {
      bool oldFollow = _transcriptSettingsPopup.Settings.FollowSpeech;
      bool newFollow = !oldFollow;
      _inputDiagnostics.LogCommand(
        physicalInputId,
        "ToggleFollow",
        route,
        shortcut,
        oldFollow,
        _speech.IsSpeaking,
        _speech.IsPaused);
      _transcriptSettingsPopup.SetSettings(
        _transcriptSettingsPopup.Settings with
        {
          FollowSpeech = newFollow
        },
        ThemeManager.IsDark(GetSelectedTheme()));
      TranscriptSettingsChanged();
      DiagnosticLog.Write("follow.changed", new
      {
        physicalInputId,
        source = "keyboard",
        route,
        oldValue = oldFollow,
        newValue = newFollow,
        shortcut,
        speaking = _speech.IsSpeaking,
        paused = _speech.IsPaused
      });
      AppendLog($"Transcript follow mode {(newFollow ? "enabled" : "disabled")}.");
      return true;
    }

    HotkeyAction action = _settingsStore.Current.Hotkeys.GetAction(keyCode);
    if (action == HotkeyAction.None)
    {
      return false;
    }

    _inputDiagnostics.LogCommand(
      physicalInputId,
      action.ToString(),
      route,
      shortcut,
      _transcriptSettingsPopup.Settings.FollowSpeech,
      _speech.IsSpeaking,
      _speech.IsPaused);

    if (action == HotkeyAction.ToggleTranscriptSize)
    {
      SetDiagnosticsMaximized(!_diagnosticsMaximized);
      return true;
    }
    Button? button = action switch
    {
      HotkeyAction.PreviousSpeaker => _rewindSpeakerButton,
      HotkeyAction.PreviousNode => _rewindNodeButton,
      HotkeyAction.PreviousSentence => _rewindSentenceButton,
      HotkeyAction.PlayPause => _playPauseButton,
      HotkeyAction.NextSentence => _forwardSentenceButton,
      HotkeyAction.NextNode => _forwardNodeButton,
      HotkeyAction.NextSpeaker => _forwardSpeakerButton,
      HotkeyAction.ProcessingTime => _processingTimeButton,
      _ => null
    };
    if (button is null)
    {
      return false;
    }
    if (!button.Enabled)
    {
      return true;
    }
    if (button.CanFocus)
    {
      button.Focus();
    }

    switch (action)
    {
      case HotkeyAction.PreviousSpeaker:
        NavigateSpeech(_speech.TryRewindSpeaker, "Previous speaker turn");
        break;
      case HotkeyAction.PreviousNode:
        NavigateSpeech(_speech.TryRewindNode, "Previous JSONL node");
        break;
      case HotkeyAction.PreviousSentence:
        NavigateSpeech(_speech.TryRewindSentence, "Previous sentence/code line");
        break;
      case HotkeyAction.PlayPause:
        _pendingPlayPauseTrigger = $"keyboard:{shortcut}";
        PlayPauseButtonClicked(button, EventArgs.Empty);
        break;
      case HotkeyAction.NextSentence:
        NavigateSpeech(_speech.TryForwardSentence, "Next sentence/code line", "Past end of last sentence/code line.");
        break;
      case HotkeyAction.NextNode:
        NavigateSpeech(_speech.TryForwardNode, "Next JSONL node", "Past end of last JSONL node.");
        break;
      case HotkeyAction.NextSpeaker:
        NavigateSpeech(_speech.TryForwardSpeaker, "Next speaker turn", "Past end of last speaker turn.");
        break;
      case HotkeyAction.ProcessingTime:
        ProcessingTimeButtonClicked(button, EventArgs.Empty);
        break;
    }
    return true;
  }

'''
  main = regex_once(
    main,
    r'  private bool ActivateTransportShortcut\(Keys keyCode, string shortcut\)\n  \{.*?\n  \}\n\n(?=  /// <summary>\n  /// Formats punctuation transport keys)',
    activate,
    "MainForm ActivateTransportShortcut")

  old_follow_handler = '''  private void TranscriptFollowSpeechChanged(bool enabled)\n  {\n    if (_transcriptSettingsPopup.Settings.FollowSpeech == enabled)\n    {\n      return;\n    }\n    _transcriptSettingsPopup.SetSettings(\n      _transcriptSettingsPopup.Settings with { FollowSpeech = enabled },\n      ThemeManager.IsDark(GetSelectedTheme()));\n    TranscriptSettingsChanged();\n    AppendLog($"Transcript follow mode {(enabled ? "enabled" : "disabled")}.");\n  }\n'''
  new_follow_handler = '''  private void TranscriptFollowSpeechChanged(bool enabled, string reason)\n  {\n    bool oldFollow = _transcriptSettingsPopup.Settings.FollowSpeech;\n    if (oldFollow == enabled)\n    {\n      return;\n    }\n    long? physicalInputId = _inputDiagnostics.RecentInputId;\n    _transcriptSettingsPopup.SetSettings(\n      _transcriptSettingsPopup.Settings with { FollowSpeech = enabled },\n      ThemeManager.IsDark(GetSelectedTheme()));\n    TranscriptSettingsChanged();\n    DiagnosticLog.Write("follow.changed", new\n    {\n      physicalInputId,\n      source = "browser",\n      reason,\n      oldValue = oldFollow,\n      newValue = enabled,\n      speaking = _speech.IsSpeaking,\n      paused = _speech.IsPaused\n    });\n    AppendLog($"Transcript follow mode {(enabled ? "enabled" : "disabled")}.");\n  }\n'''
  main = replace_once(
    main,
    old_follow_handler,
    new_follow_handler,
    "MainForm Follow handler")

  seek_old = '''    DiagnosticLog.Write("transcript.seek_requested", new\n    {\n      eventArgs.Source,\n      eventArgs.WordId\n    });'''
  seek_new = '''    DiagnosticLog.Write("transcript.seek_requested", new\n    {\n      physicalInputId = eventArgs.Source == "ctrl-click"\n        ? _inputDiagnostics.RecentMouseInputId\n        : _inputDiagnostics.RecentInputId,\n      eventArgs.Source,\n      eventArgs.WordId\n    });'''
  main = replace_once(main, seek_old, seek_new, "MainForm seek correlation")
  write(main_path, main)

  view_path = "AgentPanelSpeaker/TranscriptView.cs"
  view = read(view_path)

  old_apply = '''  public void ApplySettings(TranscriptSettings settings, bool dark)\n  {\n    LogViewState("apply-settings", "begin", requestedDark: dark);\n    _settings = settings.Normalize();\n    _virtualDocument?.SetShowRolledBackHistory(\n      _settings.ShowRolledBackHistory);\n    _dark = dark;\n    Color page = dark\n      ? Color.FromArgb(30, 32, 35)\n      : Color.FromArgb(247, 247, 245);\n    Color text = dark\n      ? Color.FromArgb(217, 220, 225)\n      : Color.FromArgb(36, 38, 41);\n    LogLoadingLabelNativeState("apply-settings", "before-back-color");\n    _loadingLabel.BackColor = page;\n    LogLoadingLabelNativeState("apply-settings", "after-back-color");\n    _loadingLabel.ForeColor = text;\n    LogLoadingLabelNativeState("apply-settings", "after-fore-color");\n    _failureLabel.BackColor = page;\n    _failureLabel.ForeColor = text;\n    if (_initialized)\n    {\n      QueueSettingsApply(immediate: false);\n    }\n    LogViewState("apply-settings", "end", requestedDark: dark);\n  }\n'''
  new_apply = '''  public void ApplySettings(TranscriptSettings settings, bool dark)\n  {\n    LogViewState("apply-settings", "begin", requestedDark: dark);\n    TranscriptSettings normalized = settings.Normalize();\n    bool oldFollow = _settings.FollowSpeech;\n    bool followEnabled = !oldFollow && normalized.FollowSpeech;\n    TranscriptPlaybackPosition? reattachPosition = null;\n    if (followEnabled)\n    {\n      if (_pendingPosition is TranscriptPlaybackPosition current &&\n          current.State is TranscriptPlaybackState.Speaking or\n            TranscriptPlaybackState.Paused &&\n          (current.WordId is > 0 || current.NodeId > 0))\n      {\n        reattachPosition = current;\n      }\n      else if (_lastLocatedContentPosition is TranscriptPlaybackPosition located &&\n               located.State is TranscriptPlaybackState.Speaking or\n                 TranscriptPlaybackState.Paused &&\n               (located.WordId is > 0 || located.NodeId > 0))\n      {\n        reattachPosition = located;\n      }\n    }\n\n    _settings = normalized;\n    _virtualDocument?.SetShowRolledBackHistory(\n      _settings.ShowRolledBackHistory);\n    _dark = dark;\n    Color page = dark\n      ? Color.FromArgb(30, 32, 35)\n      : Color.FromArgb(247, 247, 245);\n    Color text = dark\n      ? Color.FromArgb(217, 220, 225)\n      : Color.FromArgb(36, 38, 41);\n    LogLoadingLabelNativeState("apply-settings", "before-back-color");\n    _loadingLabel.BackColor = page;\n    LogLoadingLabelNativeState("apply-settings", "after-back-color");\n    _loadingLabel.ForeColor = text;\n    LogLoadingLabelNativeState("apply-settings", "after-fore-color");\n    _failureLabel.BackColor = page;\n    _failureLabel.ForeColor = text;\n    if (_initialized)\n    {\n      QueueSettingsApply(immediate: followEnabled);\n    }\n    DiagnosticLog.Write("transcript.follow_settings_transition", new\n    {\n      oldValue = oldFollow,\n      newValue = _settings.FollowSpeech,\n      followEnabled,\n      retainedWordId = reattachPosition?.WordId,\n      retainedNodeId = reattachPosition?.NodeId,\n      windowStartIndex = _windowStartIndex,\n      windowEndIndex = _windowEndIndex\n    });\n    if (reattachPosition is not null)\n    {\n      ShowPlaybackPosition(reattachPosition);\n    }\n    LogViewState("apply-settings", "end", requestedDark: dark);\n  }\n'''
  view = replace_once(view, old_apply, new_apply, "TranscriptView ApplySettings")

  view = replace_once(
    view,
    "  public event Action<bool>? FollowSpeechChanged;",
    "  public event Action<bool, string>? FollowSpeechChanged;",
    "TranscriptView FollowSpeechChanged event")

  old_follow_message = '''      if (type == "follow-changed")\n      {\n        bool enabled = ReadOptionalBoolean(root, "enabled") == true;\n        FollowSpeechChanged?.Invoke(enabled);\n        return;\n      }'''
  new_follow_message = '''      if (type == "follow-changed")\n      {\n        bool enabled = ReadOptionalBoolean(root, "enabled") == true;\n        string reason = ReadOptionalString(root, "reason");\n        DiagnosticLog.Write("transcript.follow_browser_changed", new\n        {\n          enabled,\n          reason,\n          windowStartIndex = _windowStartIndex,\n          windowEndIndex = _windowEndIndex,\n          pendingWordId = _pendingPosition?.WordId\n        });\n        FollowSpeechChanged?.Invoke(enabled, reason);\n        return;\n      }'''
  view = replace_once(
    view,
    old_follow_message,
    new_follow_message,
    "TranscriptView follow-changed WebMessage")

  settings_post = '''    Color colour = settings.GetHighlightColour(dark);\n    PostMessage(new\n    {\n      type = "settings",'''
  settings_replacement = '''    Color colour = settings.GetHighlightColour(dark);\n    long sequence = ++_settingsMessageSequence;\n    DiagnosticLog.Write("transcript.settings_posted", new\n    {\n      sequence,\n      followSpeech = settings.FollowSpeech,\n      settings.ShowRolledBackHistory,\n      layoutGeneration = _layoutGeneration,\n      windowStartIndex = _windowStartIndex,\n      windowEndIndex = _windowEndIndex\n    });\n    PostMessage(new\n    {\n      type = "settings",'''
  view = replace_once(
    view,
    settings_post,
    settings_replacement,
    "TranscriptView settings diagnostics")
  view = replace_once(
    view,
    "      sequence = ++_settingsMessageSequence,\n      highlight = ToCss(colour),",
    "      sequence,\n      highlight = ToCss(colour),",
    "TranscriptView settings sequence")

  marker_old = '''      position.WordId,\n      postedTimestamp = Stopwatch.GetTimestamp()'''
  marker_new = '''      position.WordId,\n      followSpeech = _settings.FollowSpeech,\n      windowStartIndex = _windowStartIndex,\n      windowEndIndex = _windowEndIndex,\n      postedTimestamp = Stopwatch.GetTimestamp()'''
  view = replace_once(view, marker_old, marker_new, "TranscriptView marker Follow diagnostics")

  restore_old = '''  const preservedFollow = followSpeech;\n  setPlayback(\n    retainedPlayback.state,\n    retainedPlayback.fragmentText,\n    retainedPlayback.wordIndex,\n    retainedPlayback.wordText,\n    retainedPlayback.nodeId,\n    false,\n    retainedPlayback.wordId);'''
  restore_new = '''  const preservedFollow = followSpeech;\n  setPlayback(\n    retainedPlayback.state,\n    retainedPlayback.fragmentText,\n    retainedPlayback.wordIndex,\n    retainedPlayback.wordText,\n    retainedPlayback.nodeId,\n    preservedFollow,\n    retainedPlayback.wordId);'''
  view = replace_once(view, restore_old, restore_new, "retained playback Follow restoration")

  canonical_old = '''  const target = elements[0];\n  maybePrefetchVoiceCursor(target);\n  reveal(target);'''
  canonical_new = '''  const target = elements[0];\n  if (followSpeech) openAncestors(target);\n  maybePrefetchVoiceCursor(target);\n  reveal(target);'''
  view = replace_once(view, canonical_old, canonical_new, "canonical disclosure Follow policy")

  set_follow_old = '''function setFollowSpeech(enabled, notify) {\n  followSpeech = !!enabled;\n  updateFollowToggle();\n  if (notify) {\n    chrome.webview.postMessage({type:'follow-changed', enabled:followSpeech});\n  }'''
  set_follow_new = '''function setFollowSpeech(enabled, notify, reason = 'unspecified') {\n  followSpeech = !!enabled;\n  updateFollowToggle();\n  if (notify) {\n    chrome.webview.postMessage({\n      type:'follow-changed',\n      enabled:followSpeech,\n      reason\n    });\n  }'''
  view = replace_once(view, set_follow_old, set_follow_new, "setFollowSpeech reason")

  view = view.replace(
    "if (followSpeech) setFollowSpeech(false, true);",
    "if (followSpeech) setFollowSpeech(false, true, 'find-navigation');",
    1)
  view = replace_once(
    view,
    "followToggle.addEventListener('click', () => {\n  setFollowSpeech(!followSpeech, true);",
    "followToggle.addEventListener('click', () => {\n  setFollowSpeech(!followSpeech, true, 'overlay-click');",
    "follow overlay reason")
  view = replace_once(
    view,
    "    setFollowSpeech(false, true);\n    chrome.webview.postMessage({\n      type:'window-edge',",
    "    setFollowSpeech(false, true, 'keyboard-edge');\n    chrome.webview.postMessage({\n      type:'window-edge',",
    "keyboard edge Follow reason")

  scroll_anchor = '''let lastTouchY = Number.NaN;\nlet windowShiftRequestSequence = 0;'''
  scroll_anchor_new = '''let lastTouchY = Number.NaN;\nlet scrollbarPointerActive = false;\nlet windowShiftRequestSequence = 0;'''
  view = replace_once(view, scroll_anchor, scroll_anchor_new, "scrollbar pointer state")

  pointer_old = '''window.addEventListener('pointerdown', event => {\n  if (event.button !== 0) return;\n  const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth;\n  if (scrollbarWidth > 0 && event.clientX >= document.documentElement.clientWidth) {\n    markUserScrollIntent();\n  }\n}, {capture:true});'''
  pointer_new = '''window.addEventListener('pointerdown', event => {\n  if (event.button !== 0) return;\n  const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth;\n  if (scrollbarWidth > 0 && event.clientX >= document.documentElement.clientWidth) {\n    scrollbarPointerActive = true;\n    markUserScrollIntent();\n  }\n}, {capture:true});\nwindow.addEventListener('pointermove', () => {\n  if (scrollbarPointerActive) markUserScrollIntent();\n}, {capture:true});\nwindow.addEventListener('pointerup', () => {\n  scrollbarPointerActive = false;\n}, {capture:true});\nwindow.addEventListener('pointercancel', () => {\n  scrollbarPointerActive = false;\n}, {capture:true});'''
  view = replace_once(view, pointer_old, pointer_new, "scrollbar pointer tracking")

  scroll_old = '''  const direction = delta > 0 ? 1 : -1;\n  const explicitUserIntent = now <= userScrollIntentUntil;\n  if (now <= programmaticScrollUntil) {\n    // A physical input can override a programmatic scroll only when the\n    // resulting movement agrees with that input. Window replacement and\n    // anchor restoration can move scrollY in the opposite direction while the\n    // earlier user-intent timer is still alive; that movement is not a second\n    // user gesture and must not start a competing virtual-window shift.\n    if (!explicitUserIntent ||\n        (userScrollIntentDirection !== 0 &&\n         direction !== userScrollIntentDirection)) {\n      return;\n    }\n    programmaticScrollUntil = 0;\n  }\n\n  if (followSpeech) setFollowSpeech(false, true);'''
  scroll_new = '''  const direction = delta > 0 ? 1 : -1;\n  const explicitUserIntent =\n    scrollbarPointerActive || now <= userScrollIntentUntil;\n  // A scroll event by itself is not evidence of user intent. Playback reveal,\n  // smooth scrolling, spacer replacement, and anchor restoration all generate\n  // scroll events. Only an explicit physical wheel/touch/key/scrollbar action\n  // is allowed to disable Follow or drive manual virtual-window shifting.\n  if (!explicitUserIntent) return;\n  if (now <= programmaticScrollUntil) {\n    // A physical input can override a programmatic scroll only when the\n    // resulting movement agrees with that input. Window replacement and\n    // anchor restoration can move scrollY in the opposite direction while the\n    // earlier user-intent timer is still alive; that movement is not a second\n    // user gesture and must not start a competing virtual-window shift.\n    if (userScrollIntentDirection !== 0 &&\n        direction !== userScrollIntentDirection) {\n      return;\n    }\n    programmaticScrollUntil = 0;\n  }\n\n  if (followSpeech) setFollowSpeech(false, true, 'manual-scroll');'''
  view = replace_once(view, scroll_old, scroll_new, "explicit manual scroll Follow policy")

  playback_applied_old = '''    state: data.state || '',\n    rangeStart: currentIndex,\n    rangeEnd: currentEndIndex,'''
  playback_applied_new = '''    state: data.state || '',\n    follow: followSpeech,\n    requestedPlaybackWordId,\n    windowStartIndex,\n    windowEndIndex,\n    markerVisible: currentCanonicalPlaybackElements.length > 0\n      ? currentCanonicalPlaybackElements.some(element => {\n          const rect = element.getBoundingClientRect();\n          return rect.bottom > 0 && rect.top < window.innerHeight;\n        })\n      : currentIndex >= 0 && words[currentIndex]\n        ? (() => {\n            const rect = words[currentIndex].getBoundingClientRect();\n            return rect.bottom > 0 && rect.top < window.innerHeight;\n          })()\n        : false,\n    rangeStart: currentIndex,\n    rangeEnd: currentEndIndex,'''
  view = replace_once(
    view,
    playback_applied_old,
    playback_applied_new,
    "playback-applied Follow diagnostics")
  write(view_path, view)

  readme_path = "README.md"
  readme = read(readme_path)
  if "## Diagnostic input/command/state contract" not in readme:
    diagnostic_doc = r'''

## Diagnostic input/command/state contract

The structured JSONL diagnostic log is the authoritative correlation surface for
real-machine acceptance. Every keyboard key-down/key-up and mouse button,
double-click, or wheel message delivered to Agent Panel Speaker is recorded as
`input.physical` at the Win32 message-filter boundary. A physical input keeps one
`inputId` across its down/up pair; repeated routing observations are not recorded
as separate physical presses. The log records key/button identity, modifiers,
managed target identity/path, and playback/Follow state, but does not copy the
contents of editable controls.

Recognized application actions are recorded separately as `input.command` and
carry the physical input ID when available. Follow transitions are recorded as
`follow.changed` with explicit old/new values and a source/reason. Transcript
settings, playback-marker posting/application, and virtual-window diagnostics
also carry Follow/window information so a regression review can distinguish
input receipt, command interpretation, state transition, materialization, and
observable viewport outcome.

Because exact key identities are intentionally recorded for diagnostics, log
files can contain sensitive keystroke information and should be handled as
private diagnostic artifacts.
'''
    readme += diagnostic_doc
  write(readme_path, readme)


if __name__ == "__main__":
  parser = argparse.ArgumentParser()
  parser.add_argument("--phase", choices=("test", "production"), required=True)
  args = parser.parse_args()
  if args.phase == "test":
    apply_tests()
  else:
    apply_production()
