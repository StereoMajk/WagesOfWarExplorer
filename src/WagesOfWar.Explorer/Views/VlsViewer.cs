using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WagesOfWar.Explorer.Formats;

namespace WagesOfWar.Explorer.Views;

/// <summary>
/// Viewer for VALS containers (.VLS voice-with-lipsync files and .VLA lipsync-only files).
///
/// Layout:
///   [info bar]           – entry count, distinct mouth shapes, audio duration
///   [audio controls]     – Play / Stop buttons (only when embedded WAV is present)
///   [waveform]           – simple amplitude overview (only when audio present)
///   [lipsync table]      – scrollable DataGrid: # / Mouth / Viseme / PCM offset / Time (ms)
/// </summary>
public sealed class VlsViewer : UserControl
{
    private readonly byte[]   _source;
    private readonly VlsFile  _file = new();
    private          string?  _tempWavPath;
    private          MediaElement? _media;

    // ── Construction ──────────────────────────────────────────────────────────

    public VlsViewer(string path)
        : this(File.ReadAllBytes(path)) { }

    public VlsViewer(byte[] data)
    {
        _source = data;
        try
        {
            _file = VlsDecoder.Parse(data);
        }
        catch (Exception ex)
        {
            Content = MakeError(ex.Message);
            return;
        }
        Content = BuildRoot();
    }

    // ── Root layout ───────────────────────────────────────────────────────────

    private UIElement BuildRoot()
    {
        var root = new DockPanel { LastChildFill = true };

        // Info bar
        var info = BuildInfoBar();
        DockPanel.SetDock(info, Dock.Top);
        root.Children.Add(info);

        // Audio controls + waveform (only if WAV present)
        if (_file.HasAudio)
        {
            var audioBar = BuildAudioBar();
            DockPanel.SetDock(audioBar, Dock.Top);
            root.Children.Add(audioBar);

            var waveform = BuildWaveform();
            DockPanel.SetDock(waveform, Dock.Top);
            root.Children.Add(waveform);
        }

        // Lipsync table fills the rest
        root.Children.Add(BuildLipSyncTable());
        return root;
    }

    // ── Info bar ──────────────────────────────────────────────────────────────

    private UIElement BuildInfoBar()
    {
        var realEntries = _file.LipSyncEntries.Where(e => e.MouthShape >= 0).ToArray();
        var shapes      = realEntries.Select(e => e.MouthShape).Distinct().OrderBy(x => x).ToArray();

        var parts = new List<string>
        {
            _file.HasAudio ? "VALS  ·  VLS (lipsync + audio)" : "VALS  ·  VLA (lipsync only)",
            $"{_file.LipSyncEntries.Length} keyframes",
        };
        if (shapes.Length > 0)
            parts.Add($"{shapes.Length} mouth shapes used: {string.Join(", ", shapes)}");
        if (_file.HasAudio)
            parts.Add($"audio {_file.WavLength:N0} B  ≈ {_file.DurationSeconds:F1} s @ 22050 Hz 8-bit mono");

        return new TextBox
        {
            Text            = string.Join("  ·  ", parts),
            IsReadOnly      = true,
            FontFamily      = new FontFamily("Consolas"),
            FontSize        = 11,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin          = new Thickness(4, 2, 4, 2),
        };
    }

    // ── Audio bar ─────────────────────────────────────────────────────────────

    private UIElement BuildAudioBar()
    {
        _media = new MediaElement
        {
            LoadedBehavior   = MediaState.Manual,
            UnloadedBehavior = MediaState.Stop,
            Width  = 0,
            Height = 0,
        };

        var play  = MakeButton("▶ Play");
        var stop  = MakeButton("■ Stop");

        play.Click += (_, _) =>
        {
            try
            {
                EnsureTempWav();
                _media!.Source = new Uri(_tempWavPath!);
                _media.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Playback error: {ex.Message}", "VLS Viewer",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        stop.Click += (_, _) => _media?.Stop();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(4, 0, 4, 2),
        };
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
        return bar;
    }

    private static Button MakeButton(string text) =>
        new() { Content = text, Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4) };

    // ── Waveform ──────────────────────────────────────────────────────────────

    private UIElement BuildWaveform()
    {
        var wav   = VlsDecoder.ExtractWav(_source, _file)!;
        var ticks = _file.LipSyncEntries
            .Where(e => e.MouthShape >= 0)
            .Select(e => (double)e.PcmByteOffset)
            .ToArray();
        return BuildWaveformFromWav(wav, ticks);
    }

    /// <summary>
    /// Builds a scrollable waveform strip from raw WAV bytes
    /// (standard 44-byte RIFF header + 8-bit unsigned mono PCM).
    /// Optionally draws vertical tick marks at the given PCM byte offsets.
    /// </summary>
    internal static UIElement BuildWaveformFromWav(byte[] wav, double[]? tickPcmOffsets = null)
    {
        const int DisplayWidth  = 800;
        const int DisplayHeight = 48;

        int pcmStart = 44;
        int pcmLen   = wav.Length - pcmStart;
        if (pcmLen <= 0) return new Border { Height = DisplayHeight };

        int samples = Math.Min(pcmLen, DisplayWidth * 4);
        int step    = Math.Max(1, pcmLen / samples);

        var canvas = new Canvas
        {
            Width      = DisplayWidth,
            Height     = DisplayHeight,
            Background = new SolidColorBrush(Color.FromRgb(20, 20, 30)),
        };

        double midY   = DisplayHeight / 2.0;
        double xScale = (double)DisplayWidth / (pcmLen / step);

        var pts = new PointCollection();
        for (int i = 0; i * step < pcmLen; i++)
        {
            byte sample = wav[pcmStart + i * step];
            // 8-bit unsigned PCM: 128 = silence; scale to ±(DisplayHeight/2)
            double y = midY - (sample - 128) / 128.0 * midY;
            pts.Add(new Point(i * xScale, y));
        }

        canvas.Children.Add(new System.Windows.Shapes.Polyline
        {
            Points          = pts,
            Stroke          = new SolidColorBrush(Color.FromRgb(80, 200, 120)),
            StrokeThickness = 1,
        });

        // Centre line
        canvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1              = 0,
            Y1              = midY,
            X2              = DisplayWidth,
            Y2              = midY,
            Stroke          = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            StrokeThickness = 1,
        });

        // Optional tick marks (e.g. lipsync keyframes)
        if (tickPcmOffsets is not null)
        {
            foreach (double offset in tickPcmOffsets)
            {
                double x = offset / pcmLen * DisplayWidth;
                canvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1              = x,
                    Y1              = 0,
                    X2              = x,
                    Y2              = DisplayHeight,
                    Stroke          = new SolidColorBrush(Color.FromArgb(120, 255, 200, 0)),
                    StrokeThickness = 1,
                });
            }
        }

        return new Border
        {
            BorderBrush     = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(4, 2, 4, 2),
            Child           = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                Content                       = canvas,
            },
        };
    }

    // ── Lipsync table ─────────────────────────────────────────────────────────

    private UIElement BuildLipSyncTable()
    {
        var dg = new DataGrid
        {
            IsReadOnly             = true,
            AutoGenerateColumns    = false,
            CanUserReorderColumns  = false,
            CanUserSortColumns     = true,
            FontFamily             = new FontFamily("Consolas"),
            FontSize               = 11,
            RowHeight              = 20,
            HeadersVisibility      = DataGridHeadersVisibility.Column,
            SelectionMode          = DataGridSelectionMode.Single,
            GridLinesVisibility    = DataGridGridLinesVisibility.Horizontal,
            AlternatingRowBackground = new SolidColorBrush(Color.FromArgb(12, 0, 0, 0)),
        };

        dg.Columns.Add(new DataGridTextColumn { Header = "#",          Binding = new Binding(nameof(LipRow.Index)),         Width = 55 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Mouth",      Binding = new Binding(nameof(LipRow.MouthShape)),     Width = 65 });
        dg.Columns.Add(new DataGridTextColumn { Header = "Viseme",     Binding = new Binding(nameof(LipRow.VișemeName)),     Width = 130 });
        dg.Columns.Add(new DataGridTextColumn { Header = "PCM offset", Binding = new Binding(nameof(LipRow.PcmByteOffset)), Width = 100 });
        dg.Columns.Add(new DataGridTextColumn
        {
            Header  = "Time (ms)",
            Binding = new Binding(nameof(LipRow.TimeMs)) { StringFormat = "F1" },
            Width   = new DataGridLength(1, DataGridLengthUnitType.Star),
        });

        var rows = new List<LipRow>(_file.LipSyncEntries.Length);
        for (int i = 0; i < _file.LipSyncEntries.Length; i++)
        {
            var e = _file.LipSyncEntries[i];
            rows.Add(new LipRow(i, e.MouthShape, VisemeName(e.MouthShape), e.PcmByteOffset, e.TimeMs));
        }
        dg.ItemsSource = rows;

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content                       = dg,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureTempWav()
    {
        if (_tempWavPath != null && File.Exists(_tempWavPath)) return;
        _tempWavPath = Path.Combine(Path.GetTempPath(), $"wowvals_{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(_tempWavPath, VlsDecoder.ExtractWav(_source, _file)!);
    }

    ~VlsViewer()
    {
        if (_tempWavPath != null)
            try { File.Delete(_tempWavPath); } catch { /* best-effort */ }
    }

    private static UIElement MakeError(string msg) =>
        new TextBlock { Text = $"Parse error: {msg}", Margin = new Thickness(8), Foreground = Brushes.OrangeRed };

    /// <summary>
    /// Human-readable label for a mouth-shape index.
    /// Approximate mapping based on common game viseme systems.
    /// </summary>
    private static string VisemeName(int shape) => shape switch
    {
        -1 => "(silence)",
         0 => "Rest / m b p",
         1 => "f / v",
         2 => "th",
         3 => "d t n",
         4 => "k g",
         5 => "ch sh zh",
         6 => "r",
         7 => "s z",
         8 => "AA  (father)",
         9 => "AE  (at)",
        10 => "AO  (bought)",
        11 => "AW  (boat)",
        12 => "EH  (bed)",
        13 => "IH  (bit)",
        14 => "IY  (beat)",
        15 => "OW  (boat)",
        16 => "UW  (boot)",
        17 => "ER  (bird)",
        18 => "l",
        19 => "w / uw",
        20 => "y",
        21 => "h",
        _  => $"shape {shape}",
    };

    // ── Row record ────────────────────────────────────────────────────────────

    private sealed record LipRow(
        int    Index,
        int    MouthShape,
        string VișemeName,
        uint   PcmByteOffset,
        double TimeMs);
}
