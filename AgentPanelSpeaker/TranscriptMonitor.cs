using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace AgentPanelSpeaker;

/// <summary>
/// Polls a selected UI Automation subtree and emits text ready for speech.
/// </summary>
internal sealed class TranscriptMonitor : IDisposable
{
  private readonly TranscriptReader _reader = new();
  private readonly object _sync = new();
  private CancellationTokenSource? _cancellation;
  private Thread? _thread;
  private bool _disposed;

  /// <summary>
  /// Raised when a new text fragment is ready to speak.
  /// </summary>
  public event Action<SpeechFragment>? TextReady;

  /// <summary>
  /// Raised when the visible transcript tail changes.
  /// </summary>
  public event Action<IReadOnlyList<string>>? TailChanged;

  /// <summary>
  /// Raised when monitor status changes.
  /// </summary>
  public event Action<string>? StatusChanged;

  /// <summary>
  /// Raised when monitoring fails.
  /// </summary>
  public event Action<Exception>? Faulted;

  /// <summary>
  /// Gets whether the monitor thread is active.
  /// </summary>
  public bool IsRunning
  {
    get
    {
      lock (_sync)
      {
        return _thread is not null;
      }
    }
  }

  /// <summary>
  /// Starts monitoring a selected target.
  /// </summary>
  /// <param name="target">The selected transcript window and region.</param>
  /// <param name="pollInterval">UI Automation polling interval.</param>
  /// <param name="idleTimeout">Unchanged-text flush timeout.</param>
  /// <param name="speakExistingText">
  /// Whether the initial current paragraph should be spoken.
  /// </param>
  public void Start(
    TranscriptTarget target,
    TimeSpan pollInterval,
    TimeSpan idleTimeout,
    bool speakExistingText)
  {
    ArgumentNullException.ThrowIfNull(target);
    ThrowIfDisposed();

    if (pollInterval <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(
        nameof(pollInterval),
        pollInterval,
        "The polling interval must be positive.");
    }

    if (idleTimeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(
        nameof(idleTimeout),
        idleTimeout,
        "The idle timeout must be positive.");
    }

    DiagnosticLog.Write("monitor.start_requested", new
    {
      target = target.Describe(),
      pollMilliseconds = pollInterval.TotalMilliseconds,
      idleMilliseconds = idleTimeout.TotalMilliseconds,
      speakExistingText
    });

    lock (_sync)
    {
      if (_thread is not null)
      {
        throw new InvalidOperationException(
          "The transcript monitor is already running.");
      }

      _cancellation = new CancellationTokenSource();
      CancellationToken token = _cancellation.Token;
      _thread = new Thread(() => Run(
        target,
        pollInterval,
        idleTimeout,
        speakExistingText,
        token))
      {
        IsBackground = true,
        Name = "Agent panel UI Automation monitor"
      };
      _thread.SetApartmentState(ApartmentState.MTA);
      _thread.Start();
    }
  }

  /// <summary>
  /// Stops monitoring and waits briefly for the worker thread.
  /// </summary>
  public void Stop()
  {
    Thread? thread;
    CancellationTokenSource? cancellation;

    lock (_sync)
    {
      thread = _thread;
      cancellation = _cancellation;
    }

    if (thread is null || cancellation is null)
    {
      return;
    }

    DiagnosticLog.Write("monitor.stop_requested");
    cancellation.Cancel();
    bool joined = thread.Join(TimeSpan.FromSeconds(2));
    DiagnosticLog.Write("monitor.stop_join", new { joined });

    lock (_sync)
    {
      if (ReferenceEquals(_thread, thread))
      {
        _thread = null;
        _cancellation?.Dispose();
        _cancellation = null;
      }
    }

    StatusChanged?.Invoke("Stopped.");
  }

  /// <summary>
  /// Releases the monitor and stops its worker thread.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    Stop();
    _disposed = true;
  }

  /// <summary>
  /// Runs the polling loop on a dedicated MTA thread.
  /// </summary>
  /// <param name="target">The selected transcript window and region.</param>
  /// <param name="pollInterval">UI Automation polling interval.</param>
  /// <param name="idleTimeout">Unchanged-text flush timeout.</param>
  /// <param name="speakExistingText">
  /// Whether the initial current paragraph should be spoken.
  /// </param>
  /// <param name="token">Cancellation token.</param>
  private void Run(
    TranscriptTarget target,
    TimeSpan pollInterval,
    TimeSpan idleTimeout,
    bool speakExistingText,
    CancellationToken token)
  {
    var tracker = new TailTracker(speakExistingText);
    IReadOnlyList<string> previousTail = Array.Empty<string>();
    bool hasPreviousTail = false;
    StatusChanged?.Invoke("Monitoring.");
    DiagnosticLog.Write("monitor.thread_started", new
    {
      apartment = Thread.CurrentThread.GetApartmentState().ToString(),
      target = target.Describe()
    });

    try
    {
      while (!token.IsCancellationRequested)
      {
        DateTime nowUtc = DateTime.UtcNow;
        IReadOnlyList<string> tail = _reader.ReadTail(target);
        bool tailChanged = !hasPreviousTail ||
          !TailsEqual(previousTail, tail);

        if (tailChanged)
        {
          previousTail = tail.ToArray();
          hasPreviousTail = true;
          TailChanged?.Invoke(previousTail);
          DiagnosticLog.Write("monitor.tail_changed", new
          {
            target = target.Describe(),
            source = _reader.LastReadSource,
            details = _reader.LastReadDetails,
            tail = previousTail,
            trackerBefore = tracker.DescribeState()
          });
        }

        IReadOnlyList<SpeechFragment> observed = tracker.Observe(tail, nowUtc);
        IReadOnlyList<SpeechFragment> idle = tracker.FlushIfIdle(
          nowUtc,
          idleTimeout);

        if (tailChanged || observed.Count != 0 || idle.Count != 0)
        {
          DiagnosticLog.Write("monitor.tracker_result", new
          {
            tracker.LastDecision,
            trackerAfter = tracker.DescribeState(),
            observed,
            idle
          });
        }

        Emit(observed, "observe");
        Emit(idle, "idle");

        if (token.WaitHandle.WaitOne(pollInterval))
        {
          break;
        }
      }
    }
    catch (ElementNotAvailableException exception)
    {
      DiagnosticLog.Write("monitor.fault", new
      {
        type = exception.GetType().FullName,
        exception = exception.ToString()
      });
      Faulted?.Invoke(exception);
    }
    catch (InvalidOperationException exception)
    {
      DiagnosticLog.Write("monitor.fault", new
      {
        type = exception.GetType().FullName,
        exception = exception.ToString()
      });
      Faulted?.Invoke(exception);
    }
    catch (COMException exception)
    {
      DiagnosticLog.Write("monitor.fault", new
      {
        type = exception.GetType().FullName,
        exception = exception.ToString()
      });
      Faulted?.Invoke(exception);
    }
    finally
    {
      DiagnosticLog.Write("monitor.thread_ending", new
      {
        cancelled = token.IsCancellationRequested
      });
      lock (_sync)
      {
        if (ReferenceEquals(Thread.CurrentThread, _thread))
        {
          _thread = null;
          _cancellation?.Dispose();
          _cancellation = null;
        }
      }
    }
  }

  /// <summary>
  /// Determines whether two observed tails contain the same text.
  /// </summary>
  /// <param name="left">The previous tail.</param>
  /// <param name="right">The current tail.</param>
  /// <returns>True when both tails are identical.</returns>
  private static bool TailsEqual(
    IReadOnlyList<string> left,
    IReadOnlyList<string> right)
  {
    if (left.Count != right.Count)
    {
      return false;
    }

    for (int index = 0; index < left.Count; ++index)
    {
      if (!string.Equals(
            left[index],
            right[index],
            StringComparison.Ordinal))
      {
        return false;
      }
    }

    return true;
  }

  /// <summary>
  /// Raises TextReady for each non-empty speech fragment.
  /// </summary>
  /// <param name="fragments">Speech fragments.</param>
  /// <param name="source">Tracker path that emitted the fragments.</param>
  private void Emit(
    IReadOnlyList<SpeechFragment> fragments,
    string source)
  {
    foreach (SpeechFragment fragment in fragments)
    {
      if (fragment.Text.Length != 0)
      {
        DiagnosticLog.Write("monitor.emit", new
        {
          source,
          fragment.NodeId,
          text = fragment.Text
        });
        TextReady?.Invoke(fragment);
      }
    }
  }

  /// <summary>
  /// Throws after disposal.
  /// </summary>
  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
