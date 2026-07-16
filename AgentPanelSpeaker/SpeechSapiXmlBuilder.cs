using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Builds equivalent SAPI XML and System.Speech SSML markup.
/// </summary>
internal static partial class SpeechSapiXmlBuilder
{
  private const int SsmlPitchPercentPerStep = 5;

  /// <summary>
  /// Builds one marked-up utterance.
  /// </summary>
  public static SpeechMarkup Build(
    string text,
    int pitchSetting,
    IReadOnlyList<string> spelledWords,
    PronunciationRuleSet pronunciations)
  {
    ArgumentNullException.ThrowIfNull(text);
    ArgumentNullException.ThrowIfNull(spelledWords);
    ArgumentNullException.ThrowIfNull(pronunciations);
    string sapiContent = BuildContent(text, spelledWords, pronunciations);
    string ssmlContent = ConvertContentToSsml(sapiContent);
    return new SpeechMarkup(
      WrapSapiPitch(sapiContent, pitchSetting),
      WrapSsmlPitch(ssmlContent, pitchSetting));
  }

  /// <summary>
  /// Builds one explicit IPA pronunciation for toolbar preview.
  /// </summary>
  public static SpeechMarkup BuildIpaPreview(
    string displayedText,
    string ipa,
    int pitchSetting)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(displayedText);
    ArgumentException.ThrowIfNullOrWhiteSpace(ipa);
    var content = new StringBuilder();
    AppendIpa(content, displayedText, ipa);
    string sapiContent = content.ToString();
    return new SpeechMarkup(
      WrapSapiPitch(sapiContent, pitchSetting),
      WrapSsmlPitch(sapiContent, pitchSetting));
  }

  /// <summary>
  /// Inserts pronunciation/spelling tags and expands recognizable dates/times.
  /// </summary>
  private static string BuildContent(
    string text,
    IReadOnlyList<string> spelledWords,
    PronunciationRuleSet pronunciations)
  {
    Regex? spelledWordRegex = CreateSpelledWordRegex(spelledWords);
    var output = new StringBuilder();
    int position = 0;
    while (position < text.Length)
    {
      Match dateTimeMatch = IsoDateTimeRegex().Match(text, position);
      Match dateMatch = IsoDateRegex().Match(text, position);
      Match numericDateMatch = NumericDateRegex().Match(text, position);
      Match timeMatch = TimeRegex().Match(text, position);
      Match? spelledMatch = spelledWordRegex?.Match(text, position);
      PronunciationMatch? pronunciationMatch = pronunciations.FindNext(
        text,
        position);

      SpecialMatch? next = Earliest(
        pronunciationMatch is null
          ? null
          : new SpecialMatch(
            pronunciationMatch.Match,
            SpecialMatchKind.Pronunciation,
            pronunciationMatch.Rule),
        spelledMatch is null
          ? null
          : new SpecialMatch(
            spelledMatch,
            SpecialMatchKind.Spelling,
            null),
        new SpecialMatch(
          dateTimeMatch,
          SpecialMatchKind.IsoDateTime,
          null),
        new SpecialMatch(dateMatch, SpecialMatchKind.IsoDate, null),
        new SpecialMatch(
          numericDateMatch,
          SpecialMatchKind.NumericDate,
          null),
        new SpecialMatch(timeMatch, SpecialMatchKind.Time, null));

      if (next is null || !next.Match.Success)
      {
        AppendEscaped(output, text[position..]);
        break;
      }

      AppendEscaped(output, text[position..next.Match.Index]);
      switch (next.Kind)
      {
        case SpecialMatchKind.Pronunciation:
          AppendIpa(
            output,
            next.Match.Value,
            next.Pronunciation!.Ipa);
          break;

        case SpecialMatchKind.Spelling:
          output.Append("<spell>");
          AppendEscaped(output, next.Match.Value);
          output.Append("</spell>");
          break;

        case SpecialMatchKind.IsoDateTime:
          AppendEscaped(output, FormatIsoDateTime(next.Match.Value));
          break;

        case SpecialMatchKind.IsoDate:
          AppendEscaped(output, FormatIsoDate(next.Match.Value));
          break;

        case SpecialMatchKind.NumericDate:
          AppendEscaped(output, FormatNumericDate(next.Match.Value));
          break;

        case SpecialMatchKind.Time:
          AppendEscaped(output, FormatTime(next.Match.Value));
          break;
      }
      position = next.Match.Index + next.Match.Length;
    }

    return output.ToString();
  }

  /// <summary>
  /// Converts native SAPI spelling elements into SSML spelling elements.
  /// </summary>
  private static string ConvertContentToSsml(string sapiContent)
  {
    return sapiContent
      .Replace(
        "<spell>",
        "<say-as interpret-as=\"characters\">",
        StringComparison.Ordinal)
      .Replace("</spell>", "</say-as>", StringComparison.Ordinal);
  }

  /// <summary>
  /// Returns the earliest successful match and uses kind order on ties.
  /// </summary>
  private static SpecialMatch? Earliest(params SpecialMatch?[] matches)
  {
    return matches
      .Where(candidate => candidate is { Match.Success: true })
      .OrderBy(candidate => candidate!.Match.Index)
      .ThenByDescending(candidate => candidate!.Match.Length)
      .ThenBy(candidate => candidate!.Kind)
      .FirstOrDefault();
  }

  /// <summary>
  /// Creates a whole-token, case-insensitive spelling matcher.
  /// </summary>
  private static Regex? CreateSpelledWordRegex(
    IReadOnlyList<string> spelledWords)
  {
    string[] words = spelledWords
      .Where(word => !string.IsNullOrWhiteSpace(word))
      .Select(word => word.Trim())
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderByDescending(word => word.Length)
      .Select(Regex.Escape)
      .ToArray();
    if (words.Length == 0)
    {
      return null;
    }

    string pattern =
      $@"(?<![\p{{L}}\p{{N}}_])(?:{string.Join("|", words)})" +
      @"(?![\p{L}\p{N}_])";
    return new Regex(
      pattern,
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
  }

  /// <summary>
  /// Adds an explicit IPA phoneme element.
  /// </summary>
  private static void AppendIpa(
    StringBuilder output,
    string displayedText,
    string ipa)
  {
    output.Append("<phoneme alphabet=\"ipa\" ph=\"");
    AppendAttributeEscaped(output, ipa);
    output.Append("\">");
    AppendEscaped(output, displayedText);
    output.Append("</phoneme>");
  }

  /// <summary>
  /// Applies the native SAPI absolute-middle pitch setting.
  /// </summary>
  private static string WrapSapiPitch(string content, int pitchSetting)
  {
    int pitch = Math.Clamp(pitchSetting, -10, 10);
    return $"<pitch absmiddle=\"{pitch}\">{content}</pitch>";
  }

  /// <summary>
  /// Applies the System.Speech relative pitch setting.
  /// </summary>
  private static string WrapSsmlPitch(string content, int pitchSetting)
  {
    int pitchPercent = Math.Clamp(pitchSetting, -10, 10) *
      SsmlPitchPercentPerStep;
    string pitch = pitchPercent > 0
      ? $"+{pitchPercent}%"
      : $"{pitchPercent}%";
    return $"<prosody pitch=\"{pitch}\">{content}</prosody>";
  }

  /// <summary>
  /// Expands an ISO date-time into a form voices read naturally.
  /// </summary>
  private static string FormatIsoDateTime(string value)
  {
    if (!DateTimeOffset.TryParse(
          value,
          CultureInfo.InvariantCulture,
          DateTimeStyles.AllowWhiteSpaces,
          out DateTimeOffset parsed))
    {
      return value;
    }

    string formatted = parsed.ToString(
      parsed.Second == 0
        ? "MMMM d, yyyy 'at' h:mm tt"
        : "MMMM d, yyyy 'at' h:mm:ss tt",
      CultureInfo.CurrentCulture);
    return value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
      ? formatted + " UTC"
      : formatted;
  }

  /// <summary>
  /// Expands an ISO date into a long culture-aware date.
  /// </summary>
  private static string FormatIsoDate(string value)
  {
    if (!DateTime.TryParseExact(
          value,
          "yyyy-MM-dd",
          CultureInfo.InvariantCulture,
          DateTimeStyles.None,
          out DateTime parsed))
    {
      return value;
    }
    return parsed.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
  }

  /// <summary>
  /// Expands a slash- or dash-separated date using the current culture.
  /// </summary>
  private static string FormatNumericDate(string value)
  {
    string[] invariantFormats =
    {
      "yyyy/M/d", "yyyy/MM/dd", "M/d/yyyy", "MM/dd/yyyy",
      "d/M/yyyy", "dd/MM/yyyy", "M-d-yyyy", "MM-dd-yyyy",
      "d-M-yyyy", "dd-MM-yyyy"
    };
    if (!DateTime.TryParse(
          value,
          CultureInfo.CurrentCulture,
          DateTimeStyles.AllowWhiteSpaces,
          out DateTime parsed) &&
        !DateTime.TryParseExact(
          value,
          invariantFormats,
          CultureInfo.InvariantCulture,
          DateTimeStyles.None,
          out parsed))
    {
      return value;
    }
    return parsed.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
  }

  /// <summary>
  /// Expands a 12-hour or 24-hour clock value into a natural clock form.
  /// </summary>
  private static string FormatTime(string value)
  {
    string normalized = WhitespaceRegex().Replace(value.Trim(), " ");
    string[] formats =
    {
      "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss",
      "h:mm tt", "hh:mm tt", "h:mm:ss tt", "hh:mm:ss tt"
    };
    if (!DateTime.TryParseExact(
          normalized.ToUpperInvariant(),
          formats,
          CultureInfo.InvariantCulture,
          DateTimeStyles.AllowWhiteSpaces,
          out DateTime parsed))
    {
      return value;
    }
    bool hasSeconds = normalized.Count(character => character == ':') == 2;
    return parsed.ToString(
      hasSeconds ? "h:mm:ss tt" : "h:mm tt",
      CultureInfo.CurrentCulture);
  }

  /// <summary>
  /// Escapes text before inserting it into XML.
  /// </summary>
  private static void AppendEscaped(StringBuilder output, string text)
  {
    output.Append(SecurityElement.Escape(text) ?? string.Empty);
  }

  /// <summary>
  /// Escapes an XML attribute value.
  /// </summary>
  private static void AppendAttributeEscaped(
    StringBuilder output,
    string text)
  {
    AppendEscaped(output, text);
  }

  [GeneratedRegex(
    @"\b\d{4}-\d{2}-\d{2}[T ]\d{1,2}:\d{2}(?::\d{2}(?:\.\d+)?)?" +
    @"(?:Z|[+-]\d{2}:?\d{2})?\b",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex IsoDateTimeRegex();

  [GeneratedRegex(
    @"\b\d{4}-\d{2}-\d{2}\b",
    RegexOptions.CultureInvariant)]
  private static partial Regex IsoDateRegex();

  [GeneratedRegex(
    @"\b(?:\d{4}[/-]\d{1,2}[/-]\d{1,2}|" +
    @"\d{1,2}[/-]\d{1,2}[/-]\d{2,4})\b",
    RegexOptions.CultureInvariant)]
  private static partial Regex NumericDateRegex();

  [GeneratedRegex(
    @"\b(?:[01]?\d|2[0-3]):[0-5]\d(?::[0-5]\d)?(?:\s*[AP]M)?\b",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex TimeRegex();

  [GeneratedRegex(@"\s+")]
  private static partial Regex WhitespaceRegex();

  private enum SpecialMatchKind
  {
    Pronunciation,
    Spelling,
    IsoDateTime,
    IsoDate,
    NumericDate,
    Time
  }

  private sealed record SpecialMatch(
    Match Match,
    SpecialMatchKind Kind,
    PronunciationRule? Pronunciation);
}
