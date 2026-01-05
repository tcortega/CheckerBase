namespace CheckerBase.Core.Proxies;

/// <summary>
/// Utility for loading proxies from various sources.
/// </summary>
public static class ProxyLoader
{
    /// <summary>
    /// Loads proxies from a file asynchronously, one proxy per line.
    /// Invalid lines are collected in the result for diagnostics.
    /// </summary>
    /// <param name="path">Path to the proxy file.</param>
    /// <param name="defaultType">Default proxy type if not specified in the line.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Load result with the rotator and parsing diagnostics.</returns>
    public static async Task<ProxyLoadResult> LoadFromFileAsync(
        string path,
        ProxyType defaultType = ProxyType.Http,
        CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        return LoadFromLines(lines, defaultType);
    }

    /// <summary>
    /// Loads proxies from a collection of lines.
    /// Invalid lines are collected in the result for diagnostics.
    /// </summary>
    /// <param name="lines">The lines to parse.</param>
    /// <param name="defaultType">Default proxy type if not specified in the line.</param>
    /// <returns>Load result with the rotator and parsing diagnostics.</returns>
    public static ProxyLoadResult LoadFromLines(IEnumerable<string> lines, ProxyType defaultType = ProxyType.Http)
    {
        var proxies = new List<Proxy>();
        var failedLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (Proxy.TryParse(trimmed, out var proxy, defaultType))
            {
                proxies.Add(proxy);
            }
            else
            {
                failedLines.Add(trimmed);
            }
        }

        var rotator = new ProxyRotator([.. proxies]);

        return new ProxyLoadResult(
            Rotator: rotator,
            SuccessCount: proxies.Count,
            FailedCount: failedLines.Count,
            FailedLines: failedLines);
    }
}