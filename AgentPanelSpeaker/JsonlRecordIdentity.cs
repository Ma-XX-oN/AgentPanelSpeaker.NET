using System.Globalization;
using System.Text.Json;

namespace AgentPanelSpeaker;

/// <summary>
/// Reads the persistent source identity attached to one JSONL record.
/// </summary>
internal static class JsonlRecordIdentity
{
  /// <summary>
  /// Returns the best persistent ID exposed by the source, falling back to the
  /// one-based non-empty JSONL record number.
  /// </summary>
  public static string GetSourceId(
    AgentSource source,
    JsonElement root,
    int recordNumber)
  {
    string[] directNames = source == AgentSource.Claude
      ? new[] { "uuid", "id" }
      : new[] { "id", "item_id", "event_id", "uuid" };
    foreach (string name in directNames)
    {
      if (TryGetNonEmptyString(root, name, out string value))
      {
        return value;
      }
    }

    foreach (string containerName in new[] { "payload", "message" })
    {
      if (!root.TryGetProperty(containerName, out JsonElement container) ||
          container.ValueKind != JsonValueKind.Object)
      {
        continue;
      }
      foreach (string name in directNames)
      {
        if (TryGetNonEmptyString(container, name, out string value))
        {
          return value;
        }
      }
    }

    return recordNumber.ToString(CultureInfo.InvariantCulture);
  }

  private static bool TryGetNonEmptyString(
    JsonElement root,
    string propertyName,
    out string value)
  {
    value = string.Empty;
    if (!root.TryGetProperty(propertyName, out JsonElement property) ||
        property.ValueKind != JsonValueKind.String)
    {
      return false;
    }

    value = property.GetString()?.Trim() ?? string.Empty;
    return value.Length != 0;
  }
}
