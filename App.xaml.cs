namespace AudioQualityEnhancer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = WriteCrashLog(e.Exception);
        System.Windows.MessageBox.Show(
            $"Die Anwendung konnte nicht korrekt gestartet werden.{Environment.NewLine}{Environment.NewLine}Details wurden gespeichert unter:{Environment.NewLine}{logPath}",
            "Audio Quality Enhancer",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);

        e.Handled = true;
        Current.Shutdown(-1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog(exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        e.SetObserved();
    }

    private static string WriteCrashLog(Exception exception)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioQualityEnhancer",
            "Logs");

        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        File.WriteAllText(logPath, exception.ToString());
        return logPath;
    }
}
