using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Parses and resolves explicit IPA pronunciation overrides.
/// </summary>
internal sealed class PronunciationRuleSet
{
  private PronunciationRuleSet(
    IReadOnlyList<PronunciationRule> rules,
    IReadOnlyList<string> errors)
  {
    Rules = rules;
    Errors = errors;
    NormalizedText = string.Join(
      Environment.NewLine,
      rules.Select(rule =>
        $"{rule.Token}{(rule.IgnoreCase ? "/i" : string.Empty)}=ipa:{rule.Ipa}"));
  }

  /// <summary>
  /// Gets normalized rules in source order after duplicate replacement.
  /// </summary>
  public IReadOnlyList<PronunciationRule> Rules { get; }

  /// <summary>
  /// Gets human-readable parse failures.
  /// </summary>
  public IReadOnlyList<string> Errors { get; }

  /// <summary>
  /// Gets normalized one-rule-per-line text.
  /// </summary>
  public string NormalizedText { get; }

  /// <summary>
  /// Finds the earliest whole-token rule match at or after one position.
  /// </summary>
  public PronunciationMatch? FindNext(string text, int start)
  {
    PronunciationMatch? best = null;
    foreach (PronunciationRule rule in Rules)
    {
      Match match = rule.Matcher.Match(text, start);
      if (!match.Success)
      {
        continue;
      }
      var candidate = new PronunciationMatch(match, rule);
      if (best is null ||
          candidate.Match.Index < best.Match.Index ||
          candidate.Match.Index == best.Match.Index &&
          candidate.Match.Length > best.Match.Length ||
          candidate.Match.Index == best.Match.Index &&
          candidate.Match.Length == best.Match.Length &&
          !candidate.Rule.IgnoreCase && best.Rule.IgnoreCase)
      {
        best = candidate;
      }
    }
    return best;
  }

  /// <summary>
  /// Parses token=ipa:phones and token/i=ipa:phones lines.
  /// </summary>
  public static PronunciationRuleSet Parse(string? text)
  {
    var parsed = new List<PronunciationRule>();
    var errors = new List<string>();
    var keyToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
    string[] lines = (text ?? string.Empty)
      .Replace("\r\n", "\n")
      .Replace('\r', '\n')
      .Split('\n');

    for (int lineIndex = 0; lineIndex < lines.Length; ++lineIndex)
    {
      string line = lines[lineIndex].Trim();
      if (line.Length == 0)
      {
        continue;
      }

      int equals = line.IndexOf('=');
      if (equals <= 0)
      {
        errors.Add($"Line {lineIndex + 1}: expected name=ipa:pronunciation.");
        continue;
      }

      string left = line[..equals].Trim();
      string right = line[(equals + 1)..].Trim();
      bool ignoreCase = left.EndsWith("/i", StringComparison.Ordinal);
      string token = ignoreCase ? left[..^2].Trim() : left;
      if (token.Length == 0)
      {
        errors.Add($"Line {lineIndex + 1}: the name is empty.");
        continue;
      }
      if (!right.StartsWith("ipa:", StringComparison.OrdinalIgnoreCase))
      {
        errors.Add($"Line {lineIndex + 1}: the value must begin with ipa:.");
        continue;
      }

      string ipa = right[4..].Trim();
      if (ipa.Length == 0)
      {
        errors.Add($"Line {lineIndex + 1}: the IPA pronunciation is empty.");
        continue;
      }

      var rule = PronunciationRule.Create(token, ipa, ignoreCase);
      string key = (ignoreCase ? "i:" : "e:") +
        (ignoreCase ? token.ToUpperInvariant() : token);
      if (keyToIndex.TryGetValue(key, out int existing))
      {
        parsed[existing] = rule;
      }
      else
      {
        keyToIndex.Add(key, parsed.Count);
        parsed.Add(rule);
      }
    }

    return new PronunciationRuleSet(parsed, errors);
  }
}

/// <summary>
/// Defines one whole-token IPA pronunciation override.
/// </summary>
internal sealed record PronunciationRule(
  string Token,
  string Ipa,
  bool IgnoreCase,
  Regex Matcher)
{
  /// <summary>
  /// Creates a rule with a whole-token matcher.
  /// </summary>
  public static PronunciationRule Create(
    string token,
    string ipa,
    bool ignoreCase)
  {
    string pattern =
      $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(token)}" +
      @"(?![\p{L}\p{N}_])";
    RegexOptions options = RegexOptions.CultureInvariant;
    if (ignoreCase)
    {
      options |= RegexOptions.IgnoreCase;
    }
    return new PronunciationRule(
      token,
      ipa,
      ignoreCase,
      new Regex(pattern, options));
  }
}

/// <summary>
/// Couples a text match with its pronunciation rule.
/// </summary>
internal sealed record PronunciationMatch(
  Match Match,
  PronunciationRule Rule);
