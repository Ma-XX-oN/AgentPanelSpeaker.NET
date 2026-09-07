using Markdig;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Builds a browser-DOM construction model directly from AIConversationCore's
/// canonical presentation tree. Structural transcript nodes are represented as
/// elements, attributes, text, and children; only Markdown leaf content is
/// carried as an HTML fragment.
/// </summary>
internal static class TranscriptPresentationDomFormatter
{
  private static readonly AIConversationCoreClient CoreClient = new();

  static TranscriptPresentationDomFormatter()
  {
    AppDomain.CurrentDomain.ProcessExit += (_, _) => CoreClient.Dispose();
  }

  /// <summary>
  /// Builds one DOM model and an equivalent serialized HTML form used only by
  /// the C# search/identity infrastructure. The browser display path consumes
  /// <see cref="TranscriptPresentationDomResult.Nodes"/> and creates structural
  /// DOM nodes with <c>document.createElement</c>; it does not parse this HTML.
  /// </summary>
  public static TranscriptPresentationDomResult Format(
    string path,
    AgentSource source,
    MarkdownPipeline pipeline,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(pipeline);

    IReadOnlyList<string> jsonLines = ReadJsonLines(path, cancellationToken);
    if (jsonLines.Count == 0)
    {
      return new TranscriptPresentationDomResult(
        Array.Empty<TranscriptDomNode>(),
        string.Empty);
    }

    AIConversationProjection projection = CoreClient.Project(source, jsonLines);
    cancellationToken.ThrowIfCancellationRequested();
    JsonElement tree = projection.Presentation?.Tree ?? default;
    if (tree.ValueKind != JsonValueKind.Object ||
        GetString(tree, "kind") != "conversation")
    {
      throw new InvalidOperationException(
        "AIConversationCore projection omitted the canonical presentation tree.");
    }

    var emittedSourceIndexes = new HashSet<int>();
    var nodes = new List<TranscriptDomNode>();
    if (tree.TryGetProperty("turns", out JsonElement turns) &&
        turns.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement turn in turns.EnumerateArray())
      {
        cancellationToken.ThrowIfCancellationRequested();
        nodes.Add(BuildTurn(
          turn,
          pipeline,
          emittedSourceIndexes,
          cancellationToken));
      }
    }

    string html = string.Concat(nodes.Select(SerializeNode));
    return new TranscriptPresentationDomResult(nodes, html);
  }

  private static IReadOnlyList<string> ReadJsonLines(
    string path,
    CancellationToken cancellationToken)
  {
    var jsonLines = new List<string>();
    using var stream = new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream);
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
    return jsonLines;
  }

  private static TranscriptDomNode BuildTurn(
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

    var bodyChildren = new List<TranscriptDomNode>();
    if (turn.TryGetProperty("children", out JsonElement children) &&
        children.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement child in children.EnumerateArray())
      {
        cancellationToken.ThrowIfCancellationRequested();
        TranscriptDomNode? node = BuildNode(
          child,
          pipeline,
          emittedSourceIndexes,
          cancellationToken);
        if (node is not null)
        {
          bodyChildren.Add(node);
        }
      }
    }

    return Element(
      "section",
      new Dictionary<string, string>
      {
        ["class"] = "transcript-turn",
        ["data-presentation-id"] = turnId
      },
      Element("h2", null, Text(label)),
      Element(
        "blockquote",
        new Dictionary<string, string>
        {
          ["class"] = "transcript-turn-body"
        },
        bodyChildren));
  }

  private static TranscriptDomNode? BuildNode(
    JsonElement node,
    MarkdownPipeline pipeline,
    HashSet<int> emittedSourceIndexes,
    CancellationToken cancellationToken)
  {
    string kind = GetString(node, "kind");
    return kind switch
    {
      "reasoning_group" => BuildReasoningGroup(
        node,
        pipeline,
        emittedSourceIndexes,
        cancellationToken),
      "tool" => BuildTool(node, emittedSourceIndexes),
      "interaction" => BuildTool(node, emittedSourceIndexes),
      "attachments" => BuildAttachments(node, emittedSourceIndexes),
      "user_context" => BuildUserContext(
        node,
        pipeline,
        emittedSourceIndexes),
      "reasoning" or "markdown" or "commentary" or "notice" =>
        BuildMarkdownContent(node, pipeline, emittedSourceIndexes),
      "subagent_content" => BuildSubagentContent(
        node,
        pipeline,
        emittedSourceIndexes),
      _ => null
    };
  }

  private static TranscriptDomNode BuildReasoningGroup(
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

    var bodyChildren = new List<TranscriptDomNode>();
    if (node.TryGetProperty("children", out JsonElement children) &&
        children.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement child in children.EnumerateArray())
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetString(child, "kind") == "reasoning")
        {
          var thoughtChildren = new List<TranscriptDomNode>();
          AddAnchors(thoughtChildren, child, emittedSourceIndexes);
          AddMarkdownLeaf(thoughtChildren, child, pipeline);
          bodyChildren.Add(Element("div", Class("thought"), thoughtChildren));
          continue;
        }

        TranscriptDomNode? childNode = BuildNode(
          child,
          pipeline,
          emittedSourceIndexes,
          cancellationToken);
        if (childNode is not null)
        {
          bodyChildren.Add(childNode);
        }
      }
    }

    return Element(
      "details",
      new Dictionary<string, string>
      {
        ["class"] = "reasoning",
        ["data-presentation-id"] = GetString(node, "id")
      },
      Element("summary", null, Text(summary)),
      Element("div", Class("reasoning-body"), bodyChildren));
  }

  /// <summary>
  /// Builds the semantic Codex User IDE-context disclosure used by the actual
  /// transcript DOM path. The prompt is a separate presentation sibling and is
  /// therefore never placed inside this disclosure.
  /// </summary>
  private static TranscriptDomNode BuildUserContext(
    JsonElement node,
    MarkdownPipeline pipeline,
    HashSet<int> emittedSourceIndexes)
  {
    string summary = "# Context from my IDE setup:";
    string context = string.Empty;
    if (node.TryGetProperty("blocks", out JsonElement blocks) &&
        blocks.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement block in blocks.EnumerateArray())
      {
        if (GetString(block, "type") != "user_context")
        {
          continue;
        }

        string blockSummary = GetString(block, "summary");
        if (!string.IsNullOrWhiteSpace(blockSummary))
        {
          summary = blockSummary;
        }
        context = GetString(block, "text");
        break;
      }
    }

    var detailsChildren = new List<TranscriptDomNode>
    {
      Element("summary", null, Text(summary))
    };
    AddAnchors(detailsChildren, node, emittedSourceIndexes);
    if (!string.IsNullOrWhiteSpace(context))
    {
      detailsChildren.Add(Html(Markdown.ToHtml(context, pipeline)));
    }

    var attributes = new Dictionary<string, string>
    {
      ["class"] = "user-context-details"
    };
    string id = GetString(node, "id");
    if (!string.IsNullOrWhiteSpace(id))
    {
      attributes["data-presentation-id"] = id;
    }

    return Element(
      "blockquote",
      Class("user-context"),
      Element("details", attributes, detailsChildren));
  }

  private static TranscriptDomNode BuildMarkdownContent(
    JsonElement node,
    MarkdownPipeline pipeline,
    HashSet<int> emittedSourceIndexes)
  {
    var children = new List<TranscriptDomNode>();
    AddAnchors(children, node, emittedSourceIndexes);
    AddMarkdownLeaf(children, node, pipeline);
    return Element("div", Class("presentation-content"), children);
  }

  private static TranscriptDomNode BuildTool(
    JsonElement node,
    HashSet<int> emittedSourceIndexes)
  {
    var children = new List<TranscriptDomNode>();
    children.Add(Element(
      "summary",
      null,
      Text(ToolSummary(node))));
    AddAnchors(children, node, emittedSourceIndexes);

    var payload = new StringBuilder();
    if (node.TryGetProperty("call", out JsonElement call) &&
        call.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
    {
      payload.Append(call.GetRawText());
    }
    if (node.TryGetProperty("result", out JsonElement result) &&
        result.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
    {
      if (payload.Length != 0)
      {
        payload.AppendLine();
      }
      payload.Append(result.GetRawText());
    }
    children.Add(Element(
      "pre",
      null,
      Element("code", null, Text(payload.ToString()))));

    var attributes = new Dictionary<string, string>
    {
      ["class"] = "tool"
    };
    string id = GetString(node, "id");
    if (!string.IsNullOrWhiteSpace(id))
    {
      attributes["data-presentation-id"] = id;
    }
    return Element("details", attributes, children);
  }

  private static string ToolSummary(JsonElement node)
  {
    if (node.TryGetProperty("call", out JsonElement call) &&
        call.ValueKind == JsonValueKind.Object &&
        call.TryGetProperty("input", out JsonElement input) &&
        input.ValueKind == JsonValueKind.Object)
    {
      string description = GetString(input, "description");
      if (!string.IsNullOrWhiteSpace(description))
      {
        return description;
      }
    }

    string name = GetString(node, "name");
    return string.IsNullOrWhiteSpace(name) ? "Tool" : name;
  }

  private static TranscriptDomNode BuildAttachments(
    JsonElement node,
    HashSet<int> emittedSourceIndexes)
  {
    var children = new List<TranscriptDomNode>();
    AddAnchors(children, node, emittedSourceIndexes);
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
          children.Add(Element(
            "img",
            new Dictionary<string, string>
            {
              ["src"] = src,
              ["alt"] = string.Empty
            }));
        }
      }
    }
    return Element("div", Class("attachments"), children);
  }

  private static TranscriptDomNode BuildSubagentContent(
    JsonElement node,
    MarkdownPipeline pipeline,
    HashSet<int> emittedSourceIndexes)
  {
    var children = new List<TranscriptDomNode>();
    AddAnchors(children, node, emittedSourceIndexes);
    if (node.TryGetProperty("block", out JsonElement block) &&
        block.ValueKind == JsonValueKind.Object)
    {
      string text = GetString(block, "text");
      if (string.IsNullOrWhiteSpace(text))
      {
        text = GetString(block, "result");
      }
      if (!string.IsNullOrWhiteSpace(text))
      {
        children.Add(Html(Markdown.ToHtml(text, pipeline)));
      }
    }
    return Element("div", Class("subagent-content"), children);
  }

  private static void AddMarkdownLeaf(
    ICollection<TranscriptDomNode> children,
    JsonElement node,
    MarkdownPipeline pipeline)
  {
    string html = MarkdownHtml(node, pipeline);
    if (!string.IsNullOrWhiteSpace(html))
    {
      children.Add(Html(html));
    }
  }

  private static string MarkdownHtml(JsonElement node, MarkdownPipeline pipeline)
  {
    if (!node.TryGetProperty("blocks", out JsonElement blocks) ||
        blocks.ValueKind != JsonValueKind.Array)
    {
      return string.Empty;
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
    return markdown.Length == 0
      ? string.Empty
      : Markdown.ToHtml(markdown.ToString(), pipeline);
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

  private static void AddAnchors(
    ICollection<TranscriptDomNode> children,
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
      children.Add(Element(
        "span",
        new Dictionary<string, string>
        {
          ["class"] = "record-anchor",
          ["data-jsonl-record"] = (sourceIndex + 1).ToString(
            CultureInfo.InvariantCulture),
          ["data-source-id"] = sourceId
        }));
    }
  }

  private static TranscriptDomNode Element(
    string tag,
    IReadOnlyDictionary<string, string>? attributes,
    params TranscriptDomNode[] children)
  {
    return Element(tag, attributes, (IReadOnlyList<TranscriptDomNode>)children);
  }

  private static TranscriptDomNode Element(
    string tag,
    IReadOnlyDictionary<string, string>? attributes,
    IReadOnlyList<TranscriptDomNode> children)
  {
    return new TranscriptDomNode(
      "element",
      tag,
      attributes,
      null,
      null,
      children);
  }

  private static TranscriptDomNode Text(string text)
  {
    return new TranscriptDomNode("text", null, null, text, null, null);
  }

  private static TranscriptDomNode Html(string html)
  {
    return new TranscriptDomNode("html", null, null, null, html, null);
  }

  private static IReadOnlyDictionary<string, string> Class(string value)
  {
    return new Dictionary<string, string> { ["class"] = value };
  }

  private static string SerializeNode(TranscriptDomNode node)
  {
    if (node.Kind == "text")
    {
      return WebUtility.HtmlEncode(node.Text ?? string.Empty);
    }
    if (node.Kind == "html")
    {
      return node.Html ?? string.Empty;
    }
    if (node.Kind != "element" || string.IsNullOrWhiteSpace(node.Tag))
    {
      return string.Empty;
    }

    string tag = node.Tag;
    var output = new StringBuilder();
    output.Append('<').Append(tag);
    if (node.Attributes is not null)
    {
      foreach ((string name, string value) in node.Attributes)
      {
        output.Append(' ')
          .Append(name)
          .Append("=\"")
          .Append(WebUtility.HtmlEncode(value))
          .Append('\"');
      }
    }
    output.Append('>');
    if (node.Children is not null)
    {
      foreach (TranscriptDomNode child in node.Children)
      {
        output.Append(SerializeNode(child));
      }
    }
    if (!string.Equals(tag, "img", StringComparison.OrdinalIgnoreCase))
    {
      output.Append("</").Append(tag).Append('>');
    }
    return output.ToString();
  }

  private static string GetString(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : string.Empty;
  }

  private static int GetInt32(JsonElement element, string propertyName)
  {
    return element.ValueKind == JsonValueKind.Object &&
      element.TryGetProperty(propertyName, out JsonElement value) &&
      value.ValueKind == JsonValueKind.Number &&
      value.TryGetInt32(out int result)
        ? result
        : -1;
  }
}

/// <summary>
/// Browser-DOM construction payload plus the equivalent HTML used internally
/// for C# search, word mapping, and virtual identity indexing.
/// </summary>
internal sealed record TranscriptPresentationDomResult(
  IReadOnlyList<TranscriptDomNode> Nodes,
  string Html);

/// <summary>
/// One browser-DOM construction instruction.
/// </summary>
internal sealed record TranscriptDomNode(
  string Kind,
  string? Tag,
  IReadOnlyDictionary<string, string>? Attributes,
  string? Text,
  string? Html,
  IReadOnlyList<TranscriptDomNode>? Children);
