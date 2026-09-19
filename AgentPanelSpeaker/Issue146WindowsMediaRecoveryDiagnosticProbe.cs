using System.Reflection;
using System.Text.RegularExpressions;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;
using VoiceInformation = Windows.Media.SpeechSynthesis.VoiceInformation;
using WinRtSpeechSynthesizer = Windows.Media.SpeechSynthesis.SpeechSynthesizer;

namespace AgentPanelSpeaker;

/// <summary>
/// Test-only proof that a missing Windows.Media bookmark can be recovered from
/// one exact provider input range without text matching or timing interpolation.
/// </summary>
internal static class Issue146WindowsMediaRecoveryDiagnosticProbe
{
  private const string WindowsMediaVoiceName = "Microsoft Zira";
  private const string ReproducedText =
    "chore: apply verified issue 138 production patch " +
    "chore: stage issue 138 patch helper " +
    "chore: run issue 138 patch helper " +
    "chore: remove issue 138 patch tooling " +
    "fix: preserve live-tail playback DOM identity first attempt, wrong target " +
    "chore: stage issue 138 reconciliation follow-up " +
    "chore: run issue 138 reconciliation follow-up " +
    "chore: remove issue 138 patch tooling " +
    "fix: reconcile all incoming transcript units second attempt " +
    "chore: stage issue 138 raw transcript compatibility patch " +
    "chore: run issue 138 raw transcript compatibility patch " +
    "fix: retain legacy transcript replacement path third attempt " +
    "chore: remove ... multiple " +
    "fix: preserve raw transcript replacement API fourth attempt " +
    "e8b666b test: reproduce issue 138 live-end refresh race " +
    "5d70c3d fix: retain spoken anchor at live end final fix.";

  /// <summary>
  /// Returns exact recovery evidence for every bookmark owner omitted by the
  /// provider in the reproduced utterance.
  /// </summary>
  internal static object GetRecoverySnapshot()
  {
    VoiceInformation voice = WinRtSpeechSynthesizer.AllVoices
      .FirstOrDefault(candidate => string.Equals(
        candidate.DisplayName,
        WindowsMediaVoiceName,
        StringComparison.OrdinalIgnoreCase)) ??
      throw new InvalidOperationException(
        $"Required Windows.Media CI voice '{WindowsMediaVoiceName}' is not installed.");

    SpeechMarkup markup = BuildTrackedMarkup(ReproducedText);
    string ssml = BuildBookmarkedSsml(markup, voice.Language);
    MarkerPosition[] markers = FindMarkers(ssml);

    using var synthesizer = new WinRtSpeechSynthesizer
    {
      Voice = voice
    };
    synthesizer.Options.IncludeWordBoundaryMetadata = true;
    synthesizer.Options.IncludeSentenceBoundaryMetadata = true;
    synthesizer.Options.SpeakingRate = Math.Pow(2.0, 9.0 / 10.0);
    synthesizer.Options.AudioPitch = 1.0;
    synthesizer.Options.AudioVolume = 0.70;
    using SpeechSynthesisStream stream = synthesizer
      .SynthesizeSsmlToStreamAsync(ssml)
      .AsTask()
      .GetAwaiter()
      .GetResult();

    SpeechCue[] bookmarkCues = GetTrack(stream, "SpeechBookmark")
      .Cues
      .OfType<SpeechCue>()
      .ToArray();
    SpeechCue[] wordCues = GetTrack(stream, "SpeechWord")
      .Cues
      .OfType<SpeechCue>()
      .ToArray();

    var bookmarkByOwner = bookmarkCues
      .Select(cue => (Cue: cue, Owner: TryReadBookmarkOwner(cue)))
      .Where(item => item.Owner >= 0)
      .GroupBy(item => item.Owner)
      .ToDictionary(
        group => group.Key,
        group => group.OrderBy(item => item.Cue.StartTime).First().Cue);

    SpeechMarkupWord[] words = markup.Words!.OrderBy(word => word.WordIndex).ToArray();
    int[] missingOwners = words
      .Select(word => word.WordIndex)
      .Where(owner => !bookmarkByOwner.ContainsKey(owner))
      .ToArray();
    RecoveryCandidate[] candidates = missingOwners
      .Select(owner => BuildCandidate(
        words.Single(word => word.WordIndex == owner),
        markers,
        wordCues,
        bookmarkByOwner))
      .ToArray();

    return new RecoverySnapshot(
      words.Length,
      markers.Length,
      bookmarkByOwner.Count,
      missingOwners,
      candidates.Count(candidate => candidate.Recoverable),
      candidates);
  }

  private static RecoveryCandidate BuildCandidate(
    SpeechMarkupWord word,
    IReadOnlyList<MarkerPosition> markers,
    IReadOnlyList<SpeechCue> wordCues,
    IReadOnlyDictionary<int, SpeechCue> bookmarkByOwner)
  {
    MarkerPosition marker = markers.Single(item => item.OwnerIndex == word.WordIndex);
    int nextMarkerStart = markers
      .Where(item => item.Start > marker.Start)
      .Select(item => item.Start)
      .DefaultIfEmpty(int.MaxValue)
      .Min();
    SpeechCue[] native = wordCues
      .Where(cue =>
      {
        long start = ReadInputPosition(cue, "StartPositionInInput");
        return start >= marker.End && start < nextMarkerStart;
      })
      .OrderBy(cue => cue.StartTime)
      .ToArray();
    (long Start, long End)[] ranges = native
      .Select(cue => (
        ReadInputPosition(cue, "StartPositionInInput"),
        ReadInputPosition(cue, "EndPositionInInput")))
      .Distinct()
      .ToArray();

    int previousOwner = word.WordIndex - 1;
    int nextOwner = word.WordIndex + 1;
    bookmarkByOwner.TryGetValue(previousOwner, out SpeechCue? previousBookmark);
    bookmarkByOwner.TryGetValue(nextOwner, out SpeechCue? nextBookmark);
    long? nativeTime = native.Length == 0 ? null : native[0].StartTime.Ticks;
    long? previousTime = previousBookmark?.StartTime.Ticks;
    long? nextTime = nextBookmark?.StartTime.Ticks;
    bool strictlyOrdered =
      nativeTime is long current &&
      previousTime is long previous &&
      nextTime is long next &&
      previous < current && current < next;
    bool recoverable =
      ranges.Length == 1 &&
      native.Length > 0 &&
      strictlyOrdered;

    return new RecoveryCandidate(
      word.WordIndex,
      word.Text,
      marker.End,
      nextMarkerStart == int.MaxValue ? -1 : nextMarkerStart,
      native.Length,
      ranges.Length,
      ranges.Length == 1 ? ranges[0].Start : null,
      ranges.Length == 1 ? ranges[0].End : null,
      nativeTime,
      previousOwner,
      previousTime,
      nextOwner,
      nextTime,
      strictlyOrdered,
      recoverable);
  }

  private static string BuildBookmarkedSsml(SpeechMarkup markup, string language)
  {
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "TryBuildBookmarkedSsml",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("TryBuildBookmarkedSsml is missing.");
    object?[] arguments = { markup, language, null };
    bool built = Convert.ToBoolean(method.Invoke(null, arguments));
    string ssml = arguments[2] as string ?? string.Empty;
    if (!built || ssml.Length == 0)
    {
      throw new InvalidOperationException("Bookmarked SSML could not be built.");
    }
    return ssml;
  }

  private static MarkerPosition[] FindMarkers(string ssml)
  {
    return Regex.Matches(
        ssml,
        @"<mark\b[^>]*\bname=""aps_(\d+)""[^>]*/>",
        RegexOptions.CultureInvariant)
      .Cast<Match>()
      .Select(match => new MarkerPosition(
        int.Parse(match.Groups[1].Value),
        match.Index,
        match.Index + match.Length))
      .OrderBy(marker => marker.Start)
      .ToArray();
  }

  private static TimedMetadataTrack GetTrack(
    SpeechSynthesisStream stream,
    string label)
  {
    return stream.TimedMetadataTracks.FirstOrDefault(track => string.Equals(
      track.Label,
      label,
      StringComparison.OrdinalIgnoreCase)) ??
      throw new InvalidOperationException($"Provider track '{label}' is missing.");
  }

  private static int TryReadBookmarkOwner(SpeechCue cue)
  {
    string identity = string.IsNullOrWhiteSpace(cue.Text)
      ? cue.Id ?? string.Empty
      : cue.Text;
    Match match = Regex.Match(identity, @"aps_(\d+)$");
    return match.Success && int.TryParse(match.Groups[1].Value, out int owner)
      ? owner
      : -1;
  }

  private static long ReadInputPosition(SpeechCue cue, string propertyName)
  {
    PropertyInfo property = cue.GetType().GetProperty(propertyName) ??
      throw new InvalidOperationException($"SpeechCue.{propertyName} is unavailable.");
    object value = property.GetValue(cue) ??
      throw new InvalidOperationException($"SpeechCue.{propertyName} returned null.");
    return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
  }

  private static SpeechMarkup BuildTrackedMarkup(string text)
  {
    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
      text,
      0,
      Array.Empty<string>(),
      PronunciationRuleSet.Parse(string.Empty));
    SpeechMarkupWord[] words = SpeechTokenization.Matches(text)
      .Cast<Match>()
      .Select((match, index) => new SpeechMarkupWord(
        index,
        match.Value,
        match.Index,
        match.Length))
      .ToArray();
    return markup with { Words = words };
  }

  private sealed record MarkerPosition(int OwnerIndex, int Start, int End);

  private sealed record RecoverySnapshot(
    int ExpectedOwnerCount,
    int GeneratedMarkerCount,
    int BookmarkOwnerCount,
    IReadOnlyList<int> MissingOwners,
    int RecoverableOwnerCount,
    IReadOnlyList<RecoveryCandidate> Candidates);

  private sealed record RecoveryCandidate(
    int OwnerIndex,
    string Text,
    int MarkerEnd,
    int NextMarkerStart,
    int NativeCueCount,
    int NativeDistinctRangeCount,
    long? NativeStartPosition,
    long? NativeEndPosition,
    long? NativeTimeTicks,
    int PreviousOwner,
    long? PreviousBookmarkTimeTicks,
    int NextOwner,
    long? NextBookmarkTimeTicks,
    bool StrictlyOrdered,
    bool Recoverable);
}
