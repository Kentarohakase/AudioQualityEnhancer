using System.Globalization;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// App update check: notify (and link to the download) when a newer release exists.
public sealed partial class MainViewModel
{
    private async Task CheckForAppUpdateAsync()
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
            var update = await _appUpdateService.CheckAsync(current, CancellationToken.None);
            _appUpdateLastCheckUtc = now.ToString("o", CultureInfo.InvariantCulture);

            if (update is not null)
            {
                _updateUrl = update.Url;
                UpdateNoticeText = LocalizationService.Instance.Format("Update_AvailableFormat", update.Version);
                IsUpdateAvailable = true;
            }
        }
        catch
        {
            // An update check must never disrupt startup (offline, rate limit, ...).
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
