using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WagesOfWar.Explorer.Formats;

namespace WagesOfWar.Explorer.Views;

public sealed class PcxViewer : UserControl
{
    public PcxViewer(string path)
    {
        BitmapSource bmp;
        try
        {
            bmp = PcxDecoder.Decode(path);
        }
        catch (System.Exception ex)
        {
            Content = new TextBlock
            {
                Text = "Failed to decode PCX:\n" + ex.Message,
                Margin = new Thickness(12),
                Foreground = Brushes.DarkRed,
                TextWrapping = TextWrapping.Wrap,
            };
            return;
        }

        var img = new Image
        {
            Source = bmp,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

        var info = new TextBlock
        {
            Text = $"PCX  {bmp.PixelWidth} × {bmp.PixelHeight}  ·  {bmp.Format}  ·  {new FileInfo(path).Length:N0} B",
            Margin = new Thickness(8, 4, 8, 4),
            Foreground = Brushes.DimGray,
        };

        var dock = new DockPanel();
        DockPanel.SetDock(info, Dock.Top);
        dock.Children.Add(info);
        var scroll = new ScrollViewer
        {
            Content = img,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
        };
        dock.Children.Add(scroll);
        Content = dock;
    }
}
