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
    return new IpaSymbolGroup[]
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
  }

  /// <summary>
  /// Creates a consonant group using the apa carrier word by default.
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
            "apa",
            $"a{symbol}a",
            "middle"))
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
            "h–d carrier",
            $"h{symbol}d",
            "middle"))
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
            "apa",
            $"ap{symbol}a",
            "middle"))
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
      D("k", "voiceless velar plosive", "cat", "kæt", "beginning"),
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
      M("ː", "length mark", "fleece", "fliːs", "middle"),
      M(".", "syllable boundary", "button", "bʌt.ən", "middle"),
      M("̥", "voiceless", "play", "pʰl̥eɪ", "middle"),
      M("̊", "voiceless, alternate above form", "cream", "kɹ̊im", "middle"),
      M("̬", "voiced", "zoo", "s̬u", "beginning"),
      M("̤", "breathy voiced", "bhai", "b̤aɪ", "beginning"),
      M("̰", "creaky voiced", "uh-oh", "ʌ̰ʔoʊ", "beginning"),
      M("̼", "linguolabial", "mana", "n̼ana", "beginning"),
      M("̪", "dental", "eighth", "eɪt̪θ", "middle"),
      M("̺", "apical", "pero", "peɾ̺o", "middle"),
      M("̻", "laminal", "see", "s̻i", "beginning"),
      M("̃", "nasalized", "sans", "sɑ̃", "end"),
      M("ⁿ", "nasal release", "hidden", "hɪdⁿn̩", "middle"),
      M("ˡ", "lateral release", "atlas", "ætˡləs", "middle"),
      M("̚", "no audible release", "cat", "kæt̚", "end"),
      M("̴", "velarized or pharyngealized", "feel", "fil̴", "end"),
      M("̝", "raised", "Dvořák", "ˈdvor̝aːk", "middle"),
      M("˔", "raised, alternate spacing form", "Dvořák", "ˈdvoɹ˔aːk", "middle"),
      M("̞", "lowered", "lobo", "loβ̞o", "middle"),
      M("˕", "lowered, alternate spacing form", "lobo", "loβ˕o", "middle"),
      M("̟", "advanced", "goose", "ɡu̟s", "middle"),
      M("̠", "retracted", "goo", "ɡ̠u", "beginning"),
      M("̘", "advanced tongue root", "see", "si̘", "middle"),
      M("̙", "retracted tongue root", "set", "sɛ̙t", "middle"),
      M("̈", "centralized", "roses", "ɹoʊzɪ̈z", "middle"),
      M("̽", "mid-centralized", "comma", "kɑmə̽", "end"),
      M("̹", "more rounded", "thought", "θɔ̹t", "middle"),
      M("̜", "less rounded", "foot", "fʊ̜t", "middle"),
      M("̩", "syllabic", "button", "bʌtn̩", "end"),
      M("̯", "non-syllabic", "boy", "bɔɪ̯", "end"),
      M("˞", "rhoticity", "bird", "bɜ˞d", "middle"),
      M("ʰ", "aspirated", "pin", "pʰɪn", "beginning"),
      M("ʷ", "labialized", "queen", "kʷin", "beginning"),
      M("ʲ", "palatalized", "nyet", "nʲet", "beginning"),
      M("ˠ", "velarized", "bád", "bˠaːdˠ", "beginning and end"),
      M("ˤ", "pharyngealized", "ṣād", "sˤaːd", "beginning"),
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
  string Position);
