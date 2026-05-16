namespace AudioQualityEnhancer.Models;

public sealed record ProcessingProgress(double Percentage, string Phase, string? Detail = null);
