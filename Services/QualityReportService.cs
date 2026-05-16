using System.Text;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class QualityReportService
{
    public async Task<Result<string>> SaveBatchReportAsync(
        string outputDirectory,
        IEnumerable<BatchProcessingItem> items,
        AudioPreset preset,
        ExportFormat exportFormat,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Result<string>.Failure(LocalizationService.Instance["Error_OutputFolderRequired"]);
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var reportDirectory = Path.Combine(outputDirectory, "Reports");
            Directory.CreateDirectory(reportDirectory);
            var reportPath = Path.Combine(reportDirectory, $"audio-quality-enhancer-report_{DateTime.Now:yyyyMMdd_HHmmss_fff}.md");

            var content = BuildMarkdown(items.ToArray(), preset, exportFormat);
            await File.WriteAllTextAsync(reportPath, content, Encoding.UTF8, cancellationToken);
            return Result<string>.Success(reportPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(LocalizationService.Instance["Error_ReportSaveFailed"], ex);
        }
    }

    internal static string BuildMarkdown(
        IReadOnlyList<BatchProcessingItem> items,
        AudioPreset preset,
        ExportFormat exportFormat)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {LocalizationService.Instance["Report_Title"]}");
        builder.AppendLine();
        builder.AppendLine($"- {LocalizationService.Instance["Report_GeneratedAt"]}: {DateTime.Now:G}");
        builder.AppendLine($"- {LocalizationService.Instance["Field_Preset"]}: {preset.Name}");
        builder.AppendLine($"- {LocalizationService.Instance["Field_ExportFormat"]}: {exportFormat.DisplayName}");
        builder.AppendLine();
        builder.AppendLine(LocalizationService.Instance["Report_Disclaimer"]);
        builder.AppendLine();
        builder.AppendLine($"## {LocalizationService.Instance["Report_Summary"]}");
        builder.AppendLine();
        builder.AppendLine($"- {LocalizationService.Instance["BatchStatus_Done"]}: {items.Count(item => item.Status == BatchProcessingStatus.Done)}");
        builder.AppendLine($"- {LocalizationService.Instance["Report_DoneWithWarnings"]}: {items.Count(item => item.ComparisonReport?.HasWarningsOrErrors == true)}");
        builder.AppendLine($"- {LocalizationService.Instance["BatchStatus_Failed"]}: {items.Count(item => item.Status == BatchProcessingStatus.Failed)}");
        builder.AppendLine($"- {LocalizationService.Instance["BatchStatus_Cancelled"]}: {items.Count(item => item.Status == BatchProcessingStatus.Cancelled)}");
        builder.AppendLine();

        foreach (var item in items)
        {
            AppendItem(builder, item);
        }

        return builder.ToString();
    }

    private static void AppendItem(StringBuilder builder, BatchProcessingItem item)
    {
        builder.AppendLine($"## {EscapeMarkdown(item.FileName)}");
        builder.AppendLine();
        builder.AppendLine($"- {LocalizationService.Instance["Field_Source"]}: `{item.SourcePath}`");
        builder.AppendLine($"- {LocalizationService.Instance["Field_OutputFile"]}: `{(string.IsNullOrWhiteSpace(item.OutputPath) ? "-" : item.OutputPath)}`");
        builder.AppendLine($"- {LocalizationService.Instance["Field_Status"]}: {item.StatusDisplay}");

        if (item.SelectedAudioStream is not null)
        {
            builder.AppendLine($"- {LocalizationService.Instance["Field_AudioTrack"]}: {EscapeMarkdown(item.SelectedAudioStream.DisplayName)}");
        }

        if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
        {
            builder.AppendLine($"- {LocalizationService.Instance["Field_Message"]}: {EscapeMarkdown(item.ErrorMessage)}");
        }

        var report = item.ComparisonReport;
        if (report is null)
        {
            builder.AppendLine();
            builder.AppendLine(LocalizationService.Instance["Report_NoValidation"]);
            builder.AppendLine();
            return;
        }

        builder.AppendLine($"- {LocalizationService.Instance["Field_ResultStatus"]}: {report.StatusText}");
        builder.AppendLine();
        builder.AppendLine(report.Summary);
        builder.AppendLine();

        if (report.HasFindings)
        {
            builder.AppendLine($"### {LocalizationService.Instance["Section_ResultFindings"]}");
            builder.AppendLine();
            foreach (var finding in report.Findings)
            {
                builder.AppendLine($"- **{finding.SeverityDisplay}: {EscapeMarkdown(finding.Title)}** - {EscapeMarkdown(finding.Message)}");
            }

            builder.AppendLine();
        }

        if (report.HasMetrics)
        {
            builder.AppendLine($"### {LocalizationService.Instance["Section_ResultMetrics"]}");
            builder.AppendLine();
            builder.AppendLine($"| {LocalizationService.Instance["Field_Metric"]} | {LocalizationService.Instance["Report_SourceValue"]} | {LocalizationService.Instance["Report_OutputValue"]} |");
            builder.AppendLine("|---|---:|---:|");
            foreach (var metric in report.Metrics)
            {
                builder.AppendLine($"| {EscapeMarkdown(metric.Label)} | {EscapeMarkdown(metric.SourceValue)} | {EscapeMarkdown(metric.OutputValue)} |");
            }

            builder.AppendLine();
        }
    }

    private static string EscapeMarkdown(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }
}
