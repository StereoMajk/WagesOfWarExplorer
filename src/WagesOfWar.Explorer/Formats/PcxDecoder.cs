using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WagesOfWar.Explorer.Formats;

/// <summary>
/// Minimal PCX decoder for 1/4/8-bpp images, RLE-compressed.
/// </summary>
public static class PcxDecoder
{
    public static BitmapSource Decode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 128 || bytes[0] != 0x0A)
            throw new InvalidDataException("Not a PCX file.");

        int xMin = BitConverter.ToUInt16(bytes, 4);
        int yMin = BitConverter.ToUInt16(bytes, 6);
        int xMax = BitConverter.ToUInt16(bytes, 8);
        int yMax = BitConverter.ToUInt16(bytes, 10);
        int bpp = bytes[3];
        int planes = bytes[65];
        int bytesPerLine = BitConverter.ToUInt16(bytes, 66);
        int paletteType = BitConverter.ToUInt16(bytes, 68);

        int width = xMax - xMin + 1;
        int height = yMax - yMin + 1;
        int totalLineBytes = bytesPerLine * planes;

        // RLE-decode the pixel data (between header end and trailing 769-byte palette block, if present)
        bool hasTrailingPal = bpp == 8 && planes == 1 && bytes.Length >= 128 + 769 && bytes[bytes.Length - 769] == 0x0C;
        int dataEnd = hasTrailingPal ? bytes.Length - 769 : bytes.Length;

        var raw = new byte[totalLineBytes * height];
        int dst = 0;
        int src = 128;
        while (dst < raw.Length && src < dataEnd)
        {
            byte b = bytes[src++];
            int run = 1;
            if ((b & 0xC0) == 0xC0)
            {
                run = b & 0x3F;
                if (src >= dataEnd) break;
                b = bytes[src++];
            }
            for (int i = 0; i < run && dst < raw.Length; i++)
                raw[dst++] = b;
        }

        if (bpp == 8 && planes == 1)
        {
            // 256-color paletted
            var palette = ExtractPalette(bytes, hasTrailingPal);
            var pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(raw, y * totalLineBytes, pixels, y * width, width);

            var bmpPal = new BitmapPalette(palette);
            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Indexed8, bmpPal, pixels, width);
        }

        if (bpp == 1 && planes == 1)
        {
            var pixels = new byte[totalLineBytes * height];
            Buffer.BlockCopy(raw, 0, pixels, 0, pixels.Length);
            var bmpPal = new BitmapPalette(new[] { Colors.Black, Colors.White });
            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Indexed1, bmpPal, pixels, totalLineBytes);
        }

        if (bpp == 8 && planes == 3)
        {
            // 24-bit RGB stored as 3 planes per scanline
            var rgb = new byte[width * 3 * height];
            for (int y = 0; y < height; y++)
            {
                int rowBase = y * totalLineBytes;
                for (int x = 0; x < width; x++)
                {
                    rgb[(y * width + x) * 3 + 0] = raw[rowBase + 0 * bytesPerLine + x];
                    rgb[(y * width + x) * 3 + 1] = raw[rowBase + 1 * bytesPerLine + x];
                    rgb[(y * width + x) * 3 + 2] = raw[rowBase + 2 * bytesPerLine + x];
                }
            }
            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, rgb, width * 3);
        }

        throw new NotSupportedException($"Unsupported PCX layout: bpp={bpp}, planes={planes}");
    }

    private static Color[] ExtractPalette(byte[] bytes, bool hasTrailing)
    {
        var pal = new Color[256];
        if (hasTrailing)
        {
            int p = bytes.Length - 768;
            for (int i = 0; i < 256; i++)
                pal[i] = Color.FromRgb(bytes[p + i * 3], bytes[p + i * 3 + 1], bytes[p + i * 3 + 2]);
        }
        else
        {
            // EGA palette block (16 entries) at offset 16 of the header; fall back to grayscale beyond.
            for (int i = 0; i < 16; i++)
                pal[i] = Color.FromRgb(bytes[16 + i * 3], bytes[16 + i * 3 + 1], bytes[16 + i * 3 + 2]);
            for (int i = 16; i < 256; i++)
                pal[i] = Color.FromRgb((byte)i, (byte)i, (byte)i);
        }
        return pal;
    }
}
