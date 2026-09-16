namespace AgentPanelSpeaker;

/// <summary>
/// Provides one application-wide tooltip contract for pointer hover and
/// keyboard focus.  Tooltip presentation is owned by the application rather
/// than the native automatic-hover timer so both input paths have identical,
/// deterministic timing, sizing, placement, and owner-drawn styling.
/// </summary>
internal sealed class AppToolTip : System.Windows.Forms.ToolTip
{
  internal const int PresentationDelayMilliseconds = 750;
  internal const int PointerHoverDelayMilliseconds =
    PresentationDelayMilliseconds;
  internal const int KeyboardFocusDelayMilliseconds =
    PresentationDelayMilliseconds;
  internal const int AutoPopDelayMilliseconds = 7500;
  internal const int PresentationGapPixels = 32;
  internal const int PopupWorkingAreaMarginPixels = 8;
  internal const int PopupHorizontalPaddingPixels = 12;
  internal const int PopupVerticalPaddingPixels = 8;
  internal const int MaximumPopupWidthPixels = 640;

  internal const TextFormatFlags ToolTipTextFormat =
    TextFormatFlags.Left |
    TextFormatFlags.VerticalCenter |
    TextFormatFlags.NoPrefix |
    TextFormatFlags.WordBreak;

  private static readonly object CentralRegistrationSync = new();
  private static readonly Dictionary<Control, int> CentralRegistrationCounts =
    new();

  private readonly System.Windows.Forms.Timer _presentationTimer = new()
  {
    Interval = PresentationDelayMilliseconds
  };
  private readonly HashSet<Control> _registeredControls = new();
  private readonly Dictionary<Control, string> _captions = new();
  private readonly Dictionary<Control, Control> _pointerHostsByControl = new();
  private readonly Dictionary<Control, int> _pointerHostReferenceCounts = new();
  private Control? _pointerTarget;
  private Control? _focusTarget;
  private Control? _scheduledTarget;
  private Control? _visibleTarget;
  private Control? _presentingTarget;
  private Size _presentingSize;

  /// <summary>
  /// Creates a tooltip whose drawing mode, sizing, and presentation timing are
  /// fixed for its entire lifetime.
  /// </summary>
  public AppToolTip()
  {
    OwnerDraw = true;
    AutoPopDelay = AutoPopDelayMilliseconds;
    ShowAlways = true;
    Popup += ToolTipPopup;
    _presentationTimer.Tick += PresentationTimerTick;
  }

  /// <summary>
  /// Assigns tooltip text and registers the control for explicit delayed
  /// presentation by either pointer hover or keyboard focus.
  /// </summary>
  public new void SetToolTip(Control control, string? caption)
  {
    ArgumentNullException.ThrowIfNull(control);

    // Hover-popup anchors own a richer explanatory surface. Never allow a
    // tooltip registration to compete with that popup, even when a caller
    // explicitly attempts to assign one.
    if (!string.IsNullOrEmpty(caption) &&
        TooltipCoverage.IsHoverPopupExempt(control))
    {
      caption = null;
    }

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
  /// Gets whether any AppToolTip instance currently owns a non-empty caption
  /// for the control.  Coverage auditing uses this instead of the native
  /// ToolTip caption, which AppToolTip intentionally keeps empty.
  /// </summary>
  internal static bool HasCentralToolTip(Control control)
  {
    ArgumentNullException.ThrowIfNull(control);
    lock (CentralRegistrationSync)
    {
      return CentralRegistrationCounts.TryGetValue(control, out int count) &&
        count > 0;
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

  /// <summary>
  /// Exercises global registration accounting across multiple AppToolTip
  /// instances so coverage auditing cannot mistake native empty captions for
  /// missing centralized tooltips.
  /// </summary>
  internal static object GetCentralizationContractSnapshot()
  {
    using var first = new AppToolTip();
    using var second = new AppToolTip();
    using var control = new Button();

    bool initiallyUnregistered = !HasCentralToolTip(control);
    first.SetToolTip(control, "first");
    bool firstRegistrationVisible = HasCentralToolTip(control);
    second.SetToolTip(control, "second");
    first.SetToolTip(control, null);
    bool secondRegistrationSurvivesFirstRemoval = HasCentralToolTip(control);
    second.SetToolTip(control, null);
    bool finalRemovalClearsRegistration = !HasCentralToolTip(control);

    return new
    {
      initiallyUnregistered,
      firstRegistrationVisible,
      secondRegistrationSurvivesFirstRemoval,
      finalRemovalClearsRegistration
    };
  }

  /// <summary>
  /// Exercises the parent-level pointer path used for disabled controls.
  /// </summary>
  internal static object GetDisabledControlContractSnapshot()
  {
    using var toolTip = new AppToolTip();
    using var host = new Panel();
    using var control = new Button
    {
      Bounds = new Rectangle(8, 8, 80, 24),
      Enabled = false
    };
    host.Controls.Add(control);
    toolTip.SetToolTip(control, "disabled contract");

    bool disabledControlPointerFallback =
      toolTip.IsPointerHostRegisteredFor(control);

    toolTip.ToolTipPointerHostMouseMove(
      host,
      new MouseEventArgs(MouseButtons.None, 0, 16, 16, 0));
    bool disabledPointerSchedulesPresentation =
      toolTip.IsScheduledFor(control);

    toolTip.ToolTipPointerHostMouseMove(
      host,
      new MouseEventArgs(MouseButtons.None, 0, 120, 60, 0));
    bool leavingDisabledControlCancelsPresentation =
      !toolTip.IsScheduledFor(control);

    return new
    {
      disabledControlPointerFallback,
      disabledPointerSchedulesPresentation,
      leavingDisabledControlCancelsPresentation
    };
  }

  /// <summary>
  /// Returns the stable pointer-clearance placement contract.
  /// </summary>
  internal static object GetPlacementContractSnapshot()
  {
    return new
    {
      presentationGapPixels = PresentationGapPixels,
      minimumPointerClearancePixels = PresentationGapPixels
    };
  }

  /// <summary>
  /// Exercises sizing and screen-edge placement with representative captions.
  /// </summary>
  internal static object GetSizingContractSnapshot()
  {
    Font font = GetToolTipFont();
    var wideWorkingArea = new Rectangle(0, 0, 1600, 900);
    var narrowWorkingArea = new Rectangle(0, 0, 360, 600);

    const string shortCaption = "Save settings";
    Size shortText = MeasureText(shortCaption, font, wideWorkingArea.Width);
    Size shortPopup = CalculatePopupSize(
      shortCaption,
      font,
      wideWorkingArea);
    bool shortCaptionFits =
      shortPopup.Width >= shortText.Width + PopupHorizontalPaddingPixels &&
      shortPopup.Height >= shortText.Height + PopupVerticalPaddingPixels;

    const string twoLineCaption =
      "On: start at the first speakable content.\n" +
      "Off: wait at the current conversation end.";
    Size singleLineHeight = MeasureText("sample", font, wideWorkingArea.Width);
    Size twoLinePopup = CalculatePopupSize(
      twoLineCaption,
      font,
      wideWorkingArea);
    bool explicitTwoLineCaptionFits =
      twoLinePopup.Height >=
        (singleLineHeight.Height * 2) + PopupVerticalPaddingPixels;

    const string longCaption =
      "This representative tooltip caption is deliberately long enough to " +
      "require wrapping when the available working area is narrow while " +
      "remaining complete and readable.";
    Size wideUnconstrainedText = MeasureText(
      longCaption,
      font,
      wideWorkingArea.Width);
    Size widePopup = CalculatePopupSize(longCaption, font, wideWorkingArea);
    Size narrowPopup = CalculatePopupSize(longCaption, font, narrowWorkingArea);
    bool wideWorkingAreaUsesReadableMaximumWidth =
      wideUnconstrainedText.Width + PopupHorizontalPaddingPixels >
        MaximumPopupWidthPixels &&
      widePopup.Width <= MaximumPopupWidthPixels;
    int narrowMaximumWidth = Math.Max(
      1,
      narrowWorkingArea.Width - (PopupWorkingAreaMarginPixels * 2));
    bool longCaptionWrapsWithinWorkingArea =
      narrowPopup.Width <= narrowMaximumWidth;
    bool longCaptionUsesMoreHeightWhenConstrained =
      narrowPopup.Height > widePopup.Height;

    bool paddingIsIncluded =
      shortPopup.Width > shortText.Width &&
      shortPopup.Height > shortText.Height;

    var placementWorkingArea = new Rectangle(0, 0, 400, 300);
    Point rightEdgeLocation = CalculatePopupScreenLocation(
      new Rectangle(380, 50, 20, 20),
      new Size(120, 40),
      placementWorkingArea);
    bool rightEdgePlacementClamped =
      rightEdgeLocation.X >= placementWorkingArea.Left &&
      rightEdgeLocation.X + 120 <= placementWorkingArea.Right;

    var bottomAnchor = new Rectangle(100, 260, 60, 20);
    Point bottomEdgeLocation = CalculatePopupScreenLocation(
      bottomAnchor,
      new Size(160, 50),
      placementWorkingArea);
    bool bottomEdgePlacementFlipsAbove =
      bottomEdgeLocation.Y + 50 <= bottomAnchor.Top - PresentationGapPixels;

    return new
    {
      shortCaptionFits,
      explicitTwoLineCaptionFits,
      wideWorkingAreaUsesReadableMaximumWidth,
      maximumPopupWidthPixels = MaximumPopupWidthPixels,
      longCaptionWrapsWithinWorkingArea,
      longCaptionUsesMoreHeightWhenConstrained,
      paddingIsIncluded,
      rightEdgePlacementClamped,
      bottomEdgePlacementFlipsAbove
    };
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _presentationTimer.Stop();
      _presentationTimer.Tick -= PresentationTimerTick;
      _presentationTimer.Dispose();
      Popup -= ToolTipPopup;

      foreach (Control control in _registeredControls.ToArray())
      {
        UnregisterCentralCoverage(control);
        DetachControl(control);
      }

      foreach (Control host in _pointerHostReferenceCounts.Keys.ToArray())
      {
        DetachPointerHostEvents(host);
      }

      _registeredControls.Clear();
      _captions.Clear();
      _pointerHostsByControl.Clear();
      _pointerHostReferenceCounts.Clear();
      _pointerTarget = null;
      _focusTarget = null;
      _scheduledTarget = null;
      _visibleTarget = null;
      _presentingTarget = null;
      _presentingSize = Size.Empty;
    }

    base.Dispose(disposing);
  }

  private void RegisterControl(Control control)
  {
    if (!_registeredControls.Add(control))
    {
      RefreshPointerHost(control);
      return;
    }

    RegisterCentralCoverage(control);
    control.MouseEnter += ToolTipControlMouseEnter;
    control.MouseLeave += ToolTipControlMouseLeave;
    control.Enter += ToolTipControlEnter;
    control.Leave += ToolTipControlLeave;
    control.ParentChanged += ToolTipControlParentChanged;
    control.Disposed += ToolTipControlDisposed;
    RefreshPointerHost(control);
  }

  private void UnregisterControl(Control control)
  {
    if (!_registeredControls.Remove(control))
    {
      return;
    }

    UnregisterCentralCoverage(control);
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
    RemovePointerHost(control);
    DetachControl(control);
    ScheduleRemainingActiveTarget();
  }

  private static void RegisterCentralCoverage(Control control)
  {
    lock (CentralRegistrationSync)
    {
      CentralRegistrationCounts.TryGetValue(control, out int count);
      CentralRegistrationCounts[control] = count + 1;
    }
  }

  private static void UnregisterCentralCoverage(Control control)
  {
    lock (CentralRegistrationSync)
    {
      if (!CentralRegistrationCounts.TryGetValue(control, out int count))
      {
        return;
      }
      if (count <= 1)
      {
        CentralRegistrationCounts.Remove(control);
      }
      else
      {
        CentralRegistrationCounts[control] = count - 1;
      }
    }
  }

  private void DetachControl(Control control)
  {
    control.MouseEnter -= ToolTipControlMouseEnter;
    control.MouseLeave -= ToolTipControlMouseLeave;
    control.Enter -= ToolTipControlEnter;
    control.Leave -= ToolTipControlLeave;
    control.ParentChanged -= ToolTipControlParentChanged;
    control.Disposed -= ToolTipControlDisposed;
  }

  private void RefreshPointerHost(Control control)
  {
    RemovePointerHost(control);

    Control? host = control.Parent;
    if (host is null)
    {
      return;
    }

    _pointerHostsByControl[control] = host;
    if (_pointerHostReferenceCounts.TryGetValue(host, out int count))
    {
      _pointerHostReferenceCounts[host] = count + 1;
      return;
    }

    _pointerHostReferenceCounts[host] = 1;
    host.MouseMove += ToolTipPointerHostMouseMove;
    host.MouseLeave += ToolTipPointerHostMouseLeave;
  }

  private void RemovePointerHost(Control control)
  {
    if (!_pointerHostsByControl.Remove(control, out Control? host))
    {
      return;
    }

    if (!_pointerHostReferenceCounts.TryGetValue(host, out int count))
    {
      return;
    }

    if (count > 1)
    {
      _pointerHostReferenceCounts[host] = count - 1;
      return;
    }

    _pointerHostReferenceCounts.Remove(host);
    DetachPointerHostEvents(host);
  }

  private void DetachPointerHostEvents(Control host)
  {
    host.MouseMove -= ToolTipPointerHostMouseMove;
    host.MouseLeave -= ToolTipPointerHostMouseLeave;
  }

  private void ToolTipControlMouseEnter(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control || control.IsDisposed)
    {
      return;
    }

    SetPointerTarget(control);
  }

  private void ToolTipControlMouseLeave(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control ||
        !ReferenceEquals(_pointerTarget, control))
    {
      return;
    }

    SetPointerTarget(null);
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

  private void ToolTipControlParentChanged(object? sender, EventArgs eventArgs)
  {
    if (sender is Control control && _registeredControls.Contains(control))
    {
      RefreshPointerHost(control);
    }
  }

  private void ToolTipControlDisposed(object? sender, EventArgs eventArgs)
  {
    if (sender is Control control)
    {
      _captions.Remove(control);
      UnregisterControl(control);
    }
  }

  private void ToolTipPointerHostMouseMove(object? sender, MouseEventArgs eventArgs)
  {
    if (sender is not Control host || host.IsDisposed)
    {
      return;
    }

    Control? target = FindRegisteredDisabledChild(host, eventArgs.Location);
    if (target is not null)
    {
      SetPointerTarget(target);
      return;
    }

    if (_pointerTarget is not null && !_pointerTarget.Enabled &&
        _pointerHostsByControl.TryGetValue(_pointerTarget, out Control? oldHost) &&
        ReferenceEquals(oldHost, host))
    {
      SetPointerTarget(null);
    }
  }

  private void ToolTipPointerHostMouseLeave(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control host || _pointerTarget is null ||
        _pointerTarget.Enabled ||
        !_pointerHostsByControl.TryGetValue(_pointerTarget, out Control? oldHost) ||
        !ReferenceEquals(oldHost, host))
    {
      return;
    }

    SetPointerTarget(null);
  }

  private Control? FindRegisteredDisabledChild(Control host, Point location)
  {
    foreach (Control child in host.Controls)
    {
      if (_registeredControls.Contains(child) && !child.Enabled &&
          child.Visible && child.Bounds.Contains(location))
      {
        return child;
      }
    }

    return null;
  }

  private void SetPointerTarget(Control? control)
  {
    if (ReferenceEquals(_pointerTarget, control))
    {
      return;
    }

    Control? previous = _pointerTarget;
    _pointerTarget = control;

    if (previous is not null)
    {
      HandleTargetExit(previous);
    }

    if (control is not null)
    {
      SchedulePresentation(control);
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
        !control.Visible ||
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

    Rectangle workingArea = Screen.FromControl(control).WorkingArea;
    Size popupSize = CalculatePopupSize(
      caption,
      GetToolTipFont(),
      workingArea);
    Rectangle anchorBounds = control.RectangleToScreen(control.ClientRectangle);
    Point screenLocation = CalculatePopupScreenLocation(
      anchorBounds,
      popupSize,
      workingArea);
    Point clientLocation = control.PointToClient(screenLocation);

    _presentingTarget = control;
    _presentingSize = popupSize;
    try
    {
      Show(
        caption,
        control,
        clientLocation.X,
        clientLocation.Y,
        AutoPopDelayMilliseconds);
    }
    finally
    {
      _presentingTarget = null;
      _presentingSize = Size.Empty;
    }
    _visibleTarget = control;
  }

  private void ToolTipPopup(object? sender, PopupEventArgs eventArgs)
  {
    Control? control = _presentingTarget ?? eventArgs.AssociatedControl;
    if (control is null || control.IsDisposed ||
        !_captions.TryGetValue(control, out string? caption) ||
        caption.Length == 0)
    {
      return;
    }

    if (ReferenceEquals(control, _presentingTarget) &&
        !_presentingSize.IsEmpty)
    {
      eventArgs.ToolTipSize = _presentingSize;
      return;
    }

    eventArgs.ToolTipSize = CalculatePopupSize(
      caption,
      GetToolTipFont(),
      Screen.FromControl(control).WorkingArea);
  }

  private static Font GetToolTipFont()
  {
    return SystemFonts.StatusFont ?? Control.DefaultFont;
  }

  private static Size CalculatePopupSize(
    string caption,
    Font font,
    Rectangle workingArea)
  {
    int workingAreaMaximumPopupWidth = Math.Max(
      1,
      workingArea.Width - (PopupWorkingAreaMarginPixels * 2));
    int maximumPopupWidth = Math.Min(
      MaximumPopupWidthPixels,
      workingAreaMaximumPopupWidth);
    int maximumTextWidth = Math.Max(
      1,
      maximumPopupWidth - PopupHorizontalPaddingPixels);
    Size measured = MeasureText(caption, font, maximumTextWidth);

    int popupWidth = Math.Min(
      maximumPopupWidth,
      Math.Max(1, measured.Width + PopupHorizontalPaddingPixels));
    int popupHeight = Math.Max(
      1,
      measured.Height + PopupVerticalPaddingPixels);
    return new Size(popupWidth, popupHeight);
  }

  private static Size MeasureText(
    string caption,
    Font font,
    int maximumTextWidth)
  {
    return TextRenderer.MeasureText(
      caption,
      font,
      new Size(Math.Max(1, maximumTextWidth), int.MaxValue),
      ToolTipTextFormat);
  }

  private static Point CalculatePopupScreenLocation(
    Rectangle anchorBounds,
    Size popupSize,
    Rectangle workingArea)
  {
    int minimumX = workingArea.Left;
    int maximumX = Math.Max(
      minimumX,
      workingArea.Right - popupSize.Width);
    int x = Math.Clamp(anchorBounds.Left, minimumX, maximumX);

    int belowY = anchorBounds.Bottom + PresentationGapPixels;
    int aboveY = anchorBounds.Top - PresentationGapPixels - popupSize.Height;
    int y;
    if (belowY + popupSize.Height <= workingArea.Bottom)
    {
      y = belowY;
    }
    else if (aboveY >= workingArea.Top)
    {
      y = aboveY;
    }
    else
    {
      int maximumY = Math.Max(
        workingArea.Top,
        workingArea.Bottom - popupSize.Height);
      y = Math.Clamp(belowY, workingArea.Top, maximumY);
    }

    return new Point(x, y);
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

  private bool IsPointerHostRegisteredFor(Control control)
  {
    return _pointerHostsByControl.ContainsKey(control);
  }
}
