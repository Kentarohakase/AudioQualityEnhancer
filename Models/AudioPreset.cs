namespace AudioQualityEnhancer.Models;

public sealed class AudioPreset
{
    public AudioPreset(
        string id,
        string name,
        string description,
        string qualityNote,
        bool isCopyOnly = false,
        bool isArchiveExport = false,
        bool isEverydayExport = false)
    {
        Id = id;
        Name = name;
        Description = description;
        QualityNote = qualityNote;
        IsCopyOnly = isCopyOnly;
        IsArchiveExport = isArchiveExport;
        IsEverydayExport = isEverydayExport;
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string QualityNote { get; }

    public bool IsCopyOnly { get; }

    public bool IsArchiveExport { get; }

    public bool IsEverydayExport { get; }

    public override string ToString()
    {
        return Name;
    }

    public static AudioPreset Music { get; } = new(
        "music",
        "Musik verbessern",
        "Loudness-Normalisierung auf etwa -14 LUFS mit True Peak Limit -1,5 dB.",
        "Für Musik wird bewusst keine aggressive Rauschreduzierung aktiviert, damit Dynamik und Klangcharakter möglichst erhalten bleiben.");

    public static AudioPreset Speech { get; } = new(
        "speech",
        "Sprache verbessern",
        "High-Pass bei 80 Hz, Normalisierung auf etwa -16 LUFS und optional dezente Sprachbearbeitung.",
        "Für Sprache werden Verständlichkeit und Lautheit priorisiert. Kompression und Präsenzanhebung sind bewusst moderat.");

    public static AudioPreset NoiseReduction { get; } = new(
        "noise",
        "Rauschen reduzieren",
        "Reduziert Grundrauschen mit afftdn. Die Stärke ist einstellbar.",
        "Zu starke Rauschreduzierung kann metallisch oder künstlich klingen. Beginne konservativ und prüfe das Ergebnis.");

    public static AudioPreset ExtractCopy { get; } = new(
        "copy",
        "Nur verlustfrei extrahieren",
        "Extrahiert die erste Audiospur ohne Filter und ohne Re-Encoding, wenn der Codec sinnvoll kopierbar ist.",
        "Diese Option verbessert den Klang nicht, vermeidet aber zusätzliche Qualitätsverluste.",
        isCopyOnly: true);

    public static AudioPreset ArchiveExport { get; } = new(
        "archive",
        "Archiv Export",
        "Speichert die bearbeitete Audiospur als FLAC.",
        "FLAC ist verlustfrei, stellt aber keine bereits verlorenen Details wieder her.",
        isArchiveExport: true);

    public static AudioPreset EverydayExport { get; } = new(
        "everyday",
        "Alltag Export",
        "Exportiert in ein alltagstaugliches Format mit guter Qualität bei sinnvoller Dateigröße.",
        "Für Alltagsexporte sind AAC 256k, MP3 320k oder Opus 160k/192k sinnvoll.",
        isEverydayExport: true);

    public static IReadOnlyList<AudioPreset> All { get; } = new[]
    {
        Music,
        Speech,
        NoiseReduction,
        ExtractCopy,
        ArchiveExport,
        EverydayExport
    };
}
