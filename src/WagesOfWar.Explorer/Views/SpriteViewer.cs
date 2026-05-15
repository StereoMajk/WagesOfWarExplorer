using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WagesOfWar.Explorer.Formats;

namespace WagesOfWar.Explorer.Views;

/// <summary>
/// Displays a single sprite from an engine archive entry.
/// Decodes the custom RLE pixel data and renders it using either a supplied game palette
/// (extracted from a PCX file) or a generated fallback 256-colour cube.
/// </summary>
public sealed class SpriteViewer : UserControl
{
    // ── Palette ───────────────────────────────────────────────────────────────
    internal static readonly uint[] DefaultPalette = BuildDefaultPalette();

    private static uint[] BuildDefaultPalette()
    {
        var pal = new uint[256];

        // Entry 0 – used for "transparent / background" — pure black
        pal[0] = 0xFF000000;

        // Entries 1–15: CGA/VGA standard 16 colours
        ReadOnlySpan<uint> cga = [
            0xFF000000, 0xFF0000AA, 0xFF00AA00, 0xFF00AAAA,
            0xFFAA0000, 0xFFAA00AA, 0xFFAA5500, 0xFFAAAAAA,
            0xFF555555, 0xFF5555FF, 0xFF55FF55, 0xFF55FFFF,
            0xFFFF5555, 0xFFFF55FF, 0xFFFFFF55, 0xFFFFFFFF,
        ];
        for (int i = 0; i < 16; i++) pal[i] = cga[i];

        // Entries 16–231: 6×6×6 colour cube  (same as xterm-256)
        int idx = 16;
        int[] levels = [0x00, 0x33, 0x66, 0x99, 0xCC, 0xFF];
        for (int r = 0; r < 6; r++)
        for (int g = 0; g < 6; g++)
        for (int b = 0; b < 6; b++)
            pal[idx++] = 0xFF000000u | ((uint)levels[r] << 16) | ((uint)levels[g] << 8) | (uint)levels[b];

        // Entries 232–255: greyscale ramp (8 → 238 in steps of ~10)
        for (int i = 0; i < 24; i++)
        {
            uint v = (uint)(8 + i * 10);
            pal[idx++] = 0xFF000000u | (v << 16) | (v << 8) | v;
        }

        return pal;
    }

    // ── Construction ─────────────────────────────────────────────────────────

    /// <param name="payload">Raw sprite entry bytes.</param>
    /// <param name="palette">
    /// Optional 256-entry BGRA32 palette.  When <c>null</c> the generated
    /// default colour-cube palette is used.
    /// </param>
    public SpriteViewer(byte[] payload, uint[]? palette = null)
    {
        Content = BuildContent(payload, palette ?? DefaultPalette);
    }

    // ── UI building ──────────────────────────────────────────────────────────

    private static UIElement BuildContent(byte[] payload, uint[] palette)
    {
        if (payload.Length < SpriteHeader.Size)
            return InfoLabel($"Payload too small ({payload.Length} B) — not a valid sprite.");

        SpriteHeader hdr;
        try { hdr = SpriteDecoder.ParseHeader(payload); }
        catch (Exception ex) { return InfoLabel($"Header parse error: {ex.Message}"); }

        if (!hdr.IsValid)
            return InfoLabel(
                $"Invalid sprite dimensions: {hdr.Width} × {hdr.Height}  (flags={hdr.Flags:X})");

        // Decode pixels
        byte[] indices = SpriteDecoder.DecodePixels(payload, hdr.Width, hdr.Height);

        // Build info text
        bool usingGamePalette = !ReferenceEquals(palette, DefaultPalette);
        var info = BuildInfoText(hdr, payload.Length, usingGamePalette);

        // Render to WriteableBitmap (BGRA32)
        var bitmap = RenderToBitmap(indices, hdr.Width, hdr.Height, palette);

        // Image control – nearest-neighbour scaling so pixels stay crisp when zoomed
        var image = new Image
        {
            Source  = bitmap,
            Stretch = Stretch.None,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        // Background layer – starts as checkerboard, swapped by the toolbar buttons
        var checkerBrush = MakeCheckerBrush();
        var bgBorder = new Border { Background = checkerBrush };

        // Scroll container so very large sprites don't clip
        var grid = new Grid();
        grid.Children.Add(bgBorder);
        grid.Children.Add(image);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content = grid,
        };

        // Info panel at top
        var infoBox = new TextBox
        {
            Text            = info,
            IsReadOnly      = true,
            FontFamily      = new FontFamily("Consolas"),
            FontSize        = 11,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin          = new Thickness(4, 2, 4, 2),
        };

        // Background-colour toolbar
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(4, 0, 4, 2),
        };
        toolbar.Children.Add(new TextBlock
        {
            Text              = "Background:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0),
            Foreground        = Brushes.DimGray,
            FontSize          = 11,
        });

        void AddBgButton(string label, Brush brush, bool isDefault)
        {
            var rb = new RadioButton
            {
                Content           = label,
                GroupName         = "SprBg",
                IsChecked         = isDefault,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0),
                FontSize          = 11,
            };
            rb.Checked += (_, _) => bgBorder.Background = brush;
            toolbar.Children.Add(rb);
        }

        AddBgButton("Checker", checkerBrush,  isDefault: true);
        AddBgButton("White",   Brushes.White,  isDefault: false);
        AddBgButton("Black",   Brushes.Black,  isDefault: false);

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(infoBox,  Dock.Top);
        DockPanel.SetDock(toolbar,  Dock.Top);
        panel.Children.Add(infoBox);
        panel.Children.Add(toolbar);
        panel.Children.Add(scroll);

        return panel;
    }

    private static string BuildInfoText(SpriteHeader hdr, int payloadBytes, bool usingGamePalette)
    {
        var sb = new StringBuilder();
        sb.Append($"sprite  {hdr.Width} × {hdr.Height} px");
        sb.Append($"   origin ({hdr.XOrigin}, {hdr.YOrigin})");
        sb.Append($"   flags 0x{hdr.Flags:X}");
        sb.Append($"   data {hdr.DataSize:N0} B  (payload {payloadBytes:N0} B)");
        sb.Append(usingGamePalette
            ? "   [palette: MAINPIC.PCX]"
            : "   [palette: default colour cube (no game PCX found)]");
        return sb.ToString();
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    internal static WriteableBitmap RenderToBitmap(byte[] indices, int width, int height, uint[] palette)
    {
        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        // Build pixel buffer as BGRA32 (WriteableBitmap uses BGRA byte order)
        int stride = width * 4;
        var pixelBuf = new byte[stride * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte idx = indices[y * width + x];
            uint argb = palette[idx];
            // Alpha = 0 for transparent pixels (index 0) so checkerboard shows through
            byte alpha = idx == 0 ? (byte)0 : (byte)0xFF;
            int off = (y * stride) + (x * 4);
            pixelBuf[off + 0] = (byte)(argb & 0xFF);        // B
            pixelBuf[off + 1] = (byte)((argb >> 8) & 0xFF); // G
            pixelBuf[off + 2] = (byte)((argb >> 16) & 0xFF);// R
            pixelBuf[off + 3] = alpha;                       // A
        }
        bmp.WritePixels(new Int32Rect(0, 0, width, height), pixelBuf, stride, 0);
        return bmp;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static Brush MakeCheckerBrush()
    {
        // 16×16 checker pattern drawn as a DrawingBrush tile
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.LightGray, null,
            new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(Brushes.Gray, null,
            new RectangleGeometry(new Rect(0, 0, 8, 8))));
        drawing.Children.Add(new GeometryDrawing(Brushes.Gray, null,
            new RectangleGeometry(new Rect(8, 8, 8, 8))));

        return new DrawingBrush
        {
            Drawing       = drawing,
            TileMode      = TileMode.Tile,
            Viewport      = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };
    }

    private static TextBlock InfoLabel(string msg) =>
        new() { Text = msg, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
}
