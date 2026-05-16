using System.ComponentModel;
using System.Globalization;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class AudioDiagnostics : INotifyPropertyChanged, IDisposable
{
    public AudioDiagnostics()
    {
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double? IntegratedLoudnessLufs { get; init; }

    public double? LoudnessRangeLu { get; init; }

    public double? TruePeakDb { get; init; }

    public double? MaxVolumeDb { get; init; }

    public double? MeanVolumeDb { get; init; }

    public bool HasPotentialClipping =>
        TruePeakDb is >= -0.1 ||
        MaxVolumeDb is >= -0.1;

    public string IntegratedLoudnessDisplay => FormatDbValue(IntegratedLoudnessLufs, "LUFS");

    public string LoudnessRangeDisplay => FormatDbValue(LoudnessRangeLu, "LU");

    public string TruePeakDisplay => FormatDbValue(TruePeakDb, "dBFS");

    public string MaxVolumeDisplay => FormatDbValue(MaxVolumeDb, "dBFS");

    public string MeanVolumeDisplay => FormatDbValue(MeanVolumeDb, "dB");

    public void Dispose()
    {
        LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
    }

    private static string FormatDbValue(double? value, string unit)
    {
        return value.HasValue
            ? string.Create(CultureInfo.CurrentCulture, $"{value.Value:0.0} {unit}")
            : LocalizationService.Instance["Display_Unknown"];
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
