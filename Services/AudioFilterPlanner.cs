using System.Globalization;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

internal static class AudioFilterPlanner
{
    public static AudioFilterPlan BuildPlan(ProcessingOptions options)
    {
        var dualMono = IsMonoSource(options);

        if (options.Preset.Id == AudioPreset.Music.Id)
        {
            var loudness = new LoudnessSettings("-14", "-1.5", "11", dualMono);
            return new AudioFilterPlan(
                BuildLoudnormFilter(Array.Empty<string>(), loudness, null, printJson: false),
                Array.Empty<string>(),
                loudness);
        }

        if (options.Preset.Id == AudioPreset.Speech.Id)
        {
            var preFilters = new List<string>
            {
                "highpass=f=80"
            };

            if (options.EnableSpeechPresenceBoost)
            {
                preFilters.Add("equalizer=f=3500:t=q:w=1:g=2");
            }

            if (options.EnableSpeechCompression)
            {
                preFilters.Add("acompressor=threshold=-18dB:ratio=2.5:attack=20:release=250");
            }

            var loudness = new LoudnessSettings("-16", "-1.5", "11", dualMono);
            return new AudioFilterPlan(BuildLoudnormFilter(preFilters, loudness, null, printJson: false), preFilters, loudness);
        }

        if (options.Preset.Id == AudioPreset.PodcastVoice.Id)
        {
            var preFilters = new[]
            {
                "highpass=f=80",
                "equalizer=f=180:t=q:w=1:g=-2",
                "equalizer=f=3500:t=q:w=1:g=2",
                "deesser=i=0.25:m=0.5:f=0.5",
                "acompressor=threshold=-18dB:ratio=2.5:attack=20:release=250"
            };
            var loudness = new LoudnessSettings("-16", "-1.5", "9", dualMono);
            return new AudioFilterPlan(BuildLoudnormFilter(preFilters, loudness, null, printJson: false), preFilters, loudness);
        }

        if (options.Preset.Id == AudioPreset.NoisySpeechCleanup.Id)
        {
            var preFilters = new[]
            {
                "highpass=f=90",
                "afftdn=nf=-25",
                "deesser=i=0.25:m=0.5:f=0.5",
                "acompressor=threshold=-20dB:ratio=2:attack=20:release=250"
            };
            var loudness = new LoudnessSettings("-16", "-1.5", "9", dualMono);
            return new AudioFilterPlan(BuildLoudnormFilter(preFilters, loudness, null, printJson: false), preFilters, loudness);
        }

        if (options.Preset.Id == AudioPreset.NoiseReduction.Id)
        {
            var value = Math.Clamp(options.NoiseReductionFloor, -35, -20);
            return new AudioFilterPlan(string.Create(CultureInfo.InvariantCulture, $"afftdn=nf={value}"), Array.Empty<string>(), null);
        }

        return new AudioFilterPlan(string.Empty, Array.Empty<string>(), null);
    }

    private static bool IsMonoSource(ProcessingOptions options)
    {
        var channels = options.AudioStream?.Channels ?? options.SourceInfo?.Channels;
        return channels == 1;
    }

    public static string BuildLoudnormFilter(
        IReadOnlyList<string> preFilters,
        LoudnessSettings settings,
        LoudnormMeasuredStats? stats,
        bool printJson)
    {
        var filters = new List<string>(preFilters);
        var loudnorm = string.Create(
            CultureInfo.InvariantCulture,
            $"loudnorm=I={settings.IntegratedLufs}:TP={settings.TruePeakDb}:LRA={settings.LoudnessRange}");

        if (settings.DualMono)
        {
            // Mono material is normally played back over both speakers; without this
            // flag the EBU R128 measurement would normalize it about 3 LU too loud.
            loudnorm += ":dual_mono=true";
        }

        if (stats is not null)
        {
            loudnorm += string.Create(
                CultureInfo.InvariantCulture,
                $":measured_I={stats.InputIntegrated}:measured_TP={stats.InputTruePeak}:measured_LRA={stats.InputLoudnessRange}:measured_thresh={stats.InputThreshold}:offset={stats.TargetOffset}:linear=true:print_format=summary");
        }
        else if (printJson)
        {
            loudnorm += ":print_format=json";
        }

        filters.Add(loudnorm);
        return string.Join(",", filters);
    }
}

internal sealed record AudioFilterPlan(
    string FilterGraph,
    IReadOnlyList<string> PreLoudnessFilters,
    LoudnessSettings? LoudnessSettings)
{
    public bool HasFilters => !string.IsNullOrWhiteSpace(FilterGraph);
}

internal sealed record LoudnessSettings(string IntegratedLufs, string TruePeakDb, string LoudnessRange, bool DualMono = false);

internal sealed record LoudnormMeasuredStats(
    string InputIntegrated,
    string InputTruePeak,
    string InputLoudnessRange,
    string InputThreshold,
    string TargetOffset)
{
    public static LoudnormMeasuredStats Placeholder { get; } = new("measured_I", "measured_TP", "measured_LRA", "measured_thresh", "offset");
}
