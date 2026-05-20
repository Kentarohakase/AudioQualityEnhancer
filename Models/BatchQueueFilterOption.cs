using System.ComponentModel;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class BatchQueueFilterOption : INotifyPropertyChanged
{
    public BatchQueueFilterOption(BatchQueueFilter filter, string nameKey)
    {
        Filter = filter;
        NameKey = nameKey;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BatchQueueFilter Filter { get; }

    public string NameKey { get; }

    public string DisplayName => LocalizationService.Instance[NameKey];

    public override string ToString() => DisplayName;

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]")
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }

    public static BatchQueueFilterOption AllItems { get; } = new(BatchQueueFilter.All, "BatchFilter_All");

    public static BatchQueueFilterOption Ready { get; } = new(BatchQueueFilter.Ready, "BatchFilter_Ready");

    public static BatchQueueFilterOption Processing { get; } = new(BatchQueueFilter.Processing, "BatchFilter_Processing");

    public static BatchQueueFilterOption Done { get; } = new(BatchQueueFilter.Done, "BatchFilter_Done");

    public static BatchQueueFilterOption Warnings { get; } = new(BatchQueueFilter.Warnings, "BatchFilter_Warnings");

    public static BatchQueueFilterOption Failed { get; } = new(BatchQueueFilter.Failed, "BatchFilter_Failed");

    public static BatchQueueFilterOption Cancelled { get; } = new(BatchQueueFilter.Cancelled, "BatchFilter_Cancelled");

    public static IReadOnlyList<BatchQueueFilterOption> All { get; } =
        new[] { AllItems, Ready, Processing, Done, Warnings, Failed, Cancelled };
}
