namespace AudioQualityEnhancer.Models;

public enum AudioComparisonFindingKind
{
    NoIssues,
    OutputMissing,
    OutputEmpty,
    OutputUnreadable,
    DurationMismatch,
    CodecMismatch,
    SampleRateMismatch,
    ChannelCountChanged,
    PotentialClipping,
    LowHeadroom,
    LoudnessOffTarget,
    LossyToLossless,
    StreamCopyMetadataOnly,
    OutputDiagnosticsMissing
}
