using System.Globalization;
using System.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Provides IPA chart symbols and compact preview examples.
/// </summary>
internal static class IpaSymbolCatalog
{
  /// <summary>
  /// Gets toolbar groups in IPA chart order.
  /// </summary>
  public static IReadOnlyList<IpaSymbolGroup> Groups { get; } = BuildGroups();

  /// <summary>
  /// Creates all symbol groups and applies familiar example overrides.
  /// </summary>
  private static IReadOnlyList<IpaSymbolGroup> BuildGroups()
  {
    var overrides = BuildOverrides();
    IpaSymbolGroup[] groups =
    {
      BuildSegmentGroup(
        "Pulmonic consonants",
        "p b t d ʈ ɖ c ɟ k ɡ q ɢ ʔ m ɱ n ɳ ɲ ŋ ɴ " +
        "ʙ r ʀ ⱱ ɾ ɽ ɸ β f v θ ð s z ʃ ʒ ʂ ʐ ç ʝ x ɣ χ ʁ " +
        "ħ ʕ h ɦ ɬ ɮ ʋ ɹ ɻ j ɰ l ɭ ʎ ʟ",
        "pulmonic consonant",
        overrides),
      BuildSegmentGroup(
        "Non-pulmonic consonants",
        "ʘ ǀ ǃ ǂ ǁ ɓ ɗ ʄ ɠ ʛ pʼ tʼ kʼ sʼ",
        "non-pulmonic consonant",
        overrides),
      BuildSegmentGroup(
        "Other consonant symbols",
        "ʍ w ɥ ʜ ʢ ʡ ɕ ʑ ɺ ɫ ɧ",
        "other consonant",
        overrides),
      BuildVowelGroup(
        "Vowels",
        "i y ɪ ʏ e ø ɛ œ æ a ɶ ɨ ʉ ɘ ɵ ə ɜ ɞ ɐ " +
        "ɯ u ʊ o ɤ ɚ ɔ ʌ ɑ ɒ",
        overrides),
      BuildModifierGroup(
        "Suprasegmentals",
        new[]
        {
          "ˈ", "ˌ", "ː", "ˑ", "̆", "|", "‖", ".", "‿"
        },
        "suprasegmental",
        overrides),
      BuildModifierGroup(
        "Diacritics",
        new[]
        {
          "̥", "̊", "̬", "̤", "̰", "̼", "̪", "̺", "̻", "̃", "ⁿ", "ˡ",
          "̚", "̴", "̝", "˔", "̞", "˕", "̟", "̠", "̘", "̙", "̈", "̽",
          "̹", "̜", "̩", "̯", "˞", "ʰ", "ʷ", "ʲ", "ˠ", "ˤ", "͡", "͜"
        },
        "diacritic",
        overrides),
      BuildModifierGroup(
        "Tones and accents",
        new[]
        {
          "˥", "˦", "˧", "˨", "˩", "ꜛ", "ꜜ", "↗", "↘", "̋", "́",
          "̄", "̀", "̏", "̌", "̂", "᷄", "᷅", "᷈"
        },
        "tone or accent mark",
        overrides)
    };
    Validate(groups);
    return groups;
  }

  /// <summary>
  /// Rejects catalogue data that would produce contradictory footer entries.
  /// </summary>
  private static void Validate(IReadOnlyList<IpaSymbolGroup> groups)
  {
    IpaSymbolDefinition[] definitions = groups
      .SelectMany(group => group.Symbols)
      .ToArray();
    if (definitions.Select(value => value.Symbol).Distinct().Count() !=
        definitions.Length)
    {
      throw new InvalidOperationException(
        "The IPA catalogue contains duplicate toolbar symbols.");
    }

    foreach (IpaSymbolDefinition definition in definitions)
    {
      if (!definition.ExampleIpa.Contains(
            definition.Symbol,
            StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          $"The IPA example for {definition.Symbol} does not contain it.");
      }
      if (definition.StandaloneIpa is not null &&
          !definition.StandaloneIpa.Contains(
            definition.Symbol,
            StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          $"The isolated IPA for {definition.Symbol} does not contain it.");
      }
      if (definition.ExampleKind == IpaExampleKind.Carrier &&
          !string.Equals(
            definition.ExampleWord,
            "carrier",
            StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
          $"The IPA carrier for {definition.Symbol} is not labelled carrier.");
      }
    }

    foreach (IGrouping<string, IpaSymbolDefinition> repeatedWord in
      definitions
        .Where(value => value.ExampleKind == IpaExampleKind.Word)
        .GroupBy(value => value.ExampleWord, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1))
    {
      string[] basePronunciations = repeatedWord
        .Select(value => value.ExampleIpa.Replace(
          value.Symbol,
          string.Empty,
          StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
      if (basePronunciations.Length != 1)
      {
        throw new InvalidOperationException(
          $"The repeated IPA example word '{repeatedWord.Key}' has " +
          "contradictory base pronunciations.");
      }
    }
  }

  /// <summary>
  /// Creates a consonant group using an explicitly labelled carrier.
  /// </summary>
  private static IpaSymbolGroup BuildSegmentGroup(
    string name,
    string symbols,
    string description,
    IReadOnlyDictionary<string, IpaSymbolDefinition> overrides)
  {
    return new IpaSymbolGroup(
      name,
      SplitSymbols(symbols)
        .Select(symbol => overrides.TryGetValue(symbol, out var known)
          ? known
          : new IpaSymbolDefinition(
            symbol,
            description,
            symbol,
            "carrier",
            $"a{symbol}a",
            "middle",
            IpaExampleKind.Carrier))
        .ToArray());
  }

  /// <summary>
  /// Creates a vowel group using a neutral carrier by default.
  /// </summary>
  private static IpaSymbolGroup BuildVowelGroup(
    string name,
    string symbols,
    IReadOnlyDictionary<string, IpaSymbolDefinition> overrides)
  {
    return new IpaSymbolGroup(
      name,
      SplitSymbols(symbols)
        .Select(symbol => overrides.TryGetValue(symbol, out var known)
          ? known
          : new IpaSymbolDefinition(
            symbol,
            "vowel",
            symbol,
            "carrier",
            $"h{symbol}d",
            "middle",
            IpaExampleKind.Carrier))
        .ToArray());
  }

  /// <summary>
  /// Creates a modifier group with symbol-specific examples when known.
  /// </summary>
  private static IpaSymbolGroup BuildModifierGroup(
    string name,
    IReadOnlyList<string> symbols,
    string description,
    IReadOnlyDictionary<string, IpaSymbolDefinition> overrides)
  {
    return new IpaSymbolGroup(
      name,
      symbols
        .Select(symbol => overrides.TryGetValue(symbol, out var known)
          ? known
          : new IpaSymbolDefinition(
            symbol,
            description,
            GetStandaloneIpa(symbol, $"ap{symbol}a"),
            "carrier",
            $"ap{symbol}a",
            "middle",
            IpaExampleKind.Carrier))
        .ToArray());
  }

  /// <summary>
  /// Splits a space-separated IPA symbol sequence.
  /// </summary>
  private static IEnumerable<string> SplitSymbols(string symbols)
  {
    return symbols.Split(' ', StringSplitOptions.RemoveEmptyEntries);
  }

  /// <summary>
  /// Defines familiar examples for commonly used symbols.
  /// </summary>
  private static IReadOnlyDictionary<string, IpaSymbolDefinition>
    BuildOverrides()
  {
    var values = new[]
    {
      D("p", "voiceless bilabial plosive", "spin", "spɪn", "middle"),
      D("b", "voiced bilabial plosive", "bin", "bɪn", "beginning"),
      D("t", "voiceless alveolar plosive", "stop", "stɑp", "middle"),
      D("d", "voiced alveolar plosive", "dog", "dɔɡ", "beginning"),
      D("k", "voiceless velar plosive", "kite", "kaɪt", "beginning"),
      D("ɡ", "voiced velar plosive", "git", "ɡɪt", "beginning"),
      D("m", "bilabial nasal", "map", "mæp", "beginning"),
      D("n", "alveolar nasal", "net", "nɛt", "beginning"),
      D("ŋ", "velar nasal", "sing", "sɪŋ", "end"),
      D("f", "voiceless labiodental fricative", "fine", "faɪn", "beginning"),
      D("v", "voiced labiodental fricative", "vine", "vaɪn", "beginning"),
      D("θ", "voiceless dental fricative", "thin", "θɪn", "beginning"),
      D("ð", "voiced dental fricative", "this", "ðɪs", "beginning"),
      D("s", "voiceless alveolar fricative", "sip", "sɪp", "beginning"),
      D("z", "voiced alveolar fricative", "zip", "zɪp", "beginning"),
      D("ʃ", "voiceless postalveolar fricative", "ship", "ʃɪp", "beginning"),
      D("ʒ", "voiced postalveolar fricative", "vision", "vɪʒən", "middle"),
      D("h", "voiceless glottal fricative", "hat", "hæt", "beginning"),
      D("w", "voiced labial-velar approximant", "wet", "wɛt", "beginning"),
      D("ɹ", "alveolar approximant", "red", "ɹɛd", "beginning"),
      D("j", "palatal approximant", "yes", "jɛs", "beginning"),
      D("l", "alveolar lateral approximant", "let", "lɛt", "beginning"),
      D("i", "close front unrounded vowel", "fleece", "flis", "middle"),
      D("ɪ", "near-close front unrounded vowel", "kit", "kɪt", "middle"),
      D("e", "close-mid front unrounded vowel", "café", "kæfe", "end"),
      D("ɛ", "open-mid front unrounded vowel", "dress", "dɹɛs", "middle"),
      D("æ", "near-open front unrounded vowel", "cat", "kæt", "middle"),
      D("ə", "mid central vowel", "about", "əbaʊt", "beginning"),
      D("ɜ", "open-mid central unrounded vowel", "nurse", "nɜɹs", "middle"),
      D("u", "close back rounded vowel", "goose", "ɡus", "middle"),
      D("ʊ", "near-close back rounded vowel", "foot", "fʊt", "middle"),
      D("o", "close-mid back rounded vowel", "go", "ɡo", "end"),
      D("ɔ", "open-mid back rounded vowel", "thought", "θɔt", "middle"),
      D("ʌ", "open-mid back unrounded vowel", "strut", "stɹʌt", "middle"),
      D("ɑ", "open back unrounded vowel", "father", "fɑðɚ", "middle"),
      D("ɒ", "open back rounded vowel", "lot", "lɒt", "middle"),
      M("ˈ", "primary stress", "computer", "kəmˈpjutɚ", "middle"),
      M("ˌ", "secondary stress", "information", "ˌɪnfɚˈmeɪʃən", "beginning"),
      M("ː", "length mark", "machine", "məʃiːn", "middle"),
      M(".", "syllable boundary", "react", "ɹi.ækt", "middle"),
      C("̥", "voiceless", "ar̥a", "middle"),
      C("̊", "voiceless, alternate above form", "ar̊a", "middle"),
      M("̬", "voiced", "zoo", "s̬u", "beginning"),
      C("̤", "breathy voiced", "ab̤a", "middle"),
      C("̰", "creaky voiced", "a̰a", "middle"),
      C("̼", "linguolabial", "an̼a", "middle"),
      M("̪", "dental", "eighth", "eɪt̪θ", "middle"),
      C("̺", "apical", "aɾ̺a", "middle"),
      C("̻", "laminal", "as̻a", "middle"),
      C("̃", "nasalized", "ãa", "middle"),
      M("ⁿ", "nasal release", "hidden", "hɪdⁿn̩", "middle"),
      M("ˡ", "lateral release", "atlas", "ætˡləs", "middle"),
      M("̚", "no audible release", "back", "bæk̚", "end"),
      M("̴", "velarized or pharyngealized", "feel", "fil̴", "end"),
      C("̝", "raised", "ar̝a", "middle"),
      C("˔", "raised, alternate spacing form", "ar˔a", "middle"),
      C("̞", "lowered", "aβ̞a", "middle"),
      C("˕", "lowered, alternate spacing form", "aβ˕a", "middle"),
      C("̟", "advanced", "au̟a", "middle"),
      M("̠", "retracted", "goo", "ɡ̠u", "beginning"),
      C("̘", "advanced tongue root", "ai̘a", "middle"),
      M("̙", "retracted tongue root", "set", "sɛ̙t", "middle"),
      M("̈", "centralized", "roses", "ɹoʊzɪ̈z", "middle"),
      M("̽", "mid-centralized", "comma", "kɑmə̽", "end"),
      C("̹", "more rounded", "aɔ̹a", "middle"),
      C("̜", "less rounded", "aʊ̜a", "middle"),
      M("̩", "syllabic", "button", "bʌtn̩", "end"),
      C("̯", "non-syllabic", "aɪ̯a", "middle"),
      M("˞", "rhoticity", "bird", "bɜ˞d", "middle"),
      M("ʰ", "aspirated", "pin", "pʰɪn", "beginning"),
      M("ʷ", "labialized", "queen", "kʷin", "beginning"),
      C("ʲ", "palatalized", "anʲa", "middle"),
      C("ˠ", "velarized", "alˠa", "middle"),
      C("ˤ", "pharyngealized", "asˤa", "middle"),
      M("͡", "tie bar above", "church", "t͡ʃɜɹtʃ", "beginning"),
      M("͜", "tie bar below", "church", "t͜ʃɜɹtʃ", "beginning")
    };
    return values.ToDictionary(value => value.Symbol, StringComparer.Ordinal);
  }

  private static IpaSymbolDefinition D(
    string symbol,
    string description,
    string word,
    string ipa,
    string position)
  {
    return new IpaSymbolDefinition(
      symbol,
      description,
      symbol,
      word,
      ipa,
      position);
  }

  private static IpaSymbolDefinition M(
    string symbol,
    string description,
    string word,
    string ipa,
    string position)
  {
    return new IpaSymbolDefinition(
      symbol,
      description,
      GetStandaloneIpa(symbol, ipa),
      word,
      ipa,
      position);
  }

  /// <summary>
  /// Defines a synthetic carrier without presenting it as a real word.
  /// </summary>
  private static IpaSymbolDefinition C(
    string symbol,
    string description,
    string ipa,
    string position)
  {
    return new IpaSymbolDefinition(
      symbol,
      description,
      GetStandaloneIpa(symbol, ipa),
      "carrier",
      ipa,
      position,
      IpaExampleKind.Carrier);
  }

  /// <summary>
  /// Builds a compact pronounceable carrier for a modifier when possible.
  /// </summary>
  private static string? GetStandaloneIpa(string symbol, string exampleIpa)
  {
    ArgumentException.ThrowIfNullOrEmpty(symbol);
    ArgumentException.ThrowIfNullOrEmpty(exampleIpa);

    if (symbol is "ˈ" or "ˌ" or "." or "|" or "‖" or "‿")
    {
      return null;
    }

    if (symbol is "͡" or "͜")
    {
      return $"t{symbol}ʃ";
    }

    int symbolIndex = exampleIpa.IndexOf(symbol, StringComparison.Ordinal);
    if (symbolIndex < 0)
    {
      return null;
    }

    UnicodeCategory category =
      CharUnicodeInfo.GetUnicodeCategory(symbol, 0);
    if (category is
      UnicodeCategory.NonSpacingMark or
      UnicodeCategory.SpacingCombiningMark or
      UnicodeCategory.EnclosingMark)
    {
      int carrierStart = PreviousRuneStart(exampleIpa, symbolIndex);
      return exampleIpa[carrierStart..(symbolIndex + symbol.Length)];
    }

    if (category == UnicodeCategory.ModifierLetter && symbolIndex > 0)
    {
      int carrierStart = PreviousRuneStart(exampleIpa, symbolIndex);
      return exampleIpa[carrierStart..(symbolIndex + symbol.Length)];
    }

    return $"a{symbol}";
  }

  /// <summary>
  /// Finds the UTF-16 start of the rune immediately before an index.
  /// </summary>
  private static int PreviousRuneStart(string text, int index)
  {
    int previous = index - 1;
    if (previous > 0 &&
        char.IsLowSurrogate(text[previous]) &&
        char.IsHighSurrogate(text[previous - 1]))
    {
      previous--;
    }

    return Math.Max(0, previous);
  }

  /// <summary>
  /// Adds a dotted-circle carrier when a standalone combining mark is shown.
  /// </summary>
  public static string GetDisplaySymbol(string value)
  {
    ArgumentException.ThrowIfNullOrEmpty(value);
    if (value is "͡" or "͜")
    {
      return $"◌{value}◌";
    }

    UnicodeCategory category =
      CharUnicodeInfo.GetUnicodeCategory(value, 0);
    return category is
      UnicodeCategory.NonSpacingMark or
      UnicodeCategory.SpacingCombiningMark or
      UnicodeCategory.EnclosingMark
        ? $"◌{value}"
        : value;
  }

  /// <summary>
  /// Gets Unicode code points for a tooltip.
  /// </summary>
  public static string GetCodePoints(string value)
  {
    return string.Join(
      " ",
      value.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}"));
  }
}

/// <summary>
/// Defines one named toolbar group.
/// </summary>
internal sealed record IpaSymbolGroup(
  string Name,
  IReadOnlyList<IpaSymbolDefinition> Symbols);

/// <summary>
/// Defines insertion, tooltip, display, and preview data for one IPA symbol.
/// </summary>
internal sealed record IpaSymbolDefinition(
  string Symbol,
  string Description,
  string? StandaloneIpa,
  string ExampleWord,
  string ExampleIpa,
  string Position,
  IpaExampleKind ExampleKind = IpaExampleKind.Word)
{
  /// <summary>
  /// Gets ordinary text that can be used only when a real-word IPA example
  /// is rejected by the selected voice.
  /// </summary>
  public string? ExampleFallbackText =>
    ExampleKind == IpaExampleKind.Word ? ExampleWord : null;
}

/// <summary>
/// Identifies whether an IPA example is a real displayed word or a carrier.
/// </summary>
internal enum IpaExampleKind
{
  Word,
  Carrier
}
