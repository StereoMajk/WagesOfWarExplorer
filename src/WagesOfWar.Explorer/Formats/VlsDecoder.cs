using System;
using System.Collections.Generic;

namespace WagesOfWar.Explorer.Formats;

/// <summary>
/// One lip-sync keyframe from a VALS container (.VLS or .VLA file).
/// mouth_shape == -1  → silence / mouth closed (no specific phoneme)
/// mouth_shape  0–21  → viseme index (game-specific mouth shape)
/// </summary>
public readonly record struct LipSyncEntry(int MouthShape, uint PcmByteOffset)
{
    /// <summary>Approximate time in milliseconds (assumes 22 050 Hz 8-bit mono PCM).</summary>
    public double TimeMs => PcmByteOffset / 22050.0 * 1000.0;
}

/// <summary>
/// Parsed contents of a VALS container (VLS or VLA).
/// </summary>
public sealed class VlsFile
{
    public LipSyncEntry[] LipSyncEntries { get; init; } = Array.Empty<LipSyncEntry>();

    /// <summary>Byte offset into the source array where the embedded RIFF WAV starts; −1 if none.</summary>
    public int WavOffset { get; init; } = -1;

    /// <summary>Byte length of the embedded WAV (from RIFF magic to end of file).</summary>
    public int WavLength { get; init; }

    public bool HasAudio => WavOffset >= 0;

    /// <summary>
    /// Approximate audio duration in seconds.
    /// Assumes standard 44-byte RIFF/WAV header then 8-bit mono 22 050 Hz PCM.
    /// </summary>
    public double DurationSeconds
    {
        get
        {
            if (!HasAudio || WavLength <= 44) return 0;
            return (WavLength - 44) / 22050.0;
        }
    }
}

public static class VlsDecoder
{
    public static bool IsVals(ReadOnlySpan<byte> data)
        => data.Length >= 4
           && data[0] == 'V' && data[1] == 'A' && data[2] == 'L' && data[3] == 'S';

    /// <summary>
    /// Parses a full VALS container from raw bytes.
    /// Throws <see cref="FormatException"/> if the file is not a valid VALS container.
    /// </summary>
    public static VlsFile Parse(byte[] data)
    {
        if (data.Length < 8)
            throw new FormatException($"File too small ({data.Length} B) for a VALS header.");
        if (!IsVals(data))
            throw new FormatException("Missing VALS magic bytes.");

        // Bytes 4–7: total byte size of the lipsync entry table.
        int lipTableBytes = BitConverter.ToInt32(data, 4);
        int entryCount    = lipTableBytes / 8;          // each entry is 8 bytes

        var entries = new List<LipSyncEntry>(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            int base_ = 8 + i * 8;
            if (base_ + 8 > data.Length) break;

            int  shape  = BitConverter.ToInt32(data, base_);
            uint pcmPos = BitConverter.ToUInt32(data, base_ + 4);

            // Sentinel entry: marks end-of-table (shape=-2, pcmPos=0xFFFFFFFE).
            if (shape == -2 || pcmPos == 0xFFFFFFFE) continue;

            entries.Add(new LipSyncEntry(shape, pcmPos));
        }

        // Locate embedded RIFF WAV: scan forward from end of lipsync table.
        int riffOffset = -1;
        int riffLen    = 0;
        int searchFrom = 8 + lipTableBytes;
        for (int i = searchFrom; i <= data.Length - 4; i++)
        {
            if (data[i] == 'R' && data[i + 1] == 'I' && data[i + 2] == 'F' && data[i + 3] == 'F')
            {
                riffOffset = i;
                riffLen    = data.Length - i;
                break;
            }
        }

        return new VlsFile
        {
            LipSyncEntries = entries.ToArray(),
            WavOffset      = riffOffset,
            WavLength      = riffLen,
        };
    }

    /// <summary>
    /// Returns a copy of the embedded WAV bytes, or null if the file has no audio.
    /// The returned bytes are a standard RIFF WAV and can be written to a .wav file.
    /// </summary>
    public static byte[]? ExtractWav(byte[] source, VlsFile file)
    {
        if (!file.HasAudio) return null;
        var wav = new byte[file.WavLength];
        Array.Copy(source, file.WavOffset, wav, 0, file.WavLength);
        return wav;
    }
}
