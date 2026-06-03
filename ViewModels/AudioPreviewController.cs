using System.Windows.Threading;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

internal sealed class AudioPreviewController : IDisposable
{
    private readonly AudioPreviewService _previewService;
    private DispatcherTimer? _timer;

    public AudioPreviewController(AudioPreviewService previewService)
    {
        _previewService = previewService;
        _previewService.PlaybackFailed += OnPlaybackFailed;
        _previewService.PlaybackEnded += OnPlaybackEnded;
    }

    public event EventHandler? Tick;

    public event EventHandler<string>? PlaybackFailed;

    public event EventHandler? PlaybackEnded;

    public TimeSpan? NaturalDuration => _previewService.NaturalDuration;

    public TimeSpan Position
    {
        get => _previewService.Position;
        set => _previewService.Position = value;
    }

    public Result Play(string path)
    {
        StopTimer();
        var result = _previewService.Play(path);
        if (result.IsSuccess)
        {
            StartTimer();
        }

        return result;
    }

    public void Stop()
    {
        StopTimer();
        _previewService.Stop();
    }

    public void Dispose()
    {
        _previewService.PlaybackFailed -= OnPlaybackFailed;
        _previewService.PlaybackEnded -= OnPlaybackEnded;
        Stop();
        _previewService.Dispose();
    }

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Tick?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackFailed(object? sender, string errorMessage)
    {
        StopTimer();
        PlaybackFailed?.Invoke(this, errorMessage);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        StopTimer();
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }
}
