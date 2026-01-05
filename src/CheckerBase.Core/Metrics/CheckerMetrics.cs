using System.Diagnostics;

namespace CheckerBase.Core.Metrics;

/// <summary>
/// Thread-safe metrics tracking using Interlocked/Volatile operations.
/// Uses byte-based progress for accurate ETA without pre-reading the file.
/// </summary>
public sealed class CheckerMetrics
{
    private long _totalBytes;
    private long _processedBytes;
    private long _successCount;
    private long _failedCount;
    private long _ignoredCount;
    private long _retryCount;
    private readonly Stopwatch _stopwatch = new();

    public void SetTotalBytes(long bytes) => _totalBytes = bytes;
    public void AddProcessedBytes(long bytes) => Interlocked.Add(ref _processedBytes, bytes);
    public void IncrementSuccess() => Interlocked.Increment(ref _successCount);
    public void IncrementFailed() => Interlocked.Increment(ref _failedCount);
    public void IncrementIgnored() => Interlocked.Increment(ref _ignoredCount);
    public void IncrementRetry() => Interlocked.Increment(ref _retryCount);

    public void Start() => _stopwatch.Start();
    public void Stop() => _stopwatch.Stop();
    public void Pause() => _stopwatch.Stop();
    public void Resume() => _stopwatch.Start();

    /// <summary>
    /// Gets a consistent snapshot of all metrics at a point in time.
    /// </summary>
    public MetricsSnapshot GetSnapshot()
    {
        var elapsed = _stopwatch.Elapsed;
        var totalBytes = Volatile.Read(ref _totalBytes);
        var processedBytes = Volatile.Read(ref _processedBytes);
        var success = Volatile.Read(ref _successCount);
        var failed = Volatile.Read(ref _failedCount);
        var ignored = Volatile.Read(ref _ignoredCount);
        var processedLines = success + failed + ignored;

        var seconds = elapsed.TotalSeconds;
        var bytesPerSecond = seconds > 0 ? processedBytes / seconds : 0;

        TimeSpan? eta = null;
        if (bytesPerSecond > 0 && totalBytes > processedBytes)
            eta = TimeSpan.FromSeconds((totalBytes - processedBytes) / bytesPerSecond);

        return new MetricsSnapshot
        {
            TotalBytes = totalBytes,
            ProcessedBytes = processedBytes,
            ProcessedLines = processedLines,
            SuccessCount = success,
            FailedCount = failed,
            IgnoredCount = ignored,
            RetryCount = Volatile.Read(ref _retryCount),
            ElapsedTime = elapsed,
            ProgressPercent = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
            CPM = elapsed.TotalMinutes > 0 ? processedLines / elapsed.TotalMinutes : 0,
            BytesPerSecond = bytesPerSecond,
            ETA = eta
        };
    }
}

/// <summary>
/// Immutable snapshot of metrics at a point in time.
/// </summary>
public readonly record struct MetricsSnapshot
{
    public required long TotalBytes { get; init; }
    public required long ProcessedBytes { get; init; }
    public required long ProcessedLines { get; init; }
    public required long SuccessCount { get; init; }
    public required long FailedCount { get; init; }
    public required long IgnoredCount { get; init; }
    public required long RetryCount { get; init; }
    public required TimeSpan ElapsedTime { get; init; }
    public required double ProgressPercent { get; init; }
    public required double CPM { get; init; }
    public required double BytesPerSecond { get; init; }
    public required TimeSpan? ETA { get; init; }
}