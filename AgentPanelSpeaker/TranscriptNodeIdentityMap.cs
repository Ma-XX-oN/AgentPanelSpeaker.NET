using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Reconstructs the monitor's stable accepted-node numbering from the same
/// AIConversationCore projection used by speech/history and transcript display.
/// </summary>
internal static class TranscriptNodeIdentityMap
{
  private const int MaximumRecentFingerprints = 512;

  /// <summary>
  /// Reads one session and returns accepted speech-text segments in node order.
  /// Provider-native JSON is validated only as an ordered record container;
  /// identity and conversational semantics come from AIConversationCore.
  /// </summary>
  public static IReadOnlyList<TranscriptNodeIdentity> Build(
    string path,
    AgentSource source,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    var jsonLines = new List<string>();
    foreach (string line in ReadSharedLines(path))
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      using JsonDocument document = JsonDocument.Parse(line);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
      {
        throw new JsonException("A transcript JSONL record must be an object.");
      }
      jsonLines.Add(line);
    }

    if (jsonLines.Count == 0)
    {
      return Array.Empty<TranscriptNodeIdentity>();
    }

    using var client = new AIConversationCoreClient();
    AIConversationProjection projection = CanonicalSpeechProjection.Prepare(
      client.Project(source, jsonLines));
    cancellationToken.ThrowIfCancellationRequested();

    IReadOnlyDictionary<int, string> sourceIds = BuildCanonicalSourceIds(
      projection.Events);
    var result = new List<TranscriptNodeIdentity>();
    var recentQueue = new Queue<string>();
    var recentSet = new HashSet<string>(StringComparer.Ordinal);
    long nextNodeId = 1;
    var pendingInputRequests = new Dictionary<string, CodexInputRequest>(
      StringComparer.Ordinal);

    for (int sourceIndex = 0; sourceIndex < jsonLines.Count; ++sourceIndex)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ExtractionResult extraction = CanonicalProjectionExtractor.ExtractRecord(
        projection,
        source,
        sourceIndex);

      if (extraction.InputRequest is CodexInputRequest request)
      {
        pendingInputRequests[request.CallId] = request;
      }
      IReadOnlyList<ExtractedNode> responseNodes = ResolveInputResponse(
        extraction.InputResponse,
        pendingInputRequests);
      int recordNumber = sourceIndex + 1;
      string sourceId = sourceIds.TryGetValue(sourceIndex, out string? canonicalId)
        ? canonicalId
        : recordNumber.ToString(CultureInfo.InvariantCulture);

      foreach (ExtractedNode node in extraction.Nodes.Concat(responseNodes))
      {
        IReadOnlyList<SpeechTextPart> parts = TextCleaner.ParseForSpeech(
          node.Text);
        if (parts.Count == 0)
        {
          continue;
        }

        string fingerprint = CreateFingerprint(
          node.Category + "|" + node.Kind + "|" + node.Timestamp + "|" +
          node.StartsUserTurn + "|" + string.Join(
            "|",
            parts.Select(part =>
              $"{part.Kind}:{part.Style}:{part.FenceType}:{part.Text}")));
        if (recentSet.Contains(fingerprint))
        {
          continue;
        }

        RememberFingerprint(fingerprint, recentQueue, recentSet);
        result.Add(new TranscriptNodeIdentity(
          nextNodeId++,
          recordNumber,
          sourceId,
          IsRenderedKind(source, node.Kind)
            ? BuildSegments(parts)
            : Array.Empty<string>()));
      }
    }

    return result;
  }

  /// <summary>
  /// Builds record-index to persistent source-ID mappings from canonical
  /// provenance.  Missing source IDs intentionally use the same one-based
  /// fallback used by transcript record anchors.
  /// </summary>
  private static IReadOnlyDictionary<int, string> BuildCanonicalSourceIds(
    IReadOnlyList<JsonElement> events)
  {
    var result = new Dictionary<int, string>();
    foreach (JsonElement eventElement in events)
    {
      int? sourceIndex = ReadInt32(eventElement, "source_index");
      if (sourceIndex is not int index || index < 0 || result.ContainsKey(index))
      {
        continue;
      }

      string sourceId = ReadString(eventElement, "source_record_id").Trim();
      result[index] = sourceId.Length == 0
        ? (index + 1).ToString(CultureInfo.InvariantCulture)
        : sourceId;
    }
    return result;
  }

  private static IReadOnlyList<string> BuildSegments(
    IReadOnlyList<SpeechTextPart> parts)
  {
    var segments = new List<string>();
    foreach (SpeechTextPart part in parts)
    {
      if (part.Kind == SpeechFragmentKind.Prose)
      {
        segments.AddRange(SentenceSegmenter
          .Split(part.Text, part.PauseAfter)
          .Select(sentence => sentence.Text));
      }
      else
      {
        segments.Add(part.Text);
      }
    }
    return segments;
  }

  private static bool IsRenderedKind(AgentSource source, string kind)
  {
    if (source == AgentSource.Claude)
    {
      return kind is "claude.user_text" or
        "claude.queued_command.context" or "claude.queued_command" or
        "claude.thinking" or "claude.text" or
        "claude.subagent.result";
    }

    return kind == "codex.user_message" ||
      kind.StartsWith("codex.agent_message", StringComparison.Ordinal);
  }

  private static IReadOnlyList<ExtractedNode> ResolveInputResponse(
    CodexInputResponse? response,
    IDictionary<string, CodexInputRequest> pendingInputRequests)
  {
    if (response is null ||
        !pendingInputRequests.TryGetValue(
          response.CallId,
          out CodexInputRequest? request) ||
        request is null)
    {
      return Array.Empty<ExtractedNode>();
    }
    pendingInputRequests.Remove(response.CallId);

    var nodes = new List<ExtractedNode>();
    foreach (CodexInputQuestion question in request.Questions)
    {
      if (question.IsSecret ||
          !response.Answers.TryGetValue(
            question.Id,
            out IReadOnlyList<string>? selectedAnswers) ||
          selectedAnswers is null)
      {
        continue;
      }

      var selections = new List<string>();
      foreach (string selectedAnswer in selectedAnswers)
      {
        int optionIndex = FindSelectedOptionIndex(
          selectedAnswer,
          question.Options);
        if (optionIndex >= 0)
        {
          CodexInputOption option = question.Options[optionIndex];
          string optionText = option.Label.Length != 0
            ? option.Label
            : option.Description;
          selections.Add(EnsureTerminalPunctuation(
            $"Selected option {optionIndex + 1}: {optionText}"));
        }
        else
        {
          selections.Add(EnsureTerminalPunctuation(
            $"Selected: {selectedAnswer}"));
        }
      }

      if (selections.Count != 0)
      {
        nodes.Add(new ExtractedNode(
          "codex.user_input_answer",
          ContentCategory.User,
          string.Join(" ", selections),
          response.Timestamp,
          StartsUserTurn: false));
      }
    }
    return nodes;
  }

  private static int FindSelectedOptionIndex(
    string selectedAnswer,
    IReadOnlyList<CodexInputOption> options)
  {
    string trimmed = selectedAnswer.Trim();
    for (int index = 0; index < options.Count; ++index)
    {
      if (string.Equals(
            trimmed,
            options[index].Label,
            StringComparison.OrdinalIgnoreCase) ||
          string.Equals(
            trimmed,
            $"Option {index + 1}",
            StringComparison.OrdinalIgnoreCase) ||
          trimmed.StartsWith(
            $"Option {index + 1}:",
            StringComparison.OrdinalIgnoreCase))
      {
        return index;
      }
    }

    return int.TryParse(trimmed, out int optionNumber) &&
      optionNumber >= 1 &&
      optionNumber <= options.Count
        ? optionNumber - 1
        : -1;
  }

  private static string EnsureTerminalPunctuation(string text)
  {
    string trimmed = text.Trim();
    return trimmed.Length != 0 && trimmed[^1] is not ('.' or '!' or '?')
      ? trimmed + "."
      : trimmed;
  }

  private static string CreateFingerprint(string text)
  {
    string canonical = new string(
      text
        .Where(character => !char.IsWhiteSpace(character))
        .Select(char.ToLowerInvariant)
        .ToArray());
    byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    return Convert.ToHexString(digest);
  }

  private static void RememberFingerprint(
    string fingerprint,
    Queue<string> queue,
    HashSet<string> set)
  {
    queue.Enqueue(fingerprint);
    set.Add(fingerprint);
    while (queue.Count > MaximumRecentFingerprints)
    {
      string removed = queue.Dequeue();
      set.Remove(removed);
    }
  }

  private static int? ReadInt32(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.Number &&
      value.TryGetInt32(out int result)
        ? result
        : null;
  }

  private static string ReadString(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : string.Empty;
  }

  private static IEnumerable<string> ReadSharedLines(string path)
  {
    using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(
      stream,
      Encoding.UTF8,
      detectEncodingFromByteOrderMarks: true,
      bufferSize: 64 * 1024,
      leaveOpen: false);
    while (reader.ReadLine() is string line)
    {
      yield return line;
    }
  }
}

/// <summary>
/// Associates one monitor node identifier with its canonical source provenance
/// and ordered speakable segments.
/// </summary>
internal sealed record TranscriptNodeIdentity(
  long NodeId,
  int RecordNumber,
  string SourceId,
  IReadOnlyList<string> Segments);
