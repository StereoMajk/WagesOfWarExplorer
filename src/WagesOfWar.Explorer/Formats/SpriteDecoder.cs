using System;
using System.Runtime.CompilerServices;

namespace WagesOfWar.Explorer.Formats;

/// <summary>
/// Header at the start of every sprite payload inside an engine archive entry.
///
/// On-disk layout (24 bytes, followed immediately by RLE pixel data):
///   bytes  0- 1: XOrigin   — screen X position the game draws this sprite at
///   bytes  2- 3: YOrigin   — screen Y position the game draws this sprite at
///   bytes  4- 5: Width     — sprite width in pixels
///   bytes  6- 7: Height    — sprite height in pixels
///   bytes  8-11: Flags     — game-internal field (e.g. 8044 = 0x1F6C), ignore in viewer
///   bytes 12-15: DataSize  — byte count of pixel data following the header (= payload.Length - 24)
///   bytes 16-19: (runtime-patched pointer to pixel data, value in file is irrelevant)
///   bytes 20-23: (zeroed at load time, value in file is irrelevant)
/// </summary>
public readonly struct SpriteHeader
{
    public const int Size = 24;

    public readonly short XOrigin;
    public readonly short YOrigin;
    public readonly short Width;
    public readonly short Height;
    public readonly int   Flags;
    public readonly int   DataSize;

    public SpriteHeader(ReadOnlySpan<byte> src)
    {
        XOrigin = BitConverter.ToInt16(src);
        YOrigin = BitConverter.ToInt16(src[2..]);
        Width   = BitConverter.ToInt16(src[4..]);
        Height  = BitConverter.ToInt16(src[6..]);
        Flags   = BitConverter.ToInt32(src[8..]);
        DataSize = BitConverter.ToInt32(src[12..]);
    }

    public bool IsValid =>
        Width > 0 && Height > 0 &&
        Width <= 1024 && Height <= 1024 &&
        DataSize >= 0;
}

/// <summary>
/// Decodes the custom RLE pixel encoding used by Wages of War sprite payloads.
///
/// Pixel data (starting at byte offset 24 of the sprite payload) is a variable-length
/// byte stream encoding <see cref="SpriteHeader.Height"/> rows of
/// <see cref="SpriteHeader.Width"/> pixels each.  Rows are NOT fixed-width; each row
/// ends with a 0x00 terminator byte.
///
/// Per-stream encoding:
///   0x00            → end of current row (move to next)
///   0x80  N         → skip N transparent pixels (background shows through)
///   HH (bit7 set, HH ≠ 0x80)  → literal run: (HH &amp; 0x7F) pixel bytes follow verbatim
///   NN (bit7 clear, NN ≠ 0)   → RLE run: next byte repeated NN times
///
/// Transparent pixels are represented as palette index 0x00 in the decoded buffer.
/// Actual game palette index 0 is pure black, so transparent and black look the same
/// at this layer; the viewer renders them with a checkerboard overlay.
/// </summary>
public static class SpriteDecoder
{
    /// <summary>
    /// Parses the 24-byte sprite header from the start of the payload.
    /// Throws <see cref="InvalidDataException"/> if the payload is too short.
    /// </summary>
    public static SpriteHeader ParseHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SpriteHeader.Size)
            throw new System.IO.InvalidDataException(
                $"Payload is only {payload.Length} bytes; need at least {SpriteHeader.Size}.");
        return new SpriteHeader(payload);
    }

    /// <summary>
    /// Decodes RLE pixel data from <paramref name="payload"/> into a flat
    /// <c>width × height</c> byte array of 8-bit palette indices,
    /// row-major, top-to-bottom, left-to-right.
    /// Transparent (skipped) pixels are represented by palette index 0.
    /// Returns the decoded buffer; never throws on malformed data (returns partial result).
    /// </summary>
    public static byte[] DecodePixels(ReadOnlySpan<byte> payload, int width, int height)
    {
        var pixels = new byte[width * height]; // zero-initialised = transparent
        if (width <= 0 || height <= 0 || payload.Length <= SpriteHeader.Size)
            return pixels;

        ReadOnlySpan<byte> data = payload[SpriteHeader.Size..];
        int row = 0;
        int col = 0;
        int i   = 0;

        while (i < data.Length && row < height)
        {
            byte b = data[i++];

            if (b == 0x00)
            {
                // End of row: advance to next, skip any leftover column
                row++;
                col = 0;
                continue;
            }

            if (b == 0x80)
            {
                // Transparent skip: next byte = count
                if (i >= data.Length) break;
                int skip = data[i++];
                col += skip;
                continue;
            }

            if ((b & 0x80) != 0)
            {
                // Literal run: (b & 0x7F) pixel bytes follow
                int count = b & 0x7F;
                for (int k = 0; k < count && i < data.Length && row < height; k++, i++, col++)
                {
                    if (col < width)
                        pixels[row * width + col] = data[i];
                }
                continue;
            }

            // b has bit 7 clear, b != 0: RLE run — b copies of next byte
            if (i >= data.Length) break;
            byte colour = data[i++];
            for (int k = 0; k < b && col < width && row < height; k++, col++)
                pixels[row * width + col] = colour;
        }

        return pixels;
    }
}
