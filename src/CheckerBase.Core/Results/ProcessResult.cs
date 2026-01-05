using System.Diagnostics.CodeAnalysis;

namespace CheckerBase.Core.Results;

/// <summary>
/// The result of processing a single log entry.
/// </summary>
/// <typeparam name="TResult">The type of the result data.</typeparam>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
    Justification = "Factory methods are the standard pattern for result types")]
public readonly struct ProcessResult<TResult>
{
    /// <summary>The outcome type.</summary>
    public ResultType Type { get; }

    /// <summary>The result data (only meaningful for Success).</summary>
    public TResult? Data { get; }

    /// <summary>Captured key-value pairs from processing.</summary>
    public IReadOnlyList<Capture> Captures { get; }

    /// <summary>Exception that caused failure (if any).</summary>
    public Exception? Error { get; }

    private ProcessResult(ResultType type, TResult? data, IReadOnlyList<Capture>? captures, Exception? error)
    {
        Type = type;
        Data = data;
        Captures = captures ?? [];
        Error = error;
    }

    /// <summary>Creates a successful result with data and captures.</summary>
    public static ProcessResult<TResult> Success(TResult data, params Capture[] captures)
        => new(ResultType.Success, data, captures, null);

    /// <summary>Creates a successful result with data and a list of captures.</summary>
    public static ProcessResult<TResult> Success(TResult data, IReadOnlyList<Capture> captures)
        => new(ResultType.Success, data, captures, null);

    /// <summary>Creates a failed result.</summary>
    public static ProcessResult<TResult> Failed(Exception? error = null)
        => new(ResultType.Failed, default, null, error);

    /// <summary>Creates an ignored/skipped result.</summary>
    public static ProcessResult<TResult> Ignored()
        => new(ResultType.Ignored, default, null, null);

    /// <summary>Creates a retry result indicating transient failure.</summary>
    public static ProcessResult<TResult> Retry(Exception? error = null)
        => new(ResultType.Retry, default, null, error);
}