using System.Text.Json.Serialization;
using CheckerBase.Core.Proxies;

namespace CheckerBase.App.Configuration;

/// <summary>
/// Application settings for CheckerBase.App.
/// Persisted to JSON for user convenience across sessions.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Path to the input file containing lines to process.
    /// </summary>
    public string? InputFilePath { get; set; }

    /// <summary>
    /// Path to the proxy file (optional).
    /// </summary>
    public string? ProxyFilePath { get; set; }

    /// <summary>
    /// Default proxy type for proxies that don't specify a protocol.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProxyType ProxyType { get; set; } = ProxyType.Http;

    /// <summary>
    /// Output folder for results (success.txt, failed.txt, etc.).
    /// </summary>
    public string OutputFolder { get; set; } = "output";

    /// <summary>
    /// Number of parallel workers (threads) for processing.
    /// </summary>
    public int DegreeOfParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Maximum retry attempts for transient failures.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// IMAP connection timeout in seconds.
    /// </summary>
    public int ImapTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Server discovery cache TTL in days.
    /// </summary>
    public int DiscoveryCacheDays { get; set; } = 30;

    /// <summary>
    /// Byte position to resume from (if resuming a previous run).
    /// </summary>
    public long? ResumeBytePosition { get; set; }

    /// <summary>
    /// Input file path associated with the resume position.
    /// Used to detect if the input file has changed.
    /// </summary>
    public string? ResumeInputPath { get; set; }

    /// <summary>
    /// Timestamp of when the checkpoint was created.
    /// </summary>
    public DateTime? ResumeTimestamp { get; set; }

    /// <summary>
    /// Creates a deep copy of the settings.
    /// </summary>
    public AppSettings Clone() => new()
    {
        InputFilePath = InputFilePath,
        ProxyFilePath = ProxyFilePath,
        ProxyType = ProxyType,
        OutputFolder = OutputFolder,
        DegreeOfParallelism = DegreeOfParallelism,
        MaxRetries = MaxRetries,
        ImapTimeoutSeconds = ImapTimeoutSeconds,
        DiscoveryCacheDays = DiscoveryCacheDays,
        ResumeBytePosition = ResumeBytePosition,
        ResumeInputPath = ResumeInputPath,
        ResumeTimestamp = ResumeTimestamp
    };

    /// <summary>
    /// Clears resume-related state.
    /// </summary>
    public void ClearResumeState()
    {
        ResumeBytePosition = null;
        ResumeInputPath = null;
        ResumeTimestamp = null;
    }

    /// <summary>
    /// Checks if there is a valid resume checkpoint.
    /// </summary>
    public bool HasValidCheckpoint =>
        ResumeBytePosition.HasValue &&
        ResumeBytePosition.Value > 0 &&
        !string.IsNullOrEmpty(ResumeInputPath) &&
        ResumeInputPath == InputFilePath;
}
