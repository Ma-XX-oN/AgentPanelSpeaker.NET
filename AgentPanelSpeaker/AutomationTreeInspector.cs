using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Traverses the UI Automation ancestor chain beneath a selected screen point
/// and chooses the smallest ancestor that behaves like a transcript container.
/// </summary>
internal static class AutomationTreeInspector
{
  private const int MaximumAncestorDepth = 64;
  private const int MaximumImmediateChildren = 32;
  private const int MaximumLoggedChildren = 16;
  private const int MaximumDirectTextSamples = 16;
  private const int MaximumSelectedTextSamples = 48;
  private const double MaximumCandidateWindowCoverage = 0.94;
  private const double MaximumCandidateRootHeightCoverage = 1.15;

  private static readonly string[] TranscriptClassMarkers =
  {
    "thread-scroll-container",
    "thread-content",
    "conversation-scroll",
    "transcript-scroll",
    "chat-scroll"
  };

  /// <summary>
  /// Finds and diagnoses the transcript container beneath a screen point.
  /// </summary>
  /// <param name="point">Physical screen coordinate.</param>
  /// <returns>
  /// The selected container and its owning window information.
  /// </returns>
  public static TranscriptContainerSelection SelectContainer(
    System.Drawing.Point point)
  {
    var timer = Stopwatch.StartNew();
    DiagnosticLog.Write("target.tree_traversal_started", new
    {
      point = PointToString(point)
    });

    IntPtr childWindow = NativeMethods.WindowFromPoint(
      new NativeMethods.NativePoint(point.X, point.Y));
    if (childWindow == IntPtr.Zero)
    {
      throw new InvalidOperationException(
        "No window was found beneath the pointer.");
    }

    IntPtr rootWindow = NativeMethods.GetAncestor(
      childWindow,
      NativeMethods.GetAncestorRoot);
    if (rootWindow == IntPtr.Zero)
    {
      rootWindow = childWindow;
    }

    NativeMethods.GetWindowThreadProcessId(rootWindow, out uint processId);
    if (processId == 0 || processId > int.MaxValue)
    {
      throw new InvalidOperationException(
        "The window beneath the pointer has no usable process identifier.");
    }

    AutomationElement root = AutomationElement.FromHandle(rootWindow);
    AutomationElement hovered = AutomationElement.FromPoint(
      new System.Windows.Point(point.X, point.Y));
    AutomationElementSnapshot rootSnapshot = ReadSnapshot(root);
    AutomationElementSnapshot hoveredSnapshot = ReadSnapshot(hovered);

    DiagnosticLog.Write("target.hovered_element", new
    {
      rootWindow = FormatHandle(rootWindow),
      childWindow = FormatHandle(childWindow),
      processId,
      root = rootSnapshot,
      hovered = hoveredSnapshot
    });

    IReadOnlyList<AutomationElement> chain = BuildAncestorChain(
      hovered,
      root);
    var candidates = new List<ContainerCandidate>(chain.Count);
    for (int depth = 0; depth < chain.Count; ++depth)
    {
      var levelTimer = Stopwatch.StartNew();
      ContainerCandidate candidate = AnalyzeCandidateShallow(
        chain[depth],
        depth,
        rootSnapshot.RawBounds,
        hoveredSnapshot.RawBounds,
        point,
        ElementsEqual(chain[depth], root));
      candidates.Add(candidate);

      DiagnosticLog.Write("target.tree_level", new
      {
        candidate.Depth,
        candidate.Tier,
        candidate.Score,
        candidate.SelectionReason,
        levelElapsedMilliseconds = levelTimer.ElapsedMilliseconds,
        totalElapsedMilliseconds = timer.ElapsedMilliseconds,
        rootWindow = FormatHandle(rootWindow),
        candidate.Snapshot,
        candidate.ImmediateChildCount,
        candidate.DirectTextCount,
        candidate.HasScrollPattern,
        candidate.ClassLooksLikeTranscript,
        candidate.ContainsEdit,
        candidate.ContainsTab,
        candidate.ContainsPointer,
        candidate.WindowCoverage,
        candidate.RootHeightCoverage,
        candidate.HeightMultiple,
        candidate.OversizedHeight,
        candidate.DirectTextSamples,
        candidate.ImmediateChildren
      });
    }

    ContainerCandidate selected = ChooseCandidate(candidates);
    DiagnosticLog.Write("target.tree_path", new
    {
      rootWindow = FormatHandle(rootWindow),
      elapsedMilliseconds = timer.ElapsedMilliseconds,
      levels = candidates.Select(candidate => new
      {
        candidate.Depth,
        candidate.Tier,
        candidate.Score,
        candidate.SelectionReason,
        runtimeId = candidate.Snapshot.RuntimeIdText,
        nativeWindowHandle = candidate.Snapshot.NativeWindowHandleHex,
        candidate.Snapshot.ControlType,
        candidate.Snapshot.AutomationId,
        candidate.Snapshot.ClassName,
        ownText = Abbreviate(candidate.Snapshot.Name, 320),
        candidate.Snapshot.Bounds,
        candidate.HasScrollPattern,
        candidate.ClassLooksLikeTranscript,
        candidate.DirectTextSamples
      }).ToArray()
    });

    DiagnosticLog.Write("target.tree_traversal_selected", new
    {
      elapsedMilliseconds = timer.ElapsedMilliseconds,
      ancestorCount = chain.Count,
      selected.Depth,
      selected.Tier,
      selected.Score,
      selected.SelectionReason,
      selected.Snapshot,
      selected.ImmediateChildCount,
      selected.DirectTextCount,
      selected.HasScrollPattern,
      selected.ClassLooksLikeTranscript,
      selected.ContainsEdit,
      selected.ContainsTab,
      selected.WindowCoverage,
      selected.RootHeightCoverage,
      selected.HeightMultiple,
      selected.OversizedHeight,
      selected.DirectTextSamples,
      detailedDiagnosticsQueued = true
    });

    QueueSelectedSubtreeDiagnostics(selected);

    return new TranscriptContainerSelection(
      rootWindow,
      checked((int)processId),
      point,
      selected.Element,
      selected.Snapshot,
      selected.SelectionReason,
      rootSnapshot.RawBounds);
  }

  /// <summary>
  /// Reads a safe diagnostic snapshot of a UI Automation element.
  /// </summary>
  /// <param name="element">Element to inspect.</param>
  /// <returns>The captured properties.</returns>
  public static AutomationElementSnapshot ReadSnapshot(
    AutomationElement element)
  {
    return ReadSnapshot(element, includePatterns: true);
  }

  /// <summary>
  /// Builds the raw-view chain from the hovered element toward the window root.
  /// </summary>
  /// <param name="hovered">Element beneath the pointer.</param>
  /// <param name="root">Owning top-level window element.</param>
  /// <returns>Leaf-first ancestor chain.</returns>
  private static IReadOnlyList<AutomationElement> BuildAncestorChain(
    AutomationElement hovered,
    AutomationElement root)
  {
    var result = new List<AutomationElement>();
    AutomationElement? current = hovered;

    for (int depth = 0;
         current is not null && depth < MaximumAncestorDepth;
         ++depth)
    {
      if (result.Count != 0 && ElementsEqual(result[^1], current))
      {
        DiagnosticLog.Write("target.tree_cycle", new { depth });
        break;
      }

      result.Add(current);
      if (ElementsEqual(current, root))
      {
        break;
      }

      try
      {
        current = TreeWalker.RawViewWalker.GetParent(current);
      }
      catch (Exception exception) when (
        exception is ElementNotAvailableException or
        InvalidOperationException or
        COMException)
      {
        DiagnosticLog.Write("target.tree_parent_failed", new
        {
          depth,
          exception = exception.ToString()
        });
        break;
      }
    }

    if (!result.Any(element => ElementsEqual(element, root)))
    {
      result.Add(root);
      DiagnosticLog.Write("target.tree_root_appended", new
      {
        reason = "Raw view traversal did not reach the HWND root."
      });
    }

    return result;
  }

  /// <summary>
  /// Collects only cheap properties while walking the ancestor chain.
  /// </summary>
  private static ContainerCandidate AnalyzeCandidateShallow(
    AutomationElement element,
    int depth,
    System.Windows.Rect rootBounds,
    System.Windows.Rect hoveredBounds,
    System.Drawing.Point point,
    bool isRoot)
  {
    AutomationElementSnapshot snapshot = ReadSnapshot(element);
    double windowCoverage = CalculateCoverage(
      snapshot.RawBounds,
      rootBounds);
    double rootHeightCoverage = CalculateHeightCoverage(
      snapshot.RawBounds,
      rootBounds);
    double heightMultiple = CalculateHeightMultiple(
      snapshot.RawBounds,
      hoveredBounds);
    bool rootLike = isRoot ||
      snapshot.ControlType.Equals(
        ControlType.Window.ProgrammaticName,
        StringComparison.Ordinal) ||
      windowCoverage >= MaximumCandidateWindowCoverage;
    bool oversizedHeight =
      rootHeightCoverage > MaximumCandidateRootHeightCoverage;
    bool hasScrollPattern = snapshot.Patterns.Contains(
      ScrollPattern.Pattern.ProgrammaticName,
      StringComparer.Ordinal);
    bool classLooksLikeTranscript = LooksLikeTranscriptClass(
      snapshot.ClassName);

    IReadOnlyList<AutomationElement> children = ReadImmediateChildren(element);
    var childSnapshots = new List<AutomationElementSnapshot>(
      Math.Min(children.Count, MaximumLoggedChildren));
    var directTextSamples = new List<string>(MaximumDirectTextSamples);
    AddTextSample(directTextSamples, snapshot.Name);
    bool containsEdit = snapshot.ControlType.Equals(
      ControlType.Edit.ProgrammaticName,
      StringComparison.Ordinal);
    bool containsTab = snapshot.ControlType.Equals(
      ControlType.Tab.ProgrammaticName,
      StringComparison.Ordinal);

    for (int index = 0;
         index < children.Count && index < MaximumLoggedChildren;
         ++index)
    {
      AutomationElementSnapshot child = ReadSnapshot(
        children[index],
        includePatterns: false);
      childSnapshots.Add(child);
      AddTextSample(directTextSamples, child.Name);
      containsEdit |= child.ControlType.Equals(
        ControlType.Edit.ProgrammaticName,
        StringComparison.Ordinal);
      containsTab |= child.ControlType.Equals(
        ControlType.Tab.ProgrammaticName,
        StringComparison.Ordinal);
    }

    int tier = GetQualificationTier(
      snapshot,
      depth,
      rootLike,
      oversizedHeight,
      rootHeightCoverage,
      heightMultiple,
      hasScrollPattern,
      classLooksLikeTranscript,
      directTextSamples.Count,
      children.Count,
      containsEdit,
      containsTab);
    int score = CalculateScore(
      depth,
      windowCoverage,
      rootHeightCoverage,
      hasScrollPattern,
      classLooksLikeTranscript,
      directTextSamples.Count,
      children.Count,
      containsEdit,
      containsTab);
    string reason = ExplainCandidate(
      tier,
      rootLike,
      oversizedHeight,
      hasScrollPattern,
      classLooksLikeTranscript,
      directTextSamples.Count,
      children.Count);

    return new ContainerCandidate(
      element,
      depth,
      tier,
      score,
      reason,
      snapshot,
      children.Count,
      directTextSamples.Count,
      hasScrollPattern,
      classLooksLikeTranscript,
      containsEdit,
      containsTab,
      snapshot.Contains(point),
      windowCoverage,
      rootHeightCoverage,
      heightMultiple,
      oversizedHeight,
      childSnapshots,
      directTextSamples);
  }

  /// <summary>
  /// Chooses the nearest qualified transcript ancestor.
  /// </summary>
  private static ContainerCandidate ChooseCandidate(
    IReadOnlyList<ContainerCandidate> candidates)
  {
    ContainerCandidate? selected = candidates
      .Where(candidate => candidate.Tier < int.MaxValue)
      .OrderBy(candidate => candidate.Tier)
      .ThenBy(candidate => candidate.Depth)
      .ThenByDescending(candidate => candidate.Score)
      .FirstOrDefault();
    if (selected is not null)
    {
      return selected with
      {
        SelectionReason = $"automatic tier {selected.Tier}: " +
          selected.SelectionReason
      };
    }

    selected = candidates
      .Where(candidate =>
        candidate.Depth > 0 &&
        candidate.Snapshot.Available &&
        candidate.Snapshot.HasUsableBounds &&
        !candidate.OversizedHeight &&
        candidate.WindowCoverage < MaximumCandidateWindowCoverage &&
        candidate.HasScrollPattern)
      .OrderBy(candidate => candidate.Depth)
      .ThenByDescending(candidate => candidate.Score)
      .FirstOrDefault();
    if (selected is not null)
    {
      return selected with
      {
        SelectionReason = "fallback nearest bounded scroll ancestor: " +
          selected.SelectionReason
      };
    }

    selected = candidates
      .Where(candidate =>
        candidate.Depth > 0 &&
        candidate.Snapshot.Available &&
        candidate.Snapshot.HasUsableBounds &&
        !candidate.OversizedHeight &&
        candidate.WindowCoverage < MaximumCandidateWindowCoverage &&
        candidate.HeightMultiple >= 4.0)
      .OrderBy(candidate => candidate.Depth)
      .FirstOrDefault();
    if (selected is not null)
    {
      return selected with
      {
        SelectionReason = "fallback nearest bounded ancestor: " +
          selected.SelectionReason
      };
    }

    throw new InvalidOperationException(
      "No accessible transcript parent could be found beneath the pointer.");
  }

  /// <summary>
  /// Logs an expensive descendant scan only for the selected container.
  /// </summary>
  private static void QueueSelectedSubtreeDiagnostics(
    ContainerCandidate selected)
  {
    _ = Task.Run(() =>
    {
      var timer = Stopwatch.StartNew();
      try
      {
        ReadDescendantMetrics(
          selected.Element,
          out int textElementCount,
          out int narrationTextCount,
          out int verticalTextGroups,
          out string[] textSamples,
          out int textProviderCount,
          out bool containsEdit,
          out bool containsTab);
        DiagnosticLog.Write("target.selected_subtree_diagnostics", new
        {
          elapsedMilliseconds = timer.ElapsedMilliseconds,
          selected.Depth,
          selected.Snapshot,
          textElementCount,
          narrationTextCount,
          verticalTextGroups,
          textProviderCount,
          containsEdit,
          containsTab,
          textSamples
        });
      }
      catch (Exception exception) when (
        exception is ElementNotAvailableException or
        InvalidOperationException or
        COMException)
      {
        DiagnosticLog.Write("target.selected_subtree_diagnostics_failed", new
        {
          elapsedMilliseconds = timer.ElapsedMilliseconds,
          selected.Depth,
          selected.Snapshot,
          exception = exception.ToString()
        });
      }
    });
  }

  /// <summary>
  /// Returns raw-view immediate children without scanning their descendants.
  /// </summary>
  private static IReadOnlyList<AutomationElement> ReadImmediateChildren(
    AutomationElement element)
  {
    var result = new List<AutomationElement>();
    try
    {
      AutomationElement? child = TreeWalker.RawViewWalker.GetFirstChild(
        element);
      while (child is not null && result.Count < MaximumImmediateChildren)
      {
        result.Add(child);
        child = TreeWalker.RawViewWalker.GetNextSibling(child);
      }
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      InvalidOperationException or
      COMException)
    {
    }

    return result;
  }

  /// <summary>
  /// Reads text and layout metrics for the selected subtree only.
  /// </summary>
  private static void ReadDescendantMetrics(
    AutomationElement element,
    out int textElementCount,
    out int narrationTextCount,
    out int verticalTextGroups,
    out string[] textSamples,
    out int textProviderCount,
    out bool containsEdit,
    out bool containsTab)
  {
    textElementCount = 0;
    narrationTextCount = 0;
    verticalTextGroups = 0;
    textProviderCount = 0;
    containsEdit = false;
    containsTab = false;
    var samples = new List<string>(MaximumSelectedTextSamples);
    var tops = new List<(double Top, double Height)>();

    try
    {
      var condition = new OrCondition(
        new PropertyCondition(
          AutomationElement.ControlTypeProperty,
          ControlType.Text),
        new PropertyCondition(
          AutomationElement.IsTextPatternAvailableProperty,
          true),
        new PropertyCondition(
          AutomationElement.ControlTypeProperty,
          ControlType.Edit),
        new PropertyCondition(
          AutomationElement.ControlTypeProperty,
          ControlType.Tab));
      AutomationElementCollection descendants = element.FindAll(
        TreeScope.Element | TreeScope.Descendants,
        condition);

      foreach (AutomationElement descendant in descendants)
      {
        AutomationElementSnapshot snapshot = ReadSnapshot(
          descendant,
          includePatterns: false);
        if (!snapshot.Available)
        {
          continue;
        }

        containsEdit |= snapshot.ControlType.Equals(
          ControlType.Edit.ProgrammaticName,
          StringComparison.Ordinal);
        containsTab |= snapshot.ControlType.Equals(
          ControlType.Tab.ProgrammaticName,
          StringComparison.Ordinal);

        bool hasTextPattern = HasPattern(
          descendant,
          TextPattern.Pattern);
        if (hasTextPattern)
        {
          ++textProviderCount;
        }

        if (snapshot.Name.Length == 0 ||
            (!snapshot.ControlType.Equals(
               ControlType.Text.ProgrammaticName,
               StringComparison.Ordinal) &&
             !hasTextPattern))
        {
          continue;
        }

        ++textElementCount;
        if (LooksLikeNarration(snapshot.Name))
        {
          ++narrationTextCount;
        }

        if (samples.Count < MaximumSelectedTextSamples)
        {
          samples.Add(Abbreviate(snapshot.Name, 240));
        }

        if (snapshot.HasUsableBounds)
        {
          tops.Add((snapshot.RawBounds.Top, snapshot.RawBounds.Height));
        }
      }
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      InvalidOperationException or
      COMException)
    {
    }

    tops.Sort((left, right) => left.Top.CompareTo(right.Top));
    double lastTop = double.NaN;
    double lastHeight = 0.0;
    foreach ((double top, double height) in tops)
    {
      double tolerance = Math.Max(6.0, Math.Max(lastHeight, height) * 0.65);
      if (double.IsNaN(lastTop) || Math.Abs(top - lastTop) > tolerance)
      {
        ++verticalTextGroups;
        lastTop = top;
        lastHeight = height;
      }
    }

    textSamples = samples.ToArray();
  }

  /// <summary>
  /// Assigns a qualification tier using only cheap ancestor properties.
  /// </summary>
  private static int GetQualificationTier(
    AutomationElementSnapshot snapshot,
    int depth,
    bool rootLike,
    bool oversizedHeight,
    double rootHeightCoverage,
    double heightMultiple,
    bool hasScrollPattern,
    bool classLooksLikeTranscript,
    int directTextCount,
    int immediateChildCount,
    bool containsEdit,
    bool containsTab)
  {
    if (depth == 0 ||
        rootLike ||
        !snapshot.Available ||
        !snapshot.HasUsableBounds ||
        oversizedHeight)
    {
      return int.MaxValue;
    }

    if (classLooksLikeTranscript &&
        hasScrollPattern &&
        rootHeightCoverage >= 0.20)
    {
      return 0;
    }

    if (hasScrollPattern &&
        rootHeightCoverage >= 0.20 &&
        heightMultiple >= 4.0)
    {
      return 1;
    }

    if (classLooksLikeTranscript &&
        rootHeightCoverage >= 0.20 &&
        heightMultiple >= 4.0)
    {
      return 2;
    }

    if (!containsEdit &&
        !containsTab &&
        immediateChildCount >= 2 &&
        directTextCount >= 2 &&
        rootHeightCoverage >= 0.20 &&
        heightMultiple >= 4.0)
    {
      return 3;
    }

    return int.MaxValue;
  }

  /// <summary>
  /// Scores a shallow candidate for diagnostics and fallback ordering.
  /// </summary>
  private static int CalculateScore(
    int depth,
    double windowCoverage,
    double rootHeightCoverage,
    bool hasScrollPattern,
    bool classLooksLikeTranscript,
    int directTextCount,
    int immediateChildCount,
    bool containsEdit,
    bool containsTab)
  {
    int score = 0;
    score += classLooksLikeTranscript ? 300 : 0;
    score += hasScrollPattern ? 160 : 0;
    score += Math.Min(directTextCount, 12) * 10;
    score += Math.Min(immediateChildCount, 12) * 4;
    score += rootHeightCoverage is >= 0.20 and <= 1.0 ? 40 : 0;
    score += windowCoverage is >= 0.05 and <= 0.80 ? 20 : 0;
    score -= containsEdit ? 35 : 0;
    score -= containsTab ? 45 : 0;
    score -= depth * 2;
    return score;
  }

  /// <summary>
  /// Produces a compact explanation for one shallow candidate.
  /// </summary>
  private static string ExplainCandidate(
    int tier,
    bool rootLike,
    bool oversizedHeight,
    bool hasScrollPattern,
    bool classLooksLikeTranscript,
    int directTextCount,
    int immediateChildCount)
  {
    if (rootLike)
    {
      return "rejected as the top-level or whole-window element";
    }

    if (oversizedHeight)
    {
      return "rejected because the element is taller than the owning window";
    }

    return tier switch
    {
      0 => "known transcript scroll-container class",
      1 => "nearest bounded scrollable ancestor",
      2 => "known transcript-container class without ScrollPattern",
      3 => "bounded ancestor with several direct text-bearing children",
      _ => $"not qualified: scroll={hasScrollPattern}; " +
        $"transcriptClass={classLooksLikeTranscript}; " +
        $"directText={directTextCount}; children={immediateChildCount}"
    };
  }

  /// <summary>
  /// Reads one element snapshot, optionally including supported patterns.
  /// </summary>
  private static AutomationElementSnapshot ReadSnapshot(
    AutomationElement element,
    bool includePatterns)
  {
    ArgumentNullException.ThrowIfNull(element);

    try
    {
      AutomationElement.AutomationElementInformation current =
        element.Current;
      string[] patterns = includePatterns
        ? ReadSupportedPatterns(element)
        : Array.Empty<string>();

      int[] runtimeId;
      try
      {
        runtimeId = element.GetRuntimeId();
      }
      catch (Exception exception) when (
        exception is ElementNotAvailableException or
        InvalidOperationException or
        COMException)
      {
        runtimeId = Array.Empty<int>();
      }

      return new AutomationElementSnapshot(
        runtimeId,
        current.ControlType.ProgrammaticName,
        current.LocalizedControlType,
        Normalize(current.Name),
        current.AutomationId ?? string.Empty,
        current.ClassName ?? string.Empty,
        current.FrameworkId ?? string.Empty,
        current.BoundingRectangle,
        current.IsOffscreen,
        current.IsControlElement,
        current.IsContentElement,
        current.IsKeyboardFocusable,
        current.NativeWindowHandle,
        patterns);
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      InvalidOperationException or
      COMException)
    {
      return AutomationElementSnapshot.Unavailable;
    }
  }

  /// <summary>
  /// Reads supported patterns without allowing provider failures to escape.
  /// </summary>
  private static string[] ReadSupportedPatterns(AutomationElement element)
  {
    try
    {
      return element.GetSupportedPatterns()
        .Select(pattern => pattern.ProgrammaticName)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      InvalidOperationException or
      COMException)
    {
      return Array.Empty<string>();
    }
  }

  /// <summary>
  /// Determines whether an element supports one UI Automation pattern.
  /// </summary>
  private static bool HasPattern(
    AutomationElement element,
    AutomationPattern pattern)
  {
    try
    {
      return element.TryGetCurrentPattern(pattern, out _);
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      InvalidOperationException or
      COMException)
    {
      return false;
    }
  }

  /// <summary>
  /// Compares UI Automation runtime identifiers.
  /// </summary>
  public static bool ElementsEqual(
    AutomationElement left,
    AutomationElement right)
  {
    ArgumentNullException.ThrowIfNull(left);
    ArgumentNullException.ThrowIfNull(right);

    if (ReferenceEquals(left, right))
    {
      return true;
    }

    try
    {
      int[] leftId = left.GetRuntimeId();
      int[] rightId = right.GetRuntimeId();
      return leftId.Length != 0 &&
        rightId.Length != 0 &&
        leftId.SequenceEqual(rightId);
    }
    catch (Exception exception) when (
      exception is ElementNotAvailableException or
      InvalidOperationException or
      COMException)
    {
      return false;
    }
  }

  /// <summary>
  /// Calculates how much of the top-level window a candidate occupies.
  /// </summary>
  private static double CalculateCoverage(
    System.Windows.Rect candidate,
    System.Windows.Rect root)
  {
    if (!HasUsableBounds(candidate) || !HasUsableBounds(root))
    {
      return 0.0;
    }

    double rootArea = root.Width * root.Height;
    return rootArea <= 0.0
      ? 0.0
      : candidate.Width * candidate.Height / rootArea;
  }

  /// <summary>
  /// Calculates the fraction of the top-level window height occupied by a
  /// candidate.
  /// </summary>
  private static double CalculateHeightCoverage(
    System.Windows.Rect candidate,
    System.Windows.Rect root)
  {
    if (!HasUsableBounds(candidate) || !HasUsableBounds(root))
    {
      return 0.0;
    }

    return candidate.Height / root.Height;
  }

  /// <summary>
  /// Calculates candidate height relative to the hovered leaf.
  /// </summary>
  private static double CalculateHeightMultiple(
    System.Windows.Rect candidate,
    System.Windows.Rect hovered)
  {
    if (!HasUsableBounds(candidate) || !HasUsableBounds(hovered))
    {
      return 0.0;
    }

    return candidate.Height / hovered.Height;
  }

  /// <summary>
  /// Determines whether a rectangle contains usable screen coordinates.
  /// </summary>
  internal static bool HasUsableBounds(System.Windows.Rect bounds)
  {
    return !bounds.IsEmpty &&
      !double.IsNaN(bounds.X) &&
      !double.IsNaN(bounds.Y) &&
      !double.IsNaN(bounds.Width) &&
      !double.IsNaN(bounds.Height) &&
      !double.IsInfinity(bounds.X) &&
      !double.IsInfinity(bounds.Y) &&
      !double.IsInfinity(bounds.Width) &&
      !double.IsInfinity(bounds.Height) &&
      bounds.Width > 1.0 &&
      bounds.Height > 1.0;
  }

  /// <summary>
  /// Normalizes whitespace for diagnostics and selection heuristics.
  /// </summary>
  private static string Normalize(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return string.Empty;
    }

    return string.Join(
      " ",
      text.Split(
        (char[]?)null,
        StringSplitOptions.RemoveEmptyEntries |
          StringSplitOptions.TrimEntries));
  }

  /// <summary>
  /// Recognizes sentence-like agent narration for diagnostics only.
  /// </summary>
  private static bool LooksLikeNarration(string text)
  {
    string candidate = Normalize(text);
    if (candidate.Length < 12 || CountWords(candidate) < 4)
    {
      return false;
    }

    string[] ignoredPrefixes =
    {
      "Ran ",
      "Edited ",
      "Editing ",
      "Read ",
      "Wrote ",
      "Working for ",
      "Worked for ",
      "Thinking",
      "Considering",
      "Creating",
      "Baking",
      "Queue another message"
    };
    if (ignoredPrefixes.Any(prefix => candidate.StartsWith(
          prefix,
          StringComparison.OrdinalIgnoreCase)))
    {
      return false;
    }

    return candidate.IndexOfAny(new[] { '.', '?', '!' }) >= 0 ||
      candidate.StartsWith("I ", StringComparison.OrdinalIgnoreCase) ||
      candidate.StartsWith("I’m ", StringComparison.OrdinalIgnoreCase) ||
      candidate.StartsWith("I'm ", StringComparison.OrdinalIgnoreCase) ||
      candidate.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ||
      candidate.StartsWith("This ", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Recognizes stable class markers used by transcript scroll containers.
  /// </summary>
  private static bool LooksLikeTranscriptClass(string className)
  {
    return TranscriptClassMarkers.Any(marker => className.Contains(
      marker,
      StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>
  /// Adds one normalized, abbreviated text sample if it is not duplicated.
  /// </summary>
  private static void AddTextSample(List<string> samples, string text)
  {
    string normalized = Normalize(text);
    if (normalized.Length == 0 ||
        samples.Count >= MaximumDirectTextSamples)
    {
      return;
    }

    string sample = Abbreviate(normalized, 320);
    if (!samples.Contains(sample, StringComparer.Ordinal))
    {
      samples.Add(sample);
    }
  }

  /// <summary>
  /// Counts word starts in text.
  /// </summary>
  private static int CountWords(string text)
  {
    int count = 0;
    bool inWord = false;
    foreach (char character in text)
    {
      bool isWord = char.IsLetterOrDigit(character);
      if (isWord && !inWord)
      {
        ++count;
      }

      inWord = isWord;
    }

    return count;
  }

  /// <summary>
  /// Limits one logged text sample.
  /// </summary>
  private static string Abbreviate(string text, int maximumLength)
  {
    return text.Length <= maximumLength
      ? text
      : text[..maximumLength] + "…";
  }

  /// <summary>
  /// Formats a physical screen point.
  /// </summary>
  private static string PointToString(System.Drawing.Point point)
  {
    return $"{point.X},{point.Y}";
  }

  /// <summary>
  /// Formats a native window handle for diagnostics.
  /// </summary>
  private static string FormatHandle(IntPtr handle)
  {
    return $"0x{handle.ToInt64():X}";
  }

  /// <summary>
  /// Stores all cheap metrics for one ancestor candidate.
  /// </summary>
  private sealed record ContainerCandidate(
    [property: JsonIgnore] AutomationElement Element,
    int Depth,
    int Tier,
    int Score,
    string SelectionReason,
    AutomationElementSnapshot Snapshot,
    int ImmediateChildCount,
    int DirectTextCount,
    bool HasScrollPattern,
    bool ClassLooksLikeTranscript,
    bool ContainsEdit,
    bool ContainsTab,
    bool ContainsPointer,
    double WindowCoverage,
    double RootHeightCoverage,
    double HeightMultiple,
    bool OversizedHeight,
    IReadOnlyList<AutomationElementSnapshot> ImmediateChildren,
    IReadOnlyList<string> DirectTextSamples);
}

/// <summary>
/// Stores the selected transcript container and window metadata.
/// </summary>
internal sealed record TranscriptContainerSelection(
  IntPtr WindowHandle,
  int ProcessId,
  System.Drawing.Point SelectionPoint,
  AutomationElement Container,
  AutomationElementSnapshot Snapshot,
  string SelectionReason,
  System.Windows.Rect RootBounds);

/// <summary>
/// Stores safe UI Automation properties for logging and reacquisition.
/// </summary>
internal sealed class AutomationElementSnapshot
{
  /// <summary>
  /// Initializes one element snapshot.
  /// </summary>
  public AutomationElementSnapshot(
    int[] runtimeId,
    string controlType,
    string localizedControlType,
    string name,
    string automationId,
    string className,
    string frameworkId,
    System.Windows.Rect bounds,
    bool isOffscreen,
    bool isControlElement,
    bool isContentElement,
    bool isKeyboardFocusable,
    int nativeWindowHandle,
    string[] patterns)
  {
    RuntimeId = runtimeId;
    ControlType = controlType;
    LocalizedControlType = localizedControlType;
    Name = name;
    AutomationId = automationId;
    ClassName = className;
    FrameworkId = frameworkId;
    RawBounds = bounds;
    Bounds = BoundsToString(bounds);
    IsOffscreen = isOffscreen;
    IsControlElement = isControlElement;
    IsContentElement = isContentElement;
    IsKeyboardFocusable = isKeyboardFocusable;
    NativeWindowHandle = nativeWindowHandle;
    Patterns = patterns;
    Available = true;
  }

  private AutomationElementSnapshot()
  {
    RuntimeId = Array.Empty<int>();
    ControlType = "unavailable";
    LocalizedControlType = string.Empty;
    Name = string.Empty;
    AutomationId = string.Empty;
    ClassName = string.Empty;
    FrameworkId = string.Empty;
    RawBounds = System.Windows.Rect.Empty;
    Bounds = "empty";
    Patterns = Array.Empty<string>();
  }

  /// <summary>
  /// Gets an unavailable snapshot.
  /// </summary>
  public static AutomationElementSnapshot Unavailable { get; } = new();

  public int[] RuntimeId { get; }
  public string RuntimeIdText => RuntimeId.Length == 0
    ? string.Empty
    : string.Join(".", RuntimeId);
  public string ControlType { get; }
  public string LocalizedControlType { get; }
  public string Name { get; }
  public string AutomationId { get; }
  public string ClassName { get; }
  public string FrameworkId { get; }
  public string Bounds { get; }
  public bool IsOffscreen { get; }
  public bool IsControlElement { get; }
  public bool IsContentElement { get; }
  public bool IsKeyboardFocusable { get; }
  public int NativeWindowHandle { get; }
  public string NativeWindowHandleHex =>
    $"0x{unchecked((uint)NativeWindowHandle):X}";
  public string[] Patterns { get; }
  public bool Available { get; }

  internal System.Windows.Rect RawBounds { get; }

  /// <summary>
  /// Gets whether the snapshot contains usable screen bounds.
  /// </summary>
  public bool HasUsableBounds =>
    AutomationTreeInspector.HasUsableBounds(RawBounds);

  /// <summary>
  /// Tests whether the element bounds contain a physical point.
  /// </summary>
  public bool Contains(System.Drawing.Point point)
  {
    return HasUsableBounds &&
      RawBounds.Contains(new System.Windows.Point(point.X, point.Y));
  }

  /// <summary>
  /// Formats a UI Automation rectangle.
  /// </summary>
  private static string BoundsToString(System.Windows.Rect bounds)
  {
    return AutomationTreeInspector.HasUsableBounds(bounds)
      ? $"{bounds.Left:R},{bounds.Top:R} " +
        $"{bounds.Width:R}x{bounds.Height:R}"
      : "empty";
  }
}
