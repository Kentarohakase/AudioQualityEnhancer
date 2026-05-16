using System.Windows.Media;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class AudioPreviewService : IDisposable
{
    private MediaPlayer? _player;

    public Result Play(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Result.Failure("Die Datei für die Vorschau wurde nicht gefunden.");
        }

        try
        {
            Stop();

            _player = new MediaPlayer();
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure("Die Datei konnte nicht für die Vorschau geöffnet werden. Das Windows-Wiedergabesystem unterstützt dieses Format möglicherweise nicht.", ex);
        }
    }

    public void Stop()
    {
        if (_player is null)
        {
            return;
        }

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
}
