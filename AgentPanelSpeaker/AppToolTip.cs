namespace AgentPanelSpeaker;

/// <summary>
/// Provides one application-wide tooltip contract for pointer hover and
/// keyboard focus.  Tooltip presentation is owned by the application rather
/// than the native automatic-hover timer so both input paths have identical,
/// deterministic timing and owner-drawn styling.
/// </summary>
internal sealed class AppToolTip : System.Windows.Forms.ToolTip
{
  internal const int PresentationDelayMilliseconds = 750;
  internal const int PointerHoverDelayMilliseconds =
    PresentationDelayMilliseconds;
  internal const int KeyboardFocusDelayMilliseconds =
    PresentationDelayMilliseconds;
  internal const int AutoPopDelayMilliseconds = 7500;

  private readonly System.Windows.Forms.Timer _presentationTimer = new()
  {
    Interval = PresentationDelayMilliseconds
  };
  private readonly HashSet<Control> _registeredControls = new();
  private readonly Dictionary<Control, string> _captions = new();
  private Control? _pointerTarget;
  private Control? _focusTarget;
  private Control? _scheduledTarget;
  private Control? _visibleTarget;

  /// <summary>
  /// Creates a tooltip whose drawing mode and presentation timing are fixed
  /// for its entire lifetime.
  /// </summary>
  public AppToolTip()
  {
    OwnerDraw = true;
    AutoPopDelay = AutoPopDelayMilliseconds;
    ShowAlways = true;
    _presentationTimer.Tick += PresentationTimerTick;
  }

  /// <summary>
  /// Assigns tooltip text and registers the control for explicit delayed
  /// presentation by either pointer hover or keyboard focus.
  /// </summary>
  public new void SetToolTip(Control control, string? caption)
  {
    ArgumentNullException.ThrowIfNull(control);

    // Never give the native ToolTip component a caption.  That suppresses its
    // OS-controlled automatic hover path; AppToolTip owns all presentation.
    base.SetToolTip(control, string.Empty);

    if (string.IsNullOrEmpty(caption))
    {
      _captions.Remove(control);
      UnregisterControl(control);
      return;
    }

    bool changed = !_captions.TryGetValue(control, out string? oldCaption) ||
      !string.Equals(oldCaption, caption, StringComparison.Ordinal);
    _captions[control] = caption;
    RegisterControl(control);

    if (changed && IsActive(control))
    {
      HideVisible(control);
      SchedulePresentation(control);
    }
  }

  /// <summary>
  /// Returns the application tooltip contract and exercises its real pointer
  /// and focus scheduling state transitions for generic regression probes.
  /// </summary>
  internal static object GetContractSnapshot()
  {
    using var toolTip = new AppToolTip();
    using var control = new Button();
    toolTip.SetToolTip(control, "contract");

    bool nativeAutomaticHoverSuppressed =
      string.IsNullOrEmpty(toolTip.GetToolTip(control));

    toolTip.ToolTipControlMouseEnter(control, EventArgs.Empty);
    bool pointerEnterSchedulesPresentation =
      toolTip.IsScheduledFor(control);

    toolTip.ToolTipControlMouseLeave(control, EventArgs.Empty);
    bool pointerLeaveCancelsWithoutFocus =
      !toolTip.IsScheduledFor(control);

    toolTip.ToolTipControlEnter(control, EventArgs.Empty);
    bool keyboardEnterSchedulesPresentation =
      toolTip.IsScheduledFor(control);

    toolTip.ToolTipControlMouseEnter(control, EventArgs.Empty);
    toolTip.ToolTipControlMouseLeave(control, EventArgs.Empty);
    bool pointerLeavePreservesFocus =
      toolTip.IsScheduledFor(control);

    toolTip.ToolTipControlMouseEnter(control, EventArgs.Empty);
    toolTip.ToolTipControlLeave(control, EventArgs.Empty);
    bool focusLeavePreservesPointer =
      toolTip.IsScheduledFor(control);

    return new
    {
      ownerDraw = toolTip.OwnerDraw,
      autoPopDelayMilliseconds = toolTip.AutoPopDelay,
      showAlways = toolTip.ShowAlways,
      presentationDelayMilliseconds = PresentationDelayMilliseconds,
      pointerHoverDelayMilliseconds = PointerHoverDelayMilliseconds,
      keyboardFocusDelayMilliseconds = KeyboardFocusDelayMilliseconds,
      nativeAutomaticHoverSuppressed,
      pointerEnterSchedulesPresentation,
      pointerLeaveCancelsWithoutFocus,
      keyboardEnterSchedulesPresentation,
      pointerLeavePreservesFocus,
      focusLeavePreservesPointer
    };
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _presentationTimer.Stop();
      _presentationTimer.Tick -= PresentationTimerTick;
      _presentationTimer.Dispose();

      foreach (Control control in _registeredControls.ToArray())
      {
        DetachControl(control);
      }

      _registeredControls.Clear();
      _captions.Clear();
      _pointerTarget = null;
      _focusTarget = null;
      _scheduledTarget = null;
      _visibleTarget = null;
    }

    base.Dispose(disposing);
  }

  private void RegisterControl(Control control)
  {
    if (!_registeredControls.Add(control))
    {
      return;
    }

    control.MouseEnter += ToolTipControlMouseEnter;
    control.MouseLeave += ToolTipControlMouseLeave;
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

    if (ReferenceEquals(_pointerTarget, control))
    {
      _pointerTarget = null;
    }
    if (ReferenceEquals(_focusTarget, control))
    {
      _focusTarget = null;
    }
    CancelScheduled(control);
    HideVisible(control);
    DetachControl(control);
    ScheduleRemainingActiveTarget();
  }

  private void DetachControl(Control control)
  {
    control.MouseEnter -= ToolTipControlMouseEnter;
    control.MouseLeave -= ToolTipControlMouseLeave;
    control.Enter -= ToolTipControlEnter;
    control.Leave -= ToolTipControlLeave;
    control.Disposed -= ToolTipControlDisposed;
  }

  private void ToolTipControlMouseEnter(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control || control.IsDisposed)
    {
      return;
    }

    _pointerTarget = control;
    SchedulePresentation(control);
  }

  private void ToolTipControlMouseLeave(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control ||
        !ReferenceEquals(_pointerTarget, control))
    {
      return;
    }

    _pointerTarget = null;
    HandleTargetExit(control);
  }

  private void ToolTipControlEnter(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control || control.IsDisposed)
    {
      return;
    }

    _focusTarget = control;
    SchedulePresentation(control);
  }

  private void ToolTipControlLeave(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control ||
        !ReferenceEquals(_focusTarget, control))
    {
      return;
    }

    _focusTarget = null;
    HandleTargetExit(control);
  }

  private void ToolTipControlDisposed(object? sender, EventArgs eventArgs)
  {
    if (sender is Control control)
    {
      _captions.Remove(control);
      UnregisterControl(control);
    }
  }

  private void SchedulePresentation(Control control)
  {
    if (!_captions.ContainsKey(control) || control.IsDisposed)
    {
      return;
    }

    // Repeated events for the same resting control must not postpone the
    // tooltip.  This is what makes the <=1 second contract deterministic.
    if (ReferenceEquals(_scheduledTarget, control) &&
        _presentationTimer.Enabled)
    {
      return;
    }

    if (ReferenceEquals(_visibleTarget, control))
    {
      return;
    }

    _presentationTimer.Stop();
    if (_visibleTarget is not null &&
        !ReferenceEquals(_visibleTarget, control))
    {
      HideVisible(_visibleTarget);
    }

    _scheduledTarget = control;
    _presentationTimer.Start();
  }

  private void PresentationTimerTick(object? sender, EventArgs eventArgs)
  {
    _presentationTimer.Stop();
    Control? control = _scheduledTarget;
    _scheduledTarget = null;

    if (control is null || !IsActive(control) || control.IsDisposed ||
        !control.Enabled || !control.Visible ||
        !_captions.TryGetValue(control, out string? caption) ||
        caption.Length == 0)
    {
      return;
    }

    if (_visibleTarget is not null &&
        !ReferenceEquals(_visibleTarget, control))
    {
      HideVisible(_visibleTarget);
    }

    Show(
      caption,
      control,
      0,
      control.Height + 2,
      AutoPopDelayMilliseconds);
    _visibleTarget = control;
  }

  private void HandleTargetExit(Control control)
  {
    // The other input mode still owns the same control.  Do not restart the
    // timer or hide a tooltip that is still legitimately active.
    if (IsActive(control))
    {
      return;
    }

    CancelScheduled(control);
    HideVisible(control);
    ScheduleRemainingActiveTarget();
  }

  private void CancelScheduled(Control control)
  {
    if (!ReferenceEquals(_scheduledTarget, control))
    {
      return;
    }

    _presentationTimer.Stop();
    _scheduledTarget = null;
  }

  private void HideVisible(Control control)
  {
    if (!ReferenceEquals(_visibleTarget, control))
    {
      return;
    }

    if (!control.IsDisposed)
    {
      Hide(control);
    }
    _visibleTarget = null;
  }

  private void ScheduleRemainingActiveTarget()
  {
    Control? target = _pointerTarget ?? _focusTarget;
    if (target is not null)
    {
      SchedulePresentation(target);
    }
  }

  private bool IsActive(Control control)
  {
    return ReferenceEquals(_pointerTarget, control) ||
      ReferenceEquals(_focusTarget, control);
  }

  private bool IsScheduledFor(Control control)
  {
    return ReferenceEquals(_scheduledTarget, control) &&
      _presentationTimer.Enabled;
  }
}
