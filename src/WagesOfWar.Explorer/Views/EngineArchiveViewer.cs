using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WagesOfWar.Explorer.Formats;

namespace WagesOfWar.Explorer.Views;

public sealed class EngineArchiveViewer : UserControl
{
    private readonly string _path;
    private byte[] _file = Array.Empty<byte>();

    private const int ThumbSize = 72;

    public EngineArchiveViewer(string path)
    {
        _path = path;
        try
        {
            _file = File.ReadAllBytes(path);
            var (hdr, entries) = EngineArchiveReader.Read(path);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                ItemsSource = entries.Select(e => new Row(e)).ToList(),
                HeadersVisibility = DataGridHeadersVisibility.Column,
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "#",          Binding = new System.Windows.Data.Binding(nameof(Row.Index)),        Width = 60 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Abs Offset", Binding = new System.Windows.Data.Binding(nameof(Row.AbsOffsetHex)), Width = 130 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Rel Offset", Binding = new System.Windows.Data.Binding(nameof(Row.RelOffsetHex)), Width = 110 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Size",       Binding = new System.Windows.Data.Binding(nameof(Row.SizeText)),     Width = 110 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Index bytes",Binding = new System.Windows.Data.Binding(nameof(Row.Hex)),          Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

            // ── Thumbnail gallery ─────────────────────────────────────────────
            var palette = PaletteLoader.TryFindNearbyPalette(path) ?? SpriteViewer.DefaultPalette;
            var thumbHighlights = new Dictionary<int, Border>(); // index → outer highlight border
            var thumbBgBorders  = new List<Border>();             // all inner bg borders
            var thumbWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };

            foreach (var e in entries)
            {
                var (thumbContainer, highlight, bg) = MakeThumb(e, _file, palette);
                thumbHighlights[e.Index] = highlight;
                thumbBgBorders.Add(bg);
                thumbWrap.Children.Add(thumbContainer);
            }

            var thumbScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                Content = thumbWrap,
            };

            // Sync DataGrid selection → highlight thumbnail
            grid.SelectionChanged += (_, _) =>
            {
                foreach (var b in thumbHighlights.Values)
                    b.BorderBrush = Brushes.Transparent;
                if (grid.SelectedItem is Row r && thumbHighlights.TryGetValue(r.Index, out var sel))
                    sel.BorderBrush = Brushes.DodgerBlue;
            };

            // ── Thumbnail background toolbar ───────────────────────────────────
            var checkerBrush = SpriteViewer.MakeCheckerBrush();
            var bgToolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(4, 2, 4, 2),
            };
            bgToolbar.Children.Add(new TextBlock
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
                    GroupName         = "ThumbBg",
                    IsChecked         = isDefault,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 8, 0),
                    FontSize          = 11,
                };
                rb.Checked += (_, _) => { foreach (var b in thumbBgBorders) b.Background = brush; };
                bgToolbar.Children.Add(rb);
            }

            AddBgButton("Checker", checkerBrush,  isDefault: true);
            AddBgButton("White",   Brushes.White,  isDefault: false);
            AddBgButton("Black",   Brushes.Black,  isDefault: false);

            var galleryDock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(bgToolbar, Dock.Top);
            galleryDock.Children.Add(bgToolbar);
            galleryDock.Children.Add(thumbScroll);

            // ── Layout ────────────────────────────────────────────────────────
            var info = new TextBlock
            {
                Text = $"Engine archive  ·  {hdr.SpriteCount:N0} sprites  ·  index @ 0x{hdr.IndexOffset:X}  ·  data base @ 0x{hdr.DataBaseOffset:X}  ·  file {_file.Length:N0} B",
                Margin = new Thickness(8, 4, 8, 4),
                Foreground = Brushes.DimGray,
            };

            var split = new Grid();
            split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(grid, 0);
            var splitter = new GridSplitter { Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows };
            Grid.SetRow(splitter, 1);
            Grid.SetRow(galleryDock, 2);
            split.Children.Add(grid);
            split.Children.Add(splitter);
            split.Children.Add(galleryDock);

            var dock = new DockPanel();
            DockPanel.SetDock(info, Dock.Top);
            dock.Children.Add(info);
            dock.Children.Add(split);
            Content = dock;
        }
        catch (Exception ex)
        {
            Content = new TextBlock
            {
                Text = "Not an engine archive (or unsupported variant):\n" + ex.Message,
                Margin = new Thickness(12),
                Foreground = Brushes.DarkRed,
                TextWrapping = TextWrapping.Wrap,
            };
        }
    }

    // ── Thumbnail building ────────────────────────────────────────────────────

    /// <summary>Returns (outer StackPanel, highlight Border, background Border) for a sprite entry.</summary>
    private static (UIElement container, Border highlight, Border bg) MakeThumb(
        EngineArchiveEntry e, byte[] file, uint[] palette)
    {
        UIElement imgElement;
        try
        {
            if (e.AbsoluteOffset > 0 && e.Size > 0 && (long)e.AbsoluteOffset + e.Size <= file.Length)
            {
                var payload = new byte[e.Size];
                Buffer.BlockCopy(file, e.AbsoluteOffset, payload, 0, (int)e.Size);
                var hdr = SpriteDecoder.ParseHeader(payload);
                if (hdr.IsValid)
                {
                    var indices = SpriteDecoder.DecodePixels(payload, hdr.Width, hdr.Height);
                    var bmp = SpriteViewer.RenderToBitmap(indices, hdr.Width, hdr.Height, palette);
                    var img = new Image
                    {
                        Source  = bmp,
                        Width   = ThumbSize,
                        Height  = ThumbSize,
                        Stretch = Stretch.Uniform,
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                    imgElement = img;
                }
                else
                {
                    imgElement = PlaceholderText("?");
                }
            }
            else
            {
                imgElement = PlaceholderText("—");
            }
        }
        catch
        {
            imgElement = PlaceholderText("!");
        }

        var bgBorder = new Border { Background = SpriteViewer.MakeCheckerBrush() };
        var checkerGrid = new Grid();
        checkerGrid.Children.Add(bgBorder);
        checkerGrid.Children.Add(imgElement);

        var highlight = new Border
        {
            Width           = ThumbSize + 4,
            Height          = ThumbSize + 4,
            BorderThickness = new Thickness(2),
            BorderBrush     = Brushes.Transparent,
            Child           = checkerGrid,
        };

        var label = new TextBlock
        {
            Text                = $"#{e.Index}",
            FontSize            = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground          = Brushes.DimGray,
        };

        var stack = new StackPanel { Margin = new Thickness(3) };
        stack.Children.Add(highlight);
        stack.Children.Add(label);
        return (stack, highlight, bgBorder);
    }

    private static TextBlock PlaceholderText(string text) => new()
    {
        Text                = text,
        FontSize            = 18,
        Foreground          = Brushes.Gray,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment   = VerticalAlignment.Center,
        Width               = ThumbSize,
        Height              = ThumbSize,
        TextAlignment       = TextAlignment.Center,
    };

    private sealed class Row
    {
        public Row(EngineArchiveEntry e) { Entry = e; }
        public EngineArchiveEntry Entry { get; }
        public int Index => Entry.Index;
        public string AbsOffsetHex => $"0x{Entry.AbsoluteOffset:X8}";
        public string RelOffsetHex  => $"0x{Entry.RelativeOffset:X}";
        public string SizeText => Entry.Size == 0 ? "—" : $"{Entry.Size:N0}";
        public string Hex => string.Join(' ', Entry.Raw.Select(b => b.ToString("X2")));
    }
}
