using System.Globalization;

namespace AgentPanelSpeaker;

/// <summary>
/// Identifies the speech provider that owns one installed voice.
/// </summary>
internal enum SpeechVoiceProvider
{
  SystemSpeech,
  Sapi,
  WindowsMedia
}

/// <summary>
/// Identifies one component of an installed voice's sortable display label.
/// </summary>
internal enum VoiceDisplayField
{
  Location,
  Language,
  VoiceName,
  Natural,
  Maker
}

/// <summary>
/// Identifies one installed provider voice and its structured UI metadata.
/// </summary>
/// <param name="Name">Stable selection identifier stored in settings.</param>
/// <param name="ProviderVoiceId">Provider-specific voice identifier.</param>
/// <param name="Provider">Speech provider used to render the voice.</param>
/// <param name="Maker">Voice vendor or maker.</param>
/// <param name="VoiceName">
/// Human voice name without vendor or quality tag.
/// </param>
/// <param name="Natural">
/// Natural or Natural HD quality tag when present.
/// </param>
/// <param name="Language">Spoken language.</param>
/// <param name="Location">Regional location.</param>
internal sealed record InstalledSpeechVoice(
  string Name,
  string ProviderVoiceId,
  SpeechVoiceProvider Provider,
  string Maker,
  string VoiceName,
  string Natural,
  string Language,
  string Location)
{
  private static readonly VoiceDisplayField[] DefaultOrder =
  {
    VoiceDisplayField.Location,
    VoiceDisplayField.Language,
    VoiceDisplayField.VoiceName,
    VoiceDisplayField.Natural,
    VoiceDisplayField.Maker
  };

  /// <summary>
  /// Creates structured fields from a legacy Windows voice label.
  /// </summary>
  public static InstalledSpeechVoice CreateLegacy(
    string selectionName,
    string providerVoiceId,
    SpeechVoiceProvider provider,
    string descriptiveName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(selectionName);
    ArgumentException.ThrowIfNullOrWhiteSpace(providerVoiceId);
    if (provider == SpeechVoiceProvider.WindowsMedia)
    {
      throw new ArgumentOutOfRangeException(
        nameof(provider),
        provider,
        "Use CreateWindowsMedia for modern Windows voices.");
    }

    ParsedVoiceLabel parsed = ParseLabel(
      string.IsNullOrWhiteSpace(descriptiveName)
        ? selectionName
        : descriptiveName);
    return new InstalledSpeechVoice(
      selectionName.Trim(),
      providerVoiceId.Trim(),
      provider,
      parsed.Maker,
      parsed.VoiceName,
      parsed.Natural,
      parsed.Language,
      parsed.Location);
  }

  /// <summary>
  /// Creates structured fields from Windows.Media voice metadata.
  /// </summary>
  public static InstalledSpeechVoice CreateWindowsMedia(
    string selectionName,
    string providerVoiceId,
    string displayName,
    string description,
    string languageTag)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(selectionName);
    ArgumentException.ThrowIfNullOrWhiteSpace(providerVoiceId);

    string cultureName = GetEnglishCultureName(languageTag);
    string descriptiveName = SelectMostDescriptiveName(
      displayName,
      description);
    if (cultureName.Length != 0 &&
        !descriptiveName.Contains(
          cultureName,
          StringComparison.OrdinalIgnoreCase))
    {
      descriptiveName = descriptiveName.Length == 0
        ? cultureName
        : $"{descriptiveName} - {cultureName}";
    }

    ParsedVoiceLabel parsed = ParseLabel(descriptiveName);
    return new InstalledSpeechVoice(
      selectionName.Trim(),
      providerVoiceId.Trim(),
      SpeechVoiceProvider.WindowsMedia,
      parsed.Maker,
      parsed.VoiceName,
      parsed.Natural,
      parsed.Language,
      parsed.Location);
  }

  /// <summary>
  /// Returns a provider-independent identity used to merge duplicate
  /// catalogues.  Quality metadata is deliberately excluded because one
  /// provider may identify a voice as Natural while another omits that tag.
  /// </summary>
  public string GetCatalogueKey()
  {
    string key = string.Join(
      "|",
      NormalizeKey(Maker),
      NormalizeKey(VoiceName),
      NormalizeKey(Language),
      NormalizeKey(Location));
    return key.Replace("|", string.Empty, StringComparison.Ordinal).Length == 0
      ? $"NAME:{NormalizeKey(Name)}"
      : key;
  }

  /// <summary>
  /// Retains this provider identity while filling missing display metadata
  /// from another catalogue entry for the same logical voice.
  /// </summary>
  public InstalledSpeechVoice MergeDisplayMetadata(
    InstalledSpeechVoice other)
  {
    ArgumentNullException.ThrowIfNull(other);
    return this with
    {
      Maker = Prefer(Maker, other.Maker),
      VoiceName = Prefer(VoiceName, other.VoiceName),
      Natural = Prefer(Natural, other.Natural),
      Language = Prefer(Language, other.Language),
      Location = Prefer(Location, other.Location)
    };
  }

  /// <summary>
  /// Returns one field's normalized display value.
  /// </summary>
  public string GetDisplayField(VoiceDisplayField field)
  {
    return field switch
    {
      VoiceDisplayField.Location => Location,
      VoiceDisplayField.Language => Language,
      VoiceDisplayField.VoiceName => VoiceName,
      VoiceDisplayField.Natural => Natural,
      VoiceDisplayField.Maker => Maker,
      _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };
  }

  /// <summary>
  /// Formats all non-empty fields in the requested order.
  /// </summary>
  public string Format(IReadOnlyList<VoiceDisplayField> order)
  {
    ArgumentNullException.ThrowIfNull(order);
    string[] values = order
      .Select(GetDisplayField)
      .Where(value => value.Length != 0)
      .ToArray();
    return values.Length == 0
      ? Name
      : string.Join(" - ", values);
  }

  /// <summary>
  /// Returns the default location-first label outside formatted dropdowns.
  /// </summary>
  public override string ToString()
  {
    return Format(DefaultOrder);
  }

  private static ParsedVoiceLabel ParseLabel(string descriptiveName)
  {
    string display = descriptiveName.Trim();
    string identity = display;
    string culture = string.Empty;
    int separator = display.LastIndexOf(" - ", StringComparison.Ordinal);
    if (separator > 0)
    {
      string possibleCulture = display[(separator + 3)..].Trim();
      if (LooksLikeCulture(possibleCulture))
      {
        identity = display[..separator].Trim();
        culture = possibleCulture;
      }
    }

    string natural = string.Empty;
    int naturalStart = identity.IndexOf(
      "(Natural",
      StringComparison.OrdinalIgnoreCase);
    if (naturalStart >= 0)
    {
      int naturalEnd = identity.IndexOf(')', naturalStart);
      if (naturalEnd > naturalStart)
      {
        natural = identity[(naturalStart + 1)..naturalEnd].Trim();
        identity = (
          identity[..naturalStart] + identity[(naturalEnd + 1)..])
          .Trim();
      }
    }

    string maker = string.Empty;
    string voiceName = identity;
    int firstSpace = identity.IndexOf(' ');
    if (firstSpace > 0 && firstSpace < identity.Length - 1)
    {
      maker = identity[..firstSpace].Trim();
      voiceName = identity[(firstSpace + 1)..].Trim();
    }

    string language = culture;
    string location = string.Empty;
    int locationStart = culture.LastIndexOf('(');
    if (locationStart > 0 && culture.EndsWith(')'))
    {
      language = culture[..locationStart].Trim();
      location = culture[(locationStart + 1)..^1].Trim();
    }

    return new ParsedVoiceLabel(
      maker,
      voiceName,
      natural,
      language,
      location);
  }

  private static string GetEnglishCultureName(string languageTag)
  {
    if (string.IsNullOrWhiteSpace(languageTag))
    {
      return string.Empty;
    }

    try
    {
      return CultureInfo.GetCultureInfo(languageTag.Trim()).EnglishName;
    }
    catch (CultureNotFoundException)
    {
      return languageTag.Trim();
    }
  }

  private static string SelectMostDescriptiveName(params string?[] values)
  {
    return values
      .Where(value => !string.IsNullOrWhiteSpace(value))
      .Select(value => value!.Trim())
      .OrderByDescending(value => value.Contains(
        "(Natural",
        StringComparison.OrdinalIgnoreCase))
      .ThenByDescending(value => value.Length)
      .FirstOrDefault() ?? string.Empty;
  }

  private static string Prefer(string primary, string fallback)
  {
    return primary.Length != 0 ? primary : fallback;
  }

  private static string NormalizeKey(string value)
  {
    return value.Trim().ToUpperInvariant();
  }

  private static bool LooksLikeCulture(string value)
  {
    int openingParenthesis = value.LastIndexOf('(');
    return openingParenthesis > 0 && value.EndsWith(')');
  }

  private sealed record ParsedVoiceLabel(
    string Maker,
    string VoiceName,
    string Natural,
    string Language,
    string Location);
}
