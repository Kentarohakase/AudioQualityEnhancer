using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

/// <summary>
/// The rules that decide when source or output audio counts as weak. They were written
/// out four times - in the analysis scoring, the profile advice, the result validation
/// and the view model warnings - with the same numbers in each place, so a change to one
/// of them would have made the interface contradict itself with nothing to catch it.
/// </summary>
public static class AudioQualityThresholds
{
    /// <summary>A mono source carries one channel, so it holds up at a lower bitrate.</summary>
    public const int LowBitrateMonoBitsPerSecond = 96_000;

    public const int LowBitrateMultiChannelBitsPerSecond = 128_000;

    public const int LowSampleRateHz = 32_000;

    /// <summary>Peak level from which a re-encode is likely to push samples over full scale.</summary>
    public const double LowHeadroomPeakDb = -1.0;

    public const double VeryQuietLufs = -28;

    public const double AlreadyLoudLufs = -9;

    /// <summary>A lossy source below the bitrate its channel count needs to hold up.</summary>
    public static bool HasLowBitrate(AudioInfo? info)
    {
        if (info is null || !info.IsLikelyLossy || info.BitRate is not > 0)
        {
            return false;
        }

        var threshold = info.Channels == 1
            ? LowBitrateMonoBitsPerSecond
            : LowBitrateMultiChannelBitsPerSecond;

        return info.BitRate.Value < threshold;
    }

    public static bool HasLowSampleRate(AudioInfo? info)
    {
        return info?.SampleRate is > 0 and < LowSampleRateHz;
    }

    public static bool HasPotentialClipping(AudioDiagnostics? diagnostics)
    {
        return diagnostics?.HasPotentialClipping == true;
    }

    /// <summary>
    /// Little headroom left below full scale. Actual clipping is reported on its own and
    /// would otherwise be reported twice, so it is excluded here.
    /// </summary>
    public static bool HasLowHeadroom(AudioDiagnostics? diagnostics)
    {
        return !HasPotentialClipping(diagnostics) &&
               (diagnostics?.TruePeakDb ?? diagnostics?.MaxVolumeDb) is >= LowHeadroomPeakDb;
    }

    public static bool IsVeryQuiet(AudioDiagnostics? diagnostics)
    {
        return diagnostics?.IntegratedLoudnessLufs is < VeryQuietLufs;
    }

    /// <summary>Mutually exclusive with <see cref="IsVeryQuiet"/>, the bands do not overlap.</summary>
    public static bool IsAlreadyLoud(AudioDiagnostics? diagnostics)
    {
        return diagnostics?.IntegratedLoudnessLufs is > AlreadyLoudLufs;
    }

    public static bool HasProblematicLoudness(AudioDiagnostics? diagnostics)
    {
        return IsVeryQuiet(diagnostics) || IsAlreadyLoud(diagnostics);
    }
}
