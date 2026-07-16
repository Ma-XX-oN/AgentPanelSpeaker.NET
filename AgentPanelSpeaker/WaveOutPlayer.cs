using System.Runtime.InteropServices;
using System.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Plays one PCM buffer through one WinMM waveOut stream.
/// </summary>
internal sealed class WaveOutPlayer : IDisposable
{
  private const uint WaveMapper = uint.MaxValue;
  private const uint WaveHeaderDone = 0x00000001;
  private const int NoError = 0;
  private const int WaveStillPlaying = 33;

  private IntPtr _waveOut;
  private IntPtr _sampleBuffer;
  private IntPtr _headerBuffer;
  private bool _prepared;
  private bool _disposed;

  /// <summary>
  /// Opens the default output device and starts one contiguous PCM buffer.
  /// </summary>
  public WaveOutPlayer(PcmWaveData wave)
  {
    ArgumentNullException.ThrowIfNull(wave);
    if (wave.Samples.Length == 0)
    {
      throw new ArgumentException(
        "The playback waveform contains no samples.",
        nameof(wave));
    }

    var format = new WaveFormatEx
    {
      FormatTag = 1,
      Channels = wave.Channels,
      SamplesPerSecond = checked((uint)wave.SampleRate),
      AverageBytesPerSecond = checked((uint)wave.AverageBytesPerSecond),
      BlockAlign = wave.BlockAlign,
      BitsPerSample = wave.BitsPerSample,
      ExtraSize = 0
    };

    try
    {
      CheckResult(
        waveOutOpen(
          out _waveOut,
          WaveMapper,
          ref format,
          IntPtr.Zero,
          IntPtr.Zero,
          0),
        "open the default audio output");

      _sampleBuffer = Marshal.AllocHGlobal(wave.Samples.Length);
      Marshal.Copy(wave.Samples, 0, _sampleBuffer, wave.Samples.Length);
      var header = new WaveHeader
      {
        Data = _sampleBuffer,
        BufferLength = checked((uint)wave.Samples.Length)
      };
      _headerBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
      Marshal.StructureToPtr(header, _headerBuffer, fDeleteOld: false);
      CheckResult(
        waveOutPrepareHeader(
          _waveOut,
          _headerBuffer,
          checked((uint)Marshal.SizeOf<WaveHeader>())),
        "prepare the audio buffer");
      _prepared = true;
      CheckResult(
        waveOutWrite(
          _waveOut,
          _headerBuffer,
          checked((uint)Marshal.SizeOf<WaveHeader>())),
        "start audio playback");
    }
    catch
    {
      Dispose();
      throw;
    }
  }

  /// <summary>
  /// Gets whether WinMM has consumed the complete audio buffer.
  /// </summary>
  public bool IsComplete
  {
    get
    {
      ThrowIfDisposed();
      WaveHeader header = Marshal.PtrToStructure<WaveHeader>(_headerBuffer);
      return (header.Flags & WaveHeaderDone) != 0;
    }
  }

  /// <summary>
  /// Pauses output without closing the audio stream.
  /// </summary>
  public void Pause()
  {
    ThrowIfDisposed();
    CheckResult(waveOutPause(_waveOut), "pause audio playback");
  }

  /// <summary>
  /// Restarts a paused output stream.
  /// </summary>
  public void Resume()
  {
    ThrowIfDisposed();
    CheckResult(waveOutRestart(_waveOut), "resume audio playback");
  }

  /// <summary>
  /// Stops output immediately while leaving disposal to the owner.
  /// </summary>
  public void Stop()
  {
    if (_disposed || _waveOut == IntPtr.Zero)
    {
      return;
    }
    CheckResult(waveOutReset(_waveOut), "stop audio playback");
  }

  /// <summary>
  /// Stops playback, releases the wave header, and closes the device.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    if (_waveOut != IntPtr.Zero)
    {
      _ = waveOutReset(_waveOut);
    }
    if (_prepared && _waveOut != IntPtr.Zero && _headerBuffer != IntPtr.Zero)
    {
      int result;
      do
      {
        result = waveOutUnprepareHeader(
          _waveOut,
          _headerBuffer,
          checked((uint)Marshal.SizeOf<WaveHeader>()));
        if (result == WaveStillPlaying)
        {
          Thread.Sleep(1);
        }
      }
      while (result == WaveStillPlaying);
      _prepared = false;
    }
    if (_waveOut != IntPtr.Zero)
    {
      _ = waveOutClose(_waveOut);
      _waveOut = IntPtr.Zero;
    }
    if (_headerBuffer != IntPtr.Zero)
    {
      Marshal.FreeHGlobal(_headerBuffer);
      _headerBuffer = IntPtr.Zero;
    }
    if (_sampleBuffer != IntPtr.Zero)
    {
      Marshal.FreeHGlobal(_sampleBuffer);
      _sampleBuffer = IntPtr.Zero;
    }
  }

  private static void CheckResult(int result, string action)
  {
    if (result == NoError)
    {
      return;
    }

    var message = new StringBuilder(256);
    _ = waveOutGetErrorText(result, message, message.Capacity);
    throw new InvalidOperationException(
      $"Could not {action}: {message} (WinMM {result}).");
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct WaveFormatEx
  {
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSecond;
    public uint AverageBytesPerSecond;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct WaveHeader
  {
    public IntPtr Data;
    public uint BufferLength;
    public uint BytesRecorded;
    public IntPtr User;
    public uint Flags;
    public uint Loops;
    public IntPtr Next;
    public IntPtr Reserved;
  }

  [DllImport("winmm.dll")]
  private static extern int waveOutOpen(
    out IntPtr waveOut,
    uint deviceId,
    ref WaveFormatEx format,
    IntPtr callback,
    IntPtr instance,
    uint flags);

  [DllImport("winmm.dll")]
  private static extern int waveOutPrepareHeader(
    IntPtr waveOut,
    IntPtr header,
    uint headerSize);

  [DllImport("winmm.dll")]
  private static extern int waveOutWrite(
    IntPtr waveOut,
    IntPtr header,
    uint headerSize);

  [DllImport("winmm.dll")]
  private static extern int waveOutPause(IntPtr waveOut);

  [DllImport("winmm.dll")]
  private static extern int waveOutRestart(IntPtr waveOut);

  [DllImport("winmm.dll")]
  private static extern int waveOutReset(IntPtr waveOut);

  [DllImport("winmm.dll")]
  private static extern int waveOutUnprepareHeader(
    IntPtr waveOut,
    IntPtr header,
    uint headerSize);

  [DllImport("winmm.dll")]
  private static extern int waveOutClose(IntPtr waveOut);

  [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
  private static extern int waveOutGetErrorText(
    int error,
    StringBuilder text,
    int textLength);
}
