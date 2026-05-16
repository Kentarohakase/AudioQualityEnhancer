namespace AudioQualityEnhancer.Models;

public enum AudioComparisonFindingKind
{
    NoIssues,
    OutputMissing,
    OutputEmpty,
    OutputUnreadable,
    DurationMismatch,
    PotentialClipping,
    LowHeadroom,
    LoudnessOffTarget,
    LossyToLossless,
    StreamCopyMetadataOnly,
    OutputDiagnosticsMissing
}
