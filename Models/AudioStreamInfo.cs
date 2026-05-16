using System.Globalization;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed record AudioStreamInfo(
    int StreamIndex,
    int AudioStreamIndex,
    string Codec,
    string CodecLongName,
    long? BitRate,
    int? SampleRate,
    int? Channels,
    TimeSpan? Duration,
    string Language,
    string Title,
    string HandlerName)
{
    public string FFmpegMapSpecifier => $"0:{StreamIndex}";

    public string Label
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                return Title;
            }

            if (!string.IsNullOrWhiteSpace(Language))
            {
                return Language;
            }

            if (!string.IsNullOrWhiteSpace(HandlerName))
            {
                return HandlerName;
            }

            return string.Empty;
        }
    }

    public string CodecDisplay => string.IsNullOrWhiteSpace(Codec)
        ? LocalizationService.Instance["Display_Unknown"]
        : Codec;

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

    public string DisplayName
    {
        get
        {
            var prefix = $"A{AudioStreamIndex + 1} / #{StreamIndex}";
            var label = Label;
            var details = $"{CodecDisplay}, {ChannelsDisplay}, {SampleRateDisplay}";
            return string.IsNullOrWhiteSpace(label)
                ? $"{prefix} - {details}"
                : $"{prefix} - {label} - {details}";
        }
    }
}
