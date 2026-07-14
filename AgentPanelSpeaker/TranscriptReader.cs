using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Reads the visible tail of a selected UI Automation subtree.
/// </summary>
internal sealed class TranscriptReader
{
  private const int MaxTailCharacters = 32_768;
  private const int MaxTailParagraphs = 3;

  /// <summary>
  /// Reads up to the final three visible logical text blocks exposed by the
  /// target.
  /// </summary>
  /// <param name="target">The selected transcript container.</param>
  /// <returns>The normalized text blocks in display order.</returns>
  public IReadOnlyList<string> ReadTail(AutomationElement target)
  {
    ArgumentNullException.ThrowIfNull(target);

    IReadOnlyList<string> visibleText = ReadTextElementNames(target);
    if (visibleText.Count != 0)
    {
      return visibleText;
    }

    string textPatternText = ReadBottomTextPattern(target);
    return SplitIntoParagraphs(textPatternText);
  }

  /// <summary>
  /// Finds the lowest visible TextPattern provider beneath the target.
  /// </summary>
  /// <param name="target">The selected transcript container.</param>
  /// <returns>Text from the lowest provider, or an empty string.</returns>
  private static string ReadBottomTextPattern(AutomationElement target)
  {
    var condition = new PropertyCondition(
      AutomationElement.IsTextPatternAvailableProperty,
      true);

    AutomationElementCollection providers;
    try
    {
      providers = target.FindAll(
        TreeScope.Element | TreeScope.Descendants,
        condition);
    }
    catch (ElementNotAvailableException)
    {
      return string.Empty;
    }

    string bestText = string.Empty;
    double bestBottom = double.NegativeInfinity;
    int bestParagraphCount = 0;

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

        System.Windows.Rect bounds =
          provider.Current.BoundingRectangle;
        if (!HasUsableBounds(bounds))
        {
          continue;
        }

        var pattern = (TextPattern)rawPattern;
        string candidate = ReadTextPatternTail(pattern);
        if (string.IsNullOrWhiteSpace(candidate))
        {
          continue;
        }

        int paragraphCount = CountNonEmptyLines(candidate);
        bool isLower = bounds.Bottom > bestBottom;
        bool isEquivalentPosition = bounds.Bottom == bestBottom;
        if (isLower ||
            (isEquivalentPosition &&
             (paragraphCount > bestParagraphCount ||
              (paragraphCount == bestParagraphCount &&
               candidate.Length > bestText.Length))))
        {
          bestText = candidate;
          bestBottom = bounds.Bottom;
          bestParagraphCount = paragraphCount;
        }
      }
      catch (ElementNotAvailableException)
      {
      }
      catch (InvalidOperationException)
      {
      }
    }

    return bestText;
  }

  /// <summary>
  /// Reads the final section of a TextPattern document range.
  /// </summary>
  /// <param name="pattern">The text provider.</param>
  /// <returns>The final available text from the provider.</returns>
  private static string ReadTextPatternTail(TextPattern pattern)
  {
    TextPatternRange document = pattern.DocumentRange;
    TextPatternRange tail = document.Clone();

    try
    {
      tail.MoveEndpointByRange(
        TextPatternRangeEndpoint.Start,
        document,
        TextPatternRangeEndpoint.End);
      tail.MoveEndpointByUnit(
        TextPatternRangeEndpoint.Start,
        TextUnit.Character,
        -MaxTailCharacters);
      return tail.GetText(-1);
    }
    catch (InvalidOperationException)
    {
      string allText = document.GetText(-1);
      return TakeLastCharacters(allText, MaxTailCharacters);
    }
    catch (ArgumentException)
    {
      string allText = document.GetText(-1);
      return TakeLastCharacters(allText, MaxTailCharacters);
    }
  }

  /// <summary>
  /// Falls back to visible UI Automation text element names.
  /// </summary>
  /// <param name="target">The selected transcript container.</param>
  /// <returns>The final normalized text elements.</returns>
  private static IReadOnlyList<string> ReadTextElementNames(
    AutomationElement target)
  {
    var condition = new PropertyCondition(
      AutomationElement.ControlTypeProperty,
      ControlType.Text);

    AutomationElementCollection elements;
    try
    {
      elements = target.FindAll(
        TreeScope.Element | TreeScope.Descendants,
        condition);
    }
    catch (ElementNotAvailableException)
    {
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

        string text = NormalizeParagraph(element.Current.Name);
        if (text.Length == 0)
        {
          ++ordinal;
          continue;
        }

        System.Windows.Rect bounds = element.Current.BoundingRectangle;
        if (!HasUsableBounds(bounds))
        {
          ++ordinal;
          continue;
        }

        candidates.Add(new TextCandidate(
          text,
          bounds.Top,
          bounds.Left,
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

    var result = new List<string>(MaxTailParagraphs);
    foreach (TextCandidate candidate in candidates)
    {
      if (result.Count != 0 &&
          string.Equals(result[^1], candidate.Text,
            StringComparison.Ordinal))
      {
        continue;
      }

      result.Add(candidate.Text);
    }

    return result.Count <= MaxTailParagraphs
      ? result
      : result.GetRange(result.Count - MaxTailParagraphs,
        MaxTailParagraphs);
  }

  /// <summary>
  /// Determines whether a UI Automation bounding rectangle can be ordered.
  /// </summary>
  /// <param name="bounds">The rectangle to validate.</param>
  /// <returns>True when the rectangle has finite, positive dimensions.</returns>
  private static bool HasUsableBounds(System.Windows.Rect bounds)
  {
    return !bounds.IsEmpty &&
      bounds.Width > 0.0 &&
      bounds.Height > 0.0 &&
      double.IsFinite(bounds.Top) &&
      double.IsFinite(bounds.Left) &&
      double.IsFinite(bounds.Bottom) &&
      double.IsFinite(bounds.Right);
  }

  /// <summary>
  /// Splits provider text into logical lines and returns its tail.
  /// </summary>
  /// <param name="text">Raw provider text.</param>
  /// <returns>The final normalized non-empty lines.</returns>
  private static IReadOnlyList<string> SplitIntoParagraphs(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return Array.Empty<string>();
    }

    string normalizedNewlines = text
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace('\r', '\n');

    var paragraphs = new List<string>();
    foreach (string line in normalizedNewlines.Split('\n'))
    {
      string paragraph = NormalizeParagraph(line);
      if (paragraph.Length == 0)
      {
        continue;
      }

      if (paragraphs.Count != 0 &&
          string.Equals(paragraphs[^1], paragraph,
            StringComparison.Ordinal))
      {
        continue;
      }

      paragraphs.Add(paragraph);
    }

    return paragraphs.Count <= MaxTailParagraphs
      ? paragraphs
      : paragraphs.GetRange(
        paragraphs.Count - MaxTailParagraphs,
        MaxTailParagraphs);
  }

  /// <summary>
  /// Counts non-empty logical lines in raw provider text.
  /// </summary>
  /// <param name="text">Raw provider text.</param>
  /// <returns>The number of non-empty lines.</returns>
  private static int CountNonEmptyLines(string text)
  {
    int count = 0;
    bool hasNonWhitespace = false;

    foreach (char character in text)
    {
      if (character == '\r' || character == '\n')
      {
        if (hasNonWhitespace)
        {
          ++count;
          hasNonWhitespace = false;
        }

        continue;
      }

      hasNonWhitespace |= !char.IsWhiteSpace(character);
    }

    return hasNonWhitespace ? count + 1 : count;
  }

  /// <summary>
  /// Collapses whitespace for stable comparisons and speech.
  /// </summary>
  /// <param name="text">Text to normalize.</param>
  /// <returns>Trimmed text with each whitespace run replaced by one space.</returns>
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
  /// Returns at most the requested number of trailing characters.
  /// </summary>
  /// <param name="text">Source text.</param>
  /// <param name="maximumLength">Maximum returned length.</param>
  /// <returns>The complete text or its final requested characters.</returns>
  private static string TakeLastCharacters(string text, int maximumLength)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

    return text.Length <= maximumLength
      ? text
      : text[^maximumLength..];
  }

  /// <summary>
  /// Stores a visible text element and its ordering data.
  /// </summary>
  private sealed record TextCandidate(
    string Text,
    double Top,
    double Left,
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

      int topComparison = CompareCoordinate(left.Top, right.Top);
      if (topComparison != 0)
      {
        return topComparison;
      }

      int leftComparison = CompareCoordinate(left.Left, right.Left);
      return leftComparison != 0
        ? leftComparison
        : left.Ordinal.CompareTo(right.Ordinal);
    }

    /// <summary>
    /// Compares coordinates while placing invalid values after valid values.
    /// </summary>
    /// <param name="left">Left coordinate.</param>
    /// <param name="right">Right coordinate.</param>
    /// <returns>A standard comparison result.</returns>
    private static int CompareCoordinate(double left, double right)
    {
      bool leftValid = double.IsFinite(left);
      bool rightValid = double.IsFinite(right);

      if (leftValid != rightValid)
      {
        return leftValid ? -1 : 1;
      }

      return leftValid ? left.CompareTo(right) : 0;
    }
  }
}
