namespace AgentPanelSpeaker;

/// <summary>
/// Owns one hover/focus popup tree, including nested popup lifecycle,
/// delayed opening and closing, focus retention, sibling replacement,
/// deepest-first dismissal, global outside-click handling, and event cleanup.
/// </summary>
internal sealed class HoverPopupController : IDisposable
{
  private const int WmChangeUiState = 0x0127;
  private const int UisClear = 2;
  private const int UisfHideFocus = 0x1;

  [System.Runtime.InteropServices.DllImport("user32.dll")]
  private static extern IntPtr SendMessage(
    IntPtr window,
    int message,
    IntPtr wParam,
    IntPtr lParam);

  private enum PopupState
  {
    Closed,
    OpenAwaitingEntry,
    OpenEntered
  }

  private static readonly List<WeakReference<HoverPopupController>>
    Controllers = new();
  private static readonly object ControllersLock = new();
  private static long _nextActivationOrder;

  /// <summary>
  /// Represents one popup node owned by a <see cref="HoverPopupController"/>.
  /// </summary>
  internal sealed class PopupHandle
  {
    private readonly HoverPopupController _owner;
    private readonly int _nodeId;

    internal PopupHandle(HoverPopupController owner, int nodeId)
    {
      _owner = owner;
      _nodeId = nodeId;
    }

    public bool IsOpen => _owner.IsNodeOpen(_nodeId);

    public void OpenImmediately(bool focusPopup)
    {
      _owner.OpenNode(_owner.GetNode(_nodeId), focusPopup);
    }

    public void Close(bool returnFocus)
    {
      _owner.CloseNode(
        _owner.GetNode(_nodeId),
        returnFocus,
        closeReason: "popup-handle-close");
    }

    public void ReevaluateClose()
    {
      _owner.ReevaluateClose(_owner.GetNode(_nodeId));
    }
  }

  private sealed class PopupNode
  {
    public required int Id { get; init; }
    public required Control Anchor { get; init; }
    public required Func<IEnumerable<Control>> GetPopupControls { get; init; }
    public required Action<bool> ShowPopup { get; init; }
    public required Action<bool> HidePopup { get; init; }
    public required Action FocusInitialControl { get; init; }
    public Func<bool>? KeepOpen { get; init; }
    public required int OpenDelayMilliseconds { get; init; }
    public required int CloseDelayMilliseconds { get; init; }
    public required bool OpenOnHover { get; init; }
    public required bool OpenOnFocus { get; init; }
    public PopupNode? Parent { get; init; }
    public List<PopupNode> Children { get; } = new();
    public HashSet<Control> WiredControls { get; } = new();
    public System.Windows.Forms.Timer OpenTimer { get; } = new();
    public System.Windows.Forms.Timer CloseTimer { get; } = new();
    public PopupState State { get; set; }
    public bool SuppressNextAnchorFocusOpen { get; set; }
    public bool SuppressHoverUntilAnchorExit { get; set; }
    public bool PointerEnteredAsLeaf { get; set; }
    public bool FocusOpenQueued { get; set; }
    public bool CloseScheduledWithKeyboardFocus { get; set; }
    public long Generation { get; set; }
  }

  private readonly PopupNode _root;
  private readonly List<PopupNode> _nodes = new();
  private int _nextNodeId;
  private long _activationOrder;
  private bool _disposed;

  /// <summary>
  /// Initializes the root popup node.
  /// </summary>
  public HoverPopupController(
    Control anchor,
    Func<IEnumerable<Control>> getPopupControls,
    Action<bool> showPopup,
    Action<bool> hidePopup,
    Action focusInitialControl,
    int openDelayMilliseconds = 250,
    int closeDelayMilliseconds = 1000,
    Func<bool>? keepOpen = null,
    bool openOnHover = true,
    bool openOnFocus = true)
  {
    _root = CreateNode(
      parent: null,
      anchor,
      getPopupControls,
      showPopup,
      hidePopup,
      focusInitialControl,
      openDelayMilliseconds,
      closeDelayMilliseconds,
      keepOpen,
      openOnHover,
      openOnFocus);
    RegisterController(this);
  }

  /// <summary>
  /// Gets whether the root popup is open.
  /// </summary>
  public bool IsOpen => _root.State != PopupState.Closed;

  /// <summary>
  /// Registers a nested popup under the root popup tree.
  /// Sibling popup nodes are mutually exclusive.
  /// </summary>
  public PopupHandle RegisterChild(
    Control anchor,
    Func<IEnumerable<Control>> getPopupControls,
    Action<bool> showPopup,
    Action<bool> hidePopup,
    Action focusInitialControl,
    int openDelayMilliseconds = 250,
    int closeDelayMilliseconds = 1000,
    Func<bool>? keepOpen = null,
    bool openOnHover = true,
    bool openOnFocus = true)
  {
    ThrowIfDisposed();
    PopupNode node = CreateNode(
      _root,
      anchor,
      getPopupControls,
      showPopup,
      hidePopup,
      focusInitialControl,
      openDelayMilliseconds,
      closeDelayMilliseconds,
      keepOpen,
      openOnHover,
      openOnFocus);
    _root.Children.Add(node);
    return new PopupHandle(this, node.Id);
  }

  /// <summary>
  /// Opens the root popup immediately.
  /// </summary>
  public void OpenImmediately(bool focusPopup)
  {
    OpenNode(_root, focusPopup);
  }

  /// <summary>
  /// Closes the root popup and all descendants.
  /// </summary>
  public void Close(bool returnFocus)
  {
    CloseNode(_root, returnFocus, closeReason: "controller-close");
  }

  /// <summary>
  /// Closes the deepest open popup. Returns false when nothing is open.
  /// </summary>
  public bool CloseDeepest(bool returnFocus, bool keyboardClose = false)
  {
    PopupNode? node = FindDeepestOpenNode(_root);
    if (node is null)
    {
      return false;
    }

    bool restoreFocus = returnFocus || (keyboardClose && node.Parent is null);
    CloseNode(
      node,
      restoreFocus,
      keyboardClose,
      closeReason: "close-deepest");
    return true;
  }

  /// <summary>
  /// Gets whether a control belongs to this popup tree.
  /// </summary>
  public bool ContainsControl(Control? control)
  {
    if (control is null)
    {
      return false;
    }

    for (Control? current = control; current is not null; current = current.Parent)
    {
      foreach (PopupNode node in _nodes)
      {
        if (ReferenceEquals(current, node.Anchor) ||
            node.WiredControls.Contains(current))
        {
          return true;
        }
      }
    }
    return false;
  }

  /// <summary>
  /// Collapses each active popup chain to the deepest open popup containing
  /// the pointer-down target. Clicking an ancestor closes only its open
  /// descendants; clicking outside the chain closes the whole chain.
  /// </summary>
  public static void HandleGlobalPointerDown(Control? clickedControl)
  {
    foreach (HoverPopupController controller in GetLiveControllers())
    {
      controller.HandlePointerDown(clickedControl);
    }
  }

  /// <summary>
  /// Closes popup trees owned by a deactivated form unless focus moved into
  /// one of that tree's own popup forms.
  /// </summary>
  public static void HandleOwnerDeactivated(Form owner)
  {
    ArgumentNullException.ThrowIfNull(owner);
    if (owner.IsDisposed || !owner.IsHandleCreated)
    {
      return;
    }

    PopupFormBase.WriteActivationDiagnostics(
      "owner-deactivated-handler-entry",
      owner);

    try
    {
      owner.BeginInvoke((MethodInvoker)(() =>
      {
        if (owner.IsDisposed)
        {
          return;
        }

        PopupFormBase.WriteActivationDiagnostics(
          "owner-deactivated-deferred-entry",
          owner);

        Form? activeForm = Form.ActiveForm;
        foreach (HoverPopupController controller in GetLiveControllers())
        {
          if (!controller.IsOpen ||
              !ReferenceEquals(controller._root.Anchor.FindForm(), owner))
          {
            continue;
          }

          Form? diagnosticPopup = controller.GetDeepestOpenPopupForm();
          PopupFormBase.WriteActivationDiagnostics(
            "owner-deactivated-controller-evaluate",
            owner,
            diagnosticPopup);

          if (activeForm is not null &&
              controller.ContainsOpenPopupForm(activeForm))
          {
            DiagnosticLog.Write("popup.owner_deactivate_keep_open", new
            {
              owner = DescribeControl(owner),
              activeForm = DescribeControl(activeForm)
            });
            continue;
          }
          DiagnosticLog.Write("popup.owner_deactivate_close", new
          {
            owner = DescribeControl(owner),
            activeForm = DescribeControl(activeForm),
            rootNodeId = controller._root.Id
          });
          PopupFormBase.WriteActivationDiagnostics(
            "owner-deactivated-before-root-close",
            owner,
            diagnosticPopup);
          controller.CloseNode(
            controller._root,
            returnFocus: false,
            closeReason: "owner-deactivated");
          PopupFormBase.WriteActivationDiagnostics(
            "owner-deactivated-after-root-close",
            owner,
            diagnosticPopup);
        }

        owner.BeginInvoke((MethodInvoker)(() =>
        {
          if (!owner.IsDisposed)
          {
            PopupFormBase.WriteActivationDiagnostics(
              "owner-deactivated-settled",
              owner);
          }
        }));
      }));
    }
    catch (InvalidOperationException)
    {
      // The owner is already tearing down; there is no live UI turn left.
    }
  }

  /// <summary>
  /// Handles popup-level keyboard dismissal centrally.
  /// </summary>
  public static bool HandleGlobalDismissKey(Keys keyData)
  {
    if (keyData != Keys.Escape && keyData != (Keys.Alt | Keys.F4))
    {
      return false;
    }
    return CloseDeepestGlobal(returnFocus: false, keyboardClose: true);
  }


  /// <summary>
  /// Handles popup dismissal and tab traversal past a popup's first or last
  /// selectable control.
  /// </summary>
  public static bool HandleGlobalPopupKey(Keys keyData, Control context)
  {
    if (HandleGlobalDismissKey(keyData))
    {
      return true;
    }
    bool backward = keyData == (Keys.Shift | Keys.Tab);
    if (keyData != Keys.Tab && !backward)
    {
      return false;
    }

    HoverPopupController? controller = GetLiveControllers()
      .Where(candidate => candidate.IsOpen)
      .OrderByDescending(candidate => candidate._activationOrder)
      .FirstOrDefault();
    PopupNode? leaf = controller is null
      ? null
      : controller.FindDeepestOpenNode(controller._root);
    Form? contextForm = context.FindForm();
    if (leaf is null || contextForm is null)
    {
      return false;
    }

    if (!controller!.NodeOwnsForm(leaf, contextForm))
    {
      return false;
    }

    Control[] controls = GetTabControls(context).ToArray();
    Control? focused = FindFocusedControl(context);
    if (controls.Length == 0 || focused is null)
    {
      return false;
    }
    bool atBoundary = backward
      ? ReferenceEquals(focused, controls[0])
      : ReferenceEquals(focused, controls[^1]);
    if (!atBoundary)
    {
      return false;
    }
    controller.CloseNode(
      leaf,
      returnFocus: true,
      keyboardClose: true,
      closeReason: backward ? "shift-tab-boundary" : "tab-boundary");
    controller.MoveFocusBeyondAnchor(leaf, forward: !backward);
    return true;
  }

  /// <summary>
  /// Closes every open hover/focus popup tree.
  /// </summary>
  public static void CloseAllGlobal(bool returnFocus)
  {
    HoverPopupController[] openControllers = GetLiveControllers()
      .Where(controller => controller.IsOpen)
      .OrderByDescending(controller => controller._activationOrder)
      .ToArray();
    for (int index = 0; index < openControllers.Length; index++)
    {
      openControllers[index].Close(returnFocus && index == 0);
    }
  }

  /// <summary>
  /// Closes the deepest popup in the most recently active popup tree.
  /// </summary>
  public static bool CloseDeepestGlobal(
    bool returnFocus,
    bool keyboardClose = false)
  {
    HoverPopupController? controller = GetLiveControllers()
      .Where(candidate => candidate.IsOpen)
      .OrderByDescending(candidate => candidate._activationOrder)
      .FirstOrDefault();
    return controller?.CloseDeepest(returnFocus, keyboardClose) ?? false;
  }

  /// <summary>
  /// Re-evaluates delayed closing after a temporary keep-open condition ends.
  /// </summary>
  public void ReevaluateClose()
  {
    ReevaluateClose(_root);
  }

  /// <summary>
  /// Wires any popup controls that were created dynamically after opening.
  /// </summary>
  public void RefreshPopupControls()
  {
    if (_disposed)
    {
      return;
    }
    foreach (PopupNode node in _nodes)
    {
      if (node.State != PopupState.Closed)
      {
        RefreshPopupControls(node);
      }
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
    UnregisterController(this);
    foreach (PopupNode node in _nodes.ToArray())
    {
      UnwireNode(node);
      node.OpenTimer.Stop();
      node.CloseTimer.Stop();
      node.OpenTimer.Dispose();
      node.CloseTimer.Dispose();
    }
    _nodes.Clear();
  }

  private PopupNode CreateNode(
    PopupNode? parent,
    Control anchor,
    Func<IEnumerable<Control>> getPopupControls,
    Action<bool> showPopup,
    Action<bool> hidePopup,
    Action focusInitialControl,
    int openDelayMilliseconds,
    int closeDelayMilliseconds,
    Func<bool>? keepOpen,
    bool openOnHover,
    bool openOnFocus)
  {
    ArgumentNullException.ThrowIfNull(anchor);
    ArgumentNullException.ThrowIfNull(getPopupControls);
    ArgumentNullException.ThrowIfNull(showPopup);
    ArgumentNullException.ThrowIfNull(hidePopup);
    ArgumentNullException.ThrowIfNull(focusInitialControl);

    var node = new PopupNode
    {
      Id = ++_nextNodeId,
      Parent = parent,
      Anchor = anchor,
      GetPopupControls = getPopupControls,
      ShowPopup = showPopup,
      HidePopup = hidePopup,
      FocusInitialControl = focusInitialControl,
      OpenDelayMilliseconds = Math.Max(1, openDelayMilliseconds),
      CloseDelayMilliseconds = Math.Max(1, closeDelayMilliseconds),
      KeepOpen = keepOpen,
      OpenOnHover = openOnHover,
      OpenOnFocus = openOnFocus
    };
    node.OpenTimer.Interval = node.OpenDelayMilliseconds;
    node.CloseTimer.Interval = node.CloseDelayMilliseconds;
    node.OpenTimer.Tick += (_, _) => OpenTimerTick(node);
    node.CloseTimer.Tick += (_, _) => CloseTimerTick(node);
    WireAnchor(node);
    _nodes.Add(node);
    return node;
  }

  private void OpenNode(
    PopupNode node,
    bool focusPopup,
    bool focusLastControl = false,
    bool deferFocus = false)
  {
    ThrowIfDisposed();
    node.OpenTimer.Stop();
    node.CloseTimer.Stop();
    node.CloseScheduledWithKeyboardFocus = false;
    MarkActive();

    if (node.Parent is not null)
    {
      CloseOpenSiblings(node);
    }

    if (node.State != PopupState.Closed)
    {
      CancelCloseForAncestors(node);
      if (focusPopup)
      {
        node.State = PopupState.OpenEntered;
        FocusInitialControlWithDiagnostics(
          node,
          focusLastControl ? "open-existing-backward" : "open-existing",
          focusAction: deferFocus
            ? () => FocusBoundaryControl(node, focusLastControl)
            : focusLastControl
              ? () => FocusBoundaryControl(node, backward: true)
              : null,
          deferInitialFocus: deferFocus);
      }
      return;
    }

    if (node.Parent is null)
    {
      CloseOtherRootControllers();
    }
    else
    {
      if (node.Parent.State == PopupState.Closed)
      {
        OpenNode(node.Parent, focusPopup: false);
      }
    }

    node.State = PopupState.OpenAwaitingEntry;
    node.ShowPopup(focusPopup);
    RefreshPopupControls(node);

    if (!EnumerateVisiblePopupControls(node).Any())
    {
      node.State = PopupState.Closed;
      return;
    }

    if (focusPopup || NodeContainsFocus(node) || NodeContainsPointer(node))
    {
      node.State = PopupState.OpenEntered;
      node.PointerEnteredAsLeaf = NodeContainsPointer(node);
      CancelCloseForAncestors(node);
    }

    if (focusPopup)
    {
      FocusInitialControlWithDiagnostics(
        node,
        focusLastControl ? "open-new-backward" : "open-new",
        focusAction: deferFocus
          ? () => FocusBoundaryControl(node, focusLastControl)
          : focusLastControl
            ? () => FocusBoundaryControl(node, backward: true)
            : null,
        deferInitialFocus: deferFocus);
    }
  }

  private void CloseNode(
    PopupNode node,
    bool returnFocus,
    bool keyboardClose = false,
    string closeReason = "unspecified",
    [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
  {
    if (_disposed)
    {
      return;
    }

    node.Generation++;
    node.FocusOpenQueued = false;
    foreach (PopupNode child in node.Children.ToArray())
    {
      CloseNode(
        child,
        returnFocus: false,
        closeReason: $"ancestor-close:{closeReason}");
    }
    node.OpenTimer.Stop();
    node.CloseTimer.Stop();
    if (node.State == PopupState.Closed)
    {
      return;
    }

    node.State = PopupState.Closed;
    node.PointerEnteredAsLeaf = false;
    node.SuppressHoverUntilAnchorExit = keyboardClose &&
      IsPointerInside(node.Anchor);
    node.SuppressNextAnchorFocusOpen = false;
    DiagnosticLog.Write("popup.close_before_hide", new
    {
      node.Id,
      returnFocus,
      keyboardClose,
      closeReason,
      caller,
      anchor = DescribeControl(node.Anchor),
      activeForm = DescribeControl(Form.ActiveForm),
      anchorForm = DescribeControl(node.Anchor.FindForm()),
      anchorFormActiveControl = DescribeControl(node.Anchor.FindForm()?.ActiveControl),
      anchorFormFocusedControl = DescribeControl(
        FindFocusedControl(node.Anchor.FindForm()))
    });

    // Popup-specific hide callbacks only hide/flush state. Focus restoration is
    // owned here so every popup tree gets identical semantics.
    node.HidePopup(false);
    DiagnosticLog.Write("popup.close_after_hide", new
    {
      node.Id,
      returnFocus,
      keyboardClose,
      closeReason,
      caller,
      anchor = DescribeControl(node.Anchor),
      activeForm = DescribeControl(Form.ActiveForm),
      anchorForm = DescribeControl(node.Anchor.FindForm()),
      anchorFormActiveControl = DescribeControl(node.Anchor.FindForm()?.ActiveControl),
      anchorFormFocusedControl = DescribeControl(
        FindFocusedControl(node.Anchor.FindForm()))
    });

    if (returnFocus)
    {
      RestoreAnchorFocusOnce(node, closeReason);
    }
  }

  private void RestoreAnchorFocusOnce(PopupNode node, string closeReason)
  {
    Control anchor = node.Anchor;
    if (anchor.IsDisposed || !anchor.Visible || !anchor.Enabled ||
        !anchor.CanFocus)
    {
      node.SuppressNextAnchorFocusOpen = false;
      DiagnosticLog.Write("popup.anchor_focus_restore_skipped", new
      {
        node.Id,
        closeReason,
        anchor = DescribeControl(anchor),
        anchorIsDisposed = anchor.IsDisposed,
        anchorVisible = anchor.Visible,
        anchorEnabled = anchor.Enabled,
        anchorCanFocus = anchor.CanFocus
      });
      return;
    }

    ActivateContainingForm(anchor);

    // Suppress exactly the Enter raised by this controller-owned Focus().
    // Clear unconditionally when Focus() returns so a later real Tab/Shift+Tab
    // can never inherit stale suppression state.
    node.SuppressNextAnchorFocusOpen = !anchor.ContainsFocus;
    bool focused;
    try
    {
      focused = anchor.Focus();
    }
    finally
    {
      node.SuppressNextAnchorFocusOpen = false;
    }

    DiagnosticLog.Write("popup.anchor_focus_restored", new
    {
      node.Id,
      closeReason,
      focused,
      anchor = DescribeControl(anchor),
      activeForm = DescribeControl(Form.ActiveForm),
      anchorForm = DescribeControl(anchor.FindForm()),
      anchorContainsFocus = anchor.ContainsFocus,
      suppressNextAnchorFocusOpen = node.SuppressNextAnchorFocusOpen
    });
  }

  private void CloseOpenSiblings(PopupNode node)
  {
    if (node.Parent is null)
    {
      return;
    }

    foreach (PopupNode sibling in node.Parent.Children.ToArray())
    {
      if (!ReferenceEquals(sibling, node) &&
          sibling.State != PopupState.Closed)
      {
        CloseNode(
          sibling,
          returnFocus: false,
          closeReason: "open-sibling-replacement");
      }
    }
  }

  private void MoveFocusBeyondAnchor(PopupNode node, bool forward)
  {
    if (node.Anchor.IsDisposed)
    {
      return;
    }

    if (node.Parent is not null)
    {
      Control[] parentControls = EnumerateVisiblePopupControls(node.Parent)
        .SelectMany(GetTabControls)
        .ToArray();
      int anchorIndex = Array.FindIndex(
        parentControls,
        control => ReferenceEquals(control, node.Anchor));
      if (anchorIndex >= 0)
      {
        int targetIndex = forward ? anchorIndex + 1 : anchorIndex - 1;
        if (targetIndex >= 0 && targetIndex < parentControls.Length)
        {
          Control target = parentControls[targetIndex];
          DiagnosticLog.Write("popup.exit_focus_before", new
          {
            node.Id,
            forward,
            anchor = DescribeControl(node.Anchor),
            target = DescribeControl(target),
            activeForm = DescribeControl(Form.ActiveForm),
            targetForm = DescribeControl(target.FindForm()),
            targetFormActiveControl = DescribeControl(target.FindForm()?.ActiveControl),
            targetFormFocusedControl = DescribeControl(
              FindFocusedControl(target.FindForm()))
          });
          ActivateContainingForm(target);
          bool focused = target.Focus();
          DiagnosticLog.Write("popup.exit_focus_after", new
          {
            node.Id,
            forward,
            focused,
            anchor = DescribeControl(node.Anchor),
            target = DescribeControl(target),
            activeForm = DescribeControl(Form.ActiveForm),
            targetForm = DescribeControl(target.FindForm()),
            targetFormActiveControl = DescribeControl(target.FindForm()?.ActiveControl),
            targetFormFocusedControl = DescribeControl(
              FindFocusedControl(target.FindForm()))
          });
          return;
        }
      }

      // Restore focus to the parent anchor before unwinding another popup
      // level. If there is no next control at a higher level, that restored
      // focus keeps the application active instead of leaving ActiveForm null.
      CloseNode(
        node.Parent,
        returnFocus: true,
        keyboardClose: true,
        closeReason: forward
          ? "tab-exit-parent-exhausted"
          : "shift-tab-exit-parent-exhausted");
      MoveFocusBeyondAnchor(node.Parent, forward);
      return;
    }

    Control current = node.Anchor;
    Control? parent = current.Parent;
    Form? containingForm = node.Anchor.FindForm();
    while (parent is not null && !parent.IsDisposed)
    {
      if (parent.SelectNextControl(
          current,
          forward,
          tabStopOnly: true,
          nested: true,
          wrap: false))
      {
        return;
      }

      current = parent;
      parent = parent.Parent;
    }

    // Match normal WinForms Tab behaviour at the outermost boundary: wrap to
    // the first/last active control in the containing form rather than letting
    // keyboard focus fall out of the application.
    if (containingForm is not null &&
        !containingForm.IsDisposed &&
        containingForm.SelectNextControl(
          null,
          forward,
          tabStopOnly: true,
          nested: true,
          wrap: true))
    {
      DiagnosticLog.Write("popup.main_window_tab_wrapped", new
      {
        node.Id,
        forward,
        activeForm = DescribeControl(Form.ActiveForm),
        focusedControl = DescribeControl(FindFocusedControl(containingForm))
      });
      return;
    }

    // SelectNextControl can fail if the form currently has no selectable
    // controls. Keep its activation rather than yielding to another process.
    ActivateContainingForm(node.Anchor);
    node.Anchor.Focus();
  }

  private void ReevaluateClose(PopupNode node)
  {
    if (node.State == PopupState.OpenEntered)
    {
      ScheduleCloseIfOutside(node, "explicit-reevaluate-close");
    }
  }

  private void WireAnchor(PopupNode node)
  {
    node.Anchor.MouseEnter += AnchorMouseEnter;
    node.Anchor.MouseLeave += AnchorMouseLeave;
    node.Anchor.Enter += AnchorFocusEntered;
    node.Anchor.Leave += AnchorFocusLeft;
  }

  private void WirePopupTree(PopupNode node, Control control)
  {
    if (!node.WiredControls.Add(control))
    {
      return;
    }

    control.MouseEnter += PopupMouseEntered;
    control.MouseDown += PopupMouseDown;
    control.MouseLeave += PopupMouseLeft;
    control.Enter += PopupFocusEntered;
    control.Leave += PopupFocusLeft;
    control.ControlAdded += PopupControlAdded;
    control.Disposed += PopupControlDisposed;

    foreach (Control child in control.Controls)
    {
      WirePopupTree(node, child);
    }
  }

  private void UnwireNode(PopupNode node)
  {
    node.Anchor.MouseEnter -= AnchorMouseEnter;
    node.Anchor.MouseLeave -= AnchorMouseLeave;
    node.Anchor.Enter -= AnchorFocusEntered;
    node.Anchor.Leave -= AnchorFocusLeft;
    foreach (Control control in node.WiredControls.ToArray())
    {
      UnwirePopupControl(node, control);
    }
  }

  private void UnwirePopupControl(PopupNode node, Control control)
  {
    control.MouseEnter -= PopupMouseEntered;
    control.MouseDown -= PopupMouseDown;
    control.MouseLeave -= PopupMouseLeft;
    control.Enter -= PopupFocusEntered;
    control.Leave -= PopupFocusLeft;
    control.ControlAdded -= PopupControlAdded;
    control.Disposed -= PopupControlDisposed;
    node.WiredControls.Remove(control);
  }

  private void PopupControlAdded(object? sender, ControlEventArgs eventArgs)
  {
    Control? added = eventArgs.Control;
    if (added is null)
    {
      return;
    }

    PopupNode? node = FindNodeForPopupControl(sender as Control);
    if (node is not null)
    {
      WirePopupTree(node, added);
    }
  }

  private void PopupControlDisposed(object? sender, EventArgs eventArgs)
  {
    if (sender is not Control control)
    {
      return;
    }

    PopupNode? node = _nodes.FirstOrDefault(
      candidate => candidate.WiredControls.Contains(control));
    if (node is not null)
    {
      UnwirePopupControl(node, control);
    }
  }

  private void AnchorMouseEnter(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeByAnchor(sender as Control);
    if (node is null)
    {
      return;
    }

    node.CloseTimer.Stop();
    if (node.SuppressHoverUntilAnchorExit)
    {
      return;
    }
    if (node.OpenOnHover &&
        node.State == PopupState.Closed &&
        node.Anchor.Enabled)
    {
      node.OpenTimer.Stop();
      node.OpenTimer.Start();
    }
  }

  private void AnchorMouseLeave(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeByAnchor(sender as Control);
    if (node is null)
    {
      return;
    }
    node.OpenTimer.Stop();
    node.SuppressHoverUntilAnchorExit = false;
    ScheduleCloseIfOutside(node, "anchor-mouse-leave");
  }

  private void AnchorFocusEntered(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeByAnchor(sender as Control);
    if (node is null)
    {
      return;
    }

    bool backward = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
    DiagnosticLog.Write("popup.anchor_focus_entered", new
    {
      node.Id,
      nodeState = node.State.ToString(),
      node.OpenOnFocus,
      node.Anchor.Enabled,
      node.Anchor.ContainsFocus,
      node.SuppressNextAnchorFocusOpen,
      node.FocusOpenQueued,
      node.Generation,
      backward,
      activeForm = DescribeControl(Form.ActiveForm),
      anchor = DescribeControl(node.Anchor)
    });

    node.CloseTimer.Stop();
    if (node.SuppressNextAnchorFocusOpen)
    {
      DiagnosticLog.Write("popup.anchor_focus_open_suppressed", new
      {
        node.Id,
        reason = "SuppressNextAnchorFocusOpen",
        node.Generation,
        backward,
        anchor = DescribeControl(node.Anchor)
      });
      node.SuppressNextAnchorFocusOpen = false;
      return;
    }
    if (node.OpenOnFocus &&
        node.State == PopupState.Closed &&
        node.Anchor.Enabled)
    {
      QueueFocusOpen(node, backward);
      return;
    }

    DiagnosticLog.Write("popup.anchor_focus_open_not_queued", new
    {
      node.Id,
      nodeState = node.State.ToString(),
      node.OpenOnFocus,
      node.Anchor.Enabled,
      node.Anchor.ContainsFocus,
      node.FocusOpenQueued,
      node.Generation,
      backward
    });
  }

  private void QueueFocusOpen(PopupNode node, bool backward)
  {
    if (node.FocusOpenQueued || node.Anchor.IsDisposed ||
        !node.Anchor.IsHandleCreated)
    {
      DiagnosticLog.Write("popup.focus_open_queue_rejected", new
      {
        node.Id,
        node.FocusOpenQueued,
        node.Anchor.IsDisposed,
        node.Anchor.IsHandleCreated,
        nodeState = node.State.ToString(),
        node.Generation,
        backward,
        anchor = DescribeControl(node.Anchor)
      });
      return;
    }

    node.FocusOpenQueued = true;
    long generation = node.Generation;
    DiagnosticLog.Write("popup.focus_open_queued", new
    {
      node.Id,
      generation,
      backward,
      nodeState = node.State.ToString(),
      anchor = DescribeControl(node.Anchor),
      activeForm = DescribeControl(Form.ActiveForm)
    });
    try
    {
      node.Anchor.BeginInvoke((MethodInvoker)(() =>
      {
        node.FocusOpenQueued = false;
        string? abortReason = _disposed
          ? "controller-disposed"
          : node.Generation != generation
            ? "generation-changed"
            : node.State != PopupState.Closed
              ? "node-not-closed"
              : !node.Anchor.Enabled
                ? "anchor-disabled"
                : !node.Anchor.ContainsFocus
                  ? "anchor-lost-focus"
                  : null;
        DiagnosticLog.Write("popup.focus_open_deferred_evaluate", new
        {
          node.Id,
          queuedGeneration = generation,
          currentGeneration = node.Generation,
          backward,
          nodeState = node.State.ToString(),
          node.Anchor.Enabled,
          node.Anchor.ContainsFocus,
          abortReason,
          anchor = DescribeControl(node.Anchor),
          activeForm = DescribeControl(Form.ActiveForm)
        });
        if (abortReason is not null)
        {
          return;
        }

        DiagnosticLog.Write("popup.focus_open_deferred_execute", new
        {
          node.Id,
          generation,
          backward,
          anchor = DescribeControl(node.Anchor)
        });
        OpenNode(
          node,
          focusPopup: true,
          focusLastControl: backward,
          deferFocus: true);
      }));
    }
    catch (InvalidOperationException exception)
    {
      node.FocusOpenQueued = false;
      DiagnosticLog.Write("popup.focus_open_begin_invoke_failed", new
      {
        node.Id,
        generation,
        backward,
        exception = exception.ToString(),
        anchor = DescribeControl(node.Anchor)
      });
    }
  }

  private void AnchorFocusLeft(object? sender, EventArgs eventArgs)
  {
    Control? source = sender as Control;
    PopupNode? node = FindNodeByAnchor(source);
    if (node is not null)
    {
      DiagnosticLog.Write("popup.anchor_focus_left", new
      {
        node.Id,
        source = DescribeControl(source),
        activeForm = DescribeControl(Form.ActiveForm),
        nodeState = node.State.ToString(),
        nodeContainsFocus = NodeContainsFocus(node),
        pointerInsideLeaf = IsPointerInsideLeaf(node)
      });
      ScheduleCloseIfOutside(node, "anchor-focus-left");
    }
  }

  private void PopupMouseDown(object? sender, MouseEventArgs eventArgs)
  {
    if (eventArgs.Button != MouseButtons.Left || sender is not Control control)
    {
      return;
    }

    HandleGlobalPointerDown(control);
    PopupNode? node = FindNodeForPopupControl(control);
    if (node is null || node.State == PopupState.Closed)
    {
      return;
    }

    node.State = PopupState.OpenEntered;
    if (ReferenceEquals(node, FindDeepestOpenNode(_root)))
    {
      node.PointerEnteredAsLeaf = true;
    }
    MarkActive();
    CancelCloseForAncestors(node);
    bool isBackgroundSurface = IsBackgroundSurface(node, control);
    DiagnosticLog.Write("popup.pointer_down", new
    {
      node.Id,
      state = node.State.ToString(),
      control = DescribeControl(control),
      isBackgroundSurface,
      activeForm = DescribeControl(Form.ActiveForm),
      containingForm = DescribeControl(control.FindForm()),
      activeControl = DescribeControl(control.FindForm()?.ActiveControl),
      focusedControl = DescribeControl(FindFocusedControl(control.FindForm()))
    });
    if (isBackgroundSurface)
    {
      FocusInitialControlWithDiagnostics(node, "popup-background-click", control);
    }
  }

  private void FocusBoundaryControl(PopupNode node, bool backward)
  {
    Control[] activeControls = EnumerateVisiblePopupControls(node)
      .SelectMany(GetTabControls)
      .ToArray();
    Control? target = activeControls.Length == 0
      ? null
      : backward
        ? activeControls[^1]
        : activeControls[0];
    if (target is not null && target.CanFocus)
    {
      target.Focus();
      return;
    }

    node.FocusInitialControl();
  }

  private void FocusInitialControlWithDiagnostics(
    PopupNode node,
    string reason,
    Control? triggerControl = null,
    Action? focusAction = null,
    bool deferInitialFocus = false)
  {
    long generation = node.Generation;
    Control? popup = EnumerateVisiblePopupControls(node).FirstOrDefault();
    Form? form = popup?.FindForm() ?? node.Anchor.FindForm();
    DiagnosticLog.Write("popup.focus_attempt", new
    {
      reason,
      node.Id,
      state = node.State.ToString(),
      anchor = DescribeControl(node.Anchor),
      trigger = DescribeControl(triggerControl),
      popup = DescribeControl(popup),
      activeForm = DescribeControl(Form.ActiveForm),
      containingForm = DescribeControl(form),
      activeControl = DescribeControl(form?.ActiveControl),
      focusedControl = DescribeControl(FindFocusedControl(form)),
      anchorContainsFocus = node.Anchor.ContainsFocus,
      popupContainsFocus = popup?.ContainsFocus ?? false
    });

    if (form is { IsDisposed: false, Visible: true } &&
        !ReferenceEquals(Form.ActiveForm, form))
    {
      form.Activate();
    }
    if (!deferInitialFocus)
    {
      (focusAction ?? node.FocusInitialControl)();
      ShowKeyboardFocusCue(form, node, reason);
    }

    DiagnosticLog.Write("popup.focus_attempt_immediate", new
    {
      reason,
      node.Id,
      state = node.State.ToString(),
      activeForm = DescribeControl(Form.ActiveForm),
      containingForm = DescribeControl(form),
      activeControl = DescribeControl(form?.ActiveControl),
      focusedControl = DescribeControl(FindFocusedControl(form)),
      anchorContainsFocus = node.Anchor.ContainsFocus,
      popupContainsFocus = popup?.ContainsFocus ?? false
    });

    Control? dispatcher = popup is { IsDisposed: false }
      ? popup
      : node.Anchor is { IsDisposed: false }
        ? node.Anchor
        : null;
    if (dispatcher is null || !dispatcher.IsHandleCreated)
    {
      DiagnosticLog.Write("popup.focus_attempt_deferred_unavailable", new
      {
        reason,
        node.Id,
        dispatcher = DescribeControl(dispatcher)
      });
      return;
    }

    try
    {
      dispatcher.BeginInvoke((MethodInvoker)(() =>
      {
        if (_disposed || node.Generation != generation ||
            node.State == PopupState.Closed ||
            !ReferenceEquals(node, FindDeepestOpenNode(_root)))
        {
          return;
        }
        Control? currentPopup = EnumerateVisiblePopupControls(node)
          .FirstOrDefault();
        if (currentPopup is null || !currentPopup.Visible)
        {
          return;
        }
        Form? currentForm = currentPopup.FindForm() ?? node.Anchor.FindForm();
        if (currentForm is { IsDisposed: false, Visible: true } &&
            !ReferenceEquals(Form.ActiveForm, currentForm))
        {
          currentForm.Activate();
        }
        if (deferInitialFocus || !currentPopup.ContainsFocus)
        {
          (focusAction ?? node.FocusInitialControl)();
          ShowKeyboardFocusCue(currentForm, node, reason);
        }
        DiagnosticLog.Write("popup.focus_attempt_settled", new
        {
          reason,
          node.Id,
          state = node.State.ToString(),
          activeForm = DescribeControl(Form.ActiveForm),
          containingForm = DescribeControl(currentForm),
          activeControl = DescribeControl(currentForm?.ActiveControl),
          focusedControl = DescribeControl(FindFocusedControl(currentForm)),
          anchorContainsFocus = node.Anchor.ContainsFocus,
          popup = DescribeControl(currentPopup),
          popupContainsFocus = currentPopup?.ContainsFocus ?? false
        });
      }));
    }
    catch (InvalidOperationException exception)
    {
      DiagnosticLog.Write("popup.focus_attempt_deferred_failed", new
      {
        reason,
        node.Id,
        exception = exception.ToString()
      });
    }
  }

  private static void ShowKeyboardFocusCue(
    Form? form,
    PopupNode node,
    string reason)
  {
    if (form is null || form.IsDisposed || !form.IsHandleCreated)
    {
      DiagnosticLog.Write("popup.focus_cue_unavailable", new
      {
        reason,
        node.Id,
        form = DescribeControl(form)
      });
      return;
    }

    int value = UisClear | (UisfHideFocus << 16);
    _ = SendMessage(
      form.Handle,
      WmChangeUiState,
      (IntPtr)value,
      IntPtr.Zero);

    DiagnosticLog.Write("popup.focus_cue_shown", new
    {
      reason,
      node.Id,
      form = DescribeControl(form),
      value
    });
  }

  private static Control? FindFocusedControl(Control? root)
  {
    if (root is null)
    {
      return null;
    }
    if (root.Focused)
    {
      return root;
    }
    foreach (Control child in root.Controls)
    {
      Control? focused = FindFocusedControl(child);
      if (focused is not null)
      {
        return focused;
      }
    }
    return null;
  }

  private static object? DescribeControl(Control? control)
  {
    return control is null
      ? null
      : new
      {
        type = control.GetType().FullName,
        control.Name,
        control.Text,
        control.Visible,
        control.Enabled,
        control.TabStop,
        control.CanFocus,
        control.CanSelect,
        control.Focused,
        control.ContainsFocus,
        control.IsHandleCreated,
        control.IsDisposed,
        bounds = control.Bounds.ToString()
      };
  }

  private void PopupMouseEntered(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeForPopupControl(sender as Control);
    if (node is null || node.State == PopupState.Closed)
    {
      return;
    }
    node.State = PopupState.OpenEntered;
    if (ReferenceEquals(node, FindDeepestOpenNode(_root)))
    {
      node.PointerEnteredAsLeaf = true;
    }
    MarkActive();
    CancelCloseForAncestors(node);
  }

  private void PopupMouseLeft(object? sender, EventArgs eventArgs)
  {
    Control? source = sender as Control;
    PopupNode? node = FindNodeForPopupControl(source);
    if (node is not null)
    {
      DiagnosticLog.Write("popup.mouse_left", new
      {
        node.Id,
        source = DescribeControl(source),
        nodeState = node.State.ToString(),
        node.PointerEnteredAsLeaf,
        nodeContainsFocus = NodeContainsFocus(node),
        nodeContainsPointer = NodeContainsPointer(node),
        pointerInsideLeaf = IsPointerInsideLeaf(node)
      });
      ScheduleCloseIfOutside(node, "popup-mouse-leave");
    }
  }

  private void PopupFocusEntered(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeForPopupControl(sender as Control);
    if (node is null || node.State == PopupState.Closed)
    {
      return;
    }
    node.State = PopupState.OpenEntered;
    MarkActive();
    CancelCloseForAncestors(node);
  }

  private void PopupFocusLeft(object? sender, EventArgs eventArgs)
  {
    Control? source = sender as Control;
    PopupNode? node = FindNodeForPopupControl(source);
    if (node is not null)
    {
      DiagnosticLog.Write("popup.focus_left", new
      {
        node.Id,
        source = DescribeControl(source),
        nodeState = node.State.ToString(),
        node.PointerEnteredAsLeaf,
        activeForm = DescribeControl(Form.ActiveForm),
        nodeContainsFocus = NodeContainsFocus(node),
        nodeContainsPointer = NodeContainsPointer(node),
        pointerInsideLeaf = IsPointerInsideLeaf(node),
        keepOpen = node.KeepOpen?.Invoke() ?? false
      });
      ScheduleCloseIfOutside(node, "popup-focus-left");
    }
  }

  private void OpenTimerTick(PopupNode node)
  {
    node.OpenTimer.Stop();
    if (node.State == PopupState.Closed &&
        node.Anchor.Enabled &&
        IsPointerInside(node.Anchor))
    {
      OpenNode(node, focusPopup: false);
    }
  }

  private void CloseTimerTick(PopupNode node)
  {
    node.CloseTimer.Stop();
    PopupNode? deepest = FindDeepestOpenNode(_root);
    bool isDeepest = ReferenceEquals(node, deepest);
    bool entered = node.State == PopupState.OpenEntered;
    bool pointerEnteredAsLeaf = node.PointerEnteredAsLeaf;
    bool pointerInsideLeaf = IsPointerInsideLeaf(node);
    bool keepOpen = node.KeepOpen?.Invoke() ?? false;
    bool nodeContainsFocus = NodeContainsFocus(node);
    DiagnosticLog.Write("popup.close_timer_tick", new
    {
      node.Id,
      nodeState = node.State.ToString(),
      isDeepest,
      deepestNodeId = deepest?.Id,
      entered,
      pointerEnteredAsLeaf,
      pointerInsideLeaf,
      keepOpen,
      activeForm = DescribeControl(Form.ActiveForm),
      nodeContainsFocus,
      anchorContainsFocus = node.Anchor.ContainsFocus
    });
    if (!isDeepest ||
        !entered ||
        !pointerEnteredAsLeaf ||
        pointerInsideLeaf ||
        keepOpen)
    {
      return;
    }
    bool restoreFocus = node.CloseScheduledWithKeyboardFocus ||
      nodeContainsFocus;
    DiagnosticLog.Write("popup.close_timer_close_decision", new
    {
      node.Id,
      restoreFocus,
      closeScheduledWithKeyboardFocus = node.CloseScheduledWithKeyboardFocus,
      activeForm = DescribeControl(Form.ActiveForm),
      nodeContainsFocus,
      anchorContainsFocus = node.Anchor.ContainsFocus
    });
    node.CloseScheduledWithKeyboardFocus = false;
    CloseNode(
      node,
      returnFocus: restoreFocus,
      closeReason: "close-timer-expired");
  }

  private void ScheduleCloseIfOutside(PopupNode node, string triggerReason)
  {
    PopupNode? deepest = FindDeepestOpenNode(_root);
    bool isDeepest = ReferenceEquals(node, deepest);
    bool entered = node.State == PopupState.OpenEntered;
    bool pointerEnteredAsLeaf = node.PointerEnteredAsLeaf;
    bool pointerInsideLeaf = IsPointerInsideLeaf(node);
    bool keepOpen = node.KeepOpen?.Invoke() ?? false;
    bool nodeContainsFocus = NodeContainsFocus(node);
    DiagnosticLog.Write("popup.close_schedule_evaluate", new
    {
      node.Id,
      triggerReason,
      nodeState = node.State.ToString(),
      isDeepest,
      deepestNodeId = deepest?.Id,
      entered,
      pointerEnteredAsLeaf,
      pointerInsideLeaf,
      keepOpen,
      timerEnabledBefore = node.CloseTimer.Enabled,
      activeForm = DescribeControl(Form.ActiveForm),
      nodeContainsFocus,
      anchorContainsFocus = node.Anchor.ContainsFocus
    });
    if (!isDeepest || !entered || !pointerEnteredAsLeaf)
    {
      return;
    }
    if (pointerInsideLeaf || keepOpen)
    {
      node.CloseTimer.Stop();
      node.CloseScheduledWithKeyboardFocus = false;
      DiagnosticLog.Write("popup.close_timer_cancelled", new
      {
        node.Id,
        triggerReason,
        reason = pointerInsideLeaf ? "pointer-inside-leaf" : "keep-open"
      });
      return;
    }
    node.CloseTimer.Stop();
    node.CloseScheduledWithKeyboardFocus = nodeContainsFocus;
    node.CloseTimer.Start();
    DiagnosticLog.Write("popup.close_timer_started", new
    {
      node.Id,
      triggerReason,
      closeScheduledWithKeyboardFocus = node.CloseScheduledWithKeyboardFocus,
      intervalMilliseconds = node.CloseTimer.Interval
    });
  }

  private bool IsPointerInsideLeaf(PopupNode node)
  {
    return EnumerateVisiblePopupControls(node).Any(IsPointerInside);
  }

  private bool IsInsideNodeComposite(PopupNode node)
  {
    if (IsPointerInside(node.Anchor) || node.Anchor.ContainsFocus)
    {
      return true;
    }
    foreach (Control popup in EnumerateVisiblePopupControls(node))
    {
      if (IsPointerInside(popup) || popup.ContainsFocus)
      {
        return true;
      }
    }
    foreach (PopupNode child in node.Children)
    {
      if (child.State != PopupState.Closed && IsInsideNodeComposite(child))
      {
        return true;
      }
    }
    return false;
  }

  private bool NodeContainsPointer(PopupNode node)
  {
    return EnumerateVisiblePopupControls(node).Any(IsPointerInside);
  }

  private static bool IsBackgroundSurface(PopupNode node, Control control)
  {
    if (node.GetPopupControls().Any(popup => ReferenceEquals(popup, control)))
    {
      return true;
    }
    return !control.TabStop && control is Label or Panel or UserControl;
  }

  private void HandlePointerDown(Control? clickedControl)
  {
    if (_disposed || !IsOpen)
    {
      return;
    }

    PopupNode? containing = FindDeepestOpenContaining(clickedControl);
    PopupNode? leaf = FindDeepestOpenNode(_root);
    DiagnosticLog.Write("popup.pointer_down_route", new
    {
      clickedControl = DescribeControl(clickedControl),
      containingNodeId = containing?.Id,
      deepestNodeId = leaf?.Id,
      activeForm = DescribeControl(Form.ActiveForm)
    });
    while (leaf is not null && !ReferenceEquals(leaf, containing))
    {
      PopupNode? parent = leaf.Parent;
      CloseNode(
        leaf,
        returnFocus: false,
        closeReason: "pointer-down-outside-leaf");
      leaf = parent is null ? null : FindDeepestOpenNode(parent);
    }
  }

  private PopupNode? FindDeepestOpenContaining(Control? control)
  {
    if (control is null)
    {
      return null;
    }
    PopupNode? result = null;
    foreach (PopupNode node in _nodes)
    {
      if (node.State != PopupState.Closed && NodeContainsControl(node, control))
      {
        if (result is null || IsDescendantOf(node, result))
        {
          result = node;
        }
      }
    }
    return result;
  }

  private static bool IsDescendantOf(PopupNode node, PopupNode ancestor)
  {
    for (PopupNode? current = node.Parent; current is not null;
      current = current.Parent)
    {
      if (ReferenceEquals(current, ancestor))
      {
        return true;
      }
    }
    return false;
  }

  private static bool NodeContainsControl(PopupNode node, Control control)
  {
    for (Control? current = control; current is not null; current = current.Parent)
    {
      if (node.WiredControls.Contains(current))
      {
        return true;
      }
    }
    return false;
  }

  private void MarkActive()
  {
    _activationOrder = Interlocked.Increment(ref _nextActivationOrder);
  }

  private bool NodeContainsFocus(PopupNode node)
  {
    return node.Anchor.ContainsFocus || EnumerateVisiblePopupControls(node).Any(
      popup => popup.ContainsFocus);
  }

  private void RefreshPopupControls(PopupNode node)
  {
    foreach (Control popup in EnumerateVisiblePopupControls(node))
    {
      WirePopupTree(node, popup);
    }
  }

  private void CancelCloseForAncestors(PopupNode node)
  {
    for (PopupNode? current = node; current is not null; current = current.Parent)
    {
      current.CloseTimer.Stop();
    }
  }

  private PopupNode GetNode(int nodeId)
  {
    ThrowIfDisposed();
    return _nodes.First(node => node.Id == nodeId);
  }

  private bool IsNodeOpen(int nodeId)
  {
    return !_disposed && _nodes.FirstOrDefault(node => node.Id == nodeId) is
      PopupNode node && node.State != PopupState.Closed;
  }

  private PopupNode? FindNodeByAnchor(Control? anchor)
  {
    return anchor is null
      ? null
      : _nodes.FirstOrDefault(node => ReferenceEquals(node.Anchor, anchor));
  }

  private PopupNode? FindNodeForPopupControl(Control? control)
  {
    if (control is null)
    {
      return null;
    }
    return _nodes.FirstOrDefault(node => node.WiredControls.Contains(control));
  }

  private PopupNode? FindDeepestOpenNode(PopupNode node)
  {
    foreach (PopupNode child in node.Children)
    {
      PopupNode? deepest = FindDeepestOpenNode(child);
      if (deepest is not null)
      {
        return deepest;
      }
    }
    return node.State == PopupState.Closed ? null : node;
  }

  private void CloseOtherRootControllers()
  {
    foreach (HoverPopupController controller in GetLiveControllers())
    {
      if (!ReferenceEquals(controller, this) && controller.IsOpen)
      {
        controller.Close(returnFocus: false);
      }
    }
  }

  private static IEnumerable<Control> EnumerateVisiblePopupControls(
    PopupNode node)
  {
    foreach (Control popup in node.GetPopupControls())
    {
      if (!popup.IsDisposed && popup.Visible)
      {
        yield return popup;
      }
    }
  }

  private Form? GetDeepestOpenPopupForm()
  {
    PopupNode? node = FindDeepestOpenNode(_root);
    if (node is null)
    {
      return null;
    }

    foreach (Control control in EnumerateVisiblePopupControls(node))
    {
      if (control is Form form)
      {
        return form;
      }
      Form? containingForm = control.FindForm();
      if (containingForm is not null)
      {
        return containingForm;
      }
    }
    return null;
  }

  private bool ContainsOpenPopupForm(Form form)
  {
    return _nodes.Any(node =>
      node.State != PopupState.Closed && NodeOwnsForm(node, form));
  }

  private bool NodeOwnsForm(PopupNode node, Form form)
  {
    return EnumerateVisiblePopupControls(node).Any(control =>
      ReferenceEquals(control.FindForm(), form));
  }

  private static void ActivateContainingForm(Control control)
  {
    Form? form = control.FindForm();
    if (form is { IsDisposed: false, Visible: true } &&
        !ReferenceEquals(Form.ActiveForm, form))
    {
      form.Activate();
    }
  }

  private static IEnumerable<Control> GetTabControls(Control root)
  {
    foreach (Control child in root.Controls.Cast<Control>()
      .OrderBy(control => control.TabIndex))
    {
      if (child.Visible && child.Enabled && child.TabStop && child.CanSelect)
      {
        yield return child;
      }
      foreach (Control descendant in GetTabControls(child))
      {
        yield return descendant;
      }
    }
  }

  private static bool IsPointerInside(Control control)
  {
    return control.Visible && control.ClientRectangle.Contains(
      control.PointToClient(Cursor.Position));
  }

  private static void RegisterController(HoverPopupController controller)
  {
    lock (ControllersLock)
    {
      RemoveDeadControllers();
      Controllers.Add(new WeakReference<HoverPopupController>(controller));
    }
  }

  private static void UnregisterController(HoverPopupController controller)
  {
    lock (ControllersLock)
    {
      Controllers.RemoveAll(reference =>
        !reference.TryGetTarget(out HoverPopupController? target) ||
        ReferenceEquals(target, controller));
    }
  }

  private static HoverPopupController[] GetLiveControllers()
  {
    lock (ControllersLock)
    {
      RemoveDeadControllers();
      return Controllers
        .Select(reference =>
          reference.TryGetTarget(out HoverPopupController? target)
            ? target
            : null)
        .Where(controller => controller is not null)
        .Cast<HoverPopupController>()
        .ToArray();
    }
  }

  private static void RemoveDeadControllers()
  {
    Controllers.RemoveAll(reference => !reference.TryGetTarget(out _));
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
