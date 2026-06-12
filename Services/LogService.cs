using System.Text;
using System.Text.RegularExpressions;

namespace AudioQualityEnhancer.Services;

public sealed class LogService
{
    // Bounds the in-memory log so very large batch runs cannot make the bound
    // log TextBox (and CurrentText snapshots) arbitrarily expensive.
    internal const int MaxBufferLength = 1_000_000;
    internal const int TrimmedBufferLength = 750_000;
    internal const string TruncationMarker = "[...]";

    private static readonly Regex SensitiveValueRegex = new(
        "(?i)(authorization|bearer|token|api[_-]?key|password|secret)(\\s*[:=]\\s*)\\S+",
        RegexOptions.Compiled);

    private readonly object _syncRoot = new();
    private readonly StringBuilder _buffer = new();

    public event EventHandler<string>? LogAdded;

    public string CurrentText
    {
        get
        {
            lock (_syncRoot)
            {
                return _buffer.ToString();
            }
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _buffer.Clear();
        }

        LogAdded?.Invoke(this, string.Empty);
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warning(string message)
    {
        Write("WARN", message);
    }

    public void Error(string message)
    {
        Write("ERROR", message);
    }

    public async Task<string> SaveAsync(string outputDirectory, string filePrefix, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var logDirectory = Path.Combine(outputDirectory, "Logs");
        Directory.CreateDirectory(logDirectory);

        var invalidChars = Path.GetInvalidFileNameChars();
        var safePrefix = string.Concat(filePrefix.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
        var path = Path.Combine(logDirectory, $"{safePrefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log");
        await File.WriteAllTextAsync(path, CurrentText, Encoding.UTF8, cancellationToken);
        return path;
    }

    private void Write(string level, string message)
    {
        var sanitized = Sanitize(message);
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {sanitized}";

        lock (_syncRoot)
        {
            _buffer.AppendLine(line);
            TrimBufferIfNeeded();
        }

        LogAdded?.Invoke(this, line);
    }

    private void TrimBufferIfNeeded()
    {
        if (_buffer.Length <= MaxBufferLength)
        {
            return;
        }

        var removeCount = _buffer.Length - TrimmedBufferLength;
        while (removeCount < _buffer.Length && _buffer[removeCount] != '\n')
        {
            removeCount++;
        }

        if (removeCount < _buffer.Length)
        {
            removeCount++;
        }

        _buffer.Remove(0, removeCount);
        _buffer.Insert(0, TruncationMarker + Environment.NewLine);
    }

    private static string Sanitize(string message)
    {
        return SensitiveValueRegex.Replace(message, "$1$2***");
    }
}
