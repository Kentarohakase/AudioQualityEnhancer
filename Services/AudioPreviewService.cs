using System.Windows.Media;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class AudioPreviewService : IDisposable
{
    private MediaPlayer? _player;

    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler? PlaybackEnded;

    public TimeSpan? NaturalDuration { get; private set; }

    public TimeSpan Position
    {
        get => _player?.Position ?? TimeSpan.Zero;
        set
        {
            if (_player is not null)
            {
                _player.Position = value;
            }
        }
    }

    public Result Play(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Result.Failure(LocalizationService.Instance["Error_PreviewFileNotFound"]);
        }

        try
        {
            Stop();

            _player = new MediaPlayer();
            _player.MediaOpened += OnMediaOpened;
            _player.MediaFailed += OnMediaFailed;
            _player.MediaEnded += OnMediaEnded;
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(LocalizationService.Instance["Error_PreviewOpenFailed"], ex);
        }
    }

    public void Stop()
    {
        if (_player is null)
        {
            return;
        }

        NaturalDuration = null;

        try
        {
            _player.Stop();
            _player.Close();
        }
        finally
        {
            _player = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        NaturalDuration = _player?.NaturalDuration.HasTimeSpan == true
            ? _player.NaturalDuration.TimeSpan
            : null;
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        var message = e.ErrorException?.Message is { Length: > 0 } msg
            ? LocalizationService.Instance.Format("Error_PreviewFailedFormat", msg)
            : LocalizationService.Instance["Error_PreviewFailedGeneric"];
        PlaybackFailed?.Invoke(this, message);
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }
}
