using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class AudioAnalysisInsightService
{
    public AudioAnalysisReport BuildReport(AudioInfo info, AudioDiagnostics? diagnostics)
    {
        var score = 100;
        var findings = new List<AudioAnalysisFinding>();
        var recommendations = new List<AudioAnalysisRecommendation>();

        if (info.IsLikelyLossy)
        {
            score -= 5;
            AddFinding(findings, AudioAnalysisFindingKind.LossySource, AudioInsightSeverity.Info);
            AddRecommendation(recommendations, AudioAnalysisFindingKind.LossySource);
        }

        if (info.IsLikelyLossy && info.BitRate is > 0)
        {
            var lowBitrateThreshold = info.Channels == 1 ? 96_000 : 128_000;
            if (info.BitRate.Value < lowBitrateThreshold)
            {
                score -= 15;
                AddFinding(findings, AudioAnalysisFindingKind.LowBitrate, AudioInsightSeverity.Warning);
                AddRecommendation(recommendations, AudioAnalysisFindingKind.LowBitrate);
            }
        }

        if (info.SampleRate is > 0 and < 32000)
        {
            score -= 10;
            AddFinding(findings, AudioAnalysisFindingKind.LowSampleRate, AudioInsightSeverity.Warning);
            AddRecommendation(recommendations, AudioAnalysisFindingKind.LowSampleRate);
        }

        if (diagnostics is null)
        {
            AddFinding(findings, AudioAnalysisFindingKind.AdvancedAnalysisRecommended, AudioInsightSeverity.Info);
            AddRecommendation(recommendations, AudioAnalysisFindingKind.AdvancedAnalysisRecommended);
        }
        else
        {
            var peak = diagnostics.TruePeakDb ?? diagnostics.MaxVolumeDb;
            if (diagnostics.HasPotentialClipping)
            {
                score -= 25;
                AddFinding(findings, AudioAnalysisFindingKind.PotentialClipping, AudioInsightSeverity.Critical);
                AddRecommendation(recommendations, AudioAnalysisFindingKind.PotentialClipping);
            }
            else if (peak is >= -1.0)
            {
                score -= 10;
                AddFinding(findings, AudioAnalysisFindingKind.LowHeadroom, AudioInsightSeverity.Warning);
                AddRecommendation(recommendations, AudioAnalysisFindingKind.LowHeadroom);
            }

            if (diagnostics.IntegratedLoudnessLufs is < -28)
            {
                score -= 10;
                AddFinding(findings, AudioAnalysisFindingKind.VeryQuiet, AudioInsightSeverity.Warning);
                AddRecommendation(recommendations, AudioAnalysisFindingKind.VeryQuiet);
            }
            else if (diagnostics.IntegratedLoudnessLufs is > -9)
            {
                score -= 10;
                AddFinding(findings, AudioAnalysisFindingKind.AlreadyLoud, AudioInsightSeverity.Warning);
                AddRecommendation(recommendations, AudioAnalysisFindingKind.AlreadyLoud);
            }
        }

        score = Math.Clamp(score, 0, 100);
        if (findings.Count == 0)
        {
            AddFinding(findings, AudioAnalysisFindingKind.NoIssues, AudioInsightSeverity.Info);
        }

        var status = GetStatus(score, findings);
        if (status == AudioAnalysisStatus.Excellent &&
            findings.Any(f => f.Kind == AudioAnalysisFindingKind.AdvancedAnalysisRecommended))
        {
            status = AudioAnalysisStatus.Good;
        }

        return new AudioAnalysisReport(
            score,
            status,
            LocalizationService.Instance[$"AnalysisStatus_{status}"],
            LocalizationService.Instance[$"AnalysisSummary_{status}"],
            findings,
            recommendations);
    }

    private static AudioAnalysisStatus GetStatus(int score, IReadOnlyList<AudioAnalysisFinding> findings)
    {
        if (findings.Any(f => f.Severity == AudioInsightSeverity.Critical) || score < 60)
        {
            return AudioAnalysisStatus.Critical;
        }

        if (findings.Any(f => f.Severity == AudioInsightSeverity.Warning) || score < 85)
        {
            return AudioAnalysisStatus.Caution;
        }

        return score < 95 ? AudioAnalysisStatus.Good : AudioAnalysisStatus.Excellent;
    }

    private static void AddFinding(
        ICollection<AudioAnalysisFinding> findings,
        AudioAnalysisFindingKind kind,
        AudioInsightSeverity severity)
    {
        findings.Add(new AudioAnalysisFinding(
            kind,
            severity,
            LocalizationService.Instance[$"AnalysisSeverity_{severity}"],
            LocalizationService.Instance[$"AnalysisFinding_{kind}_Title"],
            LocalizationService.Instance[$"AnalysisFinding_{kind}_Message"]));
    }

    private static void AddRecommendation(
        ICollection<AudioAnalysisRecommendation> recommendations,
        AudioAnalysisFindingKind kind)
    {
        recommendations.Add(new AudioAnalysisRecommendation(
            kind,
            LocalizationService.Instance[$"AnalysisRecommendation_{kind}"]));
    }
}
