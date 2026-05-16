namespace AudioQualityEnhancer.Models;

public sealed class AudioAnalysisReport
{
    public AudioAnalysisReport(
        int score,
        AudioAnalysisStatus status,
        string statusText,
        string summary,
        IReadOnlyList<AudioAnalysisFinding> findings,
        IReadOnlyList<AudioAnalysisRecommendation> recommendations)
    {
        Score = Math.Clamp(score, 0, 100);
        Status = status;
        StatusText = statusText;
        Summary = summary;
        Findings = findings;
        Recommendations = recommendations;
    }

    public int Score { get; }

    public string ScoreDisplay => $"{Score}/100";

    public AudioAnalysisStatus Status { get; }

    public string StatusText { get; }

    public string Summary { get; }

    public IReadOnlyList<AudioAnalysisFinding> Findings { get; }

    public IReadOnlyList<AudioAnalysisRecommendation> Recommendations { get; }

    public bool HasFindings => Findings.Count > 0;

    public bool HasRecommendations => Recommendations.Count > 0;
}
