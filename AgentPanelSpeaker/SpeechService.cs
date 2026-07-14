using System.Speech.Synthesis;

namespace AgentPanelSpeaker;

/// <summary>
/// Queues text through the installed Windows SAPI voices.
/// </summary>
internal sealed class SpeechService : IDisposable
{
  private readonly SpeechSynthesizer _synthesizer = new();
  private bool _disposed;

  /// <summary>
  /// Initializes speech output to the default audio device.
  /// </summary>
  public SpeechService()
  {
    _synthesizer.SetOutputToDefaultAudioDevice();
  }

  /// <summary>
  /// Gets the installed enabled voice names.
  /// </summary>
  /// <returns>Voice names in display order.</returns>
  public IReadOnlyList<string> GetInstalledVoiceNames()
  {
    ThrowIfDisposed();

    return _synthesizer
      .GetInstalledVoices()
      .Where(voice => voice.Enabled)
      .Select(voice => voice.VoiceInfo.Name)
      .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
      .ToArray();
  }

  /// <summary>
  /// Selects the voice and speaking rate.
  /// </summary>
  /// <param name="voiceName">Installed voice name.</param>
  /// <param name="rate">Speech rate from -10 through 10.</param>
  public void Configure(string voiceName, int rate)
  {
    ThrowIfDisposed();

    if (rate is < -10 or > 10)
    {
      throw new ArgumentOutOfRangeException(
        nameof(rate),
        rate,
        "Speech rate must be between -10 and 10.");
    }

    if (!string.IsNullOrWhiteSpace(voiceName))
    {
      _synthesizer.SelectVoice(voiceName);
    }

    _synthesizer.Rate = rate;
  }

  /// <summary>
  /// Queues text for asynchronous speech.
  /// </summary>
  /// <param name="text">Text to speak.</param>
  public void Speak(string text)
  {
    ThrowIfDisposed();

    if (!string.IsNullOrWhiteSpace(text))
    {
      _synthesizer.SpeakAsync(text.Trim());
    }
  }

  /// <summary>
  /// Cancels current and queued speech.
  /// </summary>
  public void CancelAll()
  {
    ThrowIfDisposed();
    _synthesizer.SpeakAsyncCancelAll();
  }

  /// <summary>
  /// Cancels speech and releases the synthesizer.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _synthesizer.SpeakAsyncCancelAll();
    _synthesizer.Dispose();
    _disposed = true;
  }

  /// <summary>
  /// Throws after disposal.
  /// </summary>
  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
