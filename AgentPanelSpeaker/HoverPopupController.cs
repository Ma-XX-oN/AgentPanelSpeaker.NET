namespace AgentPanelSpeaker;

/// <summary>
/// Centralizes the shared hover/focus lifecycle for overlay controls.
/// </summary>
internal sealed class HoverPopupController : IDisposable
{
  private enum PopupState
  {
    Closed,
    OpenAwaitingEntry,
    OpenEntered
  }

  private readonly Control _anchor;
  private readonly Func<IEnumerable<Control>> _getPopupControls;
  private readonly Action<bool> _showPopup;
  private readonly Action<bool> _hidePopup;
  private readonly Func<bool>? _keepOpen;
  private readonly System.Windows.Forms.Timer _openTimer = new();
  private readonly System.Windows.Forms.Timer _closeTimer = new();
  private readonly HashSet<Control> _wiredControls = new();
  private PopupState _state;
  private bool _suppressNextAnchorFocusOpen;
  private bool _disposed;

  /// <summary>
  /// Initializes one shared hover/focus popup lifecycle.
  /// </summary>
  public HoverPopupController(
    Control anchor,
    Func<IEnumerable<Control>> getPopupControls,
    Action<bool> showPopup,
    Action<bool> hidePopup,
    int openDelayMilliseconds = 250,
    int closeDelayMilliseconds = 200,
    Func<bool>? keepOpen = null)
  {
    _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
    _getPopupControls = getPopupControls ??
      throw new ArgumentNullException(nameof(getPopupControls));
    _showPopup = showPopup ?? throw new ArgumentNullException(nameof(showPopup));
    _hidePopup = hidePopup ?? throw new ArgumentNullException(nameof(hidePopup));
    _keepOpen = keepOpen;

    _openTimer.Interval = Math.Max(1, openDelayMilliseconds);
    _openTimer.Tick += OpenTimerTick;
    _closeTimer.Interval = Math.Max(1, closeDelayMilliseconds);
    _closeTimer.Tick += CloseTimerTick;

    WireAnchor();
  }

  /// <summary>
  /// Gets whether the popup is currently open.
  /// </summary>
  public bool IsOpen => _state != PopupState.Closed;

  /// <summary>
  /// Opens immediately, optionally transferring focus into the popup.
  /// </summary>
  public void OpenImmediately(bool focusPopup)
  {
    ThrowIfDisposed();
    _openTimer.Stop();
    _closeTimer.Stop();

    _showPopup(focusPopup);
    _state = PopupState.OpenAwaitingEntry;
    RefreshPopupControls();
    PromoteToEnteredWhenAppropriate();
  }

  /// <summary>
  /// Closes immediately and optionally restores focus to the anchor.
  /// </summary>
  public void Close(bool returnFocus)
  {
    if (_disposed)
    {
      return;
    }

    _openTimer.Stop();
    _closeTimer.Stop();
    _state = PopupState.Closed;
    _suppressNextAnchorFocusOpen = returnFocus && !_anchor.ContainsFocus;
    _hidePopup(returnFocus);
    if (!_anchor.ContainsFocus)
    {
      _suppressNextAnchorFocusOpen = false;
    }
  }

  /// <summary>
  /// Re-evaluates popup controls after a nested overlay is shown or hidden.
  /// </summary>
  public void RefreshPopupControls()
  {
    if (_disposed)
    {
      return;
    }

    foreach (Control popup in EnumeratePopupControls())
    {
      WirePopupTree(popup);
    }

    if (_state != PopupState.Closed)
    {
      PromoteToEnteredWhenAppropriate();
      CancelCloseWhenInside();
    }
  }

  /// <summary>
  /// Re-evaluates delayed closing after a drag or other temporary hold ends.
  /// </summary>
  public void ReevaluateClose()
  {
    if (_state == PopupState.OpenEntered)
    {
      ScheduleCloseIfOutside();
    }
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _openTimer.Stop();
    _closeTimer.Stop();
    _openTimer.Dispose();
    _closeTimer.Dispose();
  }

  private void WireAnchor()
  {
    _anchor.MouseEnter += AnchorMouseEnter;
    _anchor.MouseLeave += AnchorMouseLeave;
    _anchor.Enter += AnchorFocusEntered;
    _anchor.Leave += AnchorFocusLeft;
  }

  private void WirePopupTree(Control control)
  {
    if (!_wiredControls.Add(control))
    {
      return;
    }

    control.MouseEnter += PopupMouseEntered;
    control.MouseLeave += PopupMouseLeft;
    control.Enter += PopupFocusEntered;
    control.Leave += PopupFocusLeft;
    control.ControlAdded += PopupControlAdded;

    foreach (Control child in control.Controls)
    {
      WirePopupTree(child);
    }
  }

  private void PopupControlAdded(object? sender, ControlEventArgs eventArgs)
  {
    Control? addedControl = eventArgs.Control;
    if (addedControl is null)
    {
      return;
    }

    WirePopupTree(addedControl);
  }

  private void AnchorMouseEnter(object? sender, EventArgs eventArgs)
  {
    _closeTimer.Stop();
    if (_state == PopupState.Closed && _anchor.Enabled)
    {
      _openTimer.Stop();
      _openTimer.Start();
    }
  }

  private void AnchorMouseLeave(object? sender, EventArgs eventArgs)
  {
    _openTimer.Stop();
    ScheduleCloseIfOutside();
  }

  private void AnchorFocusEntered(object? sender, EventArgs eventArgs)
  {
    _closeTimer.Stop();
    if (_suppressNextAnchorFocusOpen)
    {
      _suppressNextAnchorFocusOpen = false;
      return;
    }

    if (_state == PopupState.Closed && _anchor.Enabled)
    {
      OpenImmediately(focusPopup: true);
    }
  }

  private void AnchorFocusLeft(object? sender, EventArgs eventArgs)
  {
    ScheduleCloseIfOutside();
  }

  private void PopupMouseEntered(object? sender, EventArgs eventArgs)
  {
    if (_state == PopupState.Closed)
    {
      return;
    }

    _state = PopupState.OpenEntered;
    _closeTimer.Stop();
  }

  private void PopupMouseLeft(object? sender, EventArgs eventArgs)
  {
    ScheduleCloseIfOutside();
  }

  private void PopupFocusEntered(object? sender, EventArgs eventArgs)
  {
    if (_state == PopupState.Closed)
    {
      return;
    }

    _state = PopupState.OpenEntered;
    _closeTimer.Stop();
  }

  private void PopupFocusLeft(object? sender, EventArgs eventArgs)
  {
    ScheduleCloseIfOutside();
  }

  private void OpenTimerTick(object? sender, EventArgs eventArgs)
  {
    _openTimer.Stop();
    if (_state == PopupState.Closed &&
        _anchor.Enabled &&
        IsPointerInside(_anchor))
    {
      OpenImmediately(focusPopup: false);
    }
  }

  private void CloseTimerTick(object? sender, EventArgs eventArgs)
  {
    _closeTimer.Stop();
    if (_state != PopupState.OpenEntered || IsInsideComposite() ||
        (_keepOpen?.Invoke() ?? false))
    {
      return;
    }

    Close(returnFocus: false);
  }

  private void ScheduleCloseIfOutside()
  {
    if (_state != PopupState.OpenEntered)
    {
      return;
    }

    if (IsInsideComposite() || (_keepOpen?.Invoke() ?? false))
    {
      _closeTimer.Stop();
      return;
    }

    _closeTimer.Stop();
    _closeTimer.Start();
  }

  private void CancelCloseWhenInside()
  {
    if (IsInsideComposite())
    {
      _closeTimer.Stop();
    }
  }

  private void PromoteToEnteredWhenAppropriate()
  {
    if (_state == PopupState.Closed)
    {
      return;
    }

    foreach (Control popup in EnumeratePopupControls())
    {
      if (IsPointerInside(popup) || popup.ContainsFocus)
      {
        _state = PopupState.OpenEntered;
        return;
      }
    }
  }

  private bool IsInsideComposite()
  {
    if (IsPointerInside(_anchor) || _anchor.ContainsFocus)
    {
      return true;
    }

    foreach (Control popup in EnumeratePopupControls())
    {
      if (IsPointerInside(popup) || popup.ContainsFocus)
      {
        return true;
      }
    }

    return false;
  }

  private IEnumerable<Control> EnumeratePopupControls()
  {
    foreach (Control popup in _getPopupControls())
    {
      if (!popup.IsDisposed && popup.Visible)
      {
        yield return popup;
      }
    }
  }

  private static bool IsPointerInside(Control control)
  {
    return control.Visible && control.ClientRectangle.Contains(
      control.PointToClient(Cursor.Position));
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
