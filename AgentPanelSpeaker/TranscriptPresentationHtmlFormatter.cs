using Markdig;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Renders AIConversationCore's canonical presentation tree directly to HTML.
/// Structural HTML is emitted from the tree; only textual content blocks pass
/// through the Markdown parser.
/// </summary>
internal static class TranscriptPresentationHtmlFormatter
{
  private static readonly AIConversationCoreClient CoreClient = new();

  static TranscriptPresentationHtmlFormatter()
  {
    AppDomain.CurrentDomain.ProcessExit += (_, _) => CoreClient.Dispose();
  }

  /// <summary>
  /// Renders one selected provider JSONL session from the canonical presentation
  /// tree without using canonical Markdown as an interchange representation.
  /// </summary>
  public static string Format(
    string path,
    AgentSource source,
    MarkdownPipeline pipeline,
    CancellationToken cancellationToken = default,
    string? structureProbeId = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(pipeline);

    var jsonLines = new List<string>();
    using (var stream = new FileStream(
             path,
             FileMode.Open,
             FileAccess.Read,
             FileShare.ReadWrite | FileShare.Delete))
    using (var reader = new StreamReader(stream))
    {
      while (reader.ReadLine() is string line)
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
    }

    if (jsonLines.Count == 0)
    {
      return string.Empty;
    }

    AIConversationProjection projection = CoreClient.Project(
      source,
      jsonLines);
    cancellationToken.ThrowIfCancellationRequested();
    JsonElement tree = projection.Presentation?.Tree ?? default;
    if (tree.ValueKind != JsonValueKind.Object ||
        GetString(tree, "kind") != "conversation")
    {
      throw new InvalidOperationException(
        "AIConversationCore projection omitted the canonical presentation tree.");
    }
    TranscriptStructureSnapshot? presentationStructure = null;
    if (!string.IsNullOrWhiteSpace(structureProbeId))
    {
      presentationStructure =
        TranscriptStructureProbe.CapturePresentationTree(structureProbeId, tree);
    }

    var output = new StringBuilder();
    var emittedSourceIndexes = new HashSet<int>();
    if (!tree.TryGetProperty("turns", out JsonElement turns) ||
        turns.ValueKind != JsonValueKind.Array)
    {
      return string.Empty;
    }

    foreach (JsonElement turn in turns.EnumerateArray())
    {
      cancellationToken.ThrowIfCancellationRequested();
      RenderTurn(
        output,
        turn,
        pipeline,
        emittedSourceIndexes,
        cancellationToken);
    }
    string html = output.ToString();
    if (presentationStructure is not null)
    {
      TranscriptStructureSnapshot directRendererStructure =
        TranscriptStructureProbe.CaptureHtml(
          structureProbeId!,
          "direct-renderer-html",
          html);
      TranscriptStructureProbe.Compare(
        presentationStructure,
        directRendererStructure);
    }
    return html;
  }

  private static void RenderTurn(
    StringBuilder output,
    JsonElement turn,
    MarkdownPipeline pipeline,
    HashSet<int> emittedSourceIndexes,
    CancellationToken cancellationToken)
  {
    string turnId = GetString(turn, "id");
    string label = "Agent";
    if (turn.TryGetProperty("actor", out JsonElement actor) &&
        actor.ValueKind == JsonValueKind.Object)
    {
      label = GetString(actor, "label");
      if (string.IsNullOrWhiteSpace(label))
      {
        label = GetString(actor, "role") == "user" ? "User" : "Agent";
      }
    }

    output.Append("<section class=\"transcript-turn\" data-presentation-id=\"")
      .Append(Html(turnId))
      .AppendLine("\">");
    output.Append("<h2>").Append(Html(label)).AppendLine("</h2>");
    output.AppendLine("<blockquote class=\"transcript-turn-body\">");

    if (turn.TryGetProperty("children", out JsonElement children) &&
        children.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement child in children.EnumerateArray())
      {
        cancellationToken.ThrowIfCancellationRequested();
        RenderNode(
          output,
          child,
          pipeline,
          emittedSourceIndexes,
          cancellationToken);
      }
    }

    output.AppendLine("</blockquote>");
    output.AppendLine("</section>");
  }

  private static void RenderNode(
    StringBuilder output,
    JsonElement node,
    MarkdownPipeline pipeline,
    HashSet<int> emittedSourceIndexes,
    CancellationToken cancellationToken)
  {
    string kind = GetString(node, "kind");
    switch (kind)
    {
      case "reasoning_group":
        RenderReasoningGroup(
          output,
          node,
          pipeline,
          emittedSourceIndexes,
          cancellationToken);
        break;
      case "tool":
        RenderTool(output, node, emittedSourceIndexes);
        break;
      case "interaction":
        EmitAnchors(output, node, emittedSourceIndexes);
        RenderTool(output, node, emittedSourceIndexes);
        break;
      case "attachments":
        EmitAnchors(output, node, emittedSourceIndexes);
        RenderAttachments(output, node);
        break;
    case "user_context":
      EmitAnchors(output, node, emittedSourceIndexes);
      RenderUserContext(output, node, pipeline);
      break;
      case "reasoning":
      case "markdown":
      case "commentary":
      case "notice":
        EmitAnchors(output, node, emittedSourceIndexes);
        RenderMarkdownNode(output, node, pipeline);
        break;
      case "subagent_content":
        EmitAnchors(output, node, emittedSourceIndexes);
        RenderSubagentContent(output, node, pipeline);
        break;
    }
  }

  private static void RenderReasoningGroup(
    StringBuilder output,
    JsonElement node,
    MarkdownPipeline pipeline,
    HashSet<int> emittedSourceIndexes,
    CancellationToken cancellationToken)
  {
    int thoughtCount = GetInt32(node, "thought_count");
    string summary = thoughtCount switch
    {
      1 => "Having a thought",
      > 1 => $"Having {thoughtCount} thoughts",
      _ => "Thought and tool activity"
    };

    output.Append("<details class=\"reasoning\" data-presentation-id=\"")
      .Append(Html(GetString(node, "id")))
      .AppendLine("\">");
    output.Append("<summary>").Append(Html(summary)).AppendLine("</summary>");
    output.AppendLine("<div class=\"reasoning-body\">");

    if (node.TryGetProperty("children", out JsonElement children) &&
        children.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement child in children.EnumerateArray())
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetString(child, "kind") == "reasoning")
        {
          output.AppendLine("<div class=\"thought\">");
          EmitAnchors(output, child, emittedSourceIndexes);
          RenderMarkdownNode(output, child, pipeline);
          output.AppendLine("</div>");
        }
        else
        {
          RenderNode(
            output,
            child,
            pipeline,
            emittedSourceIndexes,
            cancellationToken);
        }
      }
    }

    output.AppendLine("</div>");
    output.AppendLine("</details>");
  }

  private static void RenderMarkdownNode(
    StringBuilder output,
    JsonElement node,
    MarkdownPipeline pipeline)
  {
    if (!node.TryGetProperty("blocks", out JsonElement blocks) ||
        blocks.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    var markdown = new StringBuilder();
    foreach (JsonElement block in blocks.EnumerateArray())
    {
      string text = BlockMarkdown(block);
      if (string.IsNullOrWhiteSpace(text))
      {
        continue;
      }
      if (markdown.Length != 0)
      {
        markdown.AppendLine().AppendLine();
      }
      markdown.Append(text);
    }
    if (markdown.Length != 0)
    {
      output.Append(Markdown.ToHtml(markdown.ToString(), pipeline));
    }
  }

  private static string BlockMarkdown(JsonElement block)
  {
    string type = GetString(block, "type");
    if (type == "text")
    {
      return GetString(block, "text");
    }
    if (type == "reasoning_summary")
    {
      string content = GetString(block, "content");
      return string.IsNullOrWhiteSpace(content)
        ? GetString(block, "summary")
        : content;
    }
    if (type == "code")
    {
      string code = GetString(block, "code");
      if (string.IsNullOrEmpty(code))
      {
        code = GetString(block, "text");
      }
      string language = GetString(block, "language");
      return $"```{language}\n{code}\n```";
    }
    return GetString(block, "text");
  }

  private static void RenderTool(
    StringBuilder output,
    JsonElement node,
    HashSet<int> emittedSourceIndexes)
  {
    EmitAnchors(output, node, emittedSourceIndexes);
    string name = GetString(node, "name");
    if (string.IsNullOrWhiteSpace(name))
    {
      name = "Tool";
    }
    output.Append("<details class=\"tool\"><summary>")
      .Append(Html(name))
      .AppendLine("</summary>");
    output.AppendLine("<pre><code>");
    if (node.TryGetProperty("call", out JsonElement call) &&
        call.ValueKind != JsonValueKind.Null &&
        call.ValueKind != JsonValueKind.Undefined)
    {
      output.Append(Html(call.GetRawText()));
    }
    if (node.TryGetProperty("result", out JsonElement result) &&
        result.ValueKind != JsonValueKind.Null &&
        result.ValueKind != JsonValueKind.Undefined)
    {
      if (node.TryGetProperty("call", out JsonElement callAgain) &&
          callAgain.ValueKind != JsonValueKind.Null &&
          callAgain.ValueKind != JsonValueKind.Undefined)
      {
        output.AppendLine();
      }
      output.Append(Html(result.GetRawText()));
    }
    output.AppendLine("</code></pre>");
    output.AppendLine("</details>");
  }

  /// <summary>
  /// Renders semantic User context as a nested blockquote/details disclosure.
  /// </summary>
  private static void RenderUserContext(
    StringBuilder output,
    JsonElement node,
    MarkdownPipeline pipeline)
  {
    output.AppendLine("<blockquote class=\"user-context\">");
    if (node.TryGetProperty("blocks", out JsonElement blocks) &&
        blocks.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement block in blocks.EnumerateArray())
      {
        string summary = GetString(block, "summary");
        if (string.IsNullOrWhiteSpace(summary))
        {
          summary = "# Context from my IDE setup:";
        }
        output.Append("<details class=\"user-context-details\" data-presentation-id=\"")
          .Append(Html(GetString(node, "id")))
          .AppendLine("\">");
        output.Append("<summary>").Append(Html(summary)).AppendLine("</summary>");
        string body = GetString(block, "text");
        if (!string.IsNullOrWhiteSpace(body))
        {
          output.Append(Markdown.ToHtml(body, pipeline));
        }
        output.AppendLine("</details>");
      }
    }
    output.AppendLine("</blockquote>");
  }

  private static void RenderAttachments(StringBuilder output, JsonElement node)
  {
    output.AppendLine("<div class=\"attachments\">");
    if (node.TryGetProperty("blocks", out JsonElement blocks) &&
        blocks.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement block in blocks.EnumerateArray())
      {
        string src = GetString(block, "url");
        if (string.IsNullOrWhiteSpace(src))
        {
          src = GetString(block, "source_pointer");
        }
        if (!string.IsNullOrWhiteSpace(src))
        {
          output.Append("<img src=\"")
            .Append(Html(src))
            .AppendLine("\" alt=\"\">");
        }
      }
    }
    output.AppendLine("</div>");
  }

  private static void RenderSubagentContent(
    StringBuilder output,
    JsonElement node,
    MarkdownPipeline pipeline)
  {
    if (!node.TryGetProperty("block", out JsonElement block) ||
        block.ValueKind != JsonValueKind.Object)
    {
      return;
    }
    string text = GetString(block, "text");
    if (string.IsNullOrWhiteSpace(text))
    {
      text = GetString(block, "result");
    }
    if (!string.IsNullOrWhiteSpace(text))
    {
      output.Append(Markdown.ToHtml(text, pipeline));
    }
  }

  private static void EmitAnchors(
    StringBuilder output,
    JsonElement node,
    HashSet<int> emittedSourceIndexes)
  {
    if (!node.TryGetProperty("source", out JsonElement sources) ||
        sources.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    foreach (JsonElement source in sources.EnumerateArray())
    {
      int sourceIndex = GetInt32(source, "record_index");
      if (sourceIndex < 0 || !emittedSourceIndexes.Add(sourceIndex))
      {
        continue;
      }
      string sourceId = GetString(source, "record_id");
      if (string.IsNullOrWhiteSpace(sourceId))
      {
        sourceId = (sourceIndex + 1).ToString(CultureInfo.InvariantCulture);
      }
      output.Append("<span class=\"record-anchor\" data-jsonl-record=\"")
        .Append(sourceIndex + 1)
        .Append("\" data-source-id=\"")
        .Append(Html(sourceId))
        .AppendLine("\"></span>");
    }
  }

  private static string GetString(JsonElement element, string propertyName)
  {
    if (element.ValueKind != JsonValueKind.Object ||
        !element.TryGetProperty(propertyName, out JsonElement value))
    {
      return string.Empty;
    }
    return value.ValueKind == JsonValueKind.String
      ? value.GetString() ?? string.Empty
      : string.Empty;
  }

  private static int GetInt32(JsonElement element, string propertyName)
  {
    if (element.ValueKind != JsonValueKind.Object ||
        !element.TryGetProperty(propertyName, out JsonElement value) ||
        value.ValueKind != JsonValueKind.Number ||
        !value.TryGetInt32(out int result))
    {
      return -1;
    }
    return result;
  }

  private static string Html(string value)
  {
    return WebUtility.HtmlEncode(value);
  }
}
