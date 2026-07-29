using System.Runtime.InteropServices;

namespace AgentPanelSpeaker;

/// <summary>
/// Keeps the Windows display awake while requested by active speech.
/// </summary>
internal sealed class DisplayAwakeController : IDisposable
{
  private bool _isActive;
  private bool _disposed;

  /// <summary>
  /// Raised when the display request changes or Windows rejects it.
  /// </summary>
  public event Action<string>? Activity;

  /// <summary>
  /// Applies or releases the continuous display-required execution state.
  /// </summary>
  public void SetActive(bool active)
  {
    if (_disposed)
    {
      return;
    }
    if (_isActive == active)
    {
      return;
    }

    ExecutionState requested = ExecutionState.Continuous;
    if (active)
    {
      requested |= ExecutionState.DisplayRequired;
    }

    ExecutionState previous = SetThreadExecutionState(requested);
    if (previous == 0)
    {
      int error = Marshal.GetLastPInvokeError();
      DiagnosticLog.Write("display_awake.failed", new
      {
        active,
        error
      });
      Activity?.Invoke(
        $"Windows rejected the display-awake request (error {error}).");
      return;
    }

    _isActive = active;
    DiagnosticLog.Write("display_awake.changed", new { active });
    Activity?.Invoke(active
      ? "Keeping the display on while speech is playing."
      : "Display sleep prevention released.");
  }

  /// <summary>
  /// Releases any outstanding execution-state request.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    if (_isActive)
    {
      SetActive(false);
    }
    _disposed = true;
  }

  [Flags]
  private enum ExecutionState : uint
  {
    DisplayRequired = 0x00000002,
    Continuous = 0x80000000
  }

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern ExecutionState SetThreadExecutionState(
    ExecutionState executionState);
}
