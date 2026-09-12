namespace AgentPanelSpeaker;

/// <summary>
/// Identifies one canonical transcript word selected by Find or Ctrl+click.
/// </summary>
internal sealed class FindSeekRequestedEventArgs : EventArgs
{
  public FindSeekRequestedEventArgs(
    long wordId,
    string source = "find")
  {
    WordId = wordId;
    Source = source;
  }

  /// <summary>
  /// Gets the immutable Core transcript word handle.
  /// </summary>
  public long WordId { get; }

  /// <summary>
  /// Gets the UI action that requested the seek.
  /// </summary>
  public string Source { get; }
}
