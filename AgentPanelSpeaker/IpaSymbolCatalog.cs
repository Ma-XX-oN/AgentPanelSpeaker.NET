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
  /// Creates a consonant group using a carrier syllable by default.
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
            "carrier syllable",
            $"{symbol}a",
            $"[{symbol}]a",
            "beginning",
            CanSoundAlone: true))
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
            "carrier syllable",
            $"h{symbol}d",
            $"h[{symbol}]d",
            "middle",
            CanSoundAlone: true))
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
            "carrier example",
            $"a{symbol}",
            $"a{symbol}",
            "modifier",
            CanSoundAlone: false))
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
      D("p", "voiceless bilabial plosive", "spin", "spɪn", "s[p]ɪn", "middle"),
      D("b", "voiced bilabial plosive", "bin", "bɪn", "[b]ɪn", "beginning"),
      D("t", "voiceless alveolar plosive", "stop", "stɑp", "s[t]ɑp", "middle"),
      D("d", "voiced alveolar plosive", "dog", "dɔɡ", "[d]ɔɡ", "beginning"),
      D("k", "voiceless velar plosive", "cat", "kæt", "[k]æt", "beginning"),
      D("ɡ", "voiced velar plosive", "git", "ɡɪt", "[ɡ]ɪt", "beginning"),
      D("m", "bilabial nasal", "map", "mæp", "[m]æp", "beginning"),
      D("n", "alveolar nasal", "net", "nɛt", "[n]ɛt", "beginning"),
      D("ŋ", "velar nasal", "sing", "sɪŋ", "sɪ[ŋ]", "end"),
      D("f", "voiceless labiodental fricative", "fine", "faɪn", "[f]aɪn", "beginning"),
      D("v", "voiced labiodental fricative", "vine", "vaɪn", "[v]aɪn", "beginning"),
      D("θ", "voiceless dental fricative", "thin", "θɪn", "[θ]ɪn", "beginning"),
      D("ð", "voiced dental fricative", "this", "ðɪs", "[ð]ɪs", "beginning"),
      D("s", "voiceless alveolar fricative", "sip", "sɪp", "[s]ɪp", "beginning"),
      D("z", "voiced alveolar fricative", "zip", "zɪp", "[z]ɪp", "beginning"),
      D("ʃ", "voiceless postalveolar fricative", "ship", "ʃɪp", "[ʃ]ɪp", "beginning"),
      D("ʒ", "voiced postalveolar fricative", "vision", "vɪʒən", "vɪ[ʒ]ən", "middle"),
      D("h", "voiceless glottal fricative", "hat", "hæt", "[h]æt", "beginning"),
      D("w", "voiced labial-velar approximant", "wet", "wɛt", "[w]ɛt", "beginning"),
      D("ɹ", "alveolar approximant", "red", "ɹɛd", "[ɹ]ɛd", "beginning"),
      D("j", "palatal approximant", "yes", "jɛs", "[j]ɛs", "beginning"),
      D("l", "alveolar lateral approximant", "let", "lɛt", "[l]ɛt", "beginning"),
      D("i", "close front unrounded vowel", "fleece", "flis", "fl[i]s", "middle"),
      D("ɪ", "near-close front unrounded vowel", "kit", "kɪt", "k[ɪ]t", "middle"),
      D("e", "close-mid front unrounded vowel", "café", "kæfe", "kæf[e]", "end"),
      D("ɛ", "open-mid front unrounded vowel", "dress", "dɹɛs", "dɹ[ɛ]s", "middle"),
      D("æ", "near-open front unrounded vowel", "cat", "kæt", "k[æ]t", "middle"),
      D("ə", "mid central vowel", "about", "əbaʊt", "[ə]baʊt", "beginning"),
      D("ɜ", "open-mid central unrounded vowel", "nurse", "nɜɹs", "n[ɜ]ɹs", "middle"),
      D("u", "close back rounded vowel", "goose", "ɡus", "ɡ[u]s", "middle"),
      D("ʊ", "near-close back rounded vowel", "foot", "fʊt", "f[ʊ]t", "middle"),
      D("o", "close-mid back rounded vowel", "go", "ɡo", "ɡ[o]", "end"),
      D("ɔ", "open-mid back rounded vowel", "thought", "θɔt", "θ[ɔ]t", "middle"),
      D("ʌ", "open-mid back unrounded vowel", "strut", "stɹʌt", "stɹ[ʌ]t", "middle"),
      D("ɑ", "open back unrounded vowel", "father", "fɑðɚ", "f[ɑ]ðɚ", "middle"),
      D("ɒ", "open back rounded vowel", "lot", "lɒt", "l[ɒ]t", "middle"),
      M("ˈ", "primary stress", "computer", "kəmˈpjutɚ", "kəm[ˈpju]tɚ", "middle"),
      M("ˌ", "secondary stress", "information", "ˌɪnfɚˈmeɪʃən", "[ˌɪn]fɚˈmeɪʃən", "beginning"),
      M("ː", "length mark", "fleece", "fliːs", "fl[iː]s", "middle"),
      M(".", "syllable boundary", "button", "bʌt.ən", "bʌt[.]ən", "middle"),
      M("̥", "voiceless", "play", "pʰl̥eɪ", "pʰl̥eɪ", "middle"),
      M("̊", "voiceless, alternate above form", "cream", "kɹ̊im", "kɹ̊im", "middle"),
      M("̬", "voiced", "zoo", "s̬u", "s̬u", "beginning"),
      M("̤", "breathy voiced", "bhai", "b̤aɪ", "b̤aɪ", "beginning"),
      M("̰", "creaky voiced", "uh-oh", "ʌ̰ʔoʊ", "ʌ̰ʔoʊ", "beginning"),
      M("̼", "linguolabial", "mana", "n̼ana", "n̼ana", "beginning"),
      M("̪", "dental", "eighth", "eɪt̪θ", "eɪt̪θ", "middle"),
      M("̺", "apical", "pero", "peɾ̺o", "peɾ̺o", "middle"),
      M("̻", "laminal", "see", "s̻i", "s̻i", "beginning"),
      M("̃", "nasalized", "sans", "sɑ̃", "sɑ̃", "end"),
      M("ⁿ", "nasal release", "hidden", "hɪdⁿn̩", "hɪdⁿn̩", "middle"),
      M("ˡ", "lateral release", "atlas", "ætˡləs", "ætˡləs", "middle"),
      M("̚", "no audible release", "cat", "kæt̚", "kæt̚", "end"),
      M("̴", "velarized or pharyngealized", "feel", "fil̴", "fil̴", "end"),
      M("̝", "raised", "Dvořák", "ˈdvor̝aːk", "ˈdvor̝aːk", "middle"),
      M("˔", "raised, alternate spacing form", "Dvořák", "ˈdvoɹ˔aːk", "ˈdvoɹ˔aːk", "middle"),
      M("̞", "lowered", "lobo", "loβ̞o", "loβ̞o", "middle"),
      M("˕", "lowered, alternate spacing form", "lobo", "loβ˕o", "loβ˕o", "middle"),
      M("̟", "advanced", "goose", "ɡu̟s", "ɡu̟s", "middle"),
      M("̠", "retracted", "goo", "ɡ̠u", "ɡ̠u", "beginning"),
      M("̘", "advanced tongue root", "see", "si̘", "si̘", "middle"),
      M("̙", "retracted tongue root", "set", "sɛ̙t", "sɛ̙t", "middle"),
      M("̈", "centralized", "roses", "ɹoʊzɪ̈z", "ɹoʊzɪ̈z", "middle"),
      M("̽", "mid-centralized", "comma", "kɑmə̽", "kɑmə̽", "end"),
      M("̹", "more rounded", "thought", "θɔ̹t", "θɔ̹t", "middle"),
      M("̜", "less rounded", "foot", "fʊ̜t", "fʊ̜t", "middle"),
      M("̩", "syllabic", "button", "bʌtn̩", "bʌtn̩", "end"),
      M("̯", "non-syllabic", "boy", "bɔɪ̯", "bɔɪ̯", "end"),
      M("˞", "rhoticity", "bird", "bɜ˞d", "bɜ˞d", "middle"),
      M("ʰ", "aspirated", "pin", "pʰɪn", "pʰɪn", "beginning"),
      M("ʷ", "labialized", "queen", "kʷin", "kʷin", "beginning"),
      M("ʲ", "palatalized", "nyet", "nʲet", "nʲet", "beginning"),
      M("ˠ", "velarized", "bád", "bˠaːdˠ", "bˠaːdˠ", "beginning and end"),
      M("ˤ", "pharyngealized", "ṣād", "sˤaːd", "sˤaːd", "beginning"),
      M("͡", "tie bar above", "church", "t͡ʃɜɹtʃ", "t͡ʃɜɹtʃ", "beginning"),
      M("͜", "tie bar below", "church", "t͜ʃɜɹtʃ", "t͜ʃɜɹtʃ", "beginning")
    };
    return values.ToDictionary(value => value.Symbol, StringComparer.Ordinal);
  }

  private static IpaSymbolDefinition D(
    string symbol,
    string description,
    string word,
    string ipa,
    string highlighted,
    string position)
  {
    return new IpaSymbolDefinition(
      symbol,
      description,
      word,
      ipa,
      highlighted,
      position,
      CanSoundAlone: true);
  }

  private static IpaSymbolDefinition M(
    string symbol,
    string description,
    string word,
    string ipa,
    string highlighted,
    string position)
  {
    return new IpaSymbolDefinition(
      symbol,
      description,
      word,
      ipa,
      highlighted,
      position,
      CanSoundAlone: false);
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
  string ExampleWord,
  string ExampleIpa,
  string HighlightedExampleIpa,
  string Position,
  bool CanSoundAlone);
