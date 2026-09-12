using System.Diagnostics;

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
