using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// Audio preview playback and processed A/B preview rendering.
public sealed partial class MainViewModel
{
    private void PlaySourcePreview() =>
        PlayPreview(InputPath, "Button_PlaySource");

    private void PlayOutputPreview() =>
        PlayPreview(LastOutputPath, "Button_PlayOutput");

    private void PlayProcessedPreview() =>
        PlayPreview(_processedPreviewPath, "Button_PlayProcessedPreview");

    private async Task RenderProcessedPreviewAsync()
    {
        if (!CanRenderProcessedPreview())
        {
            return;
        }

        StopPreview();
        var options = BuildOptionsForItem(SelectedBatchItem);
        var cacheKey = AudioProcessedPreviewService.BuildCacheKey(options);
        if (File.Exists(_processedPreviewPath) && string.Equals(_processedPreviewCacheKey, cacheKey, StringComparison.Ordinal))
        {
            SetStatus("Status_ProcessedPreviewReadyFormat", Path.GetFileName(_processedPreviewPath));
            return;
        }

        InvalidateProcessedPreview();
        _isProcessedPreviewRendering = true;

        // The render is a full FFmpeg pass over the loudest section, so it runs as a busy
        // phase like the deep analysis does. That is what makes Cancel reach it at all.
        _previewRenderCancellation?.Dispose();
        _previewRenderCancellation = new CancellationTokenSource();
        IsBusy = true;
        RaiseCommandStates();

        try
        {
            SetStatus("Status_ProcessedPreviewRendering");
            var result = await _audioProcessedPreviewService.RenderAsync(
                options,
                _logService.Info,
                null,
                _previewRenderCancellation.Token);

            if (result.IsFailure || result.Value is null)
            {
                SetStatusRaw(result.ErrorMessage ?? LocalizationService.Instance["Error_ProcessedPreviewFailed"]);
                _logService.Error(StatusText);
                if (result.Exception is not null)
                {
                    _logService.Error($"{result.Exception.GetType().Name}: {result.Exception.Message}");
                }

                return;
            }

            _processedPreviewPath = result.Value.OutputPath;
            _processedPreviewCacheKey = cacheKey;
            OnPropertyChanged(nameof(HasProcessedPreview));
            SetStatus("Status_ProcessedPreviewReadyFormat", Path.GetFileName(_processedPreviewPath));
            _logService.Info(LocalizationService.Instance.Format("Log_ProcessedPreviewReadyFormat", _processedPreviewPath));
        }
        catch (OperationCanceledException)
        {
            SetProcessingPhase("Phase_Cancel");
            SetStatus("Error_ProcessingCancelled");
        }
        finally
        {
            _isProcessedPreviewRendering = false;
            IsBusy = false;
            _previewRenderCancellation.Dispose();
            _previewRenderCancellation = null;
            RaiseCommandStates();
        }
    }

    private void PlayPreview(string path, string labelKey)
    {
        var result = _audioPreviewController.Play(path);
        if (result.IsSuccess)
        {
            _activePreviewLabelKey = labelKey;
            IsPreviewActive = true;
            PreviewDurationSeconds = 0;
            PreviewPositionSeconds = 0;
            SetStatus("Status_PreviewPlayingFormat", LocalizationService.Instance[labelKey]);
            _logService.Info(LocalizationService.Instance.Format("Log_PreviewStartedFormat", path));
            return;
        }

        _activePreviewLabelKey = null;
        SetStatusRaw(result.ErrorMessage ?? LocalizationService.Instance["Status_PreviewFailed"]);
        _logService.Error(StatusText);

        if (result.Exception is not null)
        {
            _logService.Error($"{result.Exception.GetType().Name}: {result.Exception.Message}");
        }
    }

    private void StopPreview()
    {
        _audioPreviewController.Stop();
        IsPreviewActive = false;
        _activePreviewLabelKey = null;
        PreviewPositionSeconds = 0;
        PreviewDurationSeconds = 0;
        SetStatus("Status_PreviewStopped");
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        if (_audioPreviewController.NaturalDuration.HasValue && PreviewDurationSeconds == 0)
        {
            PreviewDurationSeconds = _audioPreviewController.NaturalDuration.Value.TotalSeconds;
        }

        _updatingPositionFromTimer = true;
        PreviewPositionSeconds = _audioPreviewController.Position.TotalSeconds;
        _updatingPositionFromTimer = false;
    }

    private void OnPlaybackFailed(object? sender, string errorMessage)
    {
        IsPreviewActive = false;
        PreviewPositionSeconds = 0;
        PreviewDurationSeconds = 0;
        _activePreviewLabelKey = null;
        SetStatusRaw(errorMessage);
        _logService.Error(errorMessage);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        IsPreviewActive = false;
        _activePreviewLabelKey = null;
        PreviewPositionSeconds = 0;
        PreviewDurationSeconds = 0;
        SetStatus("Status_PreviewEnded");
        RaiseCommandStates();
    }

    private bool CanRenderProcessedPreview()
    {
        return !IsBusy &&
               !_isProcessedPreviewRendering &&
               File.Exists(InputPath) &&
               SelectedPreset is not null &&
               AudioProcessedPreviewService.CanRender(BuildOptionsForItem(SelectedBatchItem));
    }

    private bool CanPlayProcessedPreview()
    {
        return !IsBusy &&
               File.Exists(_processedPreviewPath) &&
               string.Equals(_processedPreviewCacheKey, AudioProcessedPreviewService.BuildCacheKey(BuildOptionsForItem(SelectedBatchItem)), StringComparison.Ordinal);
    }

    private void InvalidateProcessedPreview()
    {
        if (string.IsNullOrWhiteSpace(_processedPreviewPath) && string.IsNullOrWhiteSpace(_processedPreviewCacheKey))
        {
            return;
        }

        AudioProcessedPreviewService.TryDelete(_processedPreviewPath);
        _processedPreviewPath = string.Empty;
        _processedPreviewCacheKey = string.Empty;
        OnPropertyChanged(nameof(HasProcessedPreview));
    }
}
