using System;
using System.IO;

namespace WagesOfWar.Explorer.Formats;

/// <summary>
/// Loads 256-colour palettes from game asset files.
/// All returned palettes are arrays of 256 BGRA32 <c>uint</c> values
/// (B in bits 0–7, G in 8–15, R in 16–23, A=0xFF in 24–31).
/// </summary>
public static class PaletteLoader
{
    /// <summary>
    /// Extracts the 256-colour palette from a standard PCX file.
    /// The trailing VGA palette block is the last 769 bytes: one <c>0x0C</c> marker
    /// followed by 768 bytes of R,G,B triplets.
    /// Returns <c>null</c> if the file is missing, unreadable, or has no valid palette block.
    /// </summary>
    public static uint[]? TryLoadFromPcx(string pcxPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(pcxPath);
            // Minimum size: 128-byte PCX header + 769-byte trailing palette
            if (bytes.Length < 128 + 769)
                return null;

            int markerOffset = bytes.Length - 769;
            if (bytes[markerOffset] != 0x0C)
                return null;

            var pal = new uint[256];
            int p = markerOffset + 1; // skip 0x0C marker
            for (int i = 0; i < 256; i++)
            {
                byte r = bytes[p + i * 3 + 0];
                byte g = bytes[p + i * 3 + 1];
                byte b = bytes[p + i * 3 + 2];
                // BGRA32: B low byte, A high byte
                pal[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            }
            return pal;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Searches for a game PCX file near <paramref name="archivePath"/> to use as the
    /// sprite palette.  At each directory level it checks both a sibling <c>PIC/</c>
    /// folder and the <c>iso/WOW/PIC/</c> sub-path (the workspace layout used when
    /// archives come from the <c>extracted/</c> tree rather than <c>iso/WOW/SPR/</c>).
    /// Prefers <c>MAINPIC.PCX</c>, then any other readable PCX.
    /// </summary>
    public static uint[]? TryFindNearbyPalette(string archivePath)
    {
        try
        {
            var parent = Path.GetDirectoryName(archivePath);
            for (int depth = 0; depth < 8 && parent is not null; depth++)
            {
                // Case 1: sibling PIC/ folder  (e.g. …/WOW/SPR/ → …/WOW/PIC/)
                var pal = TryPicDir(Path.Combine(parent, "PIC"));
                if (pal is not null) return pal;

                // Case 2: iso/WOW/PIC/ relative to the current ancestor
                // (covers archives under extracted/ when the workspace root is above)
                pal = TryPicDir(Path.Combine(parent, "iso", "WOW", "PIC"));
                if (pal is not null) return pal;

                var up = Path.GetDirectoryName(parent);
                if (up is null || up == parent) break;
                parent = up;
            }
        }
        catch { }

        return null;
    }

    private static uint[]? TryPicDir(string picDir)
    {
        if (!Directory.Exists(picDir))
            return null;

        // Prefer MAINPIC.PCX as the "main game" palette
        var mainPic = Path.Combine(picDir, "MAINPIC.PCX");
        if (File.Exists(mainPic))
        {
            var pal = TryLoadFromPcx(mainPic);
            if (pal is not null) return pal;
        }

        // Fall back to the first valid PCX in the directory
        foreach (var pcx in Directory.EnumerateFiles(picDir, "*.PCX"))
        {
            var pal = TryLoadFromPcx(pcx);
            if (pal is not null) return pal;
        }

        return null;
    }
}
