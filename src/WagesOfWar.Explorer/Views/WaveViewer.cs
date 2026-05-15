using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WagesOfWar.Explorer.Views;

public sealed class WaveViewer : UserControl
{
    private MediaElement? _media;

    public WaveViewer(string path)
    {
        _media = new MediaElement
        {
            LoadedBehavior   = MediaState.Manual,
            UnloadedBehavior = MediaState.Stop,
            Source           = new Uri(path),
            Volume           = 1.0,
            Width            = 0,
            Height           = 0,
        };

        var fi   = new FileInfo(path);
        var info = new TextBox
        {
            Text            = $"WAV  ·  {fi.Length:N0} bytes",
            IsReadOnly      = true,
            FontFamily      = new FontFamily("Consolas"),
            FontSize        = 11,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin          = new Thickness(4, 2, 4, 2),
        };

        var play = new Button { Content = "▶ Play", Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4) };
        var stop = new Button { Content = "■ Stop", Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4) };
        play.Click += (_, _) => _media?.Play();
        stop.Click += (_, _) => _media?.Stop();

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 2) };
        bar.Children.Add(new TextBlock
        {
            Text              = "Audio:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0),
            Foreground        = Brushes.DimGray,
            FontSize          = 11,
        });
        bar.Children.Add(play);
        bar.Children.Add(stop);
        bar.Children.Add(_media);

        var root = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(info, Dock.Top);
        DockPanel.SetDock(bar,  Dock.Top);
        root.Children.Add(info);
        root.Children.Add(bar);

        try
        {
            var wav      = File.ReadAllBytes(path);
            var waveform = VlsViewer.BuildWaveformFromWav(wav);
            DockPanel.SetDock(waveform, Dock.Top);
            root.Children.Add(waveform);
        }
        catch { /* waveform is optional */ }

        Content = root;
    }
}
