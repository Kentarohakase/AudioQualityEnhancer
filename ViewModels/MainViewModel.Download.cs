using System.Globalization;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// URL audio download: fetch the best audio stream with yt-dlp and hand the file to
// the normal analysis/processing pipeline.
public sealed partial class MainViewModel
{
    private async Task DownloadFromUrlAsync()
    {
        var url = YouTubeUrl?.Trim() ?? string.Empty;
        if (!YtDlpDownloadService.IsLikelyValidUrl(url))
        {
            SetStatus("Error_DownloadInvalidUrl");
            return;
        }

        var targetDirectory = Directory.Exists(OutputDirectory) ? OutputDirectory : GetDefaultOutputDirectory();

        StopPreview();
        _processingCancellation?.Dispose();
        _processingCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        SetProcessingPhase("Phase_Download");
        SetStatus("Status_Downloading");

        Result<IReadOnlyList<string>> result;
        try
        {
            result = await _ytDlpDownloadService.DownloadAsync(
                url,
                targetDirectory,
                SplitChapters,
                RemoveSponsorSegments,
                DownloadPlaylist,
                _logService.Info,
                value => ProgressValue = value,
                _processingCancellation.Token);
        }
        finally
        {
            IsBusy = false;
            _processingCancellation.Dispose();
            _processingCancellation = null;
        }

        if (result.IsSuccess && result.Value is { Count: > 0 } files)
        {
            ProgressValue = 100;
            SetProcessingPhase("Phase_Ready");
            _logService.Info(LocalizationService.Instance["Log_DownloadQualityNote"]);
            _logService.Info(files.Count == 1
                ? LocalizationService.Instance.Format("Log_DownloadDoneFormat", Path.GetFileName(files[0]))
                : LocalizationService.Instance.Format("Log_DownloadDoneCountFormat", files.Count));
            YouTubeUrl = string.Empty;

            if (DownloadOriginalOnly)
            {
                // Keep the untouched original: skip the analyze/enhance queue entirely.
                LastOutputPath = files[0];
                SetStatus("Status_DownloadOriginalSaved");
                return;
            }

            await LoadInputFilesAsync(files);
            return;
        }

        SetProcessingPhase("Phase_Error");
        SetStatusRaw(result.ErrorMessage ?? LocalizationService.Instance["Error_DownloadFailed"]);
        _logService.Error(StatusText);

        if (result.Exception is not null)
        {
            _logService.Error($"{result.Exception.GetType().Name}: {result.Exception.Message}");
        }
    }

    private bool CanDownloadFromUrl()
    {
        return !IsBusy && YtDlpDownloadService.IsLikelyValidUrl(YouTubeUrl);
    }

    private async Task PrepareYtDlpAsync()
    {
        try
        {
            DateTimeOffset? lastCheck = DateTimeOffset.TryParse(
                _ytDlpLastUpdateCheckUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : null;

            var newCheck = await _ytDlpDownloadService.PrepareAsync(
                _ytDlpAutoUpdate,
                lastCheck,
                _logService.Info,
                CancellationToken.None);

            if (newCheck.HasValue)
            {
                _ytDlpLastUpdateCheckUtc = newCheck.Value.ToString("o", CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // Startup preparation of the downloader is best effort and never blocks the app.
        }
    }
}
