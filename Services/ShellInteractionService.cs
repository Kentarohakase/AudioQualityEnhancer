using System.Diagnostics;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class ShellInteractionService
{
    public Result OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(LocalizationService.Instance.Format("Error_OpenPathFailedFormat", ex.Message), ex);
        }
    }

    public Result CopyText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(LocalizationService.Instance.Format("Error_ClipboardFailedFormat", ex.Message), ex);
        }
    }
}
