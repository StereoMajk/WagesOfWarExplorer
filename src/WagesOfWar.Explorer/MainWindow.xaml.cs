using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WagesOfWar.Explorer.Model;
using WagesOfWar.Explorer.Views;

namespace WagesOfWar.Explorer;

public partial class MainWindow : Window
{
    private string? _rootPath;
    private FileNode? _dirRoot;
    private FileNode? _typeRoot;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => TryLoadDefaultRoot();
    }

    private async void TryLoadDefaultRoot()
    {
        // Walk upward looking for an "extracted" sibling of the workspace root.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "extracted");
            if (Directory.Exists(candidate))
            {
                await LoadRoot(candidate);
                return;
            }
            dir = Path.GetDirectoryName(dir);
        }
        StatusText.Text = "Open a folder to begin (File ▸ Open Folder…)";
    }

    private async Task LoadRoot(string path)
    {
        _rootPath = path;
        RootText.Text = path;
        StatusText.Text = "Parsing archives…";
        LoadingText.Text = $"Parsing {Path.GetFileName(path)}…";
        LoadingOverlay.Visibility = Visibility.Visible;
        FileTree.IsEnabled = false;
        FilterBox.IsEnabled = false;

        FileNode dirRoot, typeRoot;
        try
        {
            (dirRoot, typeRoot) = await Task.Run(() =>
                (FileNode.BuildFromDirectory(path), FileNode.BuildByType(path)));
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            FileTree.IsEnabled = true;
            FilterBox.IsEnabled = true;
        }

        _dirRoot  = dirRoot;
        _typeRoot = typeRoot;
        ApplyFilter();
        StatusText.Text = $"Loaded {path}";
    }

    private bool ByType => ByTypeRadio?.IsChecked == true;

    private void ApplyFilter()
    {
        if (FileTree is null) return;
        var root = ByType ? _typeRoot : _dirRoot;
        if (root is null) { FileTree.ItemsSource = null; return; }
        var filter = FilterBox?.Text;
        var view = string.IsNullOrWhiteSpace(filter) ? root : root.Filter(filter!);
        FileTree.ItemsSource = view is null ? Array.Empty<FileNode>() : new[] { view };
        if (view is FileNode fn)
            ExpandTopLevel(fn);
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ExpandTopLevel(FileNode root)
    {
        if (FileTree.ItemContainerGenerator.ContainerFromItem(root) is TreeViewItem tvi)
            tvi.IsExpanded = true;
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select extracted game-files folder" };
        if (dlg.ShowDialog(this) == true)
            await LoadRoot(dlg.FolderName);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_rootPath is not null) await LoadRoot(_rootPath);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FileNode node || node.IsDirectory)
        {
            ViewerHost.Content = null;
            DetectedFormatText.Text = string.Empty;
            SelectedPathText.Text = (e.NewValue as FileNode)?.FullPath ?? string.Empty;
            return;
        }

        SelectedPathText.Text = node.IsArchiveEntry
            ? $"{node.ArchivePath}  →  entry #{node.ArchiveEntryIndex}"
            : node.FullPath;
        try
        {
            var (viewer, formatLabel) = node.IsArchiveEntry
                ? ViewerFactory.CreateForArchiveEntry(node.ArchivePath!, node.ArchiveEntryIndex)
                : ViewerFactory.Create(node.FullPath);
            DetectedFormatText.Text = formatLabel;
            ViewerHost.Content = viewer;
            StatusText.Text = $"{node.Name}  ·  {node.SizeText}  ·  {formatLabel}";
        }
        catch (Exception ex)
        {
            DetectedFormatText.Text = "error";
            ViewerHost.Content = new TextBlock
            {
                Text = ex.ToString(),
                Margin = new Thickness(8),
                Foreground = System.Windows.Media.Brushes.DarkRed,
                TextWrapping = TextWrapping.Wrap
            };
        }
    }
}
