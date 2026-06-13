using System.Windows;
using AudioQualityEnhancer.Services;
using AudioQualityEnhancer.ViewModels;

namespace AudioQualityEnhancer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly FileNameService _fileNameService = new();

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        RestoreWindowLayout();
    }

    private void RestoreWindowLayout()
    {
        var width = _viewModel.WindowWidth;
        var height = _viewModel.WindowHeight;
        if (width >= MinWidth && width <= SystemParameters.VirtualScreenWidth &&
            height >= MinHeight && height <= SystemParameters.VirtualScreenHeight)
        {
            Width = width;
            Height = height;
        }

        if (_viewModel.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void StoreWindowLayout()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _viewModel.WindowWidth = bounds.Width;
        _viewModel.WindowHeight = bounds.Height;
        _viewModel.WindowMaximized = WindowState == WindowState.Maximized;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void MainWindow_OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = IsSupportedFileDrop(e) || TryGetDroppedUrl(e, out _)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void MainWindow_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (HasFileDrop(e))
        {
            var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (files is { Length: > 0 })
            {
                await _viewModel.LoadInputFilesAsync(_fileNameService.ExpandInputPaths(files));
            }

            e.Handled = true;
            return;
        }

        if (TryGetDroppedUrl(e, out var url))
        {
            _viewModel.YouTubeUrl = url;
        }

        e.Handled = true;
    }

    private bool IsSupportedFileDrop(System.Windows.DragEventArgs e)
    {
        // Folders are accepted without scanning them here; DragEnter must stay cheap.
        // Their contents are expanded on drop and an empty result is reported as status.
        return GetDroppedFiles(e).Any(path =>
            Directory.Exists(path) ||
            (File.Exists(path) && _fileNameService.IsSupportedInputFile(path)));
    }

    private static bool HasFileDrop(System.Windows.DragEventArgs e)
    {
        return GetDroppedFiles(e).Any(path => File.Exists(path) || Directory.Exists(path));
    }

    private static IReadOnlyList<string> GetDroppedFiles(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return Array.Empty<string>();
        }

        return e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[]
               ?? Array.Empty<string>();
    }

    private static bool TryGetDroppedUrl(System.Windows.DragEventArgs e, out string url)
    {
        url = string.Empty;

        string? text = null;
        if (e.Data.GetDataPresent(System.Windows.DataFormats.UnicodeText))
        {
            text = e.Data.GetData(System.Windows.DataFormats.UnicodeText) as string;
        }
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.Text))
        {
            text = e.Data.GetData(System.Windows.DataFormats.Text) as string;
        }

        var extracted = YtDlpDownloadService.ExtractFirstUrl(text);
        if (string.IsNullOrEmpty(extracted))
        {
            return false;
        }

        url = extracted;
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            StoreWindowLayout();
            _viewModel.PersistSettings();
        }
        finally
        {
            _viewModel.Dispose();
            base.OnClosed(e);
        }
    }
}
