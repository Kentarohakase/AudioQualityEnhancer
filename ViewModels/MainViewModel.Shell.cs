using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// Shell interactions: opening files/folders and clipboard/log handling.
public sealed partial class MainViewModel
{
    private void OpenOutputFolder()
    {
        if (!Directory.Exists(OutputDirectory))
        {
            SetStatus("Status_OutputFolderMissing");
            return;
        }

        OpenPath(OutputDirectory, "Status_OutputFolderOpened");
    }

    private void OpenSelectedOutput()
    {
        var outputPath = SelectedBatchItem?.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            SetStatus("Status_SelectedOutputFileMissing");
            return;
        }

        OpenPath(outputPath, "Status_SelectedOutputFileOpened");
    }

    private void OpenSelectedOutputFolder()
    {
        var outputPath = SelectedBatchItem?.OutputPath;
        var directory = string.IsNullOrWhiteSpace(outputPath)
            ? null
            : Path.GetDirectoryName(outputPath);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            SetStatus("Status_SelectedOutputFolderMissing");
            return;
        }

        OpenPath(directory, "Status_SelectedOutputFolderOpened");
    }

    private void OpenLastOutput()
    {
        if (!File.Exists(LastOutputPath))
        {
            SetStatus("Status_OutputFileMissing");
            return;
        }

        OpenPath(LastOutputPath, "Status_OutputFileOpened");
    }

    private void OpenLastReport()
    {
        if (!File.Exists(LastReportPath))
        {
            SetStatus("Status_ReportFileMissing");
            return;
        }

        OpenPath(LastReportPath, "Status_ReportFileOpened");
    }

    private void OpenLastReportFolder()
    {
        var directory = GetExistingDirectoryForFile(LastReportPath);
        if (directory is null)
        {
            SetStatus("Status_ReportFolderMissing");
            return;
        }

        OpenPath(directory, "Status_ReportFolderOpened");
    }

    private void CopyLog()
    {
        if (string.IsNullOrWhiteSpace(LogText))
        {
            SetStatus("Status_LogEmpty");
            return;
        }

        var result = _shellInteractionService.CopyText(LogText);
        if (result.IsSuccess)
        {
            SetStatus("Status_LogCopied");
        }
        else
        {
            SetStatusRaw(result.ErrorMessage ?? LocalizationService.Instance["Status_LogEmpty"]);
        }
    }

    private void ClearLog()
    {
        if (IsBusy)
        {
            return;
        }

        _logService.Clear();
        SetStatus("Status_LogCleared");
    }

    private void OpenPath(string path, string successStatusKey)
    {
        var result = _shellInteractionService.OpenPath(path);
        if (result.IsSuccess)
        {
            SetStatus(successStatusKey);
            return;
        }

        SetStatusRaw(result.ErrorMessage ?? LocalizationService.Instance.Format("Error_OpenPathFailedFormat", string.Empty));
    }

    private bool CanOpenSelectedOutput()
    {
        return !IsBusy && File.Exists(SelectedBatchItem?.OutputPath);
    }

    private bool CanOpenSelectedOutputFolder()
    {
        var outputPath = SelectedBatchItem?.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(outputPath);
        return !IsBusy && !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
    }

    private bool CanOpenLastReportFolder()
    {
        return !IsBusy && HasReportFolder;
    }
}
