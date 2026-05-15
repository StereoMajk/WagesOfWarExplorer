using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WagesOfWar.Explorer.Views;

/// <summary>
/// Viewer for the game's .WRI mission-briefing files.
///
/// Format (game-specific; shares the MS Write 3.x magic but is otherwise custom):
///   Bytes 0x00–0x01 : magic  0x31 0xBE
///   Bytes 0x0E–0x0F : uint16 LE = absolute file offset of the null terminator
///                     (i.e. end of the text content)
///   Bytes 0x00–0x7F : 128-byte header (skipped entirely)
///   Bytes 0x80–[pnFntb-1] : plain ASCII text
///       0x0D  = line break
///       0x7E  = escape: next byte is a font-index digit '0'–'9' (rare / not in practice)
///       0x00  = explicit end-of-text sentinel
/// </summary>
public sealed class WriViewer : UserControl
{
    private const int HeaderSize = 0x80;      // 128 bytes
    private const int PnFntbOffset = 0x0E;   // uint16 LE: file offset of text end

    public WriViewer(string path)
    {
        var root = new DockPanel { LastChildFill = true, Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)) };

        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < HeaderSize)
                throw new InvalidDataException($"File too short ({data.Length} B) to contain the 128-byte header.");

            if (data[0] != 0x31 || data[1] != 0xBE)
                throw new InvalidDataException($"Unexpected magic bytes 0x{data[0]:X2} 0x{data[1]:X2} (expected 0x31 0xBE).");

            // End-of-text position stored at header offset 0x0E as uint16 LE (absolute file offset)
            int textEndFileOffset = BitConverter.ToUInt16(data, PnFntbOffset);
            int textEnd = Math.Min(textEndFileOffset, data.Length);
            int textLen = Math.Max(0, textEnd - HeaderSize);

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
                       $"Size: {data.Length:N0} bytes   " +
                       $"Text: {textLen:N0} bytes  (offset 0x80–0x{textEnd:X})"
            };
            DockPanel.SetDock(info, Dock.Top);
            root.Children.Add(info);

            // ── Extract & decode text ────────────────────────────────────
            var richBox = new RichTextBox
            {
                IsReadOnly = true,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x28)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xB0)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 12, 16, 12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Document = { PagePadding = new Thickness(0) }
            };

            var doc = richBox.Document;
            doc.Blocks.Clear();

            if (textLen > 0)
            {
                var rawText = data[HeaderSize..textEnd];
                BuildDocument(doc, rawText);
            }
            else
            {
                doc.Blocks.Add(new Paragraph(new Run("(no text content)"))
                {
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
            }

            root.Children.Add(richBox);
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
                Text = $"Could not decode WRI file:\n{ex.Message}"
            };
            root.Children.Add(err);
        }

        Content = root;
    }

    /// <summary>
    /// Parses the raw text bytes and populates a FlowDocument.
    /// Handles CR (0x0D) as paragraph breaks and tilde-digit (0x7E 0x30–0x39)
    /// escape sequences as font-size hints.
    /// </summary>
    private static void BuildDocument(FlowDocument doc, byte[] raw)
    {
        // Font size table for ~0 through ~9 (maps game font index to WPF em size).
        // The game uses ICFONT10–30; indices 0–4 observed in practice.
        double[] fontSizes = [10, 11, 12, 14, 16, 18, 20, 22, 24, 30];

        double currentFontSize = 13;
        var para = new Paragraph { LineHeight = 1, Margin = new Thickness(0, 0, 0, 6) };
        var sb = new StringBuilder();

        void FlushRun()
        {
            if (sb.Length == 0) return;
            var run = new Run(sb.ToString()) { FontSize = currentFontSize };
            para.Inlines.Add(run);
            sb.Clear();
        }

        int i = 0;
        while (i < raw.Length)
        {
            byte b = raw[i];

            if (b == 0x00) break;                          // null terminator

            if (b == 0x0D)                                  // carriage return → paragraph break
            {
                FlushRun();
                doc.Blocks.Add(para);
                para = new Paragraph { LineHeight = 1, Margin = new Thickness(0, 0, 0, 6) };
                i++;
                // skip a trailing 0x0A if present
                if (i < raw.Length && raw[i] == 0x0A) i++;
                continue;
            }

            if (b == 0x7E && i + 1 < raw.Length)           // tilde escape: ~N = font index N
            {
                byte next = raw[i + 1];
                if (next >= 0x30 && next <= 0x39)
                {
                    FlushRun();
                    int fontIdx = next - 0x30;
                    currentFontSize = fontIdx < fontSizes.Length ? fontSizes[fontIdx] : 13;
                    i += 2;
                    continue;
                }
            }

            sb.Append((char)b);
            i++;
        }

        // Flush any remaining text as the last paragraph
        FlushRun();
        if (para.Inlines.Count > 0)
            doc.Blocks.Add(para);
    }
}
