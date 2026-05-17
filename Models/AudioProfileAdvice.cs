namespace AudioQualityEnhancer.Models;

public sealed class AudioProfileAdvice
{
    public AudioProfileAdvice(
        IReadOnlyList<AudioProfileSuggestion> suggestions,
        bool needsAdvancedAnalysis,
        string note)
    {
        Suggestions = suggestions;
        NeedsAdvancedAnalysis = needsAdvancedAnalysis;
        Note = note;
    }

    public IReadOnlyList<AudioProfileSuggestion> Suggestions { get; }

    public bool NeedsAdvancedAnalysis { get; }

    public string Note { get; }

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}
