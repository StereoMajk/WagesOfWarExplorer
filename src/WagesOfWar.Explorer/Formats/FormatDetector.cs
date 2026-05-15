using System;
using System.IO;

namespace WagesOfWar.Explorer.Formats;

public enum DetectedFormat
{
    Unknown,
    Text,
    Pcx,
    RiffWave,
    Midi,
    Vals,          // .VLS / .VLA  — VALS lipsync container
    EngineArchive, // .obj/.spr/MISC.DAT/OFFCSPR.DAT/wages.wav style container
    Pe,            // EXE/DLL/DRV
    Empty,
    WindowsIcon,   // .ICO  — ICONDIR type 1
    WindowsCursor, // .CUR / .~CU  — ICONDIR type 2
    WindowsWrite,  // .WRI  — Microsoft Write 3.x  (magic 31 BE)
    OleDocument,   // .DOC / .XLS etc. — OLE Compound Document
    BitmapFont,    // .CHR  — custom 8-bpp bitmap font (offset table + row bitmaps)
    MissionScript, // .VAL / .TXT beginning with /*  — mission dialogue/voice scripts
    SpriteCorrelation, // .COR  — sprite animation correlation tables
    ButtonLayout,  // .BTN  — UI button coordinate layout data
    WeaponShopData,  // .DAT containing "STOCK:" — per-mission shop inventories
    AiNodeData,      // .DAT starting with "# AI NODE LIST" — AI waypoint lists
    MissionSetupData, // .DAT starting with "Enemies:" or "Animation Files:" — mission setup
    SpeechScript,    // .DAT starting with "Speech For Mission" — in-mission speech cues
}

public static class FormatDetector
{
    public static DetectedFormat Detect(string path, out byte[] head)
    {
        head = Array.Empty<byte>();
        var fi = new FileInfo(path);
        if (!fi.Exists) return DetectedFormat.Unknown;
        if (fi.Length == 0) return DetectedFormat.Empty;

        var len = (int)Math.Min(fi.Length, 64);
        head = new byte[len];
        using (var fs = File.OpenRead(path))
            fs.ReadExactly(head, 0, len);

        // PE
        if (len >= 2 && head[0] == 'M' && head[1] == 'Z') return DetectedFormat.Pe;
        // RIFF WAVE
        if (len >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' &&
            head[8] == 'W' && head[9] == 'A' && head[10] == 'V' && head[11] == 'E')
            return DetectedFormat.RiffWave;
        // MIDI
        if (len >= 4 && head[0] == 'M' && head[1] == 'T' && head[2] == 'h' && head[3] == 'd')
            return DetectedFormat.Midi;
        // VALS (.VLS/.VLA)
        if (len >= 4 && head[0] == 'V' && head[1] == 'A' && head[2] == 'L' && head[3] == 'S')
            return DetectedFormat.Vals;
        // PCX: first byte 0x0A (manufacturer), version 0..5, encoding 1, bpp in {1,2,4,8}
        if (len >= 4 && head[0] == 0x0A && head[1] <= 5 && head[2] == 1 &&
            (head[3] == 1 || head[3] == 2 || head[3] == 4 || head[3] == 8))
            return DetectedFormat.Pcx;
        // Windows ICO: ICONDIR reserved=0, type=1
        if (len >= 4 && head[0] == 0 && head[1] == 0 && head[2] == 1 && head[3] == 0)
            return DetectedFormat.WindowsIcon;
        // Windows CUR: ICONDIR reserved=0, type=2  (also backup .~CU files)
        if (len >= 4 && head[0] == 0 && head[1] == 0 && head[2] == 2 && head[3] == 0)
            return DetectedFormat.WindowsCursor;
        // Microsoft Write 3.x
        if (len >= 2 && head[0] == 0x31 && head[1] == 0xBE)
            return DetectedFormat.WindowsWrite;
        // OLE Compound Document (Word 6/95, Excel, etc.)
        if (len >= 8 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0 &&
            head[4] == 0xA1 && head[5] == 0xB1 && head[6] == 0x1A && head[7] == 0xE1)
            return DetectedFormat.OleDocument;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        // Custom bitmap font (no reliable magic — identified by extension)
        if (ext == ".chr") return DetectedFormat.BitmapFont;
        // Mission dialogue scripts: .val, or .txt beginning with /*
        if (ext == ".val" || (ext == ".txt" && len >= 2 && head[0] == '/' && head[1] == '*'))
            return DetectedFormat.MissionScript;
        // Sprite animation correlation tables
        if (ext == ".cor") return DetectedFormat.SpriteCorrelation;
        // UI button coordinate layouts
        if (ext == ".btn") return DetectedFormat.ButtonLayout;
        if (ext is ".txt" or ".ini" or ".inf" or ".bat" or ".reg" or ".id" or ".tit" or ".cnt")
            return DetectedFormat.Text;

        // DAT file sub-categories (content-based)
        if (ext == ".dat")
        {
            if (HeadContains(head, "STOCK:"))                         return DetectedFormat.WeaponShopData;
            if (HeadStartsWith(head, "# AI NODE LIST"))               return DetectedFormat.AiNodeData;
            if (HeadStartsWith(head, "Enemies:") ||
                HeadStartsWith(head, "Animation Files:"))             return DetectedFormat.MissionSetupData;
            if (HeadStartsWith(head, "Speech For Mission"))           return DetectedFormat.SpeechScript;
        }

        // Engine sprite archive: 32-byte header {count, index_offset, index_size, data_base_offset, ...}
        // followed by index table (count × 8 bytes) then payload data.
        if (EngineArchiveReader.Probe(path))
            return DetectedFormat.EngineArchive;

        // Probe printable text in first chunk
        if (LooksLikeText(head)) return DetectedFormat.Text;

        return DetectedFormat.Unknown;
    }

    private static bool HeadStartsWith(byte[] head, string text)
    {
        if (head.Length < text.Length) return false;
        for (int i = 0; i < text.Length; i++)
            if (head[i] != (byte)text[i]) return false;
        return true;
    }

    private static bool HeadContains(byte[] head, string needle)
    {
        int n = needle.Length;
        for (int i = 0; i <= head.Length - n; i++)
        {
            bool ok = true;
            for (int j = 0; j < n; j++)
                if (head[i + j] != (byte)needle[j]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    private static bool LooksLikeText(byte[] buf)
    {
        if (buf.Length == 0) return false;
        int printable = 0;
        foreach (var b in buf)
        {
            if (b == 0) return false;
            if (b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127)) printable++;
        }
        return printable >= buf.Length * 0.95;
    }
}
