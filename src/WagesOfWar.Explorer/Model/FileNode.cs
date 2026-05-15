using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WagesOfWar.Explorer.Formats;

namespace WagesOfWar.Explorer.Model;

public sealed class FileNode
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public List<FileNode> Children { get; init; } = new();

    // Set for virtual archive-entry nodes (sprite entries inside an .OBJ/.DAT file).
    public string? ArchivePath { get; init; }
    public int ArchiveEntryIndex { get; init; } = -1;
    public bool IsArchiveEntry => ArchivePath is not null;
    /// <summary>True for archive files whose sprite entries have been expanded as children.</summary>
    public bool IsArchiveContainer => !IsDirectory && !IsArchiveEntry && Children.Count > 0;

    public string Icon =>
        IsDirectory       ? "📁" :
        IsArchiveEntry    ? "🔷" :
        IsArchiveContainer? "📦" : "📄";

    public string SizeText => IsDirectory
        ? (Children.Count > 0 ? $"({Children.Count})" : "")
        : FormatSize(Size);

    private static string FormatSize(long b) =>
        b < 1024 ? $"{b} B"
      : b < 1024 * 1024 ? $"{b / 1024.0:0.#} KB"
      : $"{b / 1024.0 / 1024.0:0.##} MB";

    public static FileNode BuildFromDirectory(string path)
    {
        var di = new DirectoryInfo(path);
        var node = new FileNode
        {
            Name = di.Name,
            FullPath = di.FullName,
            IsDirectory = true,
        };
        foreach (var sub in di.EnumerateDirectories().OrderBy(d => d.Name))
            node.Children.Add(BuildFromDirectory(sub.FullName));
        foreach (var f in di.EnumerateFiles().OrderBy(f => f.Name))
            node.Children.Add(CreateFileNode(f));
        return node;
    }

    public static FileNode BuildByType(string path)
    {
        var root = new FileNode
        {
            Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            FullPath = path,
            IsDirectory = true,
        };

        var allFiles = GatherFiles(path);
        var groups = allFiles
            .GroupBy(f => { try { return FormatDetector.Detect(f, out _); } catch { return DetectedFormat.Unknown; } })
            .OrderBy(g => FormatGroupOrder(g.Key));

        foreach (var g in groups)
        {
            var groupNode = new FileNode
            {
                Name = FormatGroupLabel(g.Key),
                FullPath = string.Empty,
                IsDirectory = true,
            };
            foreach (var f in g.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var fi = new FileInfo(f);
                groupNode.Children.Add(CreateFileNode(fi));
            }
            root.Children.Add(groupNode);
        }
        return root;
    }

    /// <summary>
    /// Creates a FileNode for a file, expanding sprite archive entries as children when applicable.
    /// </summary>
    private static FileNode CreateFileNode(FileInfo fi)
    {
        var node = new FileNode
        {
            Name = fi.Name,
            FullPath = fi.FullName,
            IsDirectory = false,
            Size = fi.Length,
        };

        // Expand engine archives: add one child node per sprite entry.
        if (EngineArchiveReader.Probe(fi.FullName))
        {
            try
            {
                var (hdr, entries) = EngineArchiveReader.Read(fi.FullName);
                foreach (var e in entries)
                    node.Children.Add(MakeArchiveEntryNode(fi.FullName, e));
            }
            catch { /* leave unexpanded if the file is malformed */ }
        }

        return node;
    }

    private static FileNode MakeArchiveEntryNode(string archivePath, EngineArchiveEntry e) =>
        new()
        {
            Name = $"Sprite {e.Index:D3}",
            FullPath = archivePath,
            IsDirectory = false,
            Size = e.Size,
            ArchivePath = archivePath,
            ArchiveEntryIndex = e.Index,
        };

    private static IEnumerable<string> GatherFiles(string path)
    {
        foreach (var f in Directory.EnumerateFiles(path))
            yield return f;
        foreach (var d in Directory.EnumerateDirectories(path))
            foreach (var f in GatherFiles(d))
                yield return f;
    }

    private static int FormatGroupOrder(DetectedFormat fmt) => fmt switch
    {
        DetectedFormat.Pcx           => 0,
        DetectedFormat.RiffWave      => 1,
        DetectedFormat.Midi          => 2,
        DetectedFormat.EngineArchive => 3,
        DetectedFormat.Vals          => 4,
        DetectedFormat.Pe            => 5,
        DetectedFormat.MissionScript => 6,
        DetectedFormat.WeaponShopData    => 7,
        DetectedFormat.AiNodeData        => 8,
        DetectedFormat.MissionSetupData  => 9,
        DetectedFormat.SpeechScript      => 10,
        DetectedFormat.Text          => 11,
        DetectedFormat.SpriteCorrelation => 12,
        DetectedFormat.ButtonLayout  => 13,
        DetectedFormat.WindowsWrite  => 14,
        DetectedFormat.WindowsIcon   => 15,
        DetectedFormat.WindowsCursor => 16,
        DetectedFormat.BitmapFont    => 17,
        DetectedFormat.OleDocument   => 18,
        DetectedFormat.Empty         => 20,
        _                            => 19,
    };

    private static string FormatGroupLabel(DetectedFormat fmt) => fmt switch
    {
        DetectedFormat.Pcx           => "PCX Images",
        DetectedFormat.RiffWave      => "WAVE Audio",
        DetectedFormat.Midi          => "MIDI Music",
        DetectedFormat.EngineArchive => "Engine Archives (.obj/.spr/containers)",
        DetectedFormat.Vals          => "VALS Containers (.VLS/.VLA)",
        DetectedFormat.Pe            => "PE Binaries (.EXE/.DLL)",
        DetectedFormat.MissionScript => "Mission Dialogue Scripts (.val/.txt)",
        DetectedFormat.WeaponShopData    => "Weapon Shop Inventories (.dat)",
        DetectedFormat.AiNodeData        => "AI Waypoint Lists (.dat)",
        DetectedFormat.MissionSetupData  => "Mission Setup Files (.dat)",
        DetectedFormat.SpeechScript      => "Speech Scripts (.dat)",
        DetectedFormat.Text          => "Text Files",
        DetectedFormat.SpriteCorrelation => "Sprite Animation Tables (.cor)",
        DetectedFormat.ButtonLayout  => "UI Button Layouts (.btn)",
        DetectedFormat.WindowsWrite  => "MS Write Documents (.WRI)",
        DetectedFormat.WindowsIcon   => "Icons (.ICO)",
        DetectedFormat.WindowsCursor => "Cursors (.CUR)",
        DetectedFormat.BitmapFont    => "Bitmap Fonts (.CHR)",
        DetectedFormat.OleDocument   => "OLE Documents (.DOC)",
        DetectedFormat.Empty         => "Empty Files",
        _                            => "Unknown / Binary",
    };

    public FileNode? Filter(string substr)
    {
        if (string.IsNullOrEmpty(substr)) return this;
        var s = substr.Trim();

        // Leaf node (regular file or archive entry) — match by name only.
        if (!IsDirectory && Children.Count == 0)
            return Name.Contains(s, StringComparison.OrdinalIgnoreCase) ? this : null;

        // Directory or archive container — filter children recursively.
        var kept = Children
            .Select(c => c.Filter(s))
            .Where(c => c is not null)
            .Cast<FileNode>()
            .ToList();

        if (kept.Count == 0 && !Name.Contains(s, StringComparison.OrdinalIgnoreCase))
            return null;

        return new FileNode
        {
            Name = Name,
            FullPath = FullPath,
            IsDirectory = IsDirectory,
            Size = Size,
            ArchivePath = ArchivePath,
            ArchiveEntryIndex = ArchiveEntryIndex,
            Children = kept,
        };
    }
}
