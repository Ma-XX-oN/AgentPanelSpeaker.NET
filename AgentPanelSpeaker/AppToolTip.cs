namespace AgentPanelSpeaker;

/// <summary>
/// Provides one application-wide tooltip contract for pointer hover and
/// keyboard focus.  Owner drawing is enabled before any control is registered
/// so the native tooltip window cannot be created with a different drawing
/// mode and later restyled inconsistently.
/// </summary>
internal sealed class AppToolTip : System.Windows.Forms.ToolTip
{
  internal const int HoverDelayMilliseconds = 750;
  internal const int ReshowDelayMilliseconds = 150;
  internal const int AutoPopDelayMilliseconds = 7500;
  internal const int KeyboardFocusDelayMilliseconds = 750;

  private readonly System.Windows.Forms.Timer _keyboardFocusTimer = new()
  {
    Interval = KeyboardFocusDelayMilliseconds
  };
  private readonly HashSet<Control> _registeredControls = new();
  private Control? _keyboardFocusTarget;

  /// <summary>
  /// Creates a tooltip whose drawing mode and timing are fixed for its entire
  /// lifetime.
  /// </summary>
  public AppToolTip()
  {
    OwnerDraw = true;
    InitialDelay = HoverDelayMilliseconds;
    ReshowDelay = ReshowDelayMilliseconds;
    AutoPopDelay = AutoPopDelayMilliseconds;
    ShowAlways = true;
    _keyboardFocusTimer.Tick += KeyboardFocusTimerTick;
  }

  /// <summary>
  /// Assigns tooltip text and registers the control for the same delayed
  /// presentation when keyboard focus rests on it.
  /// </summary>
  public new void SetToolTip(Control control, string? caption)
  {
    ArgumentNullException.ThrowIfNull(control);
    base.SetToolTip(control, caption);

    if (string.IsNullOrEmpty(caption))
    {
      UnregisterControl(control);
      return;
    }

    RegisterControl(control);
  }

  /// <summary>
  /// Returns the stable application tooltip contract for generic regression
  /// probes.
  /// </summary>
  internal static object GetContractSnapshot()
  {
    using var toolTip = new AppToolTip();
    return new
    {
      ownerDraw = toolTip.OwnerDraw,
      initialDelayMilliseconds = toolTip.InitialDelay,
      reshowDelayMilliseconds = toolTip.ReshowDelay,
      autoPopDelayMilliseconds = toolTip.AutoPopDelay,
      showAlways = toolTip.ShowAlways,
      keyboardFocusDelayMilliseconds = KeyboardFocusDelayMilliseconds
    };
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _keyboardFocusTimer.Stop();
      _keyboardFocusTimer.Tick -= KeyboardFocusTimerTick;
      _keyboardFocusTimer.Dispose();

      foreach (Control control in _registeredControls.ToArray())
      {
        DetachControl(control);
      }
      _registeredControls.Clear();
      _keyboardFocusTarget = null;
    }

    base.Dispose(disposing);
  }

  private void RegisterControl(Control control)
  {
    if (!_registeredControls.Add(control))
    {
      return;
    }

    control.Enter += ToolTipControlEnter;
    control.Leave += ToolTipControlLeave;
    control.Disposed += ToolTipControlDisposed;
  }

  private void UnregisterControl(Control control)
  {
    if (!_registeredControls.Remove(control))
    {
      return;
    }

    if (ReferenceEquals(_keyboardFocusTarget, control))
    {
      CancelKeyboardFocusToolTip(control);
    }
    DetachControl(control);
  }

  private void DetachControl(Control control)
  {
    control.Enter -= ToolTipControlEnter;
    control.Leave -= ToolTipControlLeave;
    control.Disposed -= ToolTipControlDisposed;
  }

  private void ToolTipControlEnter(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control || control.IsDisposed)
    {
      return;
    }

    _keyboardFocusTimer.Stop();
    _keyboardFocusTarget = control;
    _keyboardFocusTimer.Start();
  }

  private void ToolTipControlLeave(object? sender, EventArgs eventArgs)
  {
    if (sender is Control control &&
        ReferenceEquals(_keyboardFocusTarget, control))
    {
      CancelKeyboardFocusToolTip(control);
    }
  }

  private void ToolTipControlDisposed(object? sender, EventArgs eventArgs)
  {
    if (sender is Control control)
    {
      UnregisterControl(control);
    }
  }

  private void KeyboardFocusTimerTick(object? sender, EventArgs eventArgs)
  {
    _keyboardFocusTimer.Stop();
    Control? control = _keyboardFocusTarget;
    if (control is null || control.IsDisposed || !control.Enabled ||
        !control.Visible || !control.ContainsFocus)
    {
      _keyboardFocusTarget = null;
      return;
    }

    string caption = GetToolTip(control) ?? string.Empty;
    if (caption.Length == 0)
    {
      return;
    }

    Show(
      caption,
      control,
      0,
      control.Height + 2,
      AutoPopDelayMilliseconds);
  }

  private void CancelKeyboardFocusToolTip(Control control)
  {
    _keyboardFocusTimer.Stop();
    _keyboardFocusTarget = null;
    if (!control.IsDisposed)
    {
      Hide(control);
    }
  }
}
