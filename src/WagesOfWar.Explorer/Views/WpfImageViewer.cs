using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WagesOfWar.Explorer.Views;

/// <summary>
/// Shows Windows ICO / CUR files by decoding every frame with WPF's built-in BitmapDecoder.
/// Each frame is displayed as a labelled image (pixel size shown below each).
/// </summary>
public sealed class WpfImageViewer : UserControl
{
    public WpfImageViewer(string path)
    {
        var root = new DockPanel { LastChildFill = true };

        try
        {
            BitmapDecoder decoder;
            using (var fs = File.OpenRead(path))
                decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            // ── Info bar ────────────────────────────────────────────────
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
                       $"Size: {new FileInfo(path).Length:N0} bytes   " +
                       $"Frames: {decoder.Frames.Count}"
            };
            DockPanel.SetDock(info, Dock.Top);
            root.Children.Add(info);

            // ── Frame display ───────────────────────────────────────────
            var wrap = new WrapPanel
            {
                Margin = new Thickness(12),
                Orientation = Orientation.Horizontal
            };

            foreach (var frame in decoder.Frames)
            {
                var img = new Image
                {
                    Source = frame,
                    Stretch = Stretch.None,
                    Margin = new Thickness(4)
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

                var lbl = new TextBlock
                {
                    Text = $"{frame.PixelWidth}×{frame.PixelHeight}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 2, 0, 6)
                };

                // Show on a checker background so transparency is visible
                var checker = MakeCheckerBrush();
                var back = new Border
                {
                    Background = checker,
                    Padding = new Thickness(4),
                    Child = img,
                    BorderBrush = Brushes.DimGray,
                    BorderThickness = new Thickness(1)
                };

                var cell = new StackPanel { Margin = new Thickness(6, 6, 6, 2) };
                cell.Children.Add(back);
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
                Text = $"Could not decode image:\n{ex.Message}"
            };
            root.Children.Add(err);
        }

        Content = root;
    }

    private static DrawingBrush MakeCheckerBrush()
    {
        var d = new DrawingGroup();
        d.Children.Add(new GeometryDrawing(Brushes.LightGray, null,
            new RectangleGeometry(new Rect(0, 0, 16, 16))));
        d.Children.Add(new GeometryDrawing(Brushes.Gray, null,
            new RectangleGeometry(new Rect(0, 0, 8, 8))));
        d.Children.Add(new GeometryDrawing(Brushes.Gray, null,
            new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(d)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute
        };
    }
}
