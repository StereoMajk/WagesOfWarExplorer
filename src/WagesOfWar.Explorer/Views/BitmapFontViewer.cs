using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WagesOfWar.Explorer.Views;

/// <summary>
/// Viewer for the game's custom .CHR bitmap font files.
///
/// Format:
///   Bytes 0–511  : 256 × uint16 (LE) glyph offsets into the file.
///                  offset[0] == 512 (= 256 × 2, the size of the table itself).
///   Glyph data   : fixed-size records; each record is (glyphHeight) bytes,
///                  one byte per row, 8 pixels wide, MSB = leftmost pixel.
///   Glyph height : derived from (offset[1] − offset[0]).  Matches the number
///                  in the filename, e.g. ICFONT10 → 10 rows.
/// </summary>
public sealed class BitmapFontViewer : UserControl
{
    private const int TableEntries = 256;   // one per ASCII char 0–255
    private const int TableBytes   = TableEntries * 2;   // 512
    private const int GlyphWidth   = 8;     // one byte per row → 8 pixels
    private const int Scale        = 3;     // zoom factor for rendering

    public BitmapFontViewer(string path)
    {
        var root = new DockPanel { LastChildFill = true };

        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < TableBytes)
                throw new InvalidDataException($"File too small ({data.Length} bytes) to contain the 512-byte glyph offset table.");

            // ── Parse offset table ───────────────────────────────────────
            var offsets = new ushort[TableEntries];
            for (int i = 0; i < TableEntries; i++)
                offsets[i] = BitConverter.ToUInt16(data, i * 2);

            if (offsets[0] != TableBytes)
                throw new InvalidDataException($"Unexpected first glyph offset {offsets[0]} (expected {TableBytes}). Not a CHR font?");

            int glyphHeight = offsets[1] - offsets[0];  // e.g. 10 for ICFONT10
            if (glyphHeight <= 0 || glyphHeight > 64)
                throw new InvalidDataException($"Implausible glyph height {glyphHeight}.");

            // ── Info bar ─────────────────────────────────────────────────
            long fileSize = data.Length;
            var info = new TextBox
            {
                IsReadOnly = true,
                Background = Brushes.Black,
                Foreground = Brushes.LimeGreen,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 2, 4, 2),
                Text = $"File: {Path.GetFileName(path)}   " +
                       $"Size: {fileSize:N0} bytes   " +
                       $"Glyph size: {GlyphWidth}×{glyphHeight} px   " +
                       $"Scale: {Scale}×   " +
                       $"Chars: printable ASCII (0x20–0x7E)"
            };
            DockPanel.SetDock(info, Dock.Top);
            root.Children.Add(info);

            // ── Glyph grid ───────────────────────────────────────────────
            int cellW = GlyphWidth  * Scale;
            int cellH = glyphHeight * Scale;

            var wrap = new WrapPanel
            {
                Margin = new Thickness(12),
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
            };

            for (int c = 0x20; c <= 0x7E; c++)
            {
                ushort glyphOff = offsets[c];
                if (glyphOff + glyphHeight > data.Length)
                    continue;

                var bmp = RenderGlyph(data, glyphOff, glyphHeight, cellW, cellH);

                var img = new Image
                {
                    Source = bmp,
                    Width = cellW,
                    Height = cellH,
                    Stretch = Stretch.None
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

                // Character label below the glyph
                string charLabel = c == 0x20 ? "SP" : ((char)c).ToString();
                var lbl = new TextBlock
                {
                    Text = charLabel,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 9,
                    Foreground = Brushes.DarkGray,
                    Margin = new Thickness(0, 1, 0, 4)
                };

                var cell = new StackPanel
                {
                    Margin = new Thickness(3),
                    ToolTip = $"0x{c:X2}  '{(char)c}'"
                };
                cell.Children.Add(img);
                cell.Children.Add(lbl);
                wrap.Children.Add(cell);
            }

            var sv = new ScrollViewer
            {
                Content = wrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            root.Children.Add(sv);
        }
        catch (Exception ex)
        {
            var err = new TextBox
            {
                IsReadOnly = true,
                Background = Brushes.Black,
                Foreground = Brushes.OrangeRed,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Text = $"Could not decode CHR font:\n{ex.Message}"
            };
            root.Children.Add(err);
        }

        Content = root;
    }

    /// <summary>
    /// Renders a single glyph as a BGRA32 WriteableBitmap scaled up by <see cref="Scale"/>.
    /// Foreground (set bits) = bright green.  Background (clear bits) = near-black.
    /// </summary>
    private static WriteableBitmap RenderGlyph(byte[] data, int glyphOffset, int glyphHeight, int bmpW, int bmpH)
    {
        int stride = bmpW * 4;
        var pixels = new byte[stride * bmpH];

        // BGRA values
        const byte FgB = 0x00, FgG = 0xE8, FgR = 0x00, FgA = 0xFF; // #00E800 green
        const byte BgB = 0x18, BgG = 0x18, BgR = 0x18, BgA = 0xFF; // #181818 dark

        for (int row = 0; row < glyphHeight; row++)
        {
            byte rowByte = data[glyphOffset + row];
            for (int col = 0; col < GlyphWidth; col++)
            {
                bool set = (rowByte & (0x80 >> col)) != 0;
                for (int sy = 0; sy < Scale; sy++)
                for (int sx = 0; sx < Scale; sx++)
                {
                    int px = (row * Scale + sy) * stride + (col * Scale + sx) * 4;
                    if (set)
                    {
                        pixels[px + 0] = FgB;
                        pixels[px + 1] = FgG;
                        pixels[px + 2] = FgR;
                        pixels[px + 3] = FgA;
                    }
                    else
                    {
                        pixels[px + 0] = BgB;
                        pixels[px + 1] = BgG;
                        pixels[px + 2] = BgR;
                        pixels[px + 3] = BgA;
                    }
                }
            }
        }

        var bmp = new WriteableBitmap(bmpW, bmpH, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, bmpW, bmpH), pixels, stride, 0);
        return bmp;
    }
}
