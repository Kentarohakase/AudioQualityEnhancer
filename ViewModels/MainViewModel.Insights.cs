using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// Derived analysis output: warnings, reports, profile advice and filter details.
public sealed partial class MainViewModel
{
    private void ApplyPresetDefaults(AudioPreset preset)
    {
        if (preset.IsArchiveExport)
        {
            SelectedExportFormat = ExportFormat.Flac;
        }
        else if (preset.IsEverydayExport && SelectedExportFormat?.IsLossless == true)
        {
            SelectedExportFormat = ExportFormat.Aac_256;
        }
    }

    private void UpdateQualityNotice()
    {
        var parts = new List<string>
        {
            LocalizationService.Instance["Quality_GeneralNote"]
        };

        if (AudioInfo?.IsLikelyLossy == true)
        {
            parts.Add(AudioInfo.LossyWarning);
        }

        if (!string.IsNullOrWhiteSpace(AnalysisWarningText))
        {
            parts.Add(AnalysisWarningText);
        }

        if (!string.IsNullOrWhiteSpace(SelectedPreset?.QualityNote))
        {
            parts.Add(SelectedPreset.QualityNote);
        }

        if (SelectedExportFormat?.IsLossless == true)
        {
            parts.Add(LocalizationService.Instance["Quality_LosslessTarget"]);
        }

        QualityNotice = string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private void UpdateAnalysisWarnings()
    {
        var warnings = new List<string>();

        if (AudioInfo?.IsLikelyLossy == true && AudioInfo.BitRate is > 0)
        {
            var lowBitrateThreshold = AudioInfo.Channels == 1 ? 96_000 : 128_000;
            if (AudioInfo.BitRate.Value < lowBitrateThreshold)
            {
                warnings.Add(LocalizationService.Instance.Format("Warning_LowBitrateFormat", AudioInfo.BitRateDisplay));
            }
        }

        if (AudioInfo?.SampleRate is > 0 and < 32000)
        {
            warnings.Add(LocalizationService.Instance.Format("Warning_LowSampleRateFormat", AudioInfo.SampleRateDisplay));
        }

        var diagnostics = AudioDiagnostics;
        if (diagnostics is not null)
        {
            if (diagnostics.HasPotentialClipping)
            {
                warnings.Add(LocalizationService.Instance["Warning_PotentialClipping"]);
            }
            else if ((diagnostics.TruePeakDb ?? diagnostics.MaxVolumeDb) is >= -1.0)
            {
                warnings.Add(LocalizationService.Instance["Warning_LowHeadroom"]);
            }

            if (diagnostics.IntegratedLoudnessLufs is < -28)
            {
                warnings.Add(LocalizationService.Instance["Warning_VeryQuiet"]);
            }
            else if (diagnostics.IntegratedLoudnessLufs is > -9)
            {
                warnings.Add(LocalizationService.Instance["Warning_AlreadyLoud"]);
            }
        }

        AnalysisWarningText = string.Join(Environment.NewLine + Environment.NewLine, warnings);
        HasAnalysisWarnings = warnings.Count > 0;
    }

    private void UpdateAnalysisReport()
    {
        AnalysisReport = AudioInfo is null
            ? null
            : _audioAnalysisInsightService.BuildReport(AudioInfo, AudioDiagnostics);
    }

    private void UpdateProfileAdvice()
    {
        ProfileAdvice = _audioProfileAdvisorService.BuildAdvice(AudioInfo, AudioDiagnostics);
    }

    private void ApplyProfileSuggestion(AudioProfileSuggestion? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        SelectedPreset = suggestion.Preset;
        if (suggestion.ExportFormat is not null)
        {
            SelectedExportFormat = suggestion.ExportFormat;
        }

        SetStatus("Status_ProfileAdviceAppliedFormat", suggestion.Title);
    }

    private void UpdateFilterDetails()
    {
        if (SelectedPreset is null || SelectedExportFormat is null)
        {
            FilterDetailsText = string.Empty;
            return;
        }

        FilterDetailsText = AudioProcessingService.BuildFilterPreview(BuildOptionsForItem(SelectedBatchItem));
    }
}
