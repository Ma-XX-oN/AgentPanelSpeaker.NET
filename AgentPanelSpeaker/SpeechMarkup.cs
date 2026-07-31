namespace AgentPanelSpeaker;

/// <summary>
/// Carries displayed text, spoken-text mapping, and equivalent native SAPI
/// XML and standards-based SSML markup.
/// </summary>
internal sealed record SpeechMarkup(
  string PlainText,
  string SpokenText,
  IReadOnlyList<SpeechSourceMapEntry> SourceMap,
  string SapiXml,
  string SsmlContent)
{
  /// <summary>
  /// Maps a speech-engine character boundary back to displayed-text
  /// coordinates.
  /// </summary>
  public (int Position, int Count) MapBoundary(
    int spokenPosition,
    int spokenCount)
  {
    if (SourceMap.Count == 0)
    {
      int position = Math.Clamp(spokenPosition, 0, PlainText.Length);
      int count = Math.Clamp(spokenCount, 0, PlainText.Length - position);
      return (position, count);
    }

    int boundedStart = Math.Clamp(spokenPosition, 0, SpokenText.Length);
    int boundedEnd = Math.Clamp(
      checked(boundedStart + Math.Max(1, spokenCount)),
      boundedStart,
      SpokenText.Length);
    SpeechSourceMapEntry? first = null;
    SpeechSourceMapEntry? last = null;
    foreach (SpeechSourceMapEntry entry in SourceMap)
    {
      int entryEnd = entry.SpokenStart + entry.SpokenLength;
      if (entry.SpokenStart < boundedEnd && entryEnd > boundedStart)
      {
        first ??= entry;
        last = entry;
      }
    }

    if (first is null)
    {
      SpeechSourceMapEntry nearest = SourceMap
        .OrderBy(entry => Math.Abs(entry.SpokenStart - boundedStart))
        .First();
      return (nearest.DisplayStart, Math.Max(1, nearest.DisplayLength));
    }

    int displayStart = MapWithinEntry(first!, boundedStart);
    int displayEnd = MapWithinEntry(
      last!,
      Math.Max(boundedStart + 1, boundedEnd),
      endPosition: true);
    displayEnd = Math.Clamp(displayEnd, displayStart + 1, PlainText.Length);
    return (displayStart, displayEnd - displayStart);
  }

  private static int MapWithinEntry(
    SpeechSourceMapEntry entry,
    int spokenPosition,
    bool endPosition = false)
  {
    if (entry.SpokenLength == entry.DisplayLength &&
        entry.SpokenLength > 0)
    {
      int offset = Math.Clamp(
        spokenPosition - entry.SpokenStart,
        0,
        entry.SpokenLength);
      return entry.DisplayStart + offset;
    }
    return endPosition
      ? entry.DisplayStart + Math.Max(1, entry.DisplayLength)
      : entry.DisplayStart;
  }
}

/// <summary>
/// Maps one contiguous spoken-text range to the displayed source range that
/// produced it.
/// </summary>
internal sealed record SpeechSourceMapEntry(
  int SpokenStart,
  int SpokenLength,
  int DisplayStart,
  int DisplayLength);
