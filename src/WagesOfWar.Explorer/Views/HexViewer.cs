using System;
using System.IO;
using System.Text;
using System.Windows.Controls;

namespace WagesOfWar.Explorer.Views;

public sealed class HexViewer : UserControl
{
    private const int MaxBytes = 256 * 1024; // cap render at 256 KB

    public HexViewer(string path, string? caption = null)
    {
        var fi = new FileInfo(path);
        long total = fi.Length;
        int read = (int)Math.Min(total, MaxBytes);
        var buf = new byte[read];
        using (var fs = File.OpenRead(path))
            fs.ReadExactly(buf, 0, read);

        Content = BuildTextBox(buf, total, caption);
    }

    /// <summary>Creates a hex view directly from a byte array.</summary>
    public HexViewer(byte[] data, string? caption = null)
    {
        int read = (int)Math.Min(data.Length, MaxBytes);
        var buf = data.Length <= MaxBytes ? data : data[..MaxBytes];
        Content = BuildTextBox(buf, data.Length, caption);
    }

    private static TextBox BuildTextBox(byte[] buf, long totalSize, string? caption)
    {
        var sb = new StringBuilder();
        if (caption != null) sb.AppendLine(caption);
        sb.AppendLine($"size: {totalSize:N0} bytes" + (buf.Length < totalSize ? $"  (showing first {buf.Length:N0})" : ""));
        sb.AppendLine();
        DumpHex(sb, buf);

        return new TextBox
        {
            Text = sb.ToString(),
            IsReadOnly = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = System.Windows.TextWrapping.NoWrap,
            AcceptsReturn = true,
        };
    }

    public static void DumpHex(StringBuilder sb, byte[] data, int baseOffset = 0)
    {
        for (int i = 0; i < data.Length; i += 16)
        {
            sb.AppendFormat("{0:X8}  ", baseOffset + i);
            for (int j = 0; j < 16; j++)
            {
                if (i + j < data.Length) sb.AppendFormat("{0:X2} ", data[i + j]);
                else sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int j = 0; j < 16 && i + j < data.Length; j++)
            {
                byte b = data[i + j];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            sb.AppendLine();
        }
    }
}
