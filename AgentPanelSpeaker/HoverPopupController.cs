namespace AgentPanelSpeaker;

/// <summary>
/// Owns one hover/focus popup tree, including nested popup lifecycle,
/// delayed opening and closing, focus retention, sibling replacement,
/// deepest-first dismissal, outside-click membership, and event cleanup.
/// </summary>
internal sealed class HoverPopupController : IDisposable
{
  internal enum PopupState
  {
    Closed,
    OpenAwaitingEntry,
    OpenEntered
  }

  /// <summary>
  /// Represents one popup node owned by a <see cref="HoverPopupController"/>.
  /// </summary>
  internal sealed class PopupHandle
  {
    private readonly HoverPopupController _owner;
    private readonly PopupNode _node;

    internal PopupHandle(HoverPopupController owner, PopupNode node)
    {
      _owner = owner;
      _node = node;
    }

    public bool IsOpen => _node.State != PopupState.Closed;

    public void OpenImmediately(bool focusPopup)
    {
      _owner.OpenNode(_node, focusPopup);
    }

    public void Close(bool returnFocus)
    {
      _owner.CloseNode(_node, returnFocus);
    }

    public void ReevaluateClose()
    {
      _owner.ReevaluateClose(_node);
    }
  }

  internal sealed class PopupNode
  {
    public required Control Anchor { get; init; }
    public required Func<IEnumerable<Control>> GetPopupControls { get; init; }
    public required Action<bool> ShowPopup { get; init; }
    public required Action<bool> HidePopup { get; init; }
    public Func<bool>? KeepOpen { get; init; }
    public required int OpenDelayMilliseconds { get; init; }
    public required int CloseDelayMilliseconds { get; init; }
    public PopupNode? Parent { get; init; }
    public List<PopupNode> Children { get; } = new();
    public HashSet<Control> WiredControls { get; } = new();
    public System.Windows.Forms.Timer OpenTimer { get; } = new();
    public System.Windows.Forms.Timer CloseTimer { get; } = new();
    public PopupState State { get; set; }
    public bool SuppressNextAnchorFocusOpen { get; set; }
  }

  private readonly PopupNode _root;
  private readonly List<PopupNode> _nodes = new();
  private bool _disposed;

  /// <summary>
  /// Initializes the root popup node.
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
    _root = CreateNode(
      parent: null,
      anchor,
      getPopupControls,
      showPopup,
      hidePopup,
      openDelayMilliseconds,
      closeDelayMilliseconds,
      keepOpen);
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
    int openDelayMilliseconds = 250,
    int closeDelayMilliseconds = 200,
    Func<bool>? keepOpen = null)
  {
    ThrowIfDisposed();
    PopupNode node = CreateNode(
      _root,
      anchor,
      getPopupControls,
      showPopup,
      hidePopup,
      openDelayMilliseconds,
      closeDelayMilliseconds,
      keepOpen);
    _root.Children.Add(node);
    return new PopupHandle(this, node);
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
    CloseNode(_root, returnFocus);
  }

  /// <summary>
  /// Closes the deepest open popup. Returns false when nothing is open.
  /// </summary>
  public bool CloseDeepest(bool returnFocus)
  {
    PopupNode? node = FindDeepestOpenNode(_root);
    if (node is null)
    {
      return false;
    }

    CloseNode(node, returnFocus);
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
        if (ReferenceEquals(current, node.Anchor))
        {
          return true;
        }
        if (EnumerateVisiblePopupControls(node).Any(
          popup => ReferenceEquals(current, popup)))
        {
          return true;
        }
      }
    }
    return false;
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
    int openDelayMilliseconds,
    int closeDelayMilliseconds,
    Func<bool>? keepOpen)
  {
    ArgumentNullException.ThrowIfNull(anchor);
    ArgumentNullException.ThrowIfNull(getPopupControls);
    ArgumentNullException.ThrowIfNull(showPopup);
    ArgumentNullException.ThrowIfNull(hidePopup);

    var node = new PopupNode
    {
      Parent = parent,
      Anchor = anchor,
      GetPopupControls = getPopupControls,
      ShowPopup = showPopup,
      HidePopup = hidePopup,
      OpenDelayMilliseconds = Math.Max(1, openDelayMilliseconds),
      CloseDelayMilliseconds = Math.Max(1, closeDelayMilliseconds),
      KeepOpen = keepOpen
    };
    node.OpenTimer.Interval = node.OpenDelayMilliseconds;
    node.CloseTimer.Interval = node.CloseDelayMilliseconds;
    node.OpenTimer.Tick += (_, _) => OpenTimerTick(node);
    node.CloseTimer.Tick += (_, _) => CloseTimerTick(node);
    WireAnchor(node);
    _nodes.Add(node);
    return node;
  }

  private void OpenNode(PopupNode node, bool focusPopup)
  {
    ThrowIfDisposed();
    node.OpenTimer.Stop();
    node.CloseTimer.Stop();

    if (node.Parent is not null)
    {
      if (node.Parent.State == PopupState.Closed)
      {
        OpenNode(node.Parent, focusPopup: false);
      }
      foreach (PopupNode sibling in node.Parent.Children)
      {
        if (!ReferenceEquals(sibling, node))
        {
          CloseNode(sibling, returnFocus: false);
        }
      }
    }

    node.ShowPopup(focusPopup);
    node.State = PopupState.OpenAwaitingEntry;
    RefreshPopupControls(node);
  }

  private void CloseNode(PopupNode node, bool returnFocus)
  {
    if (_disposed)
    {
      return;
    }

    foreach (PopupNode child in node.Children.ToArray())
    {
      CloseNode(child, returnFocus: false);
    }
    node.OpenTimer.Stop();
    node.CloseTimer.Stop();
    if (node.State == PopupState.Closed)
    {
      return;
    }

    node.State = PopupState.Closed;
    node.SuppressNextAnchorFocusOpen = returnFocus && !node.Anchor.ContainsFocus;
    node.HidePopup(returnFocus);
    if (!node.Anchor.ContainsFocus)
    {
      node.SuppressNextAnchorFocusOpen = false;
    }
  }

  private void ReevaluateClose(PopupNode node)
  {
    if (node.State == PopupState.OpenEntered)
    {
      ScheduleCloseIfOutside(node);
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
    if (node.State == PopupState.Closed && node.Anchor.Enabled)
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
    ScheduleCloseIfOutside(node);
  }

  private void AnchorFocusEntered(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeByAnchor(sender as Control);
    if (node is null)
    {
      return;
    }

    node.CloseTimer.Stop();
    if (node.SuppressNextAnchorFocusOpen)
    {
      node.SuppressNextAnchorFocusOpen = false;
      return;
    }
    if (node.State == PopupState.Closed && node.Anchor.Enabled)
    {
      OpenNode(node, focusPopup: true);
    }
  }

  private void AnchorFocusLeft(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeByAnchor(sender as Control);
    if (node is not null)
    {
      ScheduleCloseIfOutside(node);
    }
  }

  private void PopupMouseEntered(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeForPopupControl(sender as Control);
    if (node is null || node.State == PopupState.Closed)
    {
      return;
    }
    node.State = PopupState.OpenEntered;
    CancelCloseForAncestors(node);
  }

  private void PopupMouseLeft(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeForPopupControl(sender as Control);
    if (node is not null)
    {
      ScheduleCloseIfOutside(node);
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
    CancelCloseForAncestors(node);
  }

  private void PopupFocusLeft(object? sender, EventArgs eventArgs)
  {
    PopupNode? node = FindNodeForPopupControl(sender as Control);
    if (node is not null)
    {
      ScheduleCloseIfOutside(node);
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
    if (node.State != PopupState.OpenEntered ||
        IsInsideNodeComposite(node) ||
        (node.KeepOpen?.Invoke() ?? false))
    {
      return;
    }
    CloseNode(node, returnFocus: false);
  }

  private void ScheduleCloseIfOutside(PopupNode node)
  {
    if (node.State != PopupState.OpenEntered)
    {
      return;
    }
    if (IsInsideNodeComposite(node) || (node.KeepOpen?.Invoke() ?? false))
    {
      node.CloseTimer.Stop();
      return;
    }
    node.CloseTimer.Stop();
    node.CloseTimer.Start();
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
