namespace CheckerBase.Core.Results;

/// <summary>
/// The outcome type of processing a log entry.
/// </summary>
public enum ResultType : byte
{
    /// <summary>Processing completed successfully.</summary>
    Success,

    /// <summary>Processing failed after all retries exhausted.</summary>
    Failed,

    /// <summary>Entry was filtered out or skipped.</summary>
    Ignored,

    /// <summary>Transient failure, should retry.</summary>
    Retry
}