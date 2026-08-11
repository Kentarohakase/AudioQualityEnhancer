using System.Globalization;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer;

public partial class App : System.Windows.Application
{
    public static SettingsService SettingsService { get; } = new();

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var settings = SettingsService.Load();

        ThemeService.Instance.Apply(ThemeService.Parse(settings.Theme));

        try
        {
            LocalizationService.Instance.Culture = new CultureInfo(settings.Language);
        }
        catch (CultureNotFoundException)
        {
            LocalizationService.Instance.Culture = new CultureInfo("de");
        }

        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = WriteCrashLog(e.Exception);
        var loc = LocalizationService.Instance;
        var message = logPath is null
            ? loc["Error_AppCrash"]
            : $"{loc["Error_AppCrash"]}{Environment.NewLine}{Environment.NewLine}{loc["Error_CrashLogSaved"]}{Environment.NewLine}{logPath}";

        try
        {
            System.Windows.MessageBox.Show(
                message,
                "Audio Quality Enhancer",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        catch
        {
            // Reporting the crash must never prevent the orderly shutdown below.
        }

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

    /// <summary>Writes the crash log and returns its path, or null if it could not be written.</summary>
    private static string? WriteCrashLog(Exception exception)
    {
        try
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
        catch
        {
            // A failing crash log (read-only profile, full disk) must not throw inside
            // the crash handler and turn a handled error into a hard termination.
            return null;
        }
    }
}
