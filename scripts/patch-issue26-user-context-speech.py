from __future__ import annotations

from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]
CORE_COMMIT = "6c9c2eccc4df301105f4b330fb9216b90d35c5f7"


def replace_once(path: str, old: str, new: str) -> None:
  target = ROOT / path
  text = target.read_text(encoding="utf-8")
  count = text.count(old)
  if count != 1:
    raise RuntimeError(f"{path}: expected one replacement target, found {count}")
  target.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


# Transcript Settings plumbing.
replace_once(
  "AgentPanelSpeaker/TranscriptSettingsPopup.cs",
  """        advancedPopup.SetSettings(\n        Settings.HighlightQueueCapacity,\n        Settings.ShowRolledBackHistory);""",
  """        advancedPopup.SetSettings(\n        Settings.HighlightQueueCapacity,\n        Settings.ShowRolledBackHistory,\n        Settings.SpeakUserContext);""")
replace_once(
  "AgentPanelSpeaker/TranscriptSettingsPopup.cs",
  """      advancedPopup.SetSettings(\n        Settings.HighlightQueueCapacity,\n        Settings.ShowRolledBackHistory);""",
  """      advancedPopup.SetSettings(\n        Settings.HighlightQueueCapacity,\n        Settings.ShowRolledBackHistory,\n        Settings.SpeakUserContext);""")
replace_once(
  "AgentPanelSpeaker/TranscriptSettingsPopup.cs",
  """    popup.ApplyTheme(_dark);\n    popup.SetQueueCapacity(Settings.HighlightQueueCapacity);""",
  """    popup.ApplyTheme(_dark);\n    popup.SetSettings(\n      Settings.HighlightQueueCapacity,\n      Settings.ShowRolledBackHistory,\n      Settings.SpeakUserContext);""")
replace_once(
  "AgentPanelSpeaker/TranscriptSettingsPopup.cs",
  """        HighlightQueueCapacity = popup.QueueCapacity,\n        ShowRolledBackHistory = popup.ShowRolledBackHistory""",
  """        HighlightQueueCapacity = popup.QueueCapacity,\n        ShowRolledBackHistory = popup.ShowRolledBackHistory,\n        SpeakUserContext = popup.SpeakUserContext""")

# Core client public option and exact pin.
replace_once(
  "AgentPanelSpeaker/AIConversationCoreClient.cs",
  """/// <param name=\"CodexSessionIndexPath\">\n/// Optional caller-discovered Codex session-index path. The core reads/parses it.\n/// </param>\ninternal sealed record AIConversationCoreProjectOptions(\n  bool IncludeRolledBackTurns = false,\n  string? CodexSessionIndexPath = null);""",
  """/// <param name=\"CodexSessionIndexPath\">\n/// Optional caller-discovered Codex session-index path. The core reads/parses it.\n/// </param>\n/// <param name=\"IncludeUserContext\">\n/// Whether Core-identified User/IDE context participates in speech.\n/// </param>\ninternal sealed record AIConversationCoreProjectOptions(\n  bool IncludeRolledBackTurns = false,\n  string? CodexSessionIndexPath = null,\n  bool IncludeUserContext = false);""")
replace_once(
  "AgentPanelSpeaker/AIConversationCoreClient.cs",
  '    "54a70c2989de0c02f03b28a2f8d8c6986b974141";',
  f'    "{CORE_COMMIT}";')
replace_once(
  "AgentPanelSpeaker/AIConversationCoreClient.cs",
  """      new CoreOptions(effective.IncludeRolledBackTurns),""",
  """      new CoreOptions(\n        effective.IncludeRolledBackTurns,\n        effective.IncludeUserContext),""")
replace_once(
  "AgentPanelSpeaker/AIConversationCoreClient.cs",
  """  private sealed record CoreOptions(\n    [property: JsonPropertyName(\"includeRolledBackTurns\")] bool IncludeRolledBackTurns);""",
  """  private sealed record CoreOptions(\n    [property: JsonPropertyName(\"includeRolledBackTurns\")] bool IncludeRolledBackTurns,\n    [property: JsonPropertyName(\"includeUserContext\")] bool IncludeUserContext);""")

# Worker option forwarding and exact runtime pin.
replace_once(
  "tools/AIConversationCore-worker.mjs",
  "const CORE_COMMIT = '54a70c2989de0c02f03b28a2f8d8c6986b974141';",
  f"const CORE_COMMIT = '{CORE_COMMIT}';")
replace_once(
  "tools/AIConversationCore-worker.mjs",
  """  const options = {\n    includeRolledBackTurns: request?.options?.includeRolledBackTurns === true\n  };""",
  """  const options = {\n    includeRolledBackTurns: request?.options?.includeRolledBackTurns === true,\n    includeUserContext: request?.options?.includeUserContext === true\n  };""")

# Speech projection now honours Core block-level eligibility as well as event-level eligibility.
(ROOT / "AgentPanelSpeaker/CanonicalSpeechProjection.cs").write_text(
  """using System.Text.Json;\nusing System.Text.Json.Nodes;\n\nnamespace AgentPanelSpeaker;\n\n/// <summary>\n/// Applies AIConversationCore speech participation/timer metadata to the\n/// canonical event projection consumed by AgentPanelSpeaker speech/history.\n/// </summary>\ninternal static class CanonicalSpeechProjection\n{\n  /// <summary>\n  /// Removes canonical events/blocks explicitly marked non-speakable and\n  /// applies the core-supplied background-work identity strategy.\n  /// </summary>\n  public static AIConversationProjection Prepare(\n    AIConversationProjection projection)\n  {\n    ArgumentNullException.ThrowIfNull(projection);\n    var events = new List<JsonElement>(projection.Events.Length);\n    foreach (JsonElement eventElement in projection.Events)\n    {\n      if (TryGetSpeechEligibility(eventElement, out bool eligible) && !eligible)\n      {\n        continue;\n      }\n\n      JsonElement speakable = WithoutIneligibleBlocks(eventElement);\n      events.Add(GetBackgroundIdentityKind(speakable) == \"task_timestamp\"\n        ? WithoutToolCallRelationship(speakable)\n        : speakable);\n    }\n    return projection with { Events = events.ToArray() };\n  }\n\n  /// <summary>\n  /// Removes canonical blocks that Core explicitly marks ineligible for speech.\n  /// </summary>\n  private static JsonElement WithoutIneligibleBlocks(JsonElement eventElement)\n  {\n    JsonObject? root = JsonNode.Parse(eventElement.GetRawText()) as JsonObject;\n    if (root?[\"blocks\"] is JsonArray blocks)\n    {\n      for (int index = blocks.Count - 1; index >= 0; --index)\n      {\n        if (blocks[index] is JsonObject block &&\n            block[\"speech\"] is JsonObject speech &&\n            speech[\"eligible\"] is JsonValue value &&\n            value.TryGetValue(out bool eligible) && !eligible)\n        {\n          blocks.RemoveAt(index);\n        }\n      }\n    }\n\n    using JsonDocument document = JsonDocument.Parse(\n      root?.ToJsonString() ?? eventElement.GetRawText());\n    return document.RootElement.Clone();\n  }\n\n  /// <summary>\n  /// Reads optional core-supplied speech eligibility metadata.\n  /// </summary>\n  private static bool TryGetSpeechEligibility(\n    JsonElement eventElement,\n    out bool eligible)\n  {\n    eligible = true;\n    if (eventElement.ValueKind != JsonValueKind.Object ||\n        !eventElement.TryGetProperty(\"speech\", out JsonElement speech) ||\n        speech.ValueKind != JsonValueKind.Object ||\n        !speech.TryGetProperty(\"eligible\", out JsonElement value) ||\n        value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))\n    {\n      return false;\n    }\n\n    eligible = value.GetBoolean();\n    return true;\n  }\n\n  /// <summary>\n  /// Reads the core-supplied background timer identity strategy.\n  /// </summary>\n  private static string GetBackgroundIdentityKind(JsonElement eventElement)\n  {\n    if (!eventElement.TryGetProperty(\"speech\", out JsonElement speech) ||\n        speech.ValueKind != JsonValueKind.Object ||\n        !speech.TryGetProperty(\n          \"background_work_identity\",\n          out JsonElement identity) ||\n        identity.ValueKind != JsonValueKind.Object ||\n        !identity.TryGetProperty(\"kind\", out JsonElement kind) ||\n        kind.ValueKind != JsonValueKind.String)\n    {\n      return string.Empty;\n    }\n    return kind.GetString() ?? string.Empty;\n  }\n\n  /// <summary>\n  /// Removes the tool-call identity from one queue completion projection so the\n  /// existing app timing contract derives `taskId@timestamp` from canonical\n  /// subagent identity and canonical timestamp.\n  /// </summary>\n  private static JsonElement WithoutToolCallRelationship(JsonElement eventElement)\n  {\n    JsonObject? root = JsonNode.Parse(eventElement.GetRawText()) as JsonObject;\n    if (root?[\"relationships\"] is JsonObject relationships)\n    {\n      relationships[\"tool_call_id\"] = null;\n    }\n\n    using JsonDocument document = JsonDocument.Parse(\n      root?.ToJsonString() ?? eventElement.GetRawText());\n    return document.RootElement.Clone();\n  }\n}\n""",
  encoding="utf-8",
  newline="\n")

# Map surviving Core User blocks by Core voice-role metadata, never by provider markers.
replace_once(
  "AgentPanelSpeaker/CanonicalProjectionExtractor.cs",
  """          AddTextBlocks(\n            nodes,\n            eventElement,\n            CanonicalNodeKind(source, kind, contentType, role, channel),\n            ContentCategory.User,\n            timestamp,\n            startsUserTurn: true);""",
  """          AddCanonicalUserBlocks(\n            nodes,\n            eventElement,\n            CanonicalNodeKind(source, kind, contentType, role, channel),\n            timestamp);""")
replace_once(
  "AgentPanelSpeaker/CanonicalProjectionExtractor.cs",
  """  /// <summary>\n  /// Maps visible canonical text blocks onto one app category.\n  /// </summary>\n  private static void AddTextBlocks(""",
  """  /// <summary>\n  /// Maps Core-selected User blocks to the corresponding app voice category.\n  /// </summary>\n  private static void AddCanonicalUserBlocks(\n    ICollection<ExtractedNode> nodes,\n    JsonElement eventElement,\n    string nodeKind,\n    string? timestamp)\n  {\n    bool first = true;\n    foreach (JsonElement block in EnumerateBlocks(eventElement))\n    {\n      string blockType = GetString(block, \"type\");\n      if (blockType is not (\"text\" or \"user_context\"))\n      {\n        continue;\n      }\n\n      string voiceRole = GetNestedString(block, \"speech\", \"voice_role\") ??\n        string.Empty;\n      ContentCategory category = voiceRole == \"user_context\"\n        ? ContentCategory.UserContext\n        : ContentCategory.User;\n      AddNode(\n        nodes,\n        nodeKind,\n        category,\n        GetString(block, \"text\"),\n        timestamp,\n        startsUserTurn: first);\n      first = false;\n    }\n  }\n\n  /// <summary>\n  /// Maps visible canonical text blocks onto one app category.\n  /// </summary>\n  private static void AddTextBlocks(""")

# Monitor/session propagation.
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """  SpeechHistorySnapshot? PreindexedHistory = null,\n  bool IncludeRolledBackTurns = false);""",
  """  SpeechHistorySnapshot? PreindexedHistory = null,\n  bool IncludeRolledBackTurns = false,\n  bool IncludeUserContext = false);""")
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """    bool speakExistingLatestTurn,\n    bool includeRolledBackTurns = false)""",
  """    bool speakExistingLatestTurn,\n    bool includeRolledBackTurns = false,\n    bool includeUserContext = false)""")
# LoadHistoryPreview -> LoadExistingHistory
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """      pendingInputRequests,\n      includeRolledBackTurns);""",
  """      pendingInputRequests,\n      includeRolledBackTurns,\n      includeUserContext);""")
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """          ProjectionOptions(session, settings.IncludeRolledBackTurns));""",
  """          ProjectionOptions(\n            session,\n            settings.IncludeRolledBackTurns,\n            settings.IncludeUserContext));""")
# Two Run() history calls.
old_history = """            pendingInputRequests,\n            settings.IncludeRolledBackTurns);"""
new_history = """            pendingInputRequests,\n            settings.IncludeRolledBackTurns,\n            settings.IncludeUserContext);"""
monitor_path = ROOT / "AgentPanelSpeaker/JsonlSessionMonitor.cs"
monitor_text = monitor_path.read_text(encoding="utf-8")
count = monitor_text.count(old_history)
if count != 2:
  raise RuntimeError(f"JsonlSessionMonitor.cs: expected two Run history targets, found {count}")
monitor_path.write_text(monitor_text.replace(old_history, new_history), encoding="utf-8", newline="\n")
# LoadExistingHistory signature gets user-context option.
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """    IDictionary<string, CodexInputRequest> pendingInputRequests,\n    bool includeRolledBackTurns)""",
  """    IDictionary<string, CodexInputRequest> pendingInputRequests,\n    bool includeRolledBackTurns,\n    bool includeUserContext)""")
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """      ProjectionOptions(session, includeRolledBackTurns));""",
  """      ProjectionOptions(\n        session,\n        includeRolledBackTurns,\n        includeUserContext));""")
replace_once(
  "AgentPanelSpeaker/JsonlSessionMonitor.cs",
  """  private static AIConversationCoreProjectOptions ProjectionOptions(\n    LocatedSession session,\n    bool includeRolledBackTurns)\n  {\n    return new AIConversationCoreProjectOptions(\n      IncludeRolledBackTurns: includeRolledBackTurns,""",
  """  private static AIConversationCoreProjectOptions ProjectionOptions(\n    LocatedSession session,\n    bool includeRolledBackTurns,\n    bool includeUserContext)\n  {\n    return new AIConversationCoreProjectOptions(\n      IncludeRolledBackTurns: includeRolledBackTurns,\n      IncludeUserContext: includeUserContext,""")

# MainForm uses the persisted setting for paused history and monitoring, and
# rebuilds paused history when either Core projection flag changes.
replace_once(
  "AgentPanelSpeaker/MainForm.cs",
  """        IncludeRolledBackTurns:\n          _transcriptSettingsPopup.Settings.ShowRolledBackHistory));""",
  """        IncludeRolledBackTurns:\n          _transcriptSettingsPopup.Settings.ShowRolledBackHistory,\n        IncludeUserContext:\n          _transcriptSettingsPopup.Settings.SpeakUserContext));""")
replace_once(
  "AgentPanelSpeaker/MainForm.cs",
  """          startAtLatestTurn,\n          _transcriptSettingsPopup.Settings.ShowRolledBackHistory));""",
  """          startAtLatestTurn,\n          _transcriptSettingsPopup.Settings.ShowRolledBackHistory,\n          _transcriptSettingsPopup.Settings.SpeakUserContext));""")
replace_once(
  "AgentPanelSpeaker/MainForm.cs",
  """    bool historyVisibilityChanged =\n      _settingsStore.Current.Transcript.ShowRolledBackHistory !=\n      settings.ShowRolledBackHistory;""",
  """    bool historyProjectionChanged =\n      _settingsStore.Current.Transcript.ShowRolledBackHistory !=\n        settings.ShowRolledBackHistory ||\n      _settingsStore.Current.Transcript.SpeakUserContext !=\n        settings.SpeakUserContext;""")
replace_once(
  "AgentPanelSpeaker/MainForm.cs",
  """    if (historyVisibilityChanged && !_monitor.IsRunning &&""",
  """    if (historyProjectionChanged && !_monitor.IsRunning &&""")

# Package the Core's runtime dependency with the .NET output/publish tree.
replace_once(
  "AgentPanelSpeaker/AgentPanelSpeaker.csproj",
  """    <None Include=\"..\\dependencies\\AIConversationCore\\src\\**\\*\"\n          Link=\"tools\\AIConversationCore-runtime\\src\\%(RecursiveDir)%(Filename)%(Extension)\"\n          CopyToOutputDirectory=\"PreserveNewest\"\n          CopyToPublishDirectory=\"PreserveNewest\" />""",
  """    <None Include=\"..\\dependencies\\AIConversationCore\\src\\**\\*\"\n          Link=\"tools\\AIConversationCore-runtime\\src\\%(RecursiveDir)%(Filename)%(Extension)\"\n          CopyToOutputDirectory=\"PreserveNewest\"\n          CopyToPublishDirectory=\"PreserveNewest\" />\n    <None Include=\"..\\tools\\AIConversationCore-runtime\\node_modules\\marked\\**\\*\"\n          Link=\"tools\\AIConversationCore-runtime\\node_modules\\marked\\%(RecursiveDir)%(Filename)%(Extension)\"\n          CopyToOutputDirectory=\"PreserveNewest\"\n          CopyToPublishDirectory=\"PreserveNewest\" />""")

# Snapshot the exact Core runtime used when running from the source checkout.
core_root = ROOT / "dependencies/AIConversationCore"
runtime = ROOT / "tools/AIConversationCore-runtime"
if runtime.exists():
  shutil.rmtree(runtime / "src", ignore_errors=True)
shutil.copytree(core_root / "src", runtime / "src")
shutil.copy2(core_root / "package.json", runtime / "package.json")
if (core_root / "package-lock.json").exists():
  shutil.copy2(core_root / "package-lock.json", runtime / "package-lock.json")
(runtime / "CORE_COMMIT").write_text(CORE_COMMIT + "\n", encoding="utf-8", newline="\n")
marked_src = core_root / "node_modules/marked"
marked_dst = runtime / "node_modules/marked"
shutil.rmtree(marked_dst, ignore_errors=True)
(marked_dst / "lib").mkdir(parents=True, exist_ok=True)
for name in ("package.json", "LICENSE.md"):
  shutil.copy2(marked_src / name, marked_dst / name)
shutil.copy2(marked_src / "lib/marked.esm.js", marked_dst / "lib/marked.esm.js")

# AgentPanel's third-party notice explicitly covers the newly deployed package.
notice = ROOT / "THIRD-PARTY-NOTICES.md"
notice_text = notice.read_text(encoding="utf-8")
if "## marked" not in notice_text:
  notice_text += """\n## marked\n\nAgent Panel Speaker bundles `marked` 18.0.11 through AIConversationCore for\nCore-owned Markdown-to-HTML rendering. `marked` is distributed under the MIT\nLicense. The package's `LICENSE.md` is included with the deployed runtime.\n"""
  notice.write_text(notice_text, encoding="utf-8", newline="\n")
