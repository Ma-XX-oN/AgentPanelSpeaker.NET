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
  private static readonly Regex HtmlTagRegex = new(
    @"^<\s*(?<close>/)?\s*(?<name>[A-Za-z0-9]+)",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
  private static readonly Regex TokenRegex = new(
    @"[\p{L}\p{N}_]+(?:['’\-][\p{L}\p{N}_]+)*|[^\s\p{L}\p{N}_]",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
  private static readonly Regex RecordRegex = new(
    "class=\\\"record-anchor\\\"[^>]*data-jsonl-record=\\\"(?<record>[^\\\"]*)\\\"[^>]*data-source-id=\\\"(?<source>[^\\\"]*)\\\"",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
  private static readonly HashSet<string> BlockTags = new(
    new[] { "p", "li", "h1", "h2", "h3", "h4", "h5", "h6", "pre", "summary" },
    StringComparer.OrdinalIgnoreCase);

  private readonly SearchRecord[] _allRecords;
  private readonly SearchRecord[] _voicedRecords;

  private TranscriptSearchIndex(
    SearchRecord[] allRecords,
    SearchRecord[] voicedRecords)
  {
    _allRecords = allRecords;
    _voicedRecords = voicedRecords;
  }

  /// <summary>
  /// Builds the search corpus directly from rendered HTML and node identities.
  /// Paragraphs, list items, headings, whole code blocks, and disclosure
  /// summaries are independent regex blocks.  A newline separates adjacent
  /// blocks within a record; records are searched independently so no match
  /// can cross a record boundary.
  /// </summary>
  public static TranscriptSearchIndex Build(
    string html,
    IReadOnlyList<TranscriptNodeIdentity> identities,
    CancellationToken cancellationToken)
  {
    var tokens = new List<MutableToken>();
    var blockStack = new Stack<BlockContext>();
    int nextBlockId = 0;
    int implicitBlockId = -1;
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
          blockStack.Clear();
          implicitBlockId = -1;
        }

        Match tag = HtmlTagRegex.Match(value);
        if (!tag.Success)
        {
          continue;
        }
        string name = tag.Groups["name"].Value;
        bool closing = tag.Groups["close"].Success;
        if (closing && BlockTags.Contains(name))
        {
          PopThroughTag(blockStack, name);
          implicitBlockId = -1;
        }
        else if (!closing && BlockTags.Contains(name))
        {
          int openedBlockId = ++nextBlockId;
          blockStack.Push(new BlockContext(name, openedBlockId));
          implicitBlockId = -1;
        }
        continue;
      }

      string text = WebUtility.HtmlDecode(value);
      if (string.IsNullOrWhiteSpace(text))
      {
        continue;
      }
      int blockId;
      if (blockStack.Count != 0)
      {
        blockId = blockStack.Peek().BlockId;
      }
      else
      {
        if (implicitBlockId < 0)
        {
          implicitBlockId = ++nextBlockId;
        }
        blockId = implicitBlockId;
      }

      foreach (Match token in TokenRegex.Matches(text))
      {
        int localIndex = tokens.Count == 0 ||
          tokens[^1].RecordNumber != recordNumber ||
          !string.Equals(tokens[^1].SourceId, sourceId, StringComparison.Ordinal)
            ? 0
            : tokens[^1].RecordWordIndex + 1;
        tokens.Add(new MutableToken(
          token.Value,
          recordNumber,
          sourceId,
          blockId,
          tokens.Count,
          localIndex));
      }
    }

    MarkVoicedTokens(tokens, identities, cancellationToken);
    SearchRecord[] allRecords = BuildCorpus(tokens, voicedOnly: false);
    SearchRecord[] voicedRecords = BuildCorpus(tokens, voicedOnly: true);
    return new TranscriptSearchIndex(allRecords, voicedRecords);
  }

  /// <summary>
  /// Resolves an authoritative speech node/word position to its rendered
  /// transcript search coordinates.
  /// </summary>
  public bool TryResolveVoiceOrigin(
    long nodeId,
    int nodeWordIndex,
    out int recordNumber,
    out string sourceId,
    out int recordWordIndex)
  {
    foreach (SearchRecord record in _voicedRecords)
    {
      foreach (SearchToken token in record.Tokens)
      {
        if (token.NodeId == nodeId && token.NodeWordIndex == nodeWordIndex)
        {
          recordNumber = token.RecordNumber;
          sourceId = token.SourceId;
          recordWordIndex = token.RecordWordIndex;
          return true;
        }
      }
    }
    recordNumber = 0;
    sourceId = string.Empty;
    recordWordIndex = -1;
    return false;
  }

  /// <summary>
  /// Finds matches without touching the WebView DOM.
  /// </summary>
  public async Task<IReadOnlyList<TranscriptSearchMatch>> SearchAsync(
    TranscriptSearchRequest request,
    CancellationToken cancellationToken)
  {
    SearchRecord[] records = request.VoicedOnly ? _voicedRecords : _allRecords;
    IReadOnlyList<RecordTextMatch> raw = request.Regex
      ? await RegexWorkerClient.SearchAsync(
          records.Select(record => record.Text).ToArray(),
          request.Query,
          request.CaseSensitive,
          request.WholeWord,
          cancellationToken)
      : await Task.Run(
          () => FindLiteral(records, request, cancellationToken),
          cancellationToken);
    return MapMatches(raw, records);
  }

  private static IReadOnlyList<RecordTextMatch> FindLiteral(
    IReadOnlyList<SearchRecord> records,
    TranscriptSearchRequest request,
    CancellationToken cancellationToken)
  {
    var matches = new List<RecordTextMatch>();
    StringComparison comparison = request.CaseSensitive
      ? StringComparison.Ordinal
      : StringComparison.OrdinalIgnoreCase;
    for (int recordIndex = 0; recordIndex < records.Count; ++recordIndex)
    {
      string text = records[recordIndex].Text;
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
          matches.Add(new RecordTextMatch(
            recordIndex,
            found,
            request.Query.Length));
        }
        position = found + Math.Max(1, request.Query.Length);
      }
    }
    return matches;
  }

  private static bool IsBoundary(string text, int index)
  {
    return index < 0 || index >= text.Length ||
      !(char.IsLetterOrDigit(text[index]) || text[index] == '_');
  }

  private static IReadOnlyList<TranscriptSearchMatch> MapMatches(
    IReadOnlyList<RecordTextMatch> raw,
    IReadOnlyList<SearchRecord> records)
  {
    var result = new List<TranscriptSearchMatch>(raw.Count);
    foreach (RecordTextMatch match in raw)
    {
      if ((uint)match.RecordIndex >= (uint)records.Count)
      {
        continue;
      }
      SearchRecord record = records[match.RecordIndex];
      SearchToken[] tokens = record.Tokens;
      int first = match.Length == 0
        ? ResolveZeroLengthMatchToken(tokens, match.Start)
        : FirstEndingAfter(tokens, match.Start);
      if (first < 0 || first >= tokens.Length)
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
        result.Count + 1,
        firstToken.RecordNumber,
        firstToken.SourceId,
        firstToken.RecordWordIndex,
        tokens[last].RecordWordIndex,
        voiced.NodeId,
        voiced.NodeWordIndex));
    }
    return result;
  }


  private static int ResolveZeroLengthMatchToken(
    SearchToken[] tokens,
    int position)
  {
    int next = FirstEndingAfter(tokens, position);
    if (next > 0 && tokens[next - 1].End == position)
    {
      // A zero-length match at a token end is an end-of-block anchor ($).
      return next - 1;
    }
    if (next < tokens.Length)
    {
      // Otherwise it is at the beginning of a block (^).
      return next;
    }
    return tokens.Length == 0 ? -1 : tokens.Length - 1;
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

  private static SearchRecord[] BuildCorpus(
    IReadOnlyList<MutableToken> source,
    bool voicedOnly)
  {
    var records = new List<SearchRecord>();
    foreach (IGrouping<string, MutableToken> group in source
      .Where(token => !voicedOnly || token.NodeId > 0)
      .GroupBy(token => token.RecordKey, StringComparer.Ordinal))
    {
      var builder = new StringBuilder();
      var tokens = new List<SearchToken>();
      int? previousBlockId = null;
      foreach (MutableToken token in group)
      {
        if (builder.Length != 0)
        {
          builder.Append(previousBlockId == token.BlockId ? ' ' : '\n');
        }
        int start = builder.Length;
        builder.Append(token.Text);
        tokens.Add(new SearchToken(
          start,
          builder.Length,
          token.RecordNumber,
          token.SourceId,
          token.RecordWordIndex,
          token.NodeId,
          token.NodeWordIndex));
        previousBlockId = token.BlockId;
      }
      if (tokens.Count != 0)
      {
        // Newlines separate blocks inside a record.  The final block ends at
        // the end of that record's independent search input.
        records.Add(new SearchRecord(builder.ToString(), tokens.ToArray()));
      }
    }
    return records.ToArray();
  }

  private static void PopThroughTag(Stack<BlockContext> stack, string name)
  {
    if (!BlockTags.Contains(name))
    {
      return;
    }
    while (stack.Count != 0)
    {
      BlockContext context = stack.Pop();
      if (string.Equals(context.TagName, name, StringComparison.OrdinalIgnoreCase))
      {
        break;
      }
    }
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
          tokens[index].NodeWordIndex = nodeWordIndex++;
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

  private static string MakeKey(int recordNumber, string sourceId)
  {
    return sourceId + "\0" + recordNumber.ToString(CultureInfo.InvariantCulture);
  }

  private sealed class MutableToken
  {
    public MutableToken(
      string text,
      int recordNumber,
      string sourceId,
      int blockId,
      int renderedIndex,
      int recordWordIndex)
    {
      Text = text;
      RecordNumber = recordNumber;
      SourceId = sourceId;
      RecordKey = MakeKey(recordNumber, sourceId);
      BlockId = blockId;
      RenderedIndex = renderedIndex;
      RecordWordIndex = recordWordIndex;
    }

    public string Text { get; }
    public int RecordNumber { get; }
    public string SourceId { get; }
    public string RecordKey { get; }
    public int BlockId { get; }
    public int RenderedIndex { get; }
    public int RecordWordIndex { get; }
    public long NodeId { get; set; }
    public int NodeWordIndex { get; set; } = -1;
  }

  private readonly record struct BlockContext(string TagName, int BlockId);
  private readonly record struct SearchRecord(string Text, SearchToken[] Tokens);
  private readonly record struct SearchToken(
    int Start,
    int End,
    int RecordNumber,
    string SourceId,
    int RecordWordIndex,
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
  int FileOrdinal,
  int RecordNumber,
  string SourceId,
  int StartWordIndex,
  int EndWordIndex,
  long NodeId,
  int NodeWordIndex);

internal readonly record struct RecordTextMatch(
  int RecordIndex,
  int Start,
  int Length);

internal static class RegexWorkerClient
{
  public static async Task<IReadOnlyList<RecordTextMatch>> SearchAsync(
    IReadOnlyList<string> records,
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
      var request = new RegexWorkerRequest(records, pattern, caseSensitive, wholeWord);
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
      return response.Matches ?? Array.Empty<RecordTextMatch>();
    }
    finally
    {
    }
  }
}

internal sealed record RegexWorkerRequest(
  IReadOnlyList<string> Records,
  string Pattern,
  bool CaseSensitive,
  bool WholeWord);

internal sealed record RegexWorkerResponse(
  IReadOnlyList<RecordTextMatch>? Matches,
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
      RegexOptions options =
        RegexOptions.CultureInvariant | RegexOptions.Multiline;
      if (!request.CaseSensitive)
      {
        options |= RegexOptions.IgnoreCase;
      }
      var regex = new Regex(pattern, options);
      var matches = new List<RecordTextMatch>();
      for (int recordIndex = 0; recordIndex < request.Records.Count; ++recordIndex)
      {
        foreach (Match match in regex.Matches(request.Records[recordIndex]))
        {
          matches.Add(new RecordTextMatch(
            recordIndex,
            match.Index,
            match.Length));
        }
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
