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
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void MainWindow_OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = IsSupportedFileDrop(e) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void MainWindow_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!HasFileDrop(e))
        {
            e.Handled = true;
            return;
        }

        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        await _viewModel.LoadInputFilesAsync(files);
        e.Handled = true;
    }

    private bool IsSupportedFileDrop(System.Windows.DragEventArgs e)
    {
        return GetDroppedFiles(e).Any(path => File.Exists(path) && _fileNameService.IsSupportedInputFile(path));
    }

    private static bool HasFileDrop(System.Windows.DragEventArgs e)
    {
        return GetDroppedFiles(e).Any(File.Exists);
    }

    private static IReadOnlyList<string> GetDroppedFiles(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return Array.Empty<string>();
        }

        return (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PersistSettings();
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
