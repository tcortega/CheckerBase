using CheckerBase.App.Configuration;

namespace CheckerBase.App.State;

/// <summary>
/// Manages checkpoint creation and resume functionality.
/// </summary>
public sealed class CheckpointManager
{
    private readonly SettingsManager _settingsManager;
    private readonly AppSettings _settings;

    public CheckpointManager(SettingsManager settingsManager, AppSettings settings)
    {
        _settingsManager = settingsManager;
        _settings = settings;
    }

    /// <summary>
    /// Gets the resume byte position if a valid checkpoint exists.
    /// Returns null if no checkpoint or if the input file has changed.
    /// </summary>
    public long? GetResumePosition()
    {
        if (!_settings.HasValidCheckpoint)
            return null;

        // Verify the file still exists and hasn't been truncated
        if (!File.Exists(_settings.InputFilePath))
            return null;

        var fileInfo = new FileInfo(_settings.InputFilePath!);
        if (fileInfo.Length < _settings.ResumeBytePosition)
            return null; // File was truncated

        return _settings.ResumeBytePosition;
    }

    /// <summary>
    /// Gets a human-readable description of the checkpoint state.
    /// </summary>
    public string? GetCheckpointDescription()
    {
        var position = GetResumePosition();
        if (position == null)
            return null;

        var fileInfo = new FileInfo(_settings.InputFilePath!);
        var percent = (double)position.Value / fileInfo.Length * 100;
        var timestamp = _settings.ResumeTimestamp?.ToLocalTime().ToString("g") ?? "Unknown";

        return $"Resume from {percent:F1}% ({FormatBytes(position.Value)} / {FormatBytes(fileInfo.Length)}) - Saved: {timestamp}";
    }

    /// <summary>
    /// Saves a checkpoint at the specified byte position.
    /// </summary>
    public async Task SaveCheckpointAsync(long bytePosition)
    {
        await _settingsManager.SaveCheckpointAsync(_settings, bytePosition);
    }

    /// <summary>
    /// Clears the checkpoint (call on successful completion).
    /// </summary>
    public async Task ClearCheckpointAsync()
    {
        await _settingsManager.ClearCheckpointAsync(_settings);
    }

    /// <summary>
    /// Exports remaining unprocessed lines to a new file.
    /// </summary>
    /// <param name="inputPath">Path to the original input file.</param>
    /// <param name="fromByte">Byte position to start from.</param>
    /// <param name="outputPath">Path to write remaining lines.</param>
    /// <returns>Number of bytes written.</returns>
    public async Task<long> ExportRemainingLinesAsync(string inputPath, long fromByte, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (fromByte < 0)
            throw new ArgumentOutOfRangeException(nameof(fromByte), "Byte position cannot be negative");

        await using var source = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        source.Seek(fromByte, SeekOrigin.Begin);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        await using var dest = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        await source.CopyToAsync(dest);

        return dest.Length;
    }

    /// <summary>
    /// Creates a temporary file containing remaining content for resume.
    /// </summary>
    /// <param name="inputPath">Path to the original input file.</param>
    /// <param name="fromByte">Byte position to start from.</param>
    /// <returns>Path to the temporary file.</returns>
    public async Task<string> CreateResumeTempFileAsync(string inputPath, long fromByte)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"checkerbase_resume_{Guid.NewGuid():N}.txt");
        await ExportRemainingLinesAsync(inputPath, fromByte, tempPath);
        return tempPath;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        var index = 0;
        double size = bytes;

        while (size >= 1024 && index < suffixes.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:F1} {suffixes[index]}";
    }
}
