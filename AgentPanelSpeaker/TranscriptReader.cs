using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Reacquires the current UI Automation tree and reads the meaningful tail of
/// a selected transcript screen region.
/// </summary>
internal sealed class TranscriptReader
{
  private const int MaxParagraphSearch = 32;
  private const int MaxTailCharacters = 32_768;
  private const int MaxTailParagraphs = 3;

  /// <summary>
  /// Gets the source used by the most recent read.
  /// </summary>
  public string LastReadSource { get; private set; } = "none";

  /// <summary>
  /// Gets provider and candidate details for the most recent read.
  /// </summary>
  public string LastReadDetails { get; private set; } = string.Empty;

  /// <summary>
  /// Reads the final meaningful transcript paragraphs inside the selected
  /// region.
  /// </summary>
  /// <param name="target">Stable window and region target.</param>
  /// <returns>The normalized transcript tail in display order.</returns>
  public IReadOnlyList<string> ReadTail(TranscriptTarget target)
  {
    ArgumentNullException.ThrowIfNull(target);

    AutomationElement root = target.GetAutomationRoot();
    System.Drawing.Rectangle region = target.GetScreenRegion();

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
  /// Chooses the most specific TextPattern provider overlapping the selected
  /// region and walks backward from the region's bottom.
  /// </summary>
  /// <param name="root">Current top-level UI Automation root.</param>
  /// <param name="region">Current transcript screen region.</param>
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
      region);
    details = $"providers={providers.Count}; " +
      $"selectedType={selected.ControlType}; " +
      $"selectedName={selected.Name}; " +
      $"bounds={RectangleToString(selected.Bounds)}; " +
      $"overlap={selected.OverlapArea:R}; " +
      $"providerArea={selected.ProviderArea:R}; " +
      $"paragraphs={tail.Count}";
    return tail;
  }

  /// <summary>
  /// Reads paragraph units backward from the bottom of a selected region.
  /// </summary>
  /// <param name="pattern">Selected text provider.</param>
  /// <param name="providerBounds">Provider screen bounds.</param>
  /// <param name="region">Selected transcript region.</param>
  /// <returns>The final meaningful paragraphs.</returns>
  private static IReadOnlyList<string> ReadParagraphs(
    TextPattern pattern,
    System.Windows.Rect providerBounds,
    System.Drawing.Rectangle region)
  {
    System.Drawing.Rectangle providerRectangle = ToDrawingRectangle(
      providerBounds);
    System.Drawing.Rectangle intersection = System.Drawing.Rectangle.Intersect(
      providerRectangle,
      region);
    if (intersection.Width <= 0 || intersection.Height <= 0)
    {
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
      return ReadDocumentTail(pattern);
    }
    catch (ArgumentException)
    {
      return ReadDocumentTail(pattern);
    }

    var reverse = new List<string>(MaxTailParagraphs);
    string previousFingerprint = string.Empty;

    for (int attempt = 0;
         attempt < MaxParagraphSearch && reverse.Count < MaxTailParagraphs;
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
      if (paragraph.Length != 0 &&
          RangeIntersectsRegion(rectangles, region) &&
          !IsIgnoredText(paragraph) &&
          (reverse.Count == 0 ||
           !string.Equals(
             reverse[^1],
             paragraph,
             StringComparison.Ordinal)))
      {
        reverse.Add(paragraph);
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
    return reverse;
  }

  /// <summary>
  /// Falls back to the provider's document text when paragraph navigation is
  /// unavailable.
  /// </summary>
  /// <param name="pattern">Selected text provider.</param>
  /// <returns>The final meaningful lines.</returns>
  private static IReadOnlyList<string> ReadDocumentTail(TextPattern pattern)
  {
    string text;
    try
    {
      text = pattern.DocumentRange.GetText(-1);
    }
    catch (InvalidOperationException)
    {
      return Array.Empty<string>();
    }

    text = TakeLastCharacters(text, MaxTailCharacters);
    return SplitIntoMeaningfulLines(text);
  }

  /// <summary>
  /// Reconstructs a fallback tail from currently visible text elements in the
  /// selected region.
  /// </summary>
  /// <param name="root">Current top-level UI Automation root.</param>
  /// <param name="region">Selected transcript screen region.</param>
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
  /// Splits raw provider text into meaningful normalized lines.
  /// </summary>
  /// <param name="text">Raw provider text.</param>
  /// <returns>The final meaningful lines.</returns>
  private static IReadOnlyList<string> SplitIntoMeaningfulLines(string text)
  {
    string normalizedNewlines = text
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace('\r', '\n');

    var lines = new List<string>();
    foreach (string rawLine in normalizedNewlines.Split('\n'))
    {
      string line = NormalizeParagraph(rawLine);
      if (line.Length == 0 || IsIgnoredText(line))
      {
        continue;
      }

      if (lines.Count != 0 &&
          string.Equals(lines[^1], line, StringComparison.Ordinal))
      {
        continue;
      }

      lines.Add(line);
    }

    return lines.Count <= MaxTailParagraphs
      ? lines
      : lines.Skip(lines.Count - MaxTailParagraphs).ToArray();
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

    if (trimmed.Equals("Queue another message...",
          StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("Edit automatically",
          StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("View usage",
          StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("IN", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("OUT", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("You've used ",
          StringComparison.OrdinalIgnoreCase) ||
        IsThoughtDuration(trimmed) ||
        IsWorkingDuration(trimmed) ||
        IsToolHeading(trimmed))
    {
      return true;
    }

    return IsKnownTransientStatus(trimmed) ||
      IsOneWordEllipsisStatus(trimmed);
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
  /// Determines whether a TextPattern range overlaps the selected region.
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
      return true;
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
  /// Stores and ranks a TextPattern provider.
  /// </summary>
  private sealed record TextProviderCandidate(
    TextPattern Pattern,
    System.Windows.Rect Bounds,
    double OverlapArea,
    double ProviderArea,
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
      const double tolerance = 0.5;
      if (OverlapArea > other.OverlapArea + tolerance)
      {
        return true;
      }

      if (Math.Abs(OverlapArea - other.OverlapArea) <= tolerance)
      {
        return ProviderArea < other.ProviderArea;
      }

      return false;
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
