using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Reconstructs the monitor's stable accepted-node numbering so rendered
/// transcript words can be associated with the exact source node being read.
/// </summary>
internal static class TranscriptNodeIdentityMap
{
  private const int MaximumRecentFingerprints = 512;

  /// <summary>
  /// Reads one session and returns accepted speech-text segments in node order.
  /// </summary>
  public static IReadOnlyList<TranscriptNodeIdentity> Build(
    string path,
    AgentSource source)
  {
    var result = new List<TranscriptNodeIdentity>();
    var recentQueue = new Queue<string>();
    var recentSet = new HashSet<string>(StringComparer.Ordinal);
    long nextNodeId = 1;
    var pendingInputRequests = new Dictionary<string, CodexInputRequest>(
      StringComparer.Ordinal);

    foreach (string line in File.ReadLines(path))
    {
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      ExtractionResult extraction;
      try
      {
        extraction = JsonlRecordExtractor.Extract(source, line);
      }
      catch (JsonException)
      {
        continue;
      }

      if (extraction.InputRequest is CodexInputRequest request)
      {
        pendingInputRequests[request.CallId] = request;
      }
      IReadOnlyList<ExtractedNode> responseNodes = ResolveInputResponse(
        extraction.InputResponse,
        pendingInputRequests);

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
          IsRenderedKind(source, node.Kind)
            ? parts.Select(part => part.Text).ToArray()
            : Array.Empty<string>()));
      }
    }

    return result;
  }

  private static bool IsRenderedKind(AgentSource source, string kind)
  {
    if (source == AgentSource.Claude)
    {
      return kind is "claude.user_text" or "claude.thinking" or
        "claude.text" or "claude.subagent.result";
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
}

/// <summary>
/// Associates one monitor node identifier with its ordered speakable segments.
/// </summary>
internal sealed record TranscriptNodeIdentity(
  long NodeId,
  IReadOnlyList<string> Segments);
