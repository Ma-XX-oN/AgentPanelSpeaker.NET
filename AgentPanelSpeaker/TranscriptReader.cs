using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Reads the meaningful tail from the current descendants of a retained
/// transcript container.
/// </summary>
internal sealed class TranscriptReader
{
  private const int MaxParagraphSearch = 512;
  private const int MaxTailParagraphs = 96;
  private const int RegionLookBehindParagraphs = 16;
  private const double MinimumBroadProviderCoverage = 0.35;
  private const double MinimumHorizontalLaneCoverage = 0.45;
  private const double MaximumLookBehindDistanceInRegions = 1.5;

  /// <summary>
  /// Gets the source used by the most recent read.
  /// </summary>
  public string LastReadSource { get; private set; } = "none";

  /// <summary>
  /// Gets provider and candidate details for the most recent read.
  /// </summary>
  public string LastReadDetails { get; private set; } = string.Empty;

  /// <summary>
  /// Reads the final meaningful transcript paragraphs inside the retained
  /// transcript container.
  /// </summary>
  /// <param name="target">Stable transcript-container target.</param>
  /// <returns>The normalized transcript tail in display order.</returns>
  public IReadOnlyList<string> ReadTail(TranscriptTarget target)
  {
    ArgumentNullException.ThrowIfNull(target);

    AutomationElement root = target.GetContainer();
    System.Drawing.Rectangle region =
      target.GetContainerRectangle(root);

    IReadOnlyList<string> paragraphs = ReadTextPatternTail(
      root,
      region,
      out string textPatternDetails);
    if (paragraphs.Count != 0)
    {
      LastReadSource = "TextPattern";
      LastReadDetails = textPatternDetails;
      return paragraphs;
    }

    IReadOnlyList<string> fallback = ReadVisibleTextTail(
      root,
      region,
      out string visibleTextDetails);
    LastReadSource = "VisibleText";
    LastReadDetails = $"TextPattern: {textPatternDetails}; " +
      $"VisibleText: {visibleTextDetails}";
    return fallback;
  }

  /// <summary>
  /// Chooses the most specific TextPattern provider inside the retained
  /// container and walks backward from the container's bottom.
  /// </summary>
  /// <param name="root">Current transcript container.</param>
  /// <param name="region">Current container screen bounds.</param>
  /// <returns>The meaningful tail, or an empty list when unavailable.</returns>
  private static IReadOnlyList<string> ReadTextPatternTail(
    AutomationElement root,
    System.Drawing.Rectangle region,
    out string details)
  {
    details = string.Empty;
    var condition = new PropertyCondition(
      AutomationElement.IsTextPatternAvailableProperty,
      true);

    AutomationElementCollection providers;
    try
    {
      providers = root.FindAll(
        TreeScope.Element | TreeScope.Descendants,
        condition);
    }
    catch (ElementNotAvailableException)
    {
      details = "provider collection unavailable";
      return Array.Empty<string>();
    }

    TextProviderCandidate? selected = null;
    foreach (AutomationElement provider in providers)
    {
      try
      {
        if (provider.Current.IsOffscreen ||
            !provider.TryGetCurrentPattern(
              TextPattern.Pattern,
              out object rawPattern))
        {
          continue;
        }

        System.Windows.Rect bounds = provider.Current.BoundingRectangle;
        if (!HasUsableBounds(bounds))
        {
          continue;
        }

        double overlap = CalculateOverlapArea(bounds, region);
        if (overlap <= 0.0)
        {
          continue;
        }

        double providerArea = bounds.Width * bounds.Height;
        string providerName = NormalizeParagraph(provider.Current.Name);
        string controlType = provider.Current.ControlType.ProgrammaticName;
        var candidate = new TextProviderCandidate(
          (TextPattern)rawPattern,
          bounds,
          overlap,
          providerArea,
          region.Width * (double)region.Height,
          providerName,
          controlType);
        if (selected is null || candidate.IsBetterThan(selected))
        {
          selected = candidate;
        }
      }
      catch (ElementNotAvailableException)
      {
      }
      catch (InvalidOperationException)
      {
      }
    }

    if (selected is null)
    {
      details = $"providers={providers.Count}; selected=none";
      return Array.Empty<string>();
    }

    IReadOnlyList<string> tail = ReadParagraphs(
      selected.Pattern,
      selected.Bounds,
      region,
      out string paragraphDetails);
    details = $"providers={providers.Count}; " +
      $"selectedType={selected.ControlType}; " +
      $"selectedName={selected.Name}; " +
      $"bounds={RectangleToString(selected.Bounds)}; " +
      $"overlap={selected.OverlapArea:R}; " +
      $"providerArea={selected.ProviderArea:R}; " +
      $"regionCoverage={selected.OverlapArea / selected.RegionArea:R}; " +
      $"providerCoverage={selected.OverlapArea / selected.ProviderArea:R}; " +
      $"paragraphs={tail.Count}; {paragraphDetails}";
    return tail;
  }

  /// <summary>
  /// Reads paragraph units backward from the bottom of the container bounds.
  /// </summary>
  /// <param name="pattern">Selected text provider.</param>
  /// <param name="providerBounds">Provider screen bounds.</param>
  /// <param name="region">Selected transcript region.</param>
  /// <param name="filterDetails">Filtering counters for diagnostics.</param>
  /// <returns>The final meaningful paragraphs.</returns>
  private static IReadOnlyList<string> ReadParagraphs(
    TextPattern pattern,
    System.Windows.Rect providerBounds,
    System.Drawing.Rectangle region,
    out string filterDetails)
  {
    System.Drawing.Rectangle providerRectangle = ToDrawingRectangle(
      providerBounds);
    System.Drawing.Rectangle intersection = System.Drawing.Rectangle.Intersect(
      providerRectangle,
      region);
    if (intersection.Width <= 0 || intersection.Height <= 0)
    {
      filterDetails = "range intersection empty";
      return Array.Empty<string>();
    }

    double x = intersection.Left + Math.Min(16.0, intersection.Width / 2.0);
    double y = intersection.Bottom - 2.0;

    TextPatternRange range;
    try
    {
      range = pattern.RangeFromPoint(new System.Windows.Point(x, y));
      range.ExpandToEnclosingUnit(TextUnit.Paragraph);
    }
    catch (InvalidOperationException)
    {
      filterDetails = "paragraph navigation unavailable";
      return Array.Empty<string>();
    }
    catch (ArgumentException)
    {
      filterDetails = "paragraph point invalid";
      return Array.Empty<string>();
    }

    var reverse = new List<ParagraphCandidate>(MaxParagraphSearch);
    string previousFingerprint = string.Empty;

    for (int attempt = 0;
         attempt < MaxParagraphSearch;
         ++attempt)
    {
      string rawText;
      System.Windows.Rect[] rectangles;
      try
      {
        rawText = range.GetText(-1);
        rectangles = range.GetBoundingRectangles();
      }
      catch (InvalidOperationException)
      {
        break;
      }

      string paragraph = NormalizeParagraph(rawText);
      string fingerprint = BuildRangeFingerprint(paragraph, rectangles);
      if (string.Equals(
            fingerprint,
            previousFingerprint,
            StringComparison.Ordinal))
      {
        break;
      }

      previousFingerprint = fingerprint;
      if (paragraph.Length != 0)
      {
        System.Windows.Rect paragraphBounds = GetRangeBounds(rectangles);
        reverse.Add(new ParagraphCandidate(
          paragraph,
          IsRangeHidden(range),
          HasUsableBounds(paragraphBounds),
          RangeIntersectsRegion(rectangles, region),
          RangeSharesTranscriptLane(paragraphBounds, region),
          paragraphBounds));
      }

      int moved;
      try
      {
        moved = range.Move(TextUnit.Paragraph, -1);
      }
      catch (InvalidOperationException)
      {
        break;
      }

      if (moved == 0)
      {
        break;
      }
    }

    reverse.Reverse();
    return FilterTextPatternParagraphs(reverse, region, out filterDetails);
  }


  /// <summary>
  /// Reconstructs a fallback tail from currently visible text descendants of
  /// the retained transcript container.
  /// </summary>
  /// <param name="root">Current transcript container.</param>
  /// <param name="region">Current container screen bounds.</param>
  /// <returns>The final visible meaningful text blocks.</returns>
  private static IReadOnlyList<string> ReadVisibleTextTail(
    AutomationElement root,
    System.Drawing.Rectangle region,
    out string details)
  {
    details = string.Empty;
    var condition = new PropertyCondition(
      AutomationElement.ControlTypeProperty,
      ControlType.Text);

    AutomationElementCollection elements;
    try
    {
      elements = root.FindAll(
        TreeScope.Element | TreeScope.Descendants,
        condition);
    }
    catch (ElementNotAvailableException)
    {
      details = "text element collection unavailable";
      return Array.Empty<string>();
    }

    var candidates = new List<TextCandidate>(elements.Count);
    int ordinal = 0;

    foreach (AutomationElement element in elements)
    {
      try
      {
        if (element.Current.IsOffscreen)
        {
          ++ordinal;
          continue;
        }

        System.Windows.Rect bounds = element.Current.BoundingRectangle;
        if (!HasUsableBounds(bounds) ||
            CalculateOverlapArea(bounds, region) <= 0.0)
        {
          ++ordinal;
          continue;
        }

        string text = NormalizeParagraph(element.Current.Name);
        if (text.Length == 0 || IsIgnoredText(text))
        {
          ++ordinal;
          continue;
        }

        candidates.Add(new TextCandidate(
          text,
          bounds.Top,
          bounds.Left,
          bounds.Height,
          ordinal));
      }
      catch (ElementNotAvailableException)
      {
      }
      catch (InvalidOperationException)
      {
      }

      ++ordinal;
    }

    candidates.Sort(TextCandidate.Compare);
    IReadOnlyList<string> lines = MergeCandidatesIntoLines(candidates);
    IReadOnlyList<string> tail = lines.Count <= MaxTailParagraphs
      ? lines
      : lines.Skip(lines.Count - MaxTailParagraphs).ToArray();
    details = $"elements={elements.Count}; candidates={candidates.Count}; " +
      $"lines={lines.Count}; paragraphs={tail.Count}";
    return tail;
  }

  /// <summary>
  /// Merges nearby text fragments that occupy the same visual line.
  /// </summary>
  /// <param name="candidates">Sorted visible text fragments.</param>
  /// <returns>Reconstructed visual lines.</returns>
  private static IReadOnlyList<string> MergeCandidatesIntoLines(
    IReadOnlyList<TextCandidate> candidates)
  {
    var lines = new List<VisualLine>();

    foreach (TextCandidate candidate in candidates)
    {
      VisualLine? line = lines.Count == 0 ? null : lines[^1];
      double tolerance = Math.Max(3.0, candidate.Height * 0.45);
      if (line is null || Math.Abs(candidate.Top - line.Top) > tolerance)
      {
        line = new VisualLine(candidate.Top);
        lines.Add(line);
      }

      line.Add(candidate);
    }

    var result = new List<string>(lines.Count);
    foreach (VisualLine line in lines)
    {
      string text = line.BuildText();
      if (text.Length == 0 || IsIgnoredText(text))
      {
        continue;
      }

      if (result.Count != 0 &&
          string.Equals(result[^1], text, StringComparison.Ordinal))
      {
        continue;
      }

      result.Add(text);
    }

    return result;
  }

  /// <summary>
  /// Removes hidden and tool-card text while preserving agent narration.
  /// </summary>
  /// <param name="paragraphs">
  /// Paragraphs in forward transcript order.
  /// </param>
  /// <param name="region">Selected transcript region.</param>
  /// <param name="details">Filtering counters for diagnostics.</param>
  /// <returns>The final bounded narration tail.</returns>
  private static IReadOnlyList<string> FilterTextPatternParagraphs(
    IReadOnlyList<ParagraphCandidate> paragraphs,
    System.Drawing.Rectangle region,
    out string details)
  {
    int firstVisible = -1;
    int lastVisible = -1;
    for (int index = 0; index < paragraphs.Count; ++index)
    {
      ParagraphCandidate candidate = paragraphs[index];
      if (!candidate.IsHidden && candidate.IntersectsRegion)
      {
        firstVisible = firstVisible < 0 ? index : firstVisible;
        lastVisible = index;
      }
    }

    if (firstVisible < 0)
    {
      details = $"raw={paragraphs.Count}; visible=0; retained=0";
      return Array.Empty<string>();
    }

    int windowStart = Math.Max(
      0,
      firstVisible - RegionLookBehindParagraphs);
    int windowEnd = lastVisible;
    var result = new List<string>(windowEnd - windowStart + 1);
    int hiddenCount = 0;
    int toolCount = 0;
    int unboundedCount = 0;
    int outsideRegionCount = 0;
    int laneLookBehindCount = 0;
    int ignoredCount = 0;
    int userBubbleCount = 0;
    bool insideToolBlock = StartsInsideToolBlock(
      paragraphs,
      windowStart);

    for (int index = windowStart; index <= windowEnd; ++index)
    {
      ParagraphCandidate candidate = paragraphs[index];
      string text = candidate.Text;
      if (candidate.IsHidden)
      {
        ++hiddenCount;
        continue;
      }

      if (!candidate.HasUsableBounds)
      {
        ++unboundedCount;
        continue;
      }

      if (!candidate.IntersectsRegion)
      {
        ++outsideRegionCount;
        if (!candidate.SharesTranscriptLane)
        {
          continue;
        }

        ++laneLookBehindCount;
      }

      if (IsLikelyUserBubble(candidate, region))
      {
        ++userBubbleCount;
        continue;
      }

      if (IsToolActivity(text) ||
          IsToolHeading(text) ||
          IsToolGroupSummary(text))
      {
        insideToolBlock = true;
        ++toolCount;
        continue;
      }

      bool narration = LooksLikeAgentNarration(text);
      if (insideToolBlock)
      {
        if (!narration)
        {
          ++toolCount;
          continue;
        }

        insideToolBlock = false;
      }

      if (IsIgnoredText(text))
      {
        ++ignoredCount;
        continue;
      }

      if (result.Count != 0 &&
          string.Equals(result[^1], text, StringComparison.Ordinal))
      {
        continue;
      }

      result.Add(text);
    }

    IReadOnlyList<string> tail = result.Count <= MaxTailParagraphs
      ? result
      : result.Skip(result.Count - MaxTailParagraphs).ToArray();
    details = $"raw={paragraphs.Count}; " +
      $"visible={lastVisible - firstVisible + 1}; " +
      $"window={windowStart}..{windowEnd}; hidden={hiddenCount}; " +
      $"tool={toolCount}; user={userBubbleCount}; " +
      $"unbounded={unboundedCount}; " +
      $"outsideRegion={outsideRegionCount}; " +
      $"laneLookBehind={laneLookBehindCount}; " +
      $"ignored={ignoredCount}; " +
      $"retained={tail.Count}";
    return tail;
  }

  /// <summary>
  /// Detects a short right-aligned user-message bubble.
  /// </summary>
  /// <param name="candidate">Paragraph candidate.</param>
  /// <param name="region">Selected transcript region.</param>
  /// <returns>True when the geometry matches a user bubble.</returns>
  private static bool IsLikelyUserBubble(
    ParagraphCandidate candidate,
    System.Drawing.Rectangle region)
  {
    if (!candidate.HasUsableBounds ||
        !HasUsableBounds(candidate.Bounds) ||
        !candidate.IntersectsRegion)
    {
      return false;
    }

    double leftThreshold = region.Left + region.Width * 0.42;
    double rightThreshold = region.Left + region.Width * 0.72;
    double maximumWidth = region.Width * 0.62;
    return candidate.Bounds.Left >= leftThreshold &&
      candidate.Bounds.Right >= rightThreshold &&
      candidate.Bounds.Width <= maximumWidth;
  }

  /// <summary>
  /// Determines whether a bounded window starts inside expanded tool details.
  /// </summary>
  /// <param name="paragraphs">All paragraphs in document order.</param>
  /// <param name="windowStart">First paragraph retained for filtering.</param>
  /// <returns>
  /// True when a preceding tool heading has not reached narration.
  /// </returns>
  private static bool StartsInsideToolBlock(
    IReadOnlyList<ParagraphCandidate> paragraphs,
    int windowStart)
  {
    int lowerBound = Math.Max(0, windowStart - 24);
    for (int index = windowStart - 1; index >= lowerBound; --index)
    {
      string text = paragraphs[index].Text;
      if (IsToolActivity(text) ||
          IsToolHeading(text) ||
          IsToolGroupSummary(text))
      {
        return true;
      }

      if (LooksLikeAgentNarration(text))
      {
        return false;
      }
    }

    return false;
  }

  /// <summary>
  /// Reads the UI Automation hidden-text attribute for one paragraph range.
  /// </summary>
  /// <param name="range">Paragraph text range.</param>
  /// <returns>True only when the provider explicitly marks it hidden.</returns>
  private static bool IsRangeHidden(TextPatternRange range)
  {
    ArgumentNullException.ThrowIfNull(range);

    try
    {
      object value = range.GetAttributeValue(TextPattern.IsHiddenAttribute);
      return value is bool isHidden && isHidden;
    }
    catch (InvalidOperationException)
    {
      return false;
    }
    catch (ElementNotAvailableException)
    {
      return false;
    }
  }

  /// <summary>
  /// Detects aggregate tool cards that own expandable child details.
  /// </summary>
  /// <param name="text">Normalized paragraph text.</param>
  /// <returns>True when the paragraph starts a tool-detail block.</returns>
  private static bool IsToolGroupSummary(string text)
  {
    string candidate = text.Trim();
    string[] prefixes =
    {
      "Ran ",
      "Edited ",
      "Read ",
      "Wrote "
    };

    foreach (string prefix in prefixes)
    {
      if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      int numberStart = prefix.Length;
      int numberEnd = numberStart;
      while (numberEnd < candidate.Length &&
             char.IsDigit(candidate[numberEnd]))
      {
        ++numberEnd;
      }

      if (numberEnd == numberStart)
      {
        return false;
      }

      string suffix = candidate[numberEnd..].TrimStart();
      return suffix.StartsWith("command",
          StringComparison.OrdinalIgnoreCase) ||
        suffix.StartsWith("file",
          StringComparison.OrdinalIgnoreCase);
    }

    return false;
  }

  /// <summary>
  /// Detects sentence-like agent narration that can end a tool-detail block.
  /// </summary>
  /// <param name="text">Normalized paragraph text.</param>
  /// <returns>
  /// True when the paragraph is narration rather than tool detail.
  /// </returns>
  private static bool LooksLikeAgentNarration(string text)
  {
    string candidate = text.Trim();
    if (candidate.Length < 12 ||
        IsIgnoredText(candidate) ||
        IsCommandLikeText(candidate))
    {
      return false;
    }

    int wordCount = CountWords(candidate);
    if (wordCount < 4)
    {
      return false;
    }

    if (candidate.IndexOfAny(new[] { '.', '?', '!' }) >= 0)
    {
      return true;
    }

    string[] prefixes =
    {
      "I ",
      "I'm ",
      "I’m ",
      "I've ",
      "I’ve ",
      "I'll ",
      "I’ll ",
      "The ",
      "This ",
      "That ",
      "There ",
      "We ",
      "We're ",
      "We’re ",
      "Now ",
      "Next ",
      "First ",
      "My ",
      "It "
    };

    return prefixes.Any(prefix => candidate.StartsWith(
      prefix,
      StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>
  /// Detects shell, source, and diff text that is not agent narration.
  /// </summary>
  /// <param name="text">Normalized paragraph text.</param>
  /// <returns>
  /// True when the text looks like a command or source fragment.
  /// </returns>
  private static bool IsCommandLikeText(string text)
  {
    string candidate = text.TrimStart();
    string[] prefixes =
    {
      "$ ",
      "PS ",
      "git ",
      "rg ",
      "cmake ",
      "ctest ",
      "Get-",
      "Set-",
      "Write-",
      "if (",
      "foreach (",
      "for (",
      "while (",
      "class ",
      "struct ",
      "template<",
      "inline ",
      "constexpr ",
      "using ",
      "namespace ",
      "#include",
      "{",
      "}",
      "};"
    };

    if (prefixes.Any(prefix => candidate.StartsWith(
          prefix,
          StringComparison.OrdinalIgnoreCase)) ||
        IsFileChangeSummary(candidate))
    {
      return true;
    }

    if (candidate.All(character =>
          char.IsDigit(character) ||
          char.IsWhiteSpace(character) ||
          character is ':' or '-' or '+' or '.'))
    {
      return true;
    }

    int punctuation = candidate.Count(character =>
      character is '{' or '}' or '[' or ']' or '(' or ')' or
        '<' or '>' or '=' or ';' or '\\');
    return punctuation >= 6 && punctuation * 4 >= candidate.Length;
  }

  /// <summary>
  /// Detects compact file-change labels such as "Config.hpp+43-2".
  /// </summary>
  /// <param name="text">Normalized candidate text.</param>
  /// <returns>True when the text is a compact file-change label.</returns>
  private static bool IsFileChangeSummary(string text)
  {
    if (text.Any(char.IsWhiteSpace))
    {
      return false;
    }

    int plus = text.LastIndexOf('+');
    int minus = plus < 0
      ? -1
      : text.IndexOf('-', plus + 1);
    if (plus <= 0 || minus <= plus + 1 || minus == text.Length - 1)
    {
      return false;
    }

    return text[(plus + 1)..minus].All(char.IsDigit) &&
      text[(minus + 1)..].All(char.IsDigit) &&
      text[..plus].Contains('.');
  }

  /// <summary>
  /// Counts word starts in normalized text.
  /// </summary>
  /// <param name="text">Text to inspect.</param>
  /// <returns>Number of word starts.</returns>
  private static int CountWords(string text)
  {
    int count = 0;
    bool insideWord = false;

    foreach (char character in text)
    {
      bool isWord = char.IsLetterOrDigit(character);
      if (isWord && !insideWord)
      {
        ++count;
      }

      insideWord = isWord;
    }

    return count;
  }

  /// <summary>
  /// Determines whether text is UI chrome or a transient one-word status.
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text must not be spoken.</returns>
  private static bool IsIgnoredText(string text)
  {
    string trimmed = text.Trim();
    if (trimmed.Length == 0)
    {
      return true;
    }

    if (IsToolActivity(trimmed) ||
        IsCommandLikeText(trimmed) ||
        IsClockLabel(trimmed) ||
        trimmed.Equals("Queue another message...",
          StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("Edit automatically",
          StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("View usage",
          StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("Context automatically compacted",
          StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("Steered conversation",
          StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("IN", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("OUT", StringComparison.OrdinalIgnoreCase) ||
        IsEditorChrome(trimmed) ||
        IsDiagnosticMarker(trimmed) ||
        trimmed.StartsWith("You've used ",
          StringComparison.OrdinalIgnoreCase) ||
        IsThoughtDuration(trimmed) ||
        IsWorkingDuration(trimmed) ||
        IsWorkedDuration(trimmed) ||
        IsToolHeading(trimmed))
    {
      return true;
    }

    return IsKnownTransientStatus(trimmed) ||
      IsOneWordEllipsisStatus(trimmed);
  }


  /// <summary>
  /// Detects VS Code chrome that can leak from the enclosing document.
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is editor or panel chrome.</returns>
  private static bool IsEditorChrome(string text)
  {
    string[] exact =
    {
      "C++",
      "LF",
      "UTF-8",
      "Not Committed Yet",
      "Check Claude transcript",
      "No tasks in progress"
    };
    if (exact.Any(value => text.Equals(
          value,
          StringComparison.OrdinalIgnoreCase)))
    {
      return true;
    }

    string[] prefixes =
    {
      "Lns: ",
      "Wds: ",
      "Pos: ",
      "Ln ",
      "Spaces: ",
      "Search returned ",
      "TabOut"
    };
    if (prefixes.Any(prefix => text.StartsWith(
          prefix,
          StringComparison.OrdinalIgnoreCase)))
    {
      return true;
    }

    int index = 0;
    while (index < text.Length && char.IsDigit(text[index]))
    {
      ++index;
    }

    if (index > 0)
    {
      string suffix = text[index..].TrimStart();
      if (suffix.StartsWith("task in progress",
            StringComparison.OrdinalIgnoreCase) ||
          suffix.StartsWith("tasks in progress",
            StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return !text.Any(char.IsLetterOrDigit);
  }

  /// <summary>
  /// Detects timing/debug markers injected by transcript helpers.
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is diagnostic metadata.</returns>
  private static bool IsDiagnosticMarker(string text)
  {
    return text.Equals("text", StringComparison.OrdinalIgnoreCase) ||
      text.StartsWith("START=", StringComparison.OrdinalIgnoreCase) ||
      text.StartsWith("END=", StringComparison.OrdinalIgnoreCase) ||
      text.StartsWith("ELAPSED=", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Detects Codex and Claude tool activity cards.
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is tool activity rather than prose.</returns>
  private static bool IsToolActivity(string text)
  {
    string candidate = text.Trim();
    if (candidate.StartsWith("Ran ", StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith("Running ", StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith("Edited ", StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith("Read ", StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith("Wrote ", StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith("Editing a file",
          StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith("Editing files",
          StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    int ranIndex = candidate.IndexOf(
      ", ran ",
      StringComparison.OrdinalIgnoreCase);
    return ranIndex > 0 &&
      (candidate.EndsWith(" command",
         StringComparison.OrdinalIgnoreCase) ||
       candidate.EndsWith(" commands",
         StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>
  /// Detects time labels such as "3:16 PM".
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is a standalone clock label.</returns>
  private static bool IsClockLabel(string text)
  {
    string candidate = text.Trim();
    int colon = candidate.IndexOf(':');
    if (colon is < 1 or > 2)
    {
      return false;
    }

    int suffixStart = candidate.Length - 2;
    if (suffixStart <= colon ||
        !(candidate.EndsWith("AM", StringComparison.OrdinalIgnoreCase) ||
          candidate.EndsWith("PM", StringComparison.OrdinalIgnoreCase)))
    {
      return false;
    }

    string hour = candidate[..colon];
    string minute = candidate[(colon + 1)..suffixStart].Trim();
    return int.TryParse(hour, out int parsedHour) &&
      parsedHour is >= 1 and <= 12 &&
      int.TryParse(minute, out int parsedMinute) &&
      parsedMinute is >= 0 and <= 59;
  }

  /// <summary>
  /// Detects labels such as "Working for 1m 5s".
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is a work-duration label.</returns>
  private static bool IsWorkingDuration(string text)
  {
    return text.StartsWith("Working for ",
      StringComparison.OrdinalIgnoreCase) &&
      text.Length <= 40;
  }

  /// <summary>
  /// Detects completed duration labels such as "Worked for 1m 38s".
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is a completed-work duration label.</returns>
  private static bool IsWorkedDuration(string text)
  {
    return text.StartsWith("Worked for ",
      StringComparison.OrdinalIgnoreCase) &&
      text.Length <= 40;
  }

  /// <summary>
  /// Detects known one-word animated status labels even when an accessibility
  /// icon is appended after the word.
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is a known transient status.</returns>
  private static bool IsKnownTransientStatus(string text)
  {
    int start = 0;
    while (start < text.Length && !char.IsLetter(text[start]))
    {
      ++start;
    }

    int end = text.Length;
    while (end > start &&
           !char.IsLetter(text[end - 1]) &&
           text[end - 1] != '-')
    {
      --end;
    }

    if (start == end)
    {
      return false;
    }

    string candidate = text[start..end];
    if (candidate.Any(character =>
          !char.IsLetter(character) && character != '-'))
    {
      return false;
    }

    string[] statuses =
    {
      "Analyzing",
      "Analysing",
      "Baking",
      "Considering",
      "Creating",
      "Editing",
      "Generating",
      "Planning",
      "Processing",
      "Reading",
      "Running",
      "Searching",
      "Thinking",
      "Waiting",
      "Working",
      "Writing"
    };

    return statuses.Contains(candidate,
      StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Detects labels such as "Thought for 1s".
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is a thought-duration label.</returns>
  private static bool IsThoughtDuration(string text)
  {
    return text.StartsWith("Thought for ",
      StringComparison.OrdinalIgnoreCase) &&
      text.EndsWith('s') &&
      text.Length <= 40;
  }

  /// <summary>
  /// Detects short tool-card headings without excluding ordinary prose such
  /// as "Writing the new file now".
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is a tool-card heading.</returns>
  private static bool IsToolHeading(string text)
  {
    if (text.Equals("Shell", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (text.Length > 100)
    {
      return false;
    }

    string[] prefixes =
    {
      "Bash ",
      "Read ",
      "Write ",
      "Edit ",
      "Glob ",
      "Grep ",
      "Task "
    };

    return prefixes.Any(prefix => text.StartsWith(
      prefix,
      StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>
  /// Detects a single status word followed by an ellipsis, such as
  /// "Considering..." or "Creating...".
  /// </summary>
  /// <param name="text">Normalized text.</param>
  /// <returns>True when the text is a transient status.</returns>
  private static bool IsOneWordEllipsisStatus(string text)
  {
    string candidate = text.TrimStart(
      '•',
      '*',
      '+',
      '-',
      '›',
      '>');
    candidate = candidate.TrimStart();

    if (candidate.EndsWith('…'))
    {
      candidate = candidate[..^1];
    }
    else if (candidate.EndsWith("...", StringComparison.Ordinal))
    {
      candidate = candidate[..^3];
    }
    else
    {
      return false;
    }

    if (candidate.Length == 0 || candidate.Length > 40)
    {
      return false;
    }

    foreach (char character in candidate)
    {
      if (!char.IsLetter(character) && character != '-')
      {
        return false;
      }
    }

    return true;
  }

  /// <summary>
  /// Returns the union of all usable range rectangles.
  /// </summary>
  /// <param name="rectangles">Range rectangles.</param>
  /// <returns>The union, or an empty rectangle when none are usable.</returns>
  private static System.Windows.Rect GetRangeBounds(
    IReadOnlyList<System.Windows.Rect> rectangles)
  {
    System.Windows.Rect result = System.Windows.Rect.Empty;
    foreach (System.Windows.Rect bounds in rectangles)
    {
      if (!HasUsableBounds(bounds))
      {
        continue;
      }

      result = result.IsEmpty
        ? bounds
        : System.Windows.Rect.Union(result, bounds);
    }

    return result;
  }

  /// <summary>
  /// Determines whether a TextPattern range overlaps the container bounds.
  /// </summary>
  /// <param name="rectangles">Range rectangles.</param>
  /// <param name="region">Selected region.</param>
  /// <returns>True when any range rectangle overlaps the region.</returns>
  private static bool RangeIntersectsRegion(
    IReadOnlyList<System.Windows.Rect> rectangles,
    System.Drawing.Rectangle region)
  {
    if (rectangles.Count == 0)
    {
      return false;
    }

    foreach (System.Windows.Rect bounds in rectangles)
    {
      if (HasUsableBounds(bounds) &&
          CalculateOverlapArea(bounds, region) > 0.0)
      {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// Determines whether an off-region paragraph belongs to the same transcript
  /// column as the retained transcript container.
  /// </summary>
  /// <param name="bounds">Combined paragraph bounds.</param>
  /// <param name="region">Selected transcript region.</param>
  /// <returns>
  /// True when the paragraph is horizontally aligned with the transcript and
  /// close enough above it to be safe look-behind text.
  /// </returns>
  private static bool RangeSharesTranscriptLane(
    System.Windows.Rect bounds,
    System.Drawing.Rectangle region)
  {
    if (!HasUsableBounds(bounds))
    {
      return false;
    }

    double horizontalLeft = Math.Max(bounds.Left, region.Left);
    double horizontalRight = Math.Min(bounds.Right, region.Right);
    double horizontalOverlap = Math.Max(
      0.0,
      horizontalRight - horizontalLeft);
    double comparisonWidth = Math.Min(bounds.Width, region.Width);
    if (comparisonWidth <= 0.0 ||
        horizontalOverlap / comparisonWidth <
          MinimumHorizontalLaneCoverage)
    {
      return false;
    }

    double maximumDistance = region.Height *
      MaximumLookBehindDistanceInRegions;
    return bounds.Top <= region.Bottom &&
      bounds.Bottom >= region.Top - maximumDistance;
  }

  /// <summary>
  /// Builds a small identity for detecting a TextPattern provider that did not
  /// actually move to another paragraph.
  /// </summary>
  /// <param name="text">Normalized range text.</param>
  /// <param name="rectangles">Range rectangles.</param>
  /// <returns>A stable range fingerprint.</returns>
  private static string BuildRangeFingerprint(
    string text,
    IReadOnlyList<System.Windows.Rect> rectangles)
  {
    var builder = new StringBuilder(text.Length + 96);
    builder.Append(text);
    int limit = Math.Min(rectangles.Count, 2);
    for (int index = 0; index < limit; ++index)
    {
      System.Windows.Rect rectangle = rectangles[index];
      builder.Append('|');
      builder.Append(rectangle.X.ToString("R"));
      builder.Append(',');
      builder.Append(rectangle.Y.ToString("R"));
      builder.Append(',');
      builder.Append(rectangle.Width.ToString("R"));
      builder.Append(',');
      builder.Append(rectangle.Height.ToString("R"));
    }

    return builder.ToString();
  }

  /// <summary>
  /// Formats a UI Automation rectangle for diagnostics.
  /// </summary>
  /// <param name="rectangle">Rectangle to format.</param>
  /// <returns>Left, top, width, and height.</returns>
  private static string RectangleToString(System.Windows.Rect rectangle)
  {
    return $"{rectangle.Left:R},{rectangle.Top:R} " +
      $"{rectangle.Width:R}x{rectangle.Height:R}";
  }

  /// <summary>
  /// Calculates the overlap area between a UI Automation rectangle and a
  /// drawing rectangle.
  /// </summary>
  /// <param name="bounds">UI Automation rectangle.</param>
  /// <param name="region">Drawing rectangle.</param>
  /// <returns>The overlap area in square pixels.</returns>
  private static double CalculateOverlapArea(
    System.Windows.Rect bounds,
    System.Drawing.Rectangle region)
  {
    double left = Math.Max(bounds.Left, region.Left);
    double top = Math.Max(bounds.Top, region.Top);
    double right = Math.Min(bounds.Right, region.Right);
    double bottom = Math.Min(bounds.Bottom, region.Bottom);
    return right <= left || bottom <= top
      ? 0.0
      : (right - left) * (bottom - top);
  }

  /// <summary>
  /// Determines whether a UI Automation bounding rectangle can be used.
  /// </summary>
  /// <param name="bounds">Rectangle to validate.</param>
  /// <returns>True when it has finite, positive dimensions.</returns>
  private static bool HasUsableBounds(System.Windows.Rect bounds)
  {
    return !bounds.IsEmpty &&
      bounds.Width > 0.0 &&
      bounds.Height > 0.0 &&
      double.IsFinite(bounds.Left) &&
      double.IsFinite(bounds.Top) &&
      double.IsFinite(bounds.Right) &&
      double.IsFinite(bounds.Bottom);
  }

  /// <summary>
  /// Converts a UI Automation rectangle to a drawing rectangle.
  /// </summary>
  /// <param name="bounds">UI Automation rectangle.</param>
  /// <returns>The enclosing integer drawing rectangle.</returns>
  private static System.Drawing.Rectangle ToDrawingRectangle(
    System.Windows.Rect bounds)
  {
    return System.Drawing.Rectangle.FromLTRB(
      checked((int)Math.Floor(bounds.Left)),
      checked((int)Math.Floor(bounds.Top)),
      checked((int)Math.Ceiling(bounds.Right)),
      checked((int)Math.Ceiling(bounds.Bottom)));
  }

  /// <summary>
  /// Collapses whitespace for stable comparison and speech.
  /// </summary>
  /// <param name="text">Text to normalize.</param>
  /// <returns>
  /// Trimmed text with each whitespace run replaced by one space.
  /// </returns>
  private static string NormalizeParagraph(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return string.Empty;
    }

    var builder = new StringBuilder(text.Length);
    bool previousWasWhitespace = false;

    foreach (char character in text)
    {
      if (character is '\uFFFC' or '\u200B' or '\uFEFF')
      {
        continue;
      }

      if (char.IsWhiteSpace(character))
      {
        if (!previousWasWhitespace && builder.Length != 0)
        {
          builder.Append(' ');
        }

        previousWasWhitespace = true;
        continue;
      }

      builder.Append(character);
      previousWasWhitespace = false;
    }

    return builder.ToString().Trim();
  }

  /// <summary>
  /// Returns at most the requested trailing characters.
  /// </summary>
  /// <param name="text">Source text.</param>
  /// <param name="maximumLength">Maximum returned length.</param>
  /// <returns>The complete text or its trailing requested characters.</returns>
  private static string TakeLastCharacters(string text, int maximumLength)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
    return text.Length <= maximumLength
      ? text
      : text[^maximumLength..];
  }

  /// <summary>
  /// Stores one TextPattern paragraph and its visibility information.
  /// </summary>
  private sealed record ParagraphCandidate(
    string Text,
    bool IsHidden,
    bool HasUsableBounds,
    bool IntersectsRegion,
    bool SharesTranscriptLane,
    System.Windows.Rect Bounds);

  /// <summary>
  /// Stores and ranks a TextPattern provider.
  /// </summary>
  private sealed record TextProviderCandidate(
    TextPattern Pattern,
    System.Windows.Rect Bounds,
    double OverlapArea,
    double ProviderArea,
    double RegionArea,
    string Name,
    string ControlType)
  {
    /// <summary>
    /// Determines whether this provider is a better region match.
    /// </summary>
    /// <param name="other">Current best provider.</param>
    /// <returns>True when this provider is preferred.</returns>
    public bool IsBetterThan(TextProviderCandidate other)
    {
      const double tolerance = 0.0001;
      double regionCoverage = RegionArea <= 0.0
        ? 0.0
        : OverlapArea / RegionArea;
      double otherRegionCoverage = other.RegionArea <= 0.0
        ? 0.0
        : other.OverlapArea / other.RegionArea;
      bool isBroad = regionCoverage >= MinimumBroadProviderCoverage;
      bool otherIsBroad =
        otherRegionCoverage >= MinimumBroadProviderCoverage;
      if (isBroad != otherIsBroad)
      {
        return isBroad;
      }

      double providerCoverage = ProviderArea <= 0.0
        ? 0.0
        : OverlapArea / ProviderArea;
      double otherProviderCoverage = other.ProviderArea <= 0.0
        ? 0.0
        : other.OverlapArea / other.ProviderArea;

      if (isBroad &&
          Math.Abs(providerCoverage - otherProviderCoverage) > tolerance)
      {
        return providerCoverage > otherProviderCoverage;
      }

      if (Math.Abs(regionCoverage - otherRegionCoverage) > tolerance)
      {
        return regionCoverage > otherRegionCoverage;
      }

      if (Math.Abs(providerCoverage - otherProviderCoverage) > tolerance)
      {
        return providerCoverage > otherProviderCoverage;
      }

      return ProviderArea < other.ProviderArea;
    }
  }

  /// <summary>
  /// Stores a visible text fragment and display-order coordinates.
  /// </summary>
  private sealed record TextCandidate(
    string Text,
    double Top,
    double Left,
    double Height,
    int Ordinal)
  {
    /// <summary>
    /// Compares two candidates in approximate display order.
    /// </summary>
    /// <param name="left">Left candidate.</param>
    /// <param name="right">Right candidate.</param>
    /// <returns>A standard comparison result.</returns>
    public static int Compare(TextCandidate? left, TextCandidate? right)
    {
      if (ReferenceEquals(left, right))
      {
        return 0;
      }

      if (left is null)
      {
        return -1;
      }

      if (right is null)
      {
        return 1;
      }

      int topComparison = left.Top.CompareTo(right.Top);
      if (topComparison != 0)
      {
        return topComparison;
      }

      int leftComparison = left.Left.CompareTo(right.Left);
      return leftComparison != 0
        ? leftComparison
        : left.Ordinal.CompareTo(right.Ordinal);
    }
  }

  /// <summary>
  /// Collects fragments that belong to one visual line.
  /// </summary>
  private sealed class VisualLine
  {
    private readonly List<TextCandidate> _candidates = new();

    /// <summary>
    /// Initializes a visual line.
    /// </summary>
    /// <param name="top">Approximate top coordinate.</param>
    public VisualLine(double top)
    {
      Top = top;
    }

    /// <summary>
    /// Gets the approximate top coordinate.
    /// </summary>
    public double Top { get; }

    /// <summary>
    /// Adds a text fragment.
    /// </summary>
    /// <param name="candidate">Fragment to add.</param>
    public void Add(TextCandidate candidate)
    {
      ArgumentNullException.ThrowIfNull(candidate);
      _candidates.Add(candidate);
    }

    /// <summary>
    /// Builds the line in horizontal display order.
    /// </summary>
    /// <returns>The normalized line text.</returns>
    public string BuildText()
    {
      _candidates.Sort((left, right) =>
      {
        int comparison = left.Left.CompareTo(right.Left);
        return comparison != 0
          ? comparison
          : left.Ordinal.CompareTo(right.Ordinal);
      });

      var builder = new StringBuilder();
      string previous = string.Empty;
      foreach (TextCandidate candidate in _candidates)
      {
        if (string.Equals(
              previous,
              candidate.Text,
              StringComparison.Ordinal))
        {
          continue;
        }

        if (builder.Length != 0)
        {
          builder.Append(' ');
        }

        builder.Append(candidate.Text);
        previous = candidate.Text;
      }

      return NormalizeParagraph(builder.ToString());
    }
  }
}
