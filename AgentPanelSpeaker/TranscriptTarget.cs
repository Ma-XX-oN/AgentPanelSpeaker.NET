using System.Windows.Automation;

namespace AgentPanelSpeaker;

/// <summary>
/// Retains the selected transcript container and reacquires it only when the
/// stored UI Automation element becomes unavailable.
/// </summary>
internal sealed class TranscriptTarget
{
  private const double MaximumReacquireDistance = 1.25;

  private readonly object _sync = new();
  private readonly AutomationElementSnapshot _selectedSnapshot;
  private readonly double _leftRatio;
  private readonly double _topRatio;
  private readonly double _widthRatio;
  private readonly double _heightRatio;
  private AutomationElement _container;
  private int[] _currentRuntimeId;

  /// <summary>
  /// Initializes a stable transcript-container target.
  /// </summary>
  private TranscriptTarget(TranscriptContainerSelection selection)
  {
    ArgumentNullException.ThrowIfNull(selection);

    WindowHandle = selection.WindowHandle;
    ProcessId = selection.ProcessId;
    SelectionPoint = selection.SelectionPoint;
    SelectionReason = selection.SelectionReason;
    _container = selection.Container;
    _selectedSnapshot = selection.Snapshot;
    _currentRuntimeId = selection.Snapshot.RuntimeId.ToArray();

    System.Windows.Rect root = selection.RootBounds;
    System.Windows.Rect bounds = selection.Snapshot.RawBounds;
    if (!AutomationTreeInspector.HasUsableBounds(root) ||
        !AutomationTreeInspector.HasUsableBounds(bounds))
    {
      throw new InvalidOperationException(
        "The selected transcript container has no usable screen bounds.");
    }

    _leftRatio = (bounds.Left - root.Left) / root.Width;
    _topRatio = (bounds.Top - root.Top) / root.Height;
    _widthRatio = bounds.Width / root.Width;
    _heightRatio = bounds.Height / root.Height;
  }

  /// <summary>
  /// Gets the owning top-level window.
  /// </summary>
  public IntPtr WindowHandle { get; }

  /// <summary>
  /// Gets the owning process identifier.
  /// </summary>
  public int ProcessId { get; }

  /// <summary>
  /// Gets the physical screen point used for selection.
  /// </summary>
  public System.Drawing.Point SelectionPoint { get; }

  /// <summary>
  /// Gets why the automatic ancestor selector chose this element.
  /// </summary>
  public string SelectionReason { get; }

  /// <summary>
  /// Selects the transcript container beneath a physical screen point.
  /// </summary>
  /// <param name="point">Physical screen point over narration text.</param>
  /// <returns>The selected target.</returns>
  public static TranscriptTarget CreateFromPoint(
    System.Drawing.Point point)
  {
    TranscriptContainerSelection selection =
      AutomationTreeInspector.SelectContainer(point);
    return new TranscriptTarget(selection);
  }

  /// <summary>
  /// Returns the retained container, reacquiring it only after it becomes
  /// unavailable.
  /// </summary>
  /// <returns>The current transcript container.</returns>
  public AutomationElement GetContainer()
  {
    lock (_sync)
    {
      if (IsCurrentContainerUsable(_container))
      {
        return _container;
      }

      DiagnosticLog.Write("target.container_unavailable", new
      {
        selected = _selectedSnapshot,
        reason = "The retained element no longer exposes current properties."
      });
      _container = ReacquireContainer();
      _currentRuntimeId = AutomationTreeInspector
        .ReadSnapshot(_container)
        .RuntimeId;
      return _container;
    }
  }

  /// <summary>
  /// Returns the current screen rectangle of a retained container.
  /// </summary>
  /// <param name="container">Current target container.</param>
  /// <returns>The container bounds in physical screen pixels.</returns>
  public System.Drawing.Rectangle GetContainerRectangle(
    AutomationElement container)
  {
    ArgumentNullException.ThrowIfNull(container);

    System.Windows.Rect bounds;
    try
    {
      bounds = container.Current.BoundingRectangle;
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      InvalidOperationException)
    {
      container = GetContainer();
      bounds = container.Current.BoundingRectangle;
    }

    if (!AutomationTreeInspector.HasUsableBounds(bounds))
    {
      throw new InvalidOperationException(
        "The transcript container has no usable screen bounds.");
    }

    return System.Drawing.Rectangle.FromLTRB(
      checked((int)Math.Floor(bounds.Left)),
      checked((int)Math.Floor(bounds.Top)),
      checked((int)Math.Ceiling(bounds.Right)),
      checked((int)Math.Ceiling(bounds.Bottom)));
  }

  /// <summary>
  /// Creates a readable target description without walking the full subtree.
  /// </summary>
  /// <returns>Window, process, and selected container information.</returns>
  public string Describe()
  {
    return $"PID {ProcessId}; {_selectedSnapshot.ControlType}; " +
      $"name={Abbreviate(_selectedSnapshot.Name, 80)}; " +
      $"bounds={_selectedSnapshot.Bounds}; " +
      $"reason={SelectionReason}";
  }

  /// <summary>
  /// Validates that the retained element still belongs to the selected process.
  /// </summary>
  private bool IsCurrentContainerUsable(AutomationElement container)
  {
    if (!NativeMethods.IsWindow(WindowHandle))
    {
      throw new InvalidOperationException(
        "The selected VS Code window no longer exists.");
    }

    NativeMethods.GetWindowThreadProcessId(
      WindowHandle,
      out uint currentProcessId);
    if (currentProcessId != ProcessId)
    {
      throw new InvalidOperationException(
        "The selected window handle now belongs to another process.");
    }

    try
    {
      AutomationElement.AutomationElementInformation current =
        container.Current;
      if (current.ProcessId != ProcessId ||
          !AutomationTreeInspector.HasUsableBounds(
            current.BoundingRectangle))
      {
        return false;
      }

      int[] runtimeId = container.GetRuntimeId();
      return _currentRuntimeId.Length == 0 ||
        runtimeId.Length == 0 ||
        runtimeId.SequenceEqual(_currentRuntimeId);
    }
    catch (ElementNotAvailableException)
    {
      return false;
    }
    catch (InvalidOperationException)
    {
      return false;
    }
  }

  /// <summary>
  /// Reacquires a replaced container using its stable properties and relative
  /// window location.
  /// </summary>
  /// <returns>The best matching current element.</returns>
  private AutomationElement ReacquireContainer()
  {
    AutomationElement root = AutomationElement.FromHandle(WindowHandle);
    Condition condition = BuildReacquireCondition();
    AutomationElementCollection matches;
    try
    {
      matches = root.FindAll(TreeScope.Descendants, condition);
    }
    catch (ElementNotAvailableException exception)
    {
      throw new InvalidOperationException(
        "The transcript container could not be reacquired.",
        exception);
    }

    DiagnosticLog.Write("target.reacquire_search", new
    {
      mode = "stable properties",
      matchCount = matches.Count
    });
    if (matches.Count == 0 &&
        !ReferenceEquals(condition, Condition.TrueCondition))
    {
      matches = root.FindAll(
        TreeScope.Descendants,
        Condition.TrueCondition);
      DiagnosticLog.Write("target.reacquire_search", new
      {
        mode = "broad fallback",
        matchCount = matches.Count
      });
    }

    System.Windows.Rect rootBounds = root.Current.BoundingRectangle;
    var candidates = new List<ReacquireCandidate>();
    foreach (AutomationElement match in matches)
    {
      AutomationElementSnapshot snapshot =
        AutomationTreeInspector.ReadSnapshot(match);
      if (!snapshot.Available || !snapshot.HasUsableBounds)
      {
        continue;
      }

      double distance = CalculateRelativeDistance(
        snapshot.RawBounds,
        rootBounds);
      int score = CalculateReacquireScore(snapshot, distance);
      var candidate = new ReacquireCandidate(match, snapshot, distance, score);
      candidates.Add(candidate);
      DiagnosticLog.Write("target.reacquire_candidate", new
      {
        candidate.Score,
        candidate.RelativeDistance,
        candidate.Snapshot
      });
    }

    ReacquireCandidate? selected = candidates
      .Where(candidate =>
        candidate.RelativeDistance <= MaximumReacquireDistance)
      .OrderByDescending(candidate => candidate.Score)
      .ThenBy(candidate => candidate.RelativeDistance)
      .FirstOrDefault();
    if (selected is null)
    {
      throw new InvalidOperationException(
        "The retained transcript container disappeared and no matching " +
        "replacement was found. Reselect the transcript text and send the " +
        "diagnostic log.");
    }

    DiagnosticLog.Write("target.container_reacquired", new
    {
      selected.Score,
      selected.RelativeDistance,
      selected.Snapshot
    });
    return selected.Element;
  }

  /// <summary>
  /// Builds the narrowest reliable UI Automation search condition.
  /// </summary>
  private Condition BuildReacquireCondition()
  {
    var conditions = new List<Condition>();
    if (_selectedSnapshot.AutomationId.Length != 0)
    {
      conditions.Add(new PropertyCondition(
        AutomationElement.AutomationIdProperty,
        _selectedSnapshot.AutomationId));
    }

    if (_selectedSnapshot.ClassName.Length != 0)
    {
      conditions.Add(new PropertyCondition(
        AutomationElement.ClassNameProperty,
        _selectedSnapshot.ClassName));
    }

    if (_selectedSnapshot.FrameworkId.Length != 0)
    {
      conditions.Add(new PropertyCondition(
        AutomationElement.FrameworkIdProperty,
        _selectedSnapshot.FrameworkId));
    }

    return conditions.Count switch
    {
      0 => Condition.TrueCondition,
      1 => conditions[0],
      _ => new AndCondition(conditions.ToArray())
    };
  }

  /// <summary>
  /// Scores a potential replacement against the original container.
  /// </summary>
  private int CalculateReacquireScore(
    AutomationElementSnapshot snapshot,
    double relativeDistance)
  {
    int score = 0;
    if (snapshot.RuntimeId.SequenceEqual(_selectedSnapshot.RuntimeId))
    {
      score += 1000;
    }

    if (snapshot.ControlType.Equals(
          _selectedSnapshot.ControlType,
          StringComparison.Ordinal))
    {
      score += 200;
    }

    if (snapshot.AutomationId.Equals(
          _selectedSnapshot.AutomationId,
          StringComparison.Ordinal))
    {
      score += 150;
    }

    if (snapshot.ClassName.Equals(
          _selectedSnapshot.ClassName,
          StringComparison.Ordinal))
    {
      score += 100;
    }

    if (snapshot.FrameworkId.Equals(
          _selectedSnapshot.FrameworkId,
          StringComparison.Ordinal))
    {
      score += 75;
    }

    if (snapshot.Name.Equals(
          _selectedSnapshot.Name,
          StringComparison.Ordinal))
    {
      score += 40;
    }

    score -= checked((int)Math.Round(relativeDistance * 100.0));
    return score;
  }

  /// <summary>
  /// Calculates normalized position and size distance from the selected bounds.
  /// </summary>
  private double CalculateRelativeDistance(
    System.Windows.Rect bounds,
    System.Windows.Rect rootBounds)
  {
    if (!AutomationTreeInspector.HasUsableBounds(bounds) ||
        !AutomationTreeInspector.HasUsableBounds(rootBounds))
    {
      return double.PositiveInfinity;
    }

    double left = (bounds.Left - rootBounds.Left) / rootBounds.Width;
    double top = (bounds.Top - rootBounds.Top) / rootBounds.Height;
    double width = bounds.Width / rootBounds.Width;
    double height = bounds.Height / rootBounds.Height;

    return Math.Abs(left - _leftRatio) +
      Math.Abs(top - _topRatio) +
      Math.Abs(width - _widthRatio) +
      Math.Abs(height - _heightRatio);
  }

  /// <summary>
  /// Limits one target-description field.
  /// </summary>
  private static string Abbreviate(string text, int maximumLength)
  {
    return text.Length <= maximumLength
      ? text
      : text[..maximumLength] + "…";
  }

  /// <summary>
  /// Stores one possible replacement container.
  /// </summary>
  private sealed record ReacquireCandidate(
    AutomationElement Element,
    AutomationElementSnapshot Snapshot,
    double RelativeDistance,
    int Score);
}
