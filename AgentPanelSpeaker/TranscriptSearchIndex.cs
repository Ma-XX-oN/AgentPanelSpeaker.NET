using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentPanelSpeaker;

/// <summary>
/// Owns the C# transcript search corpus and compact rendered-token mapping.
/// </summary>
internal sealed class TranscriptSearchIndex
{
  private static readonly Regex HtmlPartRegex = new(
    "<[^>]+>|[^<]+",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
  private static readonly Regex TokenRegex = new(
    @"[\p{L}\p{N}_]+(?:['’\-][\p{L}\p{N}_]+)*|[^\s\p{L}\p{N}_]",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
  private static readonly Regex RecordRegex = new(
    "class=\\\"record-anchor\\\"[^>]*data-jsonl-record=\\\"(?<record>[^\\\"]*)\\\"[^>]*data-source-id=\\\"(?<source>[^\\\"]*)\\\"",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private readonly string _allText;
  private readonly string _voicedText;
  private readonly SearchToken[] _allTokens;
  private readonly SearchToken[] _voicedTokens;

  private TranscriptSearchIndex(
    string allText,
    SearchToken[] allTokens,
    string voicedText,
    SearchToken[] voicedTokens)
  {
    _allText = allText;
    _allTokens = allTokens;
    _voicedText = voicedText;
    _voicedTokens = voicedTokens;
  }

  /// <summary>
  /// Builds the search corpus directly from rendered HTML and node identities.
  /// </summary>
  public static TranscriptSearchIndex Build(
    string html,
    IReadOnlyList<TranscriptNodeIdentity> identities,
    CancellationToken cancellationToken)
  {
    var tokens = new List<MutableToken>();
    int recordNumber = 0;
    string sourceId = string.Empty;
    foreach (Match part in HtmlPartRegex.Matches(html))
    {
      cancellationToken.ThrowIfCancellationRequested();
      string value = part.Value;
      if (value.StartsWith('<'))
      {
        Match anchor = RecordRegex.Match(value);
        if (anchor.Success)
        {
          _ = int.TryParse(
            WebUtility.HtmlDecode(anchor.Groups["record"].Value),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out recordNumber);
          sourceId = WebUtility.HtmlDecode(anchor.Groups["source"].Value);
        }
        continue;
      }

      string text = WebUtility.HtmlDecode(value);
      foreach (Match token in TokenRegex.Matches(text))
      {
        tokens.Add(new MutableToken(
          token.Value,
          recordNumber,
          sourceId,
          tokens.Count));
      }
    }

    MarkVoicedTokens(tokens, identities, cancellationToken);
    SearchToken[] allTokens = BuildCorpus(tokens, voicedOnly: false, out string allText);
    SearchToken[] voicedTokens = BuildCorpus(tokens, voicedOnly: true, out string voicedText);
    return new TranscriptSearchIndex(allText, allTokens, voicedText, voicedTokens);
  }

  /// <summary>
  /// Finds matches without touching the WebView DOM.
  /// </summary>
  public async Task<IReadOnlyList<TranscriptSearchMatch>> SearchAsync(
    TranscriptSearchRequest request,
    CancellationToken cancellationToken)
  {
    string text = request.VoicedOnly ? _voicedText : _allText;
    SearchToken[] tokens = request.VoicedOnly ? _voicedTokens : _allTokens;
    IReadOnlyList<TextMatch> raw = request.Regex
      ? await RegexWorkerClient.SearchAsync(
          text,
          request.Query,
          request.CaseSensitive,
          request.WholeWord,
          cancellationToken)
      : await Task.Run(
          () => FindLiteral(text, request, cancellationToken),
          cancellationToken);
    return MapMatches(raw, tokens);
  }

  private static IReadOnlyList<TextMatch> FindLiteral(
    string text,
    TranscriptSearchRequest request,
    CancellationToken cancellationToken)
  {
    var matches = new List<TextMatch>();
    StringComparison comparison = request.CaseSensitive
      ? StringComparison.Ordinal
      : StringComparison.OrdinalIgnoreCase;
    int position = 0;
    while (position <= text.Length - request.Query.Length)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int found = text.IndexOf(request.Query, position, comparison);
      if (found < 0)
      {
        break;
      }
      int end = found + request.Query.Length;
      if (!request.WholeWord ||
          (IsBoundary(text, found - 1) && IsBoundary(text, end)))
      {
        matches.Add(new TextMatch(found, request.Query.Length));
      }
      position = found + Math.Max(1, request.Query.Length);
    }
    return matches;
  }

  private static bool IsBoundary(string text, int index)
  {
    return index < 0 || index >= text.Length ||
      !(char.IsLetterOrDigit(text[index]) || text[index] == '_');
  }

  private static IReadOnlyList<TranscriptSearchMatch> MapMatches(
    IReadOnlyList<TextMatch> raw,
    SearchToken[] tokens)
  {
    var result = new List<TranscriptSearchMatch>(raw.Count);
    foreach (TextMatch match in raw)
    {
      int first = FirstEndingAfter(tokens, match.Start);
      if (first >= tokens.Length)
      {
        continue;
      }
      int matchEnd = match.Start + Math.Max(1, match.Length);
      int last = first;
      while (last + 1 < tokens.Length && tokens[last + 1].Start < matchEnd)
      {
        last++;
      }
      SearchToken firstToken = tokens[first];
      SearchToken voiced = firstToken;
      for (int index = first; index <= last; ++index)
      {
        if (tokens[index].NodeId > 0 && tokens[index].NodeWordIndex >= 0)
        {
          voiced = tokens[index];
          break;
        }
      }
      result.Add(new TranscriptSearchMatch(
        firstToken.RenderedIndex,
        tokens[last].RenderedIndex,
        voiced.NodeId,
        voiced.NodeWordIndex));
    }
    return result;
  }

  private static int FirstEndingAfter(SearchToken[] tokens, int position)
  {
    int low = 0;
    int high = tokens.Length;
    while (low < high)
    {
      int middle = (low + high) >>> 1;
      if (tokens[middle].End <= position)
      {
        low = middle + 1;
      }
      else
      {
        high = middle;
      }
    }
    return low;
  }

  private static SearchToken[] BuildCorpus(
    IReadOnlyList<MutableToken> source,
    bool voicedOnly,
    out string text)
  {
    var builder = new StringBuilder();
    var result = new List<SearchToken>();
    foreach (MutableToken token in source)
    {
      if (voicedOnly && token.NodeId <= 0)
      {
        continue;
      }
      if (builder.Length != 0)
      {
        builder.Append(' ');
      }
      int start = builder.Length;
      builder.Append(token.Text);
      result.Add(new SearchToken(
        start,
        builder.Length,
        token.RenderedIndex,
        token.NodeId,
        token.NodeWordIndex));
    }
    text = builder.ToString();
    return result.ToArray();
  }

  private static void MarkVoicedTokens(
    IReadOnlyList<MutableToken> tokens,
    IReadOnlyList<TranscriptNodeIdentity> identities,
    CancellationToken cancellationToken)
  {
    var cursors = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (TranscriptNodeIdentity identity in identities)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string key = MakeKey(identity.RecordNumber, identity.SourceId);
      int cursor = cursors.TryGetValue(key, out int value) ? value : 0;
      int nodeWordIndex = 0;
      foreach (string segment in identity.Segments)
      {
        string[] target = TokenRegex.Matches(segment)
          .Cast<Match>()
          .Select(match => match.Value.ToLowerInvariant())
          .ToArray();
        if (target.Length == 0)
        {
          continue;
        }
        int start = FindTokenSequence(tokens, key, target, cursor);
        if (start < 0 && cursor > 0)
        {
          start = FindTokenSequence(tokens, key, target, 0);
        }
        if (start < 0)
        {
          continue;
        }
        for (int index = start; index < start + target.Length; ++index)
        {
          tokens[index].NodeId = identity.NodeId;
          tokens[index].NodeWordIndex = IsLexical(tokens[index].Text)
            ? nodeWordIndex++
            : -1;
        }
        cursor = start + target.Length;
        cursors[key] = cursor;
      }
    }
  }

  private static int FindTokenSequence(
    IReadOnlyList<MutableToken> tokens,
    string key,
    IReadOnlyList<string> target,
    int start)
  {
    for (int index = Math.Max(0, start);
         index <= tokens.Count - target.Count;
         ++index)
    {
      if (!string.Equals(tokens[index].RecordKey, key, StringComparison.Ordinal))
      {
        continue;
      }
      bool equal = true;
      for (int offset = 0; offset < target.Count; ++offset)
      {
        MutableToken candidate = tokens[index + offset];
        if (!string.Equals(candidate.RecordKey, key, StringComparison.Ordinal) ||
            !string.Equals(
              candidate.Text,
              target[offset],
              StringComparison.OrdinalIgnoreCase))
        {
          equal = false;
          break;
        }
      }
      if (equal)
      {
        return index;
      }
    }
    return -1;
  }

  private static bool IsLexical(string text)
  {
    return text.Any(character => char.IsLetterOrDigit(character) || character == '_');
  }

  private static string MakeKey(int recordNumber, string sourceId)
  {
    return sourceId + "\0" + recordNumber.ToString(CultureInfo.InvariantCulture);
  }

  private sealed class MutableToken
  {
    public MutableToken(string text, int recordNumber, string sourceId, int renderedIndex)
    {
      Text = text;
      RecordKey = MakeKey(recordNumber, sourceId);
      RenderedIndex = renderedIndex;
    }

    public string Text { get; }
    public string RecordKey { get; }
    public int RenderedIndex { get; }
    public long NodeId { get; set; }
    public int NodeWordIndex { get; set; } = -1;
  }

  private readonly record struct SearchToken(
    int Start,
    int End,
    int RenderedIndex,
    long NodeId,
    int NodeWordIndex);
}

internal sealed record TranscriptSearchRequest(
  long RequestId,
  string Query,
  bool CaseSensitive,
  bool WholeWord,
  bool Regex,
  bool VoicedOnly);

internal sealed record TranscriptSearchMatch(
  int StartWordIndex,
  int EndWordIndex,
  long NodeId,
  int NodeWordIndex);

internal readonly record struct TextMatch(int Start, int Length);

internal static class RegexWorkerClient
{
  public static async Task<IReadOnlyList<TextMatch>> SearchAsync(
    string text,
    string pattern,
    bool caseSensitive,
    bool wholeWord,
    CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable."),
      Arguments = "--regex-search-worker",
      UseShellExecute = false,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true
    };
    using var process = new Process { StartInfo = startInfo };
    if (!process.Start())
    {
      throw new InvalidOperationException("Regex worker failed to start.");
    }
    try
    {
      var request = new RegexWorkerRequest(text, pattern, caseSensitive, wholeWord);
      await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
      process.StandardInput.Close();
      using CancellationTokenRegistration registration = cancellationToken.Register(() =>
      {
        try
        {
          if (!process.HasExited)
          {
            process.Kill(entireProcessTree: true);
          }
        }
        catch (InvalidOperationException)
        {
        }
      });
      string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken);
      cancellationToken.ThrowIfCancellationRequested();
      RegexWorkerResponse? response = line is null
        ? null
        : JsonSerializer.Deserialize<RegexWorkerResponse>(line);
      if (response is null)
      {
        throw new InvalidOperationException("Regex worker returned no result.");
      }
      if (!string.IsNullOrEmpty(response.Error))
      {
        throw new ArgumentException(response.Error);
      }
      return response.Matches ?? Array.Empty<TextMatch>();
    }
    finally
    {
    }
  }
}

internal sealed record RegexWorkerRequest(
  string Text,
  string Pattern,
  bool CaseSensitive,
  bool WholeWord);

internal sealed record RegexWorkerResponse(
  IReadOnlyList<TextMatch>? Matches,
  string? Error);

internal static class RegexSearchWorker
{
  public static int Run()
  {
    try
    {
      string? line = Console.In.ReadLine();
      RegexWorkerRequest request = JsonSerializer.Deserialize<RegexWorkerRequest>(
        line ?? throw new InvalidOperationException("Missing request.")) ??
        throw new InvalidOperationException("Invalid request.");
      string pattern = request.WholeWord
        ? $@"(?<![\p{{L}}\p{{N}}_])(?:{request.Pattern})(?![\p{{L}}\p{{N}}_])"
        : request.Pattern;
      RegexOptions options = RegexOptions.CultureInvariant;
      if (!request.CaseSensitive)
      {
        options |= RegexOptions.IgnoreCase;
      }
      var regex = new Regex(pattern, options);
      var matches = new List<TextMatch>();
      foreach (Match match in regex.Matches(request.Text))
      {
        matches.Add(new TextMatch(match.Index, match.Length));
      }
      Console.Out.WriteLine(JsonSerializer.Serialize(
        new RegexWorkerResponse(matches, null)));
      return 0;
    }
    catch (Exception exception)
    {
      Console.Out.WriteLine(JsonSerializer.Serialize(
        new RegexWorkerResponse(null, exception.Message)));
      return 1;
    }
  }
}
