using CheckerBase.Core.Proxies;
using CheckerBase.Core.Results;

namespace CheckerBase.Core.Engine;

/// <summary>
/// Interface for implementing custom checkers.
/// </summary>
/// <typeparam name="TLogEntry">The parsed log entry type.</typeparam>
/// <typeparam name="TResult">The result data type.</typeparam>
/// <typeparam name="TClient">The client type (must be disposable).</typeparam>
public interface IChecker<TLogEntry, TResult, TClient>
    where TClient : IDisposable
{
    /// <summary>
    /// Quick validation to filter out invalid lines early.
    /// Should be fast and avoid allocations.
    /// </summary>
    /// <param name="line">Raw line from input.</param>
    /// <returns>True if line should be processed further.</returns>
    bool QuickValidate(ReadOnlySpan<char> line);

    /// <summary>
    /// Parse a line into a strongly-typed log entry.
    /// Called only if QuickValidate returns true.
    /// </summary>
    /// <param name="line">Raw line from input.</param>
    /// <returns>Parsed entry, or null if parsing fails.</returns>
    TLogEntry? Parse(string line);

    /// <summary>
    /// Process a parsed entry with the provided client.
    /// </summary>
    /// <param name="entry">Parsed log entry.</param>
    /// <param name="client">Client instance (created by CreateClient).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result.</returns>
    ValueTask<ProcessResult<TResult>> ProcessAsync(
        TLogEntry entry,
        TClient client,
        CancellationToken cancellationToken);

    /// <summary>
    /// Factory to create a new client instance.
    /// Called once per line (client isolation).
    /// </summary>
    /// <param name="proxy">Proxy to use, or null if no proxies configured.</param>
    /// <returns>New client instance.</returns>
    TClient CreateClient(Proxy? proxy);

    /// <summary>
    /// Determines if an exception is transient and should be retried.
    /// Default implementation considers HttpRequestException and TaskCanceledException as transient.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <returns>True if the operation should be retried.</returns>
    bool IsTransientException(Exception exception)
        => exception is HttpRequestException or TaskCanceledException or TimeoutException or IOException;
}