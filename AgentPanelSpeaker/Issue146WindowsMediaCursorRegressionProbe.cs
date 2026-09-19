using System.Reflection;
using System.Text.RegularExpressions;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;
using VoiceInformation = Windows.Media.SpeechSynthesis.VoiceInformation;
using WinRtSpeechSynthesizer = Windows.Media.SpeechSynthesis.SpeechSynthesizer;

namespace AgentPanelSpeaker;

/// <summary>
/// Production-path probe for issue #146 exact Windows.Media word ownership.
/// </summary>
internal static class Issue146WindowsMediaCursorRegressionProbe
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
  /// Synthesizes the reproduced block through the production Windows.Media
  /// renderer and requires complete exact bookmark ownership.
  /// </summary>
  internal static object GetContractSnapshot()
  {
    VoiceInformation voice = WinRtSpeechSynthesizer.AllVoices
      .FirstOrDefault(candidate => string.Equals(
        candidate.DisplayName,
        WindowsMediaVoiceName,
        StringComparison.OrdinalIgnoreCase)) ??
      throw new InvalidOperationException(
        $"Required Windows.Media CI voice '{WindowsMediaVoiceName}' is not installed.");

    SpeechMarkup markup = BuildTrackedMarkup(ReproducedText);
    var profile = new SpeechProfileSettings(WindowsMediaVoiceName, 9, 0)
    {
      Volume = 70
    };
    BookmarkInventory inventory = InspectProviderBookmarks(
      markup,
      profile,
      voice);
    using var synthesizer = new WinRtSpeechSynthesizer();
    MethodInfo method = typeof(SapiSpeechEngine).GetMethod(
      "RenderWindowsMediaSpeech",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("RenderWindowsMediaSpeech is missing.");
    object?[] arguments =
    {
      markup,
      profile,
      voice.Id,
      synthesizer,
      WindowsMediaBookmarkMode.Fallback,
      null,
      null
    };

    PcmWaveData wave;
    try
    {
      wave = method.Invoke(null, arguments) as PcmWaveData ??
        throw new InvalidOperationException(
          "Production Windows.Media synthesis returned no waveform.");
    }
    catch (TargetInvocationException exception) when (
      exception.InnerException is not null)
    {
      throw exception.InnerException;
    }

    IReadOnlyList<SpeechWordBoundary> boundaries =
      arguments[5] as IReadOnlyList<SpeechWordBoundary> ??
      throw new InvalidOperationException(
        "Production Windows.Media synthesis returned no boundary list.");
    SpeechTrackingDegradation? degradation =
      arguments[6] as SpeechTrackingDegradation;
    int[] expected = markup.Words!
      .Select(word => word.WordIndex)
      .Order()
      .ToArray();
    int[] observed = boundaries
      .SelectMany(boundary => Enumerable.Range(
        boundary.WordIndex,
        boundary.WordCount))
      .Distinct()
      .Order()
      .ToArray();

    Require(wave.Samples.Length > 0,
      "Windows.Media returned an empty waveform.");
    Require(degradation is null,
      "Windows.Media degraded exact word tracking: " +
      $"{degradation?.Reason ?? "unknown"}. " +
      $"Provider bookmark cues={inventory.BookmarkCueCount}, " +
      $"owners={inventory.ObservedOwnerCount}/{inventory.ExpectedOwnerCount}, " +
      $"missing=[{string.Join(", ", inventory.MissingOwners)}], " +
      $"tracks=[{string.Join(", ", inventory.TrackLabels)}].");
    Require(boundaries.Count > 1,
      "Windows.Media did not return moving word-level boundaries.");
    Require(observed.SequenceEqual(expected),
      "Windows.Media boundaries did not retain every exact markup word owner: " +
      $"expected={expected.Length}, observed={observed.Length}.");
    Require(boundaries.All(boundary => boundary.Exact),
      "Windows.Media returned a non-exact word boundary.");

    return new ContractSnapshot(
      Voice: voice.DisplayName,
      ExpectedWordCount: expected.Length,
      ObservedWordCount: observed.Length,
      BoundaryCount: boundaries.Count,
      Exact: boundaries.All(boundary => boundary.Exact),
      DegradationReason: degradation?.Reason ?? string.Empty);
  }

  private static BookmarkInventory InspectProviderBookmarks(
    SpeechMarkup markup,
    SpeechProfileSettings profile,
    VoiceInformation voice)
  {
    MethodInfo builder = typeof(SapiSpeechEngine).GetMethod(
      "TryBuildBookmarkedSsml",
      BindingFlags.Static | BindingFlags.NonPublic) ??
      throw new InvalidOperationException("TryBuildBookmarkedSsml is missing.");
    object?[] buildArguments = { markup, voice.Language, null };
    bool built = builder.Invoke(null, buildArguments) as bool? ?? false;
    string ssml = buildArguments[2] as string ?? string.Empty;
    Require(built && ssml.Length != 0,
      "Production Windows.Media bookmark SSML could not be built.");

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

    string[] labels = stream.TimedMetadataTracks
      .Select(track => track.Label ?? string.Empty)
      .ToArray();
    TimedMetadataTrack? bookmarkTrack = stream.TimedMetadataTracks
      .FirstOrDefault(track => string.Equals(
        track.Label,
        "SpeechBookmark",
        StringComparison.OrdinalIgnoreCase));
    var observedOwners = new List<int>();
    int cueCount = 0;
    if (bookmarkTrack is not null)
    {
      foreach (SpeechCue cue in bookmarkTrack.Cues.OfType<SpeechCue>())
      {
        ++cueCount;
        string identity = string.IsNullOrWhiteSpace(cue.Text)
          ? cue.Id ?? string.Empty
          : cue.Text;
        Match match = Regex.Match(identity, @"aps_(\d+)$");
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out int ownerIndex))
        {
          observedOwners.Add(ownerIndex);
        }
      }
    }

    SpeechMarkupWord[] expectedWords = markup.Words!.ToArray();
    HashSet<int> observed = observedOwners.ToHashSet();
    string[] missing = expectedWords
      .Where(word => !observed.Contains(word.WordIndex))
      .Select(word =>
        $"{word.WordIndex}:{EscapeDiagnosticToken(word.Text)}")
      .ToArray();
    return new BookmarkInventory(
      expectedWords.Length,
      observed.Count,
      cueCount,
      missing,
      labels);
  }

  private static string EscapeDiagnosticToken(string value)
  {
    return value
      .Replace("\\", "\\\\", StringComparison.Ordinal)
      .Replace("\r", "\\r", StringComparison.Ordinal)
      .Replace("\n", "\\n", StringComparison.Ordinal)
      .Replace(",", "\\,", StringComparison.Ordinal);
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

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }

  private sealed record BookmarkInventory(
    int ExpectedOwnerCount,
    int ObservedOwnerCount,
    int BookmarkCueCount,
    IReadOnlyList<string> MissingOwners,
    IReadOnlyList<string> TrackLabels);

  private sealed record ContractSnapshot(
    string Voice,
    int ExpectedWordCount,
    int ObservedWordCount,
    int BoundaryCount,
    bool Exact,
    string DegradationReason);
}
