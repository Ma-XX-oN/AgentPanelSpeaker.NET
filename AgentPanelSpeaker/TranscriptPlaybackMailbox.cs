namespace AgentPanelSpeaker;

/// <summary>
/// Retains a bounded number of newest transcript playback positions while
/// allowing at most one pending UI-thread wake-up.
/// </summary>
internal sealed class TranscriptPlaybackMailbox
{
  private readonly object _sync = new();
  private TranscriptPlaybackPosition?[] _buffer;
  private int _head;
  private int _count;
  private bool _wakePending;

  /// <summary>
  /// Creates a mailbox with the requested bounded capacity.
  /// </summary>
  public TranscriptPlaybackMailbox(int capacity)
  {
    _buffer = new TranscriptPlaybackPosition?[NormalizeCapacity(capacity)];
  }

  /// <summary>
  /// Publishes one position and reports whether the caller must request a UI
  /// wake-up. When full, the oldest retained position is discarded.
  /// </summary>
  public bool Publish(TranscriptPlaybackPosition position)
  {
    ArgumentNullException.ThrowIfNull(position);

    lock (_sync)
    {
      if (_count == _buffer.Length)
      {
        _buffer[_head] = null;
        _head = (_head + 1) % _buffer.Length;
        _count--;
      }

      int tail = (_head + _count) % _buffer.Length;
      _buffer[tail] = position;
      _count++;

      if (_wakePending)
      {
        return false;
      }

      _wakePending = true;
      return true;
    }
  }

  /// <summary>
  /// Captures the number of positions retained when one UI wake-up begins.
  /// Positions published while that wake-up runs remain for the next wake-up.
  /// </summary>
  public int GetWakeBatchCount()
  {
    lock (_sync)
    {
      return _count;
    }
  }

  /// <summary>
  /// Removes the oldest retained position.
  /// </summary>
  public bool TryTake(out TranscriptPlaybackPosition position)
  {
    lock (_sync)
    {
      if (_count == 0)
      {
        position = null!;
        return false;
      }

      position = _buffer[_head]!;
      _buffer[_head] = null;
      _head = (_head + 1) % _buffer.Length;
      _count--;
      return true;
    }
  }

  /// <summary>
  /// Completes one UI wake-up. Returns true when another wake-up is required
  /// because a producer published while the current wake-up was running.
  /// </summary>
  public bool CompleteWake()
  {
    lock (_sync)
    {
      if (_count != 0)
      {
        return true;
      }

      _wakePending = false;
      return false;
    }
  }

  /// <summary>
  /// Changes the bounded capacity while preserving the newest retained values.
  /// </summary>
  public void SetCapacity(int capacity)
  {
    int normalized = NormalizeCapacity(capacity);
    lock (_sync)
    {
      if (normalized == _buffer.Length)
      {
        return;
      }

      int retained = Math.Min(_count, normalized);
      var replacement = new TranscriptPlaybackPosition?[normalized];
      int firstRetained = _count - retained;
      for (int index = 0; index < retained; ++index)
      {
        int source = (_head + firstRetained + index) % _buffer.Length;
        replacement[index] = _buffer[source];
      }

      _buffer = replacement;
      _head = 0;
      _count = retained;
    }
  }

  /// <summary>
  /// Discards retained positions and releases any pending-wake state.
  /// </summary>
  public void Clear()
  {
    lock (_sync)
    {
      Array.Clear(_buffer, 0, _buffer.Length);
      _head = 0;
      _count = 0;
      _wakePending = false;
    }
  }

  private static int NormalizeCapacity(int capacity)
  {
    return Math.Clamp(capacity, 1, 16);
  }
}
