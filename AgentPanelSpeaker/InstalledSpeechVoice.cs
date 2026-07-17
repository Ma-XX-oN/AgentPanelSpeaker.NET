namespace AgentPanelSpeaker;

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
/// <param name="Name">Stable provider name stored in settings.</param>
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
  /// Creates structured fields from the descriptive Windows voice label.
  /// </summary>
  public static InstalledSpeechVoice Create(
    string providerName,
    string descriptiveName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

    string display = string.IsNullOrWhiteSpace(descriptiveName)
      ? providerName.Trim()
      : descriptiveName.Trim();
    string identity = display;
    string culture = string.Empty;
    int separator = display.LastIndexOf(
      " - ",
      StringComparison.Ordinal);
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

    return new InstalledSpeechVoice(
      providerName.Trim(),
      maker,
      voiceName,
      natural,
      language,
      location);
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

  private static bool LooksLikeCulture(string value)
  {
    int openingParenthesis = value.LastIndexOf('(');
    return openingParenthesis > 0 && value.EndsWith(')');
  }
}
