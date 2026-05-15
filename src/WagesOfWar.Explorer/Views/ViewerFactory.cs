using System;
using System.IO;
using System.Windows.Controls;
using WagesOfWar.Explorer.Formats;
using WagesOfWar.Explorer.Views;

namespace WagesOfWar.Explorer.Views;

public static class ViewerFactory
{
    public static (UserControl viewer, string label) Create(string path)
    {
        var fmt = FormatDetector.Detect(path, out _);
        var label = $"detected: {fmt}  ·  {Path.GetExtension(path).ToLowerInvariant()}";

        UserControl viewer = fmt switch
        {
            DetectedFormat.Text              => new TextViewer(path),
            DetectedFormat.MissionScript     => new TextViewer(path),
            DetectedFormat.SpriteCorrelation => new TextViewer(path),
            DetectedFormat.ButtonLayout      => new TextViewer(path),
            DetectedFormat.WeaponShopData    => new TextViewer(path),
            DetectedFormat.AiNodeData        => new TextViewer(path),
            DetectedFormat.MissionSetupData  => new TextViewer(path),
            DetectedFormat.SpeechScript      => new TextViewer(path),
            DetectedFormat.Pcx => new PcxViewer(path),
            DetectedFormat.RiffWave => new WaveViewer(path),
            DetectedFormat.Midi => new HexViewer(path, "MIDI (header preview)"),
            DetectedFormat.Vals => new VlsViewer(path),
            DetectedFormat.EngineArchive => new EngineArchiveViewer(path),
            DetectedFormat.Pe => new HexViewer(path, "Win32 PE binary"),
            DetectedFormat.Empty => new TextViewer.EmptyControl(),
            DetectedFormat.WindowsIcon => new WpfImageViewer(path),
            DetectedFormat.WindowsCursor => new WpfImageViewer(path),
            DetectedFormat.WindowsWrite  => new WriViewer(path),
            DetectedFormat.OleDocument => new HexViewer(path, "OLE Compound Document — Word 6/95 or similar"),
            DetectedFormat.BitmapFont => new BitmapFontViewer(path),
            _ => new HexViewer(path, "Unknown — hex view"),
        };
        return (viewer, label);
    }

    /// <summary>
    /// Creates a viewer for a single sprite entry extracted from an engine archive.
    /// </summary>
    public static (UserControl viewer, string label) CreateForArchiveEntry(string archivePath, int entryIndex)
    {
        var payload = EngineArchiveReader.ReadEntryPayload(archivePath, entryIndex);
        var label = $"sprite entry #{entryIndex}  ·  {payload.Length:N0} B";

        var palette = PaletteLoader.TryFindNearbyPalette(archivePath);
        var sv = TryMakeSpriteViewer(payload, palette);
        UserControl viewer = sv is not null ? (UserControl)sv : new HexViewer(payload, label);
        return (viewer, label);
    }

    private static SpriteViewer? TryMakeSpriteViewer(byte[] payload, uint[]? palette)
    {
        try
        {
            var hdr = SpriteDecoder.ParseHeader(payload);
            return hdr.IsValid ? new SpriteViewer(payload, palette) : null;
        }
        catch { return null; }
    }
}
