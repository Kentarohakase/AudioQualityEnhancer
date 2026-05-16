using System.ComponentModel;
using System.Globalization;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class AudioInfo : INotifyPropertyChanged
{
    public AudioInfo()
    {
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourcePath { get; init; } = string.Empty;

    public string FileName => Path.GetFileName(SourcePath);

    public string Codec { get; init; } = string.Empty;

    public string CodecLongName { get; init; } = string.Empty;

    public long? BitRate { get; init; }

    public int? SampleRate { get; init; }

    public int? Channels { get; init; }

    public TimeSpan? Duration { get; init; }

    public string Container { get; init; } = string.Empty;

    public string ContainerDisplay => string.IsNullOrWhiteSpace(Container)
        ? LocalizationService.Instance["Display_Unknown"]
        : Container;

    public long FileSizeBytes { get; init; }

    public bool IsLikelyLossy { get; init; }

    public string CodecDisplay
    {
        get
        {
            var codec = string.IsNullOrWhiteSpace(Codec)
                ? LocalizationService.Instance["Display_Unknown"]
                : Codec;
            return string.IsNullOrWhiteSpace(CodecLongName)
                ? codec
                : $"{codec} ({CodecLongName})";
        }
    }

    public string BitRateDisplay => BitRate is > 0
        ? $"{BitRate.Value / 1000d:0} kbit/s"
        : LocalizationService.Instance["Display_Unknown"];

    public string SampleRateDisplay => SampleRate is > 0
        ? $"{SampleRate.Value.ToString("N0", CultureInfo.CurrentCulture)} Hz"
        : LocalizationService.Instance["Display_Unknown"];

    public string ChannelsDisplay => Channels switch
    {
        1 => LocalizationService.Instance["Display_Mono"],
        2 => LocalizationService.Instance["Display_Stereo"],
        > 2 => LocalizationService.Instance.Format("Display_ChannelsFormat", Channels),
        _ => LocalizationService.Instance["Display_Unknown"]
    };

    public string DurationDisplay => Duration.HasValue
        ? Duration.Value.ToString(Duration.Value.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.CurrentCulture)
        : LocalizationService.Instance["Display_Unknown"];

    public string FileSizeDisplay => FileSizeBytes > 0
        ? $"{FileSizeBytes / 1024d / 1024d:0.0} MB"
        : LocalizationService.Instance["Display_Unknown"];

    public string LossyDisplay => IsLikelyLossy
        ? LocalizationService.Instance["Display_LikelyLossy"]
        : LocalizationService.Instance["Display_LikelyLossless"];

    public string LossyWarning => IsLikelyLossy
        ? LocalizationService.Instance["Warning_LossySource"]
        : string.Empty;

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]")
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
