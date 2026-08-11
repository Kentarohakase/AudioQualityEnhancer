using System.Globalization;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// App update check: notify (and link to the download) when a newer release exists.
public sealed partial class MainViewModel
{
    private async Task CheckForAppUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_checkForUpdates)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (DateTimeOffset.TryParse(
                    _appUpdateLastCheckUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var last) && now - last < TimeSpan.FromHours(24))
            {
                return;
            }

            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var update = await _appUpdateService.CheckAsync(current, cancellationToken);

            // The window may have closed while the request was in flight; writing view
            // model state after that would only raise change notifications into a
            // torn-down view.
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _appUpdateLastCheckUtc = now.ToString("o", CultureInfo.InvariantCulture);

            if (update is not null)
            {
                _updateUrl = update.Url;
                UpdateNoticeText = LocalizationService.Instance.Format("Update_AvailableFormat", update.Version);
                IsUpdateAvailable = true;
            }
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
        catch (Exception exception)
        {
            // An update check must never disrupt startup (offline, rate limit, ...),
            // but a genuine defect in this path should not stay invisible either.
            _logService.Warning($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OpenUpdate()
    {
        if (!string.IsNullOrWhiteSpace(_updateUrl))
        {
            _shellInteractionService.OpenPath(_updateUrl);
        }
    }
}
