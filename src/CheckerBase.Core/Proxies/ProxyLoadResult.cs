namespace CheckerBase.Core.Proxies;

/// <summary>
/// Result of loading proxies from a source, including diagnostics.
/// </summary>
/// <param name="Rotator">The proxy rotator with successfully parsed proxies.</param>
/// <param name="SuccessCount">Number of successfully parsed proxies.</param>
/// <param name="FailedCount">Number of lines that failed to parse.</param>
/// <param name="FailedLines">The lines that failed to parse.</param>
public readonly record struct ProxyLoadResult(
    ProxyRotator Rotator,
    int SuccessCount,
    int FailedCount,
    IReadOnlyList<string> FailedLines);