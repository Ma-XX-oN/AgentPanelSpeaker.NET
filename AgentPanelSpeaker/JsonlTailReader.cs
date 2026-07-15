using System.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Reads complete UTF-8 JSONL records appended to a file shared by another
/// process.
/// </summary>
internal sealed class JsonlTailReader
{
  private readonly List<byte> _pendingBytes = new();
  private long _offset;

  /// <summary>
  /// Initializes a tail reader at the current end of the file.
  /// </summary>
  /// <param name="path">JSONL path.</param>
  public JsonlTailReader(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    Path = System.IO.Path.GetFullPath(path);
    using var stream = OpenShared(Path);
    _offset = stream.Length;
  }

  /// <summary>
  /// Gets the monitored file path.
  /// </summary>
  public string Path { get; }

  /// <summary>
  /// Gets the next unread byte offset.
  /// </summary>
  public long Offset => _offset;

  /// <summary>
  /// Reads all currently available complete lines.
  /// </summary>
  /// <returns>Complete JSONL lines without line terminators.</returns>
  public IReadOnlyList<string> ReadAvailableLines()
  {
    using FileStream stream = OpenShared(Path);
    if (stream.Length < _offset)
    {
      DiagnosticLog.Write("jsonl.file_truncated", new
      {
        path = Path,
        previousOffset = _offset,
        newLength = stream.Length
      });
      _offset = stream.Length;
      _pendingBytes.Clear();
      return Array.Empty<string>();
    }

    if (stream.Length == _offset)
    {
      return Array.Empty<string>();
    }

    stream.Position = _offset;
    var buffer = new byte[64 * 1024];
    while (stream.Position < stream.Length)
    {
      int read = stream.Read(buffer, 0, buffer.Length);
      if (read <= 0)
      {
        break;
      }

      _pendingBytes.AddRange(buffer.AsSpan(0, read).ToArray());
      _offset += read;
    }

    return ExtractCompleteLines();
  }

  /// <summary>
  /// Opens a JSONL file without blocking the agent writer.
  /// </summary>
  private static FileStream OpenShared(string path)
  {
    return new FileStream(
      path,
      FileMode.Open,
      FileAccess.Read,
      FileShare.ReadWrite | FileShare.Delete);
  }

  /// <summary>
  /// Extracts newline-terminated UTF-8 records and preserves a partial tail.
  /// </summary>
  private IReadOnlyList<string> ExtractCompleteLines()
  {
    if (_pendingBytes.Count == 0)
    {
      return Array.Empty<string>();
    }

    var lines = new List<string>();
    int lineStart = 0;
    for (int index = 0; index < _pendingBytes.Count; ++index)
    {
      if (_pendingBytes[index] != (byte)'\n')
      {
        continue;
      }

      int length = index - lineStart;
      if (length > 0 && _pendingBytes[index - 1] == (byte)'\r')
      {
        --length;
      }

      if (length > 0)
      {
        byte[] bytes = _pendingBytes
          .Skip(lineStart)
          .Take(length)
          .ToArray();
        lines.Add(Encoding.UTF8.GetString(bytes));
      }

      lineStart = index + 1;
    }

    if (lineStart != 0)
    {
      _pendingBytes.RemoveRange(0, lineStart);
    }

    return lines;
  }
}
