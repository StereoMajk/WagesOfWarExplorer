using System.IO;
using System.Text;
using System.Windows.Controls;

namespace WagesOfWar.Explorer.Views;

public sealed class TextViewer : UserControl
{
    public TextViewer(string path)
    {
        var text = ReadAsText(path);
        Content = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = System.Windows.TextWrapping.NoWrap,
            AcceptsReturn = true,
        };
    }

    private static string ReadAsText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // Try UTF-8 with BOM, otherwise Windows-1252 (most game text is plain ASCII)
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.Latin1.GetString(bytes);
    }

    public sealed class EmptyControl : UserControl
    {
        public EmptyControl()
        {
            Content = new TextBlock
            {
                Text = "(empty file)",
                Margin = new System.Windows.Thickness(12),
                Foreground = System.Windows.Media.Brushes.Gray,
            };
        }
    }
}
