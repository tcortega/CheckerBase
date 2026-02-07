using System.Collections.Concurrent;
using CheckerBase.App.Discovery;

namespace CheckerBase.App.Registry;

/// <summary>
/// Tracks in-flight discovery operations to prevent duplicate lookups
/// for the same domain when multiple accounts share a domain.
/// </summary>
public sealed class PendingDiscoveryTracker
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<ImapServerConfig>>> _pending = new();

    /// <summary>
    /// Gets or creates a pending discovery task for the specified domain.
    /// </summary>
    /// <param name="domain">The email domain to discover.</param>
    /// <returns>
    /// A tuple where IsFirst indicates if this caller should perform the discovery,
    /// and Awaiter is the task to await for the result.
    /// </returns>
    public (bool IsFirst, Task<IReadOnlyList<ImapServerConfig>> Awaiter) GetOrCreate(string domain)
    {
        while (true)
        {
            if (_pending.TryGetValue(domain, out var existingTcs))
            {
                return (false, existingTcs.Task);
            }

            var myTcs = new TaskCompletionSource<IReadOnlyList<ImapServerConfig>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            if (_pending.TryAdd(domain, myTcs))
            {
                return (true, myTcs.Task);
            }
        }
    }

    /// <summary>
    /// Completes a pending discovery with a successful result.
    /// </summary>
    public void Complete(string domain, IReadOnlyList<ImapServerConfig> result)
    {
        if (_pending.TryRemove(domain, out var tcs))
        {
            tcs.TrySetResult(result);
        }
    }

    /// <summary>
    /// Completes a pending discovery with an exception.
    /// </summary>
    public void Fail(string domain, Exception ex)
    {
        if (_pending.TryRemove(domain, out var tcs))
        {
            tcs.TrySetException(ex);
        }
    }

    /// <summary>
    /// Cancels a pending discovery.
    /// </summary>
    public void Cancel(string domain)
    {
        if (_pending.TryRemove(domain, out var tcs))
        {
            tcs.TrySetCanceled();
        }
    }
}