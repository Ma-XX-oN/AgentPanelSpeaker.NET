using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;
using VoiceInformation = Windows.Media.SpeechSynthesis.VoiceInformation;
using WinRtSpeechSynthesizer = Windows.Media.SpeechSynthesis.SpeechSynthesizer;

namespace AgentPanelSpeaker;

/// <summary>
/// Test-only provider metadata diagnostics for issue #146.
/// </summary>
internal static class Issue146WindowsMediaMetadataDiagnosticProbe
{
  private const string WindowsMediaVoiceName = "Microsoft Zira";
  private const string IsolatedRunText =
    "chore: run issue 138 raw transcript compatibility patch";
  private const string PairedRawText =
    "chore: stage issue 138 raw transcript compatibility patch " +
    "chore: run issue 138 raw transcript compatibility patch";
  private const string PrefixThroughFailureText =
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
    "chore: run issue 138 raw transcript compatibility patch";
  private const string ReproducedText =
    PrefixThroughFailureText + " " +
    "fix: retain legacy transcript replacement path third attempt " +
    "chore: remove ... multiple " +
    "fix: preserve raw transcript replacement API fourth attempt " +
    "e8b666b test: reproduce issue 138 live-end refresh race " +
    "5d70c3d fix: retain spoken anchor at live end final fix.";

  /// <summary>
  /// Returns provider metadata for the exact failing phrase in several scopes.
  /// </summary>
  internal static object GetMetadataSnapshot()
  {
    VoiceInformation voice = WinRtSpeechSynthesizer.AllVoices
      .FirstOrDefault(candidate => string.Equals(
        candidate.DisplayName,
        WindowsMediaVoiceName,
        StringComparison.OrdinalIgnoreCase)) ??
      throw new InvalidOperationException(
        $"Required Windows.Media CI voice '{WindowsMediaVoiceName}' is not installed.");

    var profile = new SpeechProfileSettings(WindowsMediaVoiceName, 9, 0)
    {
      Volume = 70
    };
    return new MetadataSnapshot(
      voice.DisplayName,
      new[]
      {
        InspectCase("isolated-run", IsolatedRunText, profile, voice),
        InspectCase("paired-raw", PairedRawText, profile, voice),
        InspectCase("prefix-through-failure", PrefixThroughFailureText, profile, voice),
        InspectCase("full-reproduction", ReproducedText, profile, voice)
      });
  }

  private static CaseSnapshot InspectCase(
    string name,
    string text,
    SpeechProfileSettings profile,
    VoiceInformation voice)
  {
    SpeechMarkup markup = BuildTrackedMarkup(text);
    MethodInfo builder = typeof(SapiSpeechEngine).GetMethod(
      "TryBuildBookmarkedSsml",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("TryBuildBookmarkedSsml is missing.");
    object?[] buildArguments = { markup, voice.Language, null };
    bool built = builder.Invoke(null, buildArguments) as bool? ?? false;
    string ssml = buildArguments[2] as string ?? string.Empty;
    if (!built || ssml.Length == 0)
    {
      throw new InvalidOperationException(
        $"Production bookmark SSML could not be built for {name}.");
    }

    XDocument document = XDocument.Parse(ssml);
    string[] generatedMarks = document.Descendants()
      .Where(element => string.Equals(
        element.Name.LocalName,
        "mark",
        StringComparison.OrdinalIgnoreCase))
      .Select(element => element.Attribute("name")?.Value ?? string.Empty)
      .ToArray();

    using var synthesizer = new WinRtSpeechSynthesizer
    {
      Voice = voice
    };
    synthesizer.Options.IncludeWordBoundaryMetadata = true;
    synthesizer.Options.IncludeSentenceBoundaryMetadata = true;
    synthesizer.Options.SpeakingRate = Math.Pow(2.0, profile.Rate / 10.0);
    synthesizer.Options.AudioPitch = 1.0;
    synthesizer.Options.AudioVolume = profile.Volume / 100.0;
    using SpeechSynthesisStream stream = synthesizer
      .SynthesizeSsmlToStreamAsync(ssml)
      .AsTask()
      .GetAwaiter()
      .GetResult();

    SpeechMarkupWord[] expectedWords = markup.Words!.ToArray();
    TimedMetadataTrack? bookmarkTrack = FindTrack(stream, "SpeechBookmark");
    int[] bookmarkOwners = bookmarkTrack is null
      ? Array.Empty<int>()
      : bookmarkTrack.Cues
        .OfType<SpeechCue>()
        .Select(TryReadBookmarkOwner)
        .Where(owner => owner >= 0)
        .ToArray();
    HashSet<int> observed = bookmarkOwners.ToHashSet();
    string[] missingOwners = expectedWords
      .Where(word => !observed.Contains(word.WordIndex))
      .Select(word => $"{word.WordIndex}:{word.Text}")
      .ToArray();

    TimedMetadataTrack? wordTrack = FindTrack(stream, "SpeechWord");
    SpeechCue[] wordCues = wordTrack?.Cues.OfType<SpeechCue>().ToArray() ??
      Array.Empty<SpeechCue>();
    var diagnosticIndexes = new SortedSet<int>();
    for (int index = 0; index < wordCues.Length; ++index)
    {
      if (!string.Equals(wordCues[index].Text, "138", StringComparison.Ordinal))
      {
        continue;
      }
      for (int nearby = Math.Max(0, index - 2);
           nearby <= Math.Min(wordCues.Length - 1, index + 2);
           ++nearby)
      {
        diagnosticIndexes.Add(nearby);
      }
    }
    string[] wordCueDiagnostics = diagnosticIndexes
      .Select(index => $"cue[{index}] {DescribeCue(wordCues[index])}")
      .ToArray();

    return new CaseSnapshot(
      name,
      expectedWords.Length,
      generatedMarks.Length,
      generatedMarks.Distinct(StringComparer.Ordinal).Count(),
      bookmarkTrack?.Cues.Count ?? 0,
      bookmarkOwners.Distinct().Count(),
      missingOwners,
      wordCues.Length,
      wordCueDiagnostics,
      stream.TimedMetadataTracks.Select(track => track.Label ?? string.Empty).ToArray());
  }

  private static TimedMetadataTrack? FindTrack(
    SpeechSynthesisStream stream,
    string label)
  {
    return stream.TimedMetadataTracks.FirstOrDefault(track => string.Equals(
      track.Label,
      label,
      StringComparison.OrdinalIgnoreCase));
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

  private static string DescribeCue(SpeechCue cue)
  {
    var parts = new List<string>();
    foreach (PropertyInfo property in cue.GetType()
      .GetProperties(BindingFlags.Instance | BindingFlags.Public)
      .Where(property => property.GetIndexParameters().Length == 0)
      .OrderBy(property => property.Name, StringComparer.Ordinal))
    {
      object? value;
      try
      {
        value = property.GetValue(cue);
      }
      catch (TargetInvocationException exception)
      {
        value = $"<{exception.InnerException?.GetType().Name ?? exception.GetType().Name}>";
      }
      parts.Add($"{property.Name}={value ?? "<null>"}");
    }
    return string.Join("; ", parts);
  }

  private static SpeechMarkup BuildTrackedMarkup(string text)
  {
    SpeechMarkup markup = SpeechSapiXmlBuilder.Build(
      text,
      0,
      Array.Empty<string>(),
      PronunciationRuleSet.Parse(string.Empty));
    MatchCollection matches = SpeechTokenization.Matches(text);
    SpeechMarkupWord[] words = matches
      .Cast<Match>()
      .Select((match, index) => new SpeechMarkupWord(
        index,
        match.Value,
        match.Index,
        match.Length))
      .ToArray();
    return markup with { Words = words };
  }

  private sealed record MetadataSnapshot(
    string Voice,
    IReadOnlyList<CaseSnapshot> Cases);

  private sealed record CaseSnapshot(
    string Name,
    int ExpectedWordCount,
    int GeneratedMarkCount,
    int DistinctGeneratedMarkCount,
    int BookmarkCueCount,
    int ObservedBookmarkOwnerCount,
    IReadOnlyList<string> MissingBookmarkOwners,
    int SpeechWordCueCount,
    IReadOnlyList<string> SpeechWordCueDiagnostics,
    IReadOnlyList<string> TrackLabels);
}
