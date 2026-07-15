namespace AgentPanelSpeaker;

/// <summary>
/// Holds the normalized case-insensitive allow-list for fenced-code types.
/// </summary>
internal sealed class FencedCodeTypeSet
{
  private readonly HashSet<string> _types;

  private FencedCodeTypeSet(IReadOnlyList<string> orderedTypes)
  {
    OrderedTypes = orderedTypes;
    _types = new HashSet<string>(orderedTypes, StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Gets normalized entries in first-occurrence order.
  /// </summary>
  public IReadOnlyList<string> OrderedTypes { get; }

  /// <summary>
  /// Gets the normalized CSV representation.
  /// </summary>
  public string NormalizedCsv => string.Join(", ", OrderedTypes);

  /// <summary>
  /// Parses, trims, de-duplicates, and normalizes one CSV string.
  /// </summary>
  public static FencedCodeTypeSet Parse(string? csv)
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var ordered = new List<string>();
    foreach (string item in (csv ?? string.Empty).Split(','))
    {
      string normalized = item.Trim().ToLowerInvariant();
      if (normalized.Length != 0 && seen.Add(normalized))
      {
        ordered.Add(normalized);
      }
    }

    return new FencedCodeTypeSet(ordered);
  }

  /// <summary>
  /// Returns whether the normalized type is enabled.
  /// </summary>
  public bool Contains(string fenceType)
  {
    return _types.Contains("*") || _types.Contains(fenceType);
  }
}
