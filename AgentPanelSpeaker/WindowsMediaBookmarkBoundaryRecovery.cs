using System.Reflection;
using System.Text.RegularExpressions;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;

namespace AgentPanelSpeaker;

/// <summary>
/// Recovers omitted Windows.Media bookmark owners only when provider-native
/// input ranges prove exact ownership in the submitted bookmarked SSML.
/// </summary>
internal static class WindowsMediaBookmarkBoundaryRecovery
{
  /// <summary>
  /// Adds exact boundaries for bookmark owners omitted by Windows.Media.
  /// Existing bookmark boundaries remain authoritative.  Recovery succeeds
  /// only when every omitted owner has one exact provider input range and a
  /// native time strictly between both adjacent canonical bookmark times.
  /// </summary>
  internal static bool TryRecoverMissingOwners(
    SpeechMarkup markup,
    string submittedSsml,
    SpeechSynthesisStream stream,
    IReadOnlyList<SpeechMarkupWord> words,
    IReadOnlyList<SpeechWordBoundary> bookmarkBoundaries,
    out IReadOnlyList<SpeechWordBoundary> recoveredBoundaries,
    out string rejectionReason)
  {
    recoveredBoundaries = Array.Empty<SpeechWordBoundary>();
    rejectionReason = string.Empty;

    int[] expectedOwners = words
      .Select(word => word.WordIndex)
      .Order()
      .ToArray();
    if (expectedOwners.Distinct().Count() != expectedOwners.Length)
    {
      rejectionReason = "duplicate_canonical_owner";
      return false;
    }

    HashSet<int> observedOwners = bookmarkBoundaries
      .Select(boundary => boundary.WordIndex)
      .ToHashSet();
    int[] missingOwners = expectedOwners
      .Where(owner => !observedOwners.Contains(owner))
      .ToArray();
    if (missingOwners.Length == 0)
    {
      return true;
    }

    WindowsMediaMarkerPosition[] markers = FindMarkers(submittedSsml);
    int[] markerOwners = markers
      .Select(marker => marker.OwnerIndex)
      .Order()
      .ToArray();
    if (markers.Length != words.Count ||
        markerOwners.Distinct().Count() != markerOwners.Length ||
        !markerOwners.SequenceEqual(expectedOwners))
    {
      rejectionReason = "generated_marker_inventory_mismatch";
      return false;
    }

    TimedMetadataTrack? wordTrack = stream.TimedMetadataTracks
      .FirstOrDefault(track => string.Equals(
        track.Label,
        "SpeechWord",
        StringComparison.OrdinalIgnoreCase));
    if (wordTrack is null)
    {
      rejectionReason = "missing_speechword_track";
      return false;
    }

    var locatedCues = new List<LocatedSpeechCue>();
    foreach (SpeechCue cue in wordTrack.Cues.OfType<SpeechCue>())
    {
      if (!TryReadInputPosition(cue, "StartPositionInInput", out long start) ||
          !TryReadInputPosition(cue, "EndPositionInInput", out long end))
      {
        rejectionReason = "speechword_input_range_unavailable";
        return false;
      }
      locatedCues.Add(new LocatedSpeechCue(cue, start, end));
    }
    if (locatedCues.Count == 0)
    {
      rejectionReason = "missing_speechword_cues";
      return false;
    }

    SpeechMarkupWord[] sourceOrderedWords = words
      .OrderBy(word => word.CharacterStart)
      .ThenBy(word => word.WordIndex)
      .ToArray();
    var bookmarkTimes = bookmarkBoundaries
      .GroupBy(boundary => boundary.WordIndex)
      .ToDictionary(
        group => group.Key,
        group => group
          .Select(boundary => boundary.AudioPosition)
          .Distinct()
          .Order()
          .ToArray());
    var recovered = new List<SpeechWordBoundary>();

    foreach (int missingOwner in missingOwners)
    {
      int wordPosition = Array.FindIndex(
        sourceOrderedWords,
        word => word.WordIndex == missingOwner);
      if (wordPosition <= 0 || wordPosition >= sourceOrderedWords.Length - 1)
      {
        rejectionReason = "missing_owner_has_no_two_sided_bookmark_context";
        return false;
      }

      int previousOwner = sourceOrderedWords[wordPosition - 1].WordIndex;
      int nextOwner = sourceOrderedWords[wordPosition + 1].WordIndex;
      if (!bookmarkTimes.TryGetValue(
            previousOwner,
            out TimeSpan[]? previousTimes) ||
          previousTimes.Length != 1 ||
          !bookmarkTimes.TryGetValue(nextOwner, out TimeSpan[]? nextTimes) ||
          nextTimes.Length != 1)
      {
        rejectionReason = "adjacent_bookmark_time_is_not_unique";
        return false;
      }

      int markerIndex = Array.FindIndex(
        markers,
        marker => marker.OwnerIndex == missingOwner);
      if (markerIndex < 0 || markerIndex >= markers.Length - 1)
      {
        rejectionReason = "missing_owner_marker_interval_unavailable";
        return false;
      }
      WindowsMediaMarkerPosition marker = markers[markerIndex];
      int nextMarkerStart = markers[markerIndex + 1].Start;

      LocatedSpeechCue[] native = locatedCues
        .Where(item =>
          item.Start >= marker.End &&
          item.Start < nextMarkerStart &&
          item.End > item.Start &&
          item.End <= nextMarkerStart)
        .OrderBy(item => item.Cue.StartTime)
        .ToArray();
      if (native.Length == 0)
      {
        rejectionReason = "missing_owner_has_no_native_cue";
        return false;
      }

      (long Start, long End)[] nativeRanges = native
        .Select(item => (item.Start, item.End))
        .Distinct()
        .ToArray();
      if (nativeRanges.Length != 1)
      {
        rejectionReason = "missing_owner_native_range_is_ambiguous";
        return false;
      }

      TimeSpan nativeTime = native[0].Cue.StartTime;
      if (!(previousTimes[0] < nativeTime && nativeTime < nextTimes[0]))
      {
        rejectionReason = "missing_owner_native_time_is_not_strictly_ordered";
        return false;
      }

      SpeechMarkupWord word = sourceOrderedWords[wordPosition];
      recovered.Add(new SpeechWordBoundary(
        nativeTime,
        word.WordIndex,
        word.CharacterStart,
        word.CharacterLength,
        word.Text,
        Exact: true));
    }

    int[] finalOwners = bookmarkBoundaries
      .Select(boundary => boundary.WordIndex)
      .Concat(recovered.Select(boundary => boundary.WordIndex))
      .Distinct()
      .Order()
      .ToArray();
    if (!finalOwners.SequenceEqual(expectedOwners))
    {
      rejectionReason = "recovered_owner_inventory_incomplete";
      return false;
    }

    recoveredBoundaries = recovered;
    return true;
  }

  private static WindowsMediaMarkerPosition[] FindMarkers(string ssml)
  {
    var markers = new List<WindowsMediaMarkerPosition>();
    foreach (Match match in Regex.Matches(
      ssml,
      @"<mark\b[^>]*\bname=""aps_(\d+)""[^>]*/>",
      RegexOptions.CultureInvariant))
    {
      if (!int.TryParse(match.Groups[1].Value, out int ownerIndex))
      {
        continue;
      }
      markers.Add(new WindowsMediaMarkerPosition(
        ownerIndex,
        match.Index,
        match.Index + match.Length));
    }
    return markers
      .OrderBy(marker => marker.Start)
      .ToArray();
  }

  private static bool TryReadInputPosition(
    SpeechCue cue,
    string propertyName,
    out long position)
  {
    position = 0;
    PropertyInfo? property = cue.GetType().GetProperty(propertyName);
    if (property is null)
    {
      return false;
    }
    object? value;
    try
    {
      value = property.GetValue(cue);
    }
    catch (TargetInvocationException)
    {
      return false;
    }
    if (value is null)
    {
      return false;
    }
    try
    {
      position = Convert.ToInt64(
        value,
        System.Globalization.CultureInfo.InvariantCulture);
      return true;
    }
    catch (Exception exception) when (
      exception is FormatException or InvalidCastException or OverflowException)
    {
      return false;
    }
  }

  private readonly record struct WindowsMediaMarkerPosition(
    int OwnerIndex,
    int Start,
    int End);

  private readonly record struct LocatedSpeechCue(
    SpeechCue Cue,
    long Start,
    long End);
}
