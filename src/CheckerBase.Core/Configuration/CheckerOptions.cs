namespace CheckerBase.Core.Configuration;

/// <summary>
/// Configuration options for the checker engine.
/// </summary>
public sealed record CheckerOptions
{
    /// <summary>
    /// Number of dedicated worker tasks.
    /// </summary>
    public required int DegreeOfParallelism { get; init; }

    /// <summary>
    /// Maximum retry attempts for transient failures.
    /// </summary>
    public required int MaxRetries { get; init; }

    /// <summary>
    /// Bounded input channel capacity.
    /// Default: 10,000
    /// </summary>
    public int InputChannelCapacity { get; init; } = 10_000;

    /// <summary>
    /// PipeReader buffer size in bytes.
    /// Default: 1MB
    /// </summary>
    public int ReadBufferSize { get; init; } = 1024 * 1024;

    /// <summary>
    /// Output StreamWriter buffer size in bytes.
    /// Default: 64KB
    /// </summary>
    public int WriteBufferSize { get; init; } = 64 * 1024;

    /// <summary>
    /// Interval for flushing output files.
    /// Default: 1 second
    /// </summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(1);
}