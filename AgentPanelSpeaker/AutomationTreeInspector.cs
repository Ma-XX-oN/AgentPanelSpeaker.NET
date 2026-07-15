using System.Diagnostics;
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
  private const int MaximumImmediateChildren = 128;
  private const int MaximumLoggedChildren = 24;
  private const int MaximumTextSamples = 32;
  private const double MaximumCandidateWindowCoverage = 0.94;

  /// <summary>
  /// Finds and diagnoses the transcript container beneath a screen point.
  /// </summary>
  /// <param name="point">Physical screen coordinate.</param>
  /// <returns>The selected container and its owning window information.</returns>
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
      rootWindow = $"0x{rootWindow.ToInt64():X}",
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
      ContainerCandidate candidate = AnalyzeCandidate(
        chain[depth],
        depth,
        rootSnapshot.RawBounds,
        hoveredSnapshot.RawBounds,
        point,
        ElementsEqual(chain[depth], root));
      candidates.Add(candidate);
      DiagnosticLog.Write("target.tree_node", candidate);
    }

    ContainerCandidate selected = ChooseCandidate(candidates);
    DiagnosticLog.Write("target.tree_traversal_selected", new
    {
      elapsedMilliseconds = timer.ElapsedMilliseconds,
      ancestorCount = chain.Count,
      selected.Depth,
      selected.Tier,
      selected.Score,
      selected.SelectionReason,
      selected.Snapshot,
      selected.TextElementCount,
      selected.NarrationTextCount,
      selected.TextBearingChildCount,
      selected.VerticalTextGroupCount,
      selected.HasScrollPattern,
      selected.ContainsEdit,
      selected.ContainsTab,
      selected.WindowCoverage,
      selected.RootHeightCoverage,
      selected.HeightMultiple
    });

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
    ArgumentNullException.ThrowIfNull(element);

    try
    {
      AutomationElement.AutomationElementInformation current =
        element.Current;
      string[] patterns;
      try
      {
        patterns = element.GetSupportedPatterns()
          .Select(pattern => pattern.ProgrammaticName)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();
      }
      catch (InvalidOperationException)
      {
        patterns = Array.Empty<string>();
      }

      int[] runtimeId;
      try
      {
        runtimeId = element.GetRuntimeId();
      }
      catch (InvalidOperationException)
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
    catch (ElementNotAvailableException)
    {
      return AutomationElementSnapshot.Unavailable;
    }
    catch (InvalidOperationException)
    {
      return AutomationElementSnapshot.Unavailable;
    }
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
      catch (ElementNotAvailableException exception)
      {
        DiagnosticLog.Write("target.tree_parent_failed", new
        {
          depth,
          exception = exception.ToString()
        });
        break;
      }
      catch (InvalidOperationException exception)
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
  /// Collects selection metrics for one ancestor candidate.
  /// </summary>
  /// <param name="element">Candidate element.</param>
  /// <param name="depth">Leaf-first ancestor depth.</param>
  /// <param name="rootBounds">Top-level window bounds.</param>
  /// <param name="hoveredBounds">Hovered leaf bounds.</param>
  /// <param name="point">Selected screen point.</param>
  /// <param name="isRoot">Whether this is the top-level window root.</param>
  /// <returns>Candidate metrics and child samples.</returns>
  private static ContainerCandidate AnalyzeCandidate(
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

    IReadOnlyList<AutomationElement> children = ReadImmediateChildren(element);
    var childSnapshots = new List<AutomationElementSnapshot>(
      Math.Min(children.Count, MaximumLoggedChildren));
    int textBearingChildren = 0;
    for (int index = 0; index < children.Count; ++index)
    {
      AutomationElement child = children[index];
      if (index < MaximumLoggedChildren)
      {
        childSnapshots.Add(ReadSnapshot(child));
      }

      if (ElementContainsText(child))
      {
        ++textBearingChildren;
      }
    }

    int textElementCount = 0;
    int narrationTextCount = 0;
    int verticalTextGroups = 0;
    string[] textSamples = Array.Empty<string>();
    int textProviderCount = 0;
    bool containsEdit = false;
    bool containsTab = false;

    if (!rootLike && snapshot.Available)
    {
      ReadDescendantMetrics(
        element,
        out textElementCount,
        out narrationTextCount,
        out verticalTextGroups,
        out textSamples,
        out textProviderCount,
        out containsEdit,
        out containsTab);
    }

    bool hasScrollPattern = HasPattern(element, ScrollPattern.Pattern);
    int tier = GetQualificationTier(
      snapshot,
      depth,
      isRoot,
      windowCoverage,
      rootHeightCoverage,
      heightMultiple,
      textElementCount,
      narrationTextCount,
      textBearingChildren,
      verticalTextGroups,
      hasScrollPattern,
      containsEdit,
      containsTab);
    int score = CalculateScore(
      depth,
      windowCoverage,
      textElementCount,
      narrationTextCount,
      textBearingChildren,
      verticalTextGroups,
      textProviderCount,
      hasScrollPattern,
      containsEdit,
      containsTab);
    string reason = ExplainCandidate(
      tier,
      rootLike,
      hasScrollPattern,
      textBearingChildren,
      narrationTextCount,
      verticalTextGroups);

    return new ContainerCandidate(
      element,
      depth,
      tier,
      score,
      reason,
      snapshot,
      children.Count,
      textBearingChildren,
      textElementCount,
      narrationTextCount,
      verticalTextGroups,
      textProviderCount,
      hasScrollPattern,
      containsEdit,
      containsTab,
      snapshot.Contains(point),
      windowCoverage,
      rootHeightCoverage,
      heightMultiple,
      childSnapshots,
      textSamples);
  }

  /// <summary>
  /// Chooses the smallest qualified ancestor, preferring a scroll container.
  /// </summary>
  /// <param name="candidates">Leaf-first candidate chain.</param>
  /// <returns>The chosen transcript container.</returns>
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
        candidate.WindowCoverage < MaximumCandidateWindowCoverage &&
        (candidate.TextElementCount >= 2 ||
         candidate.HasScrollPattern))
      .OrderByDescending(candidate => candidate.Score)
      .ThenBy(candidate => candidate.Depth)
      .FirstOrDefault();
    if (selected is not null)
    {
      return selected with
      {
        SelectionReason = "fallback highest-scoring text ancestor: " +
          selected.SelectionReason
      };
    }

    selected = candidates.FirstOrDefault(candidate => candidate.Depth > 0);
    if (selected is not null)
    {
      return selected with
      {
        SelectionReason = "fallback immediate ancestor: " +
          selected.SelectionReason
      };
    }

    throw new InvalidOperationException(
      "No accessible parent could be found beneath the pointer.");
  }

  /// <summary>
  /// Returns raw-view immediate children without retaining stale child nodes.
  /// </summary>
  /// <param name="element">Parent element.</param>
  /// <returns>Immediate children in raw-view order.</returns>
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
    catch (ElementNotAvailableException)
    {
    }
    catch (InvalidOperationException)
    {
    }

    return result;
  }

  /// <summary>
  /// Determines whether one immediate child owns any readable text.
  /// </summary>
  /// <param name="element">Child subtree.</param>
  /// <returns>True when named text is present.</returns>
  private static bool ElementContainsText(AutomationElement element)
  {
    AutomationElementSnapshot snapshot = ReadSnapshot(element);
    if (snapshot.Name.Length != 0 &&
        !snapshot.ControlType.Equals(
          ControlType.Button.ProgrammaticName,
          StringComparison.Ordinal) &&
        !snapshot.ControlType.Equals(
          ControlType.TabItem.ProgrammaticName,
          StringComparison.Ordinal))
    {
      return true;
    }

    try
    {
      var condition = new PropertyCondition(
        AutomationElement.ControlTypeProperty,
        ControlType.Text);
      AutomationElement? text = element.FindFirst(
        TreeScope.Descendants,
        condition);
      return text is not null &&
        ReadSnapshot(text).Name.Length != 0;
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
  /// Reads text, layout, provider, and chrome metrics for one subtree.
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
    var samples = new List<string>(MaximumTextSamples);
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
        AutomationElementSnapshot snapshot = ReadSnapshot(descendant);
        if (!snapshot.Available)
        {
          continue;
        }

        if (snapshot.ControlType.Equals(
              ControlType.Edit.ProgrammaticName,
              StringComparison.Ordinal))
        {
          containsEdit = true;
        }

        if (snapshot.ControlType.Equals(
              ControlType.Tab.ProgrammaticName,
              StringComparison.Ordinal))
        {
          containsTab = true;
        }

        if (snapshot.Patterns.Contains(
              TextPattern.Pattern.ProgrammaticName,
              StringComparer.Ordinal))
        {
          ++textProviderCount;
        }

        if (snapshot.Name.Length == 0 ||
            (!snapshot.ControlType.Equals(
               ControlType.Text.ProgrammaticName,
               StringComparison.Ordinal) &&
             !snapshot.Patterns.Contains(
               TextPattern.Pattern.ProgrammaticName,
               StringComparer.Ordinal)))
        {
          continue;
        }

        ++textElementCount;
        if (LooksLikeNarration(snapshot.Name))
        {
          ++narrationTextCount;
        }

        if (samples.Count < MaximumTextSamples)
        {
          samples.Add(Abbreviate(snapshot.Name, 180));
        }

        if (snapshot.HasUsableBounds)
        {
          tops.Add((snapshot.RawBounds.Top, snapshot.RawBounds.Height));
        }
      }
    }
    catch (ElementNotAvailableException)
    {
    }
    catch (InvalidOperationException)
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
  /// Assigns a qualification tier to a candidate ancestor.
  /// </summary>
  private static int GetQualificationTier(
    AutomationElementSnapshot snapshot,
    int depth,
    bool isRoot,
    double windowCoverage,
    double rootHeightCoverage,
    double heightMultiple,
    int textElementCount,
    int narrationTextCount,
    int textBearingChildren,
    int verticalTextGroups,
    bool hasScrollPattern,
    bool containsEdit,
    bool containsTab)
  {
    if (depth == 0 ||
        isRoot ||
        !snapshot.Available ||
        !snapshot.HasUsableBounds ||
        windowCoverage >= MaximumCandidateWindowCoverage)
    {
      return int.MaxValue;
    }

    if (hasScrollPattern &&
        textBearingChildren >= 2 &&
        narrationTextCount >= 2 &&
        rootHeightCoverage >= 0.20)
    {
      return 0;
    }

    if (!containsEdit &&
        !containsTab &&
        textBearingChildren >= 3 &&
        narrationTextCount >= 2 &&
        verticalTextGroups >= 3 &&
        rootHeightCoverage >= 0.25 &&
        heightMultiple >= 4.0)
    {
      return 1;
    }

    if (!containsTab &&
        textBearingChildren >= 2 &&
        narrationTextCount >= 3 &&
        verticalTextGroups >= 4 &&
        rootHeightCoverage >= 0.25 &&
        heightMultiple >= 4.0)
    {
      return 2;
    }

    if (textElementCount >= 6 &&
        narrationTextCount >= 3 &&
        verticalTextGroups >= 4 &&
        rootHeightCoverage >= 0.25 &&
        heightMultiple >= 4.0 &&
        windowCoverage < 0.85)
    {
      return 3;
    }

    return int.MaxValue;
  }

  /// <summary>
  /// Scores a candidate for diagnostics and fallback ordering.
  /// </summary>
  private static int CalculateScore(
    int depth,
    double windowCoverage,
    int textElementCount,
    int narrationTextCount,
    int textBearingChildren,
    int verticalTextGroups,
    int textProviderCount,
    bool hasScrollPattern,
    bool containsEdit,
    bool containsTab)
  {
    int score = 0;
    score += hasScrollPattern ? 120 : 0;
    score += Math.Min(textBearingChildren, 12) * 18;
    score += Math.Min(narrationTextCount, 24) * 6;
    score += Math.Min(verticalTextGroups, 24) * 4;
    score += Math.Min(textElementCount, 64);
    score += Math.Min(textProviderCount, 8) * 5;
    score += windowCoverage is >= 0.05 and <= 0.80 ? 30 : 0;
    score -= containsEdit ? 35 : 0;
    score -= containsTab ? 45 : 0;
    score -= depth * 2;
    return score;
  }

  /// <summary>
  /// Produces a compact explanation for one candidate's qualification.
  /// </summary>
  private static string ExplainCandidate(
    int tier,
    bool rootLike,
    bool hasScrollPattern,
    int textBearingChildren,
    int narrationTextCount,
    int verticalTextGroups)
  {
    if (rootLike)
    {
      return "rejected as the top-level or whole-window element";
    }

    if (tier == 0)
    {
      return "scrollable ancestor with multiple text-bearing children";
    }

    if (tier == 1)
    {
      return "small ancestor with several transcript-like child blocks";
    }

    if (tier == 2)
    {
      return "ancestor with multiple narration blocks and vertical groups";
    }

    if (tier == 3)
    {
      return "bounded ancestor with a dense narration subtree";
    }

    return $"not qualified: scroll={hasScrollPattern}; " +
      $"textChildren={textBearingChildren}; " +
      $"narration={narrationTextCount}; groups={verticalTextGroups}";
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
  /// Recognizes sentence-like agent narration for container selection only.
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
  /// Stores all metrics for one ancestor candidate.
  /// </summary>
  private sealed record ContainerCandidate(
    [property: JsonIgnore] AutomationElement Element,
    int Depth,
    int Tier,
    int Score,
    string SelectionReason,
    AutomationElementSnapshot Snapshot,
    int ImmediateChildCount,
    int TextBearingChildCount,
    int TextElementCount,
    int NarrationTextCount,
    int VerticalTextGroupCount,
    int TextProviderCount,
    bool HasScrollPattern,
    bool ContainsEdit,
    bool ContainsTab,
    bool ContainsPointer,
    double WindowCoverage,
    double RootHeightCoverage,
    double HeightMultiple,
    IReadOnlyList<AutomationElementSnapshot> ImmediateChildren,
    IReadOnlyList<string> TextSamples);
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
