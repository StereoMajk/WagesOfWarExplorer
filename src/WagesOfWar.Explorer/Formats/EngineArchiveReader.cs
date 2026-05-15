using System;
using System.Collections.Generic;
using System.IO;

namespace WagesOfWar.Explorer.Formats;

/// <summary>
/// The 32-byte header found at offset 0 in every engine sprite archive (.OBJ / .DAT).
///
/// File layout (confirmed by reverse engineering wow.exe Archive_Open/Archive_ReadIndex):
///   bytes  0- 3: SpriteCount      — total number of sprites
///   bytes  4- 7: IndexOffset      — absolute file offset of the index table (seek SEEK_SET)
///   bytes  8-11: IndexSize        — total byte count of the index table (= SpriteCount × 8)
///   bytes 12-15: DataBaseOffset   — added to each entry's RelativeOffset for the actual seek
///   bytes 16-31: reserved / unknown
///
/// Note: the game's File_Seek wrapper maps origin=1 → _llseek mode 0 (SEEK_SET),
/// so the IndexOffset field is an absolute position, not a relative one.
/// </summary>
public sealed record SpriteArchiveHeader(
    int SpriteCount,
    int IndexOffset,
    int IndexSize,
    int DataBaseOffset);

/// <summary>One 8-byte record from the archive's index table.</summary>
public sealed record EngineArchiveEntry(
    int Index,
    int RelativeOffset,   // as stored in the 8-byte index record
    int AbsoluteOffset,   // DataBaseOffset + RelativeOffset — actual file seek position
    int Size,
    byte[] Raw);          // the raw 8 bytes of this index record

public static class EngineArchiveReader
{
    public const int FileHeaderSize = 32;
    public const int IndexEntrySize = 8;

    /// <summary>
    /// Parses the header and full index table of an engine archive.
    /// Throws <see cref="InvalidDataException"/> if the file does not conform to the format.
    /// </summary>
    public static (SpriteArchiveHeader Header, IReadOnlyList<EngineArchiveEntry> Entries) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < FileHeaderSize)
            throw new InvalidDataException("File too small for an engine archive header.");

        var hdr = ParseHeader(bytes);
        ValidateHeader(hdr, bytes.Length);

        var entries = new List<EngineArchiveEntry>(hdr.SpriteCount);
        int p = hdr.IndexOffset;
        for (int i = 0; i < hdr.SpriteCount; i++, p += IndexEntrySize)
        {
            int rel = BitConverter.ToInt32(bytes, p);
            int sz  = BitConverter.ToInt32(bytes, p + 4);
            var raw = new byte[IndexEntrySize];
            Buffer.BlockCopy(bytes, p, raw, 0, IndexEntrySize);
            entries.Add(new EngineArchiveEntry(i, rel, hdr.DataBaseOffset + rel, sz, raw));
        }
        return (hdr, entries);
    }

    /// <summary>
    /// Reads only the payload bytes for one entry without loading the full archive into memory.
    /// </summary>
    public static byte[] ReadEntryPayload(string path, int entryIndex)
    {
        using var fs = File.OpenRead(path);
        Span<byte> hdrBuf = stackalloc byte[FileHeaderSize];
        fs.ReadExactly(hdrBuf);
        var hdr = ParseHeader(hdrBuf);
        ValidateHeader(hdr, fs.Length);

        if ((uint)entryIndex >= (uint)hdr.SpriteCount)
            throw new ArgumentOutOfRangeException(nameof(entryIndex));

        fs.Seek(hdr.IndexOffset + (long)entryIndex * IndexEntrySize, SeekOrigin.Begin);
        Span<byte> idxBuf = stackalloc byte[IndexEntrySize];
        fs.ReadExactly(idxBuf);
        int rel = BitConverter.ToInt32(idxBuf);
        int sz  = BitConverter.ToInt32(idxBuf[4..]);

        var payload = new byte[sz];
        fs.Seek(hdr.DataBaseOffset + rel, SeekOrigin.Begin);
        fs.ReadExactly(payload);
        return payload;
    }

    /// <summary>Returns true if the file appears to be an engine sprite archive.</summary>
    public static bool Probe(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < FileHeaderSize) return false;
            var buf = new byte[FileHeaderSize];
            using var fs = File.OpenRead(path);
            fs.ReadExactly(buf, 0, FileHeaderSize);
            ValidateHeader(ParseHeader(buf), fi.Length);
            return true;
        }
        catch { return false; }
    }

    private static SpriteArchiveHeader ParseHeader(ReadOnlySpan<byte> b) =>
        new(BitConverter.ToInt32(b),
            BitConverter.ToInt32(b[4..]),
            BitConverter.ToInt32(b[8..]),
            BitConverter.ToInt32(b[12..]));

    private static void ValidateHeader(SpriteArchiveHeader hdr, long fileLength)
    {
        if (hdr.SpriteCount is <= 0 or > 200_000)
            throw new InvalidDataException($"Implausible sprite count: {hdr.SpriteCount}.");
        if (hdr.IndexOffset < FileHeaderSize)
            throw new InvalidDataException($"Index offset 0x{hdr.IndexOffset:X} is before end of header.");
        long expectedSize = (long)hdr.SpriteCount * IndexEntrySize;
        if (hdr.IndexSize != expectedSize)
            throw new InvalidDataException(
                $"Index size {hdr.IndexSize} ≠ expected {expectedSize} (count={hdr.SpriteCount}).");
        if ((long)hdr.IndexOffset + hdr.IndexSize > fileLength)
            throw new InvalidDataException("Index table extends past end of file.");
    }
}
