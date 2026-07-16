using System.Text;

namespace AgentPanelSpeaker;

/// <summary>
/// Stores one uncompressed PCM waveform and supports lossless concatenation.
/// </summary>
internal sealed class PcmWaveData
{
  private const ushort PcmFormatTag = 1;
  private const ushort ExtensibleFormatTag = 0xFFFE;
  private static readonly byte[] PcmSubFormatGuid =
  {
    0x01, 0x00, 0x00, 0x00,
    0x00, 0x00,
    0x10, 0x00,
    0x80, 0x00,
    0x00, 0xAA,
    0x00, 0x38, 0x9B, 0x71
  };

  private PcmWaveData(
    ushort channels,
    int sampleRate,
    ushort bitsPerSample,
    byte[] samples)
  {
    ArgumentNullException.ThrowIfNull(samples);
    if (channels == 0)
    {
      throw new ArgumentOutOfRangeException(nameof(channels));
    }
    if (sampleRate <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sampleRate));
    }
    if (bitsPerSample is not (8 or 16 or 24 or 32))
    {
      throw new NotSupportedException(
        $"PCM sample size {bitsPerSample} bits is unsupported.");
    }

    int blockAlign = checked(channels * (bitsPerSample / 8));
    if (samples.Length % blockAlign != 0)
    {
      throw new InvalidDataException(
        "PCM sample data is not aligned to complete sample frames.");
    }

    Channels = channels;
    SampleRate = sampleRate;
    BitsPerSample = bitsPerSample;
    Samples = samples;
  }

  public ushort Channels { get; }

  public int SampleRate { get; }

  public ushort BitsPerSample { get; }

  public ushort BlockAlign => checked(
    (ushort)(Channels * (BitsPerSample / 8)));

  public int AverageBytesPerSecond => checked(SampleRate * BlockAlign);

  public byte[] Samples { get; }

  /// <summary>
  /// Parses a RIFF WAVE file containing integer PCM samples.
  /// </summary>
  public static PcmWaveData Parse(byte[] waveFile)
  {
    ArgumentNullException.ThrowIfNull(waveFile);
    using var stream = new MemoryStream(waveFile, writable: false);
    using var reader = new BinaryReader(
      stream,
      Encoding.ASCII,
      leaveOpen: true);
    if (stream.Length < 12 || ReadFourCc(reader) != "RIFF")
    {
      throw new InvalidDataException("Speech output is not a RIFF file.");
    }
    _ = reader.ReadUInt32();
    if (ReadFourCc(reader) != "WAVE")
    {
      throw new InvalidDataException("Speech output is not a WAVE file.");
    }

    ushort formatTag = 0;
    ushort channels = 0;
    int sampleRate = 0;
    ushort blockAlign = 0;
    ushort bitsPerSample = 0;
    byte[]? formatExtra = null;
    byte[]? samples = null;

    while (stream.Position + 8 <= stream.Length)
    {
      string chunkId = ReadFourCc(reader);
      uint chunkSize = reader.ReadUInt32();
      long chunkEnd = checked(stream.Position + chunkSize);
      if (chunkEnd > stream.Length)
      {
        throw new InvalidDataException("A WAVE chunk extends past the file.");
      }

      if (chunkId == "fmt ")
      {
        if (chunkSize < 16)
        {
          throw new InvalidDataException(
            "The WAVE format chunk is incomplete.");
        }
        formatTag = reader.ReadUInt16();
        channels = reader.ReadUInt16();
        sampleRate = reader.ReadInt32();
        _ = reader.ReadInt32();
        blockAlign = reader.ReadUInt16();
        bitsPerSample = reader.ReadUInt16();
        int extraLength = checked((int)chunkSize - 16);
        formatExtra = reader.ReadBytes(extraLength);
        if (formatExtra.Length != extraLength)
        {
          throw new EndOfStreamException();
        }
      }
      else if (chunkId == "data")
      {
        int dataLength = checked((int)chunkSize);
        samples = reader.ReadBytes(dataLength);
        if (samples.Length != dataLength)
        {
          throw new EndOfStreamException();
        }
      }

      stream.Position = chunkEnd + (chunkSize & 1U);
    }

    if (formatTag == 0 || samples is null)
    {
      throw new InvalidDataException(
        "The WAVE file does not contain both format and sample data.");
    }
    if (!IsIntegerPcm(formatTag, formatExtra))
    {
      throw new NotSupportedException(
        $"Speech produced unsupported WAVE format 0x{formatTag:X4}.");
    }

    int expectedBlockAlign = checked(channels * (bitsPerSample / 8));
    if (channels == 0 || sampleRate <= 0 ||
        bitsPerSample is not (8 or 16 or 24 or 32) ||
        blockAlign != expectedBlockAlign)
    {
      throw new InvalidDataException(
        "Speech produced an invalid or unsupported PCM format.");
    }

    return new PcmWaveData(channels, sampleRate, bitsPerSample, samples);
  }

  /// <summary>
  /// Creates a standard RIFF WAVE file for playback or diagnostics.
  /// </summary>
  public byte[] ToWaveFile()
  {
    int dataPadding = Samples.Length & 1;
    int riffSize = checked(36 + Samples.Length + dataPadding);
    using var stream = new MemoryStream(
      checked(44 + Samples.Length + dataPadding));
    using var writer = new BinaryWriter(
      stream,
      Encoding.ASCII,
      leaveOpen: true);
    WriteFourCc(writer, "RIFF");
    writer.Write(riffSize);
    WriteFourCc(writer, "WAVE");
    WriteFourCc(writer, "fmt ");
    writer.Write(16);
    writer.Write(PcmFormatTag);
    writer.Write(Channels);
    writer.Write(SampleRate);
    writer.Write(AverageBytesPerSecond);
    writer.Write(BlockAlign);
    writer.Write(BitsPerSample);
    WriteFourCc(writer, "data");
    writer.Write(Samples.Length);
    writer.Write(Samples);
    if (dataPadding != 0)
    {
      writer.Write((byte)0);
    }
    writer.Flush();
    return stream.ToArray();
  }

  /// <summary>
  /// Concatenates waveforms whose PCM formats are identical.
  /// </summary>
  public static PcmWaveData Concatenate(
    IReadOnlyList<PcmWaveData> parts)
  {
    ArgumentNullException.ThrowIfNull(parts);
    if (parts.Count == 0)
    {
      throw new ArgumentException(
        "At least one waveform is required.",
        nameof(parts));
    }

    PcmWaveData first = parts[0];
    int totalLength = 0;
    foreach (PcmWaveData part in parts)
    {
      first.AssertSameFormat(part);
      totalLength = checked(totalLength + part.Samples.Length);
    }

    var combined = new byte[totalLength];
    int offset = 0;
    foreach (PcmWaveData part in parts)
    {
      Buffer.BlockCopy(
        part.Samples,
        0,
        combined,
        offset,
        part.Samples.Length);
      offset += part.Samples.Length;
    }
    return new PcmWaveData(
      first.Channels,
      first.SampleRate,
      first.BitsPerSample,
      combined);
  }

  /// <summary>
  /// Converts any supported PCM input to 48 kHz-style mono 16-bit PCM.
  /// </summary>
  public PcmWaveData ConvertToMono16(int targetSampleRate)
  {
    if (targetSampleRate <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
    }

    int sourceFrames = Samples.Length / BlockAlign;
    if (sourceFrames == 0)
    {
      return new PcmWaveData(1, targetSampleRate, 16, Array.Empty<byte>());
    }

    var source = new double[sourceFrames];
    int bytesPerSample = BitsPerSample / 8;
    for (int frame = 0; frame < sourceFrames; ++frame)
    {
      double sum = 0.0;
      for (int channel = 0; channel < Channels; ++channel)
      {
        int offset = (frame * Channels + channel) * bytesPerSample;
        sum += ReadNormalizedSample(Samples, offset);
      }
      source[frame] = sum / Channels;
    }

    int targetFrames = Math.Max(
      1,
      checked((int)Math.Round(
        sourceFrames * (double)targetSampleRate / SampleRate)));
    var target = new byte[checked(targetFrames * 2)];
    for (int frame = 0; frame < targetFrames; ++frame)
    {
      double sourcePosition = targetFrames == 1
        ? 0.0
        : frame * (sourceFrames - 1.0) / (targetFrames - 1.0);
      int lower = (int)sourcePosition;
      int upper = Math.Min(sourceFrames - 1, lower + 1);
      double fraction = sourcePosition - lower;
      double sample = source[lower] +
        (source[upper] - source[lower]) * fraction;
      short value = (short)Math.Clamp(
        Math.Round(sample * short.MaxValue),
        short.MinValue,
        short.MaxValue);
      int offset = frame * 2;
      target[offset] = (byte)value;
      target[offset + 1] = (byte)(value >> 8);
    }

    return new PcmWaveData(1, targetSampleRate, 16, target);
  }

  /// <summary>
  /// Creates a sine tone in this waveform's exact PCM format.
  /// </summary>
  public PcmWaveData CreateTone(
    int frequencyHertz,
    int volumePercent,
    int durationMilliseconds)
  {
    if (frequencyHertz <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(frequencyHertz));
    }
    int volume = Math.Clamp(volumePercent, 0, 100);
    int duration = Math.Max(0, durationMilliseconds);
    int frameCount = checked((int)Math.Max(
      1L,
      (long)SampleRate * duration / 1000L));
    var samples = CreateSilenceBytes(frameCount);
    int fadeFrames = Math.Min(frameCount / 2, SampleRate / 200);
    int bytesPerSample = BitsPerSample / 8;

    for (int frame = 0; frame < frameCount; ++frame)
    {
      double fade = 1.0;
      if (fadeFrames > 0 && frame < fadeFrames)
      {
        fade = frame / (double)fadeFrames;
      }
      else if (fadeFrames > 0 && frame >= frameCount - fadeFrames)
      {
        fade = (frameCount - frame - 1) / (double)fadeFrames;
      }

      double angle = 2.0 * Math.PI * frequencyHertz * frame / SampleRate;
      double normalized = Math.Sin(angle) * (volume / 100.0) *
        Math.Max(0.0, fade);
      for (int channel = 0; channel < Channels; ++channel)
      {
        int offset = (frame * Channels + channel) * bytesPerSample;
        WriteNormalizedSample(samples, offset, normalized);
      }
    }

    return new PcmWaveData(Channels, SampleRate, BitsPerSample, samples);
  }

  /// <summary>
  /// Creates digital silence in this waveform's exact PCM format.
  /// </summary>
  public PcmWaveData CreateSilence(int durationMilliseconds)
  {
    int duration = Math.Max(0, durationMilliseconds);
    int frameCount = checked((int)(
      (long)SampleRate * duration / 1000L));
    return new PcmWaveData(
      Channels,
      SampleRate,
      BitsPerSample,
      CreateSilenceBytes(frameCount));
  }

  /// <summary>
  /// Creates an empty-format template suitable for a standalone wake test.
  /// </summary>
  public static PcmWaveData CreateDefaultFormat()
  {
    return new PcmWaveData(1, 48000, 16, Array.Empty<byte>());
  }

  private static bool IsIntegerPcm(ushort formatTag, byte[]? formatExtra)
  {
    if (formatTag == PcmFormatTag)
    {
      return true;
    }
    if (formatTag != ExtensibleFormatTag ||
        formatExtra is null ||
        formatExtra.Length < 24)
    {
      return false;
    }
    return formatExtra.AsSpan(8, 16).SequenceEqual(PcmSubFormatGuid);
  }

  private void AssertSameFormat(PcmWaveData other)
  {
    if (Channels != other.Channels ||
        SampleRate != other.SampleRate ||
        BitsPerSample != other.BitsPerSample)
    {
      throw new InvalidDataException(
        "Speech segments produced incompatible PCM formats.");
    }
  }

  private byte[] CreateSilenceBytes(int frameCount)
  {
    int byteCount = checked(frameCount * BlockAlign);
    var samples = new byte[byteCount];
    if (BitsPerSample == 8)
    {
      Array.Fill(samples, (byte)128);
    }
    return samples;
  }

  private double ReadNormalizedSample(byte[] source, int offset)
  {
    return BitsPerSample switch
    {
      8 => (source[offset] - 128) / 128.0,
      16 => (short)(source[offset] | (source[offset + 1] << 8)) /
        32768.0,
      24 => ReadSigned24(source, offset) / 8388608.0,
      32 => BitConverter.ToInt32(source, offset) / 2147483648.0,
      _ => throw new InvalidOperationException("Unsupported PCM sample size.")
    };
  }

  private static int ReadSigned24(byte[] source, int offset)
  {
    int value = source[offset] |
      (source[offset + 1] << 8) |
      (source[offset + 2] << 16);
    return (value & 0x00800000) == 0
      ? value
      : value | unchecked((int)0xFF000000);
  }

  private void WriteNormalizedSample(
    byte[] destination,
    int offset,
    double normalized)
  {
    double clamped = Math.Clamp(normalized, -1.0, 1.0);
    switch (BitsPerSample)
    {
      case 8:
      {
        int value = (int)Math.Round(128.0 + clamped * 127.0);
        destination[offset] = (byte)Math.Clamp(value, 0, 255);
        break;
      }

      case 16:
      {
        short value = (short)Math.Clamp(
          Math.Round(clamped * short.MaxValue),
          short.MinValue,
          short.MaxValue);
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        break;
      }

      case 24:
      {
        int value = (int)Math.Clamp(
          Math.Round(clamped * 8388607.0),
          -8388608.0,
          8388607.0);
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
        break;
      }

      case 32:
      {
        int value = (int)Math.Clamp(
          Math.Round(clamped * int.MaxValue),
          int.MinValue,
          int.MaxValue);
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
        destination[offset + 3] = (byte)(value >> 24);
        break;
      }

      default:
        throw new InvalidOperationException("Unsupported PCM sample size.");
    }
  }

  private static string ReadFourCc(BinaryReader reader)
  {
    byte[] bytes = reader.ReadBytes(4);
    if (bytes.Length != 4)
    {
      throw new EndOfStreamException();
    }
    return Encoding.ASCII.GetString(bytes);
  }

  private static void WriteFourCc(BinaryWriter writer, string value)
  {
    if (value.Length != 4)
    {
      throw new ArgumentException("A FourCC must contain four characters.");
    }
    writer.Write(Encoding.ASCII.GetBytes(value));
  }
}
