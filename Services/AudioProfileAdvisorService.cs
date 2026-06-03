using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class AudioProfileAdvisorService
{
    private readonly FileNameService _fileNameService;

    public AudioProfileAdvisorService(FileNameService fileNameService)
    {
        _fileNameService = fileNameService;
    }

    public AudioProfileAdvice BuildAdvice(AudioInfo? info, AudioDiagnostics? diagnostics)
    {
        if (info is null)
        {
            return new AudioProfileAdvice(Array.Empty<AudioProfileSuggestion>(), false, string.Empty);
        }

        var suggestions = new Dictionary<string, AudioProfileSuggestion>(StringComparer.Ordinal);
        var hasLowBitrate = HasLowBitrate(info);
        var hasLowSampleRate = info.SampleRate is > 0 and < 32000;
        var looksSpeechLike = info.Channels == 1 || hasLowBitrate || hasLowSampleRate;
        var isVideoSource = IsVideoSource(info);
        var hasAdvancedDiagnostics = diagnostics is not null;
        var hasPotentialClipping = diagnostics?.HasPotentialClipping == true;
        var hasLowHeadroom = !hasPotentialClipping && (diagnostics?.TruePeakDb ?? diagnostics?.MaxVolumeDb) is >= -1.0;
        var hasProblematicLoudness = diagnostics?.IntegratedLoudnessLufs is < -28 or > -9;
        var hasTechnicalWarnings = info.IsLikelyLossy || hasLowBitrate || hasLowSampleRate || hasPotentialClipping || hasLowHeadroom || hasProblematicLoudness;

        if (looksSpeechLike)
        {
            AddSuggestion(
                suggestions,
                new AudioProfileSuggestion(
                    "podcast_voice",
                    AudioPreset.PodcastVoice,
                    hasPotentialClipping || hasLowHeadroom ? ExportFormat.Flac : ExportFormat.Aac_256,
                    98,
                    "ProfileAdvice_PodcastVoice_Title",
                    "ProfileAdvice_PodcastVoice_Reason",
                    "ProfileAdvice_PodcastVoice_Note"));

            if (hasLowBitrate || hasLowSampleRate)
            {
                AddSuggestion(
                    suggestions,
                    new AudioProfileSuggestion(
                        "noisy_speech",
                        AudioPreset.NoisySpeechCleanup,
                        hasPotentialClipping || hasLowHeadroom ? ExportFormat.Flac : ExportFormat.Aac_256,
                        93,
                        "ProfileAdvice_NoisySpeech_Title",
                        "ProfileAdvice_NoisySpeech_Reason",
                        "ProfileAdvice_NoisySpeech_Note"));
            }

            AddSuggestion(
                suggestions,
                new AudioProfileSuggestion(
                    "speech",
                    AudioPreset.Speech,
                    hasPotentialClipping || hasLowHeadroom ? ExportFormat.Flac : ExportFormat.Aac_256,
                    88,
                    "ProfileAdvice_Speech_Title",
                    "ProfileAdvice_Speech_Reason",
                    "ProfileAdvice_Speech_Note"));
        }

        if (hasPotentialClipping || hasLowHeadroom)
        {
            AddSuggestion(
                suggestions,
                new AudioProfileSuggestion(
                    "lossless_headroom",
                    AudioPreset.Music,
                    ExportFormat.Flac,
                    90,
                    "ProfileAdvice_LosslessHeadroom_Title",
                    "ProfileAdvice_LosslessHeadroom_Reason",
                    "ProfileAdvice_LosslessHeadroom_Note"));
        }

        var copySuggestion = _fileNameService.SuggestCopyOutput(info);
        if (copySuggestion is not null)
        {
            AddSuggestion(
                suggestions,
                new AudioProfileSuggestion(
                    "stream_copy",
                    AudioPreset.ExtractCopy,
                    null,
                    hasTechnicalWarnings ? 70 : 88,
                    "ProfileAdvice_StreamCopy_Title",
                    "ProfileAdvice_StreamCopy_Reason",
                    "ProfileAdvice_StreamCopy_Note"));
        }

        if (!hasTechnicalWarnings && hasAdvancedDiagnostics)
        {
            AddSuggestion(
                suggestions,
                new AudioProfileSuggestion(
                    "everyday_aac",
                    AudioPreset.EverydayExport,
                    ExportFormat.Aac_256,
                    85,
                    "ProfileAdvice_Everyday_Title",
                    "ProfileAdvice_Everyday_Reason",
                    "ProfileAdvice_Everyday_Note"));
        }

        if (isVideoSource)
        {
            AddSuggestion(
                suggestions,
                new AudioProfileSuggestion(
                    "premiere",
                    looksSpeechLike ? AudioPreset.PodcastVoice : AudioPreset.Music,
                    ExportFormat.PremierePro,
                    looksSpeechLike ? 94 : 78,
                    "ProfileAdvice_Premiere_Title",
                    "ProfileAdvice_Premiere_Reason",
                    "ProfileAdvice_Premiere_Note"));
        }

        AddSuggestion(
            suggestions,
            new AudioProfileSuggestion(
                "music_flac",
                AudioPreset.Music,
                ExportFormat.Flac,
                looksSpeechLike ? 55 : 80,
                "ProfileAdvice_MusicFlac_Title",
                "ProfileAdvice_MusicFlac_Reason",
                "ProfileAdvice_MusicFlac_Note"));

        var note = hasAdvancedDiagnostics
            ? string.Empty
            : LocalizationService.Instance["ProfileAdvice_AdvancedAnalysisNote"];

        return new AudioProfileAdvice(
            suggestions.Values
                .OrderByDescending(suggestion => suggestion.Priority)
                .ThenBy(suggestion => suggestion.Title, StringComparer.CurrentCulture)
                .Take(3)
                .ToArray(),
            !hasAdvancedDiagnostics,
            note);
    }

    private static void AddSuggestion(IDictionary<string, AudioProfileSuggestion> suggestions, AudioProfileSuggestion suggestion)
    {
        var key = $"{suggestion.Preset.Id}|{suggestion.ExportFormat?.Id ?? "auto"}";
        if (!suggestions.TryGetValue(key, out var existing) || existing.Priority < suggestion.Priority)
        {
            suggestions[key] = suggestion;
        }
    }

    private static bool HasLowBitrate(AudioInfo info)
    {
        if (!info.IsLikelyLossy || info.BitRate is not > 0)
        {
            return false;
        }

        var threshold = info.Channels == 1 ? 96_000 : 128_000;
        return info.BitRate.Value < threshold;
    }

    private static bool IsVideoSource(AudioInfo info)
    {
        var extension = Path.GetExtension(info.SourcePath);
        if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var container = info.Container.ToLowerInvariant();
        return container.Contains("matroska", StringComparison.Ordinal) ||
               container.Contains("mov,mp4", StringComparison.Ordinal) ||
               container.Contains("mp4", StringComparison.Ordinal);
    }
}
