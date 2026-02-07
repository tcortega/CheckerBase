using CheckerBase.App.Discovery.Strategies;
using CheckerBase.App.Registry;
using DnsClient;

namespace CheckerBase.App.Discovery;

/// <summary>
/// Orchestrates IMAP server discovery using multiple strategies.
/// Aggregates candidates from all strategies, handles caching and deduplication.
/// </summary>
public sealed class ServerDiscoveryService : IAsyncDisposable
{
    private readonly ServerRegistry _registry;
    private readonly PendingDiscoveryTracker _tracker;
    private readonly IDiscoveryStrategy[] _strategies;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheTtl;
    private readonly TimeSpan _strategyTimeout;

    public ServerDiscoveryService(
        ServerRegistry? registry = null,
        TimeSpan? cacheTtl = null,
        TimeSpan? strategyTimeout = null)
    {
        _registry = registry ?? new ServerRegistry();
        _tracker = new();
        _cacheTtl = cacheTtl ?? TimeSpan.FromDays(30);
        _strategyTimeout = strategyTimeout ?? TimeSpan.FromSeconds(10);

        _httpClient = new()
        {
            Timeout = _strategyTimeout
        };

        // Initialize strategies in priority order
        _strategies =
        [
            new IspdbStrategy(_httpClient),
            new AutoconfigStrategy(_httpClient),
            new MxLookupStrategy(_httpClient, new LookupClient()),
            new PortGuessingStrategy(_strategyTimeout)
        ];

        Array.Sort(_strategies, (a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// Gets candidates for a domain, using caching and deduplication.
    /// Returns verified config if available, otherwise aggregates from all strategies.
    /// </summary>
    public async Task<IReadOnlyList<ImapServerConfig>> GetCandidatesAsync(string domain, CancellationToken cancellationToken)
    {
        var normalizedDomain = domain.ToLowerInvariant();

        // 1. Check for verified config first (fast path)
        var verified = await _registry.GetVerifiedAsync(normalizedDomain);
        if (verified != null)
            return [verified];

        // 2. Check for cached candidates
        var cached = await _registry.GetCandidatesAsync(normalizedDomain);
        if (cached.Count > 0)
            return cached;

        // 3. Check/register pending discovery (deduplication)
        var (isFirst, awaiter) = _tracker.GetOrCreate(normalizedDomain);
        if (!isFirst)
        {
            // Another task is already discovering this domain - wait for it
            return await awaiter;
        }

        try
        {
            // 4. Run ALL strategies and aggregate results
            var allCandidates = await RunAllStrategiesAsync(normalizedDomain, cancellationToken);

            // 5. Deduplicate by (hostname, port), keep lowest priority
            var deduplicated = allCandidates
                .GroupBy(c => (c.Hostname.ToLowerInvariant(), c.Port))
                .Select(g => g.OrderBy(c => c.Priority).First())
                .OrderBy(c => c.Priority)
                .ToList();

            // 6. Cache candidates
            if (deduplicated.Count > 0)
                await _registry.SetCandidatesAsync(normalizedDomain, deduplicated, _cacheTtl);

            _tracker.Complete(normalizedDomain, deduplicated);
            return deduplicated;
        }
        catch (OperationCanceledException)
        {
            _tracker.Cancel(normalizedDomain);
            throw;
        }
        catch (Exception ex)
        {
            _tracker.Fail(normalizedDomain, ex);
            throw;
        }
    }

    /// <summary>
    /// Marks a configuration as verified (auth succeeded).
    /// Called by ImapChecker when authentication succeeds.
    /// </summary>
    public Task MarkVerifiedAsync(string domain, ImapServerConfig config)
        => _registry.SetVerifiedAsync(domain.ToLowerInvariant(), config, _cacheTtl);

    private async Task<List<ImapServerConfig>> RunAllStrategiesAsync(string domain, CancellationToken cancellationToken)
    {
        var allCandidates = new List<ImapServerConfig>();

        foreach (var strategy in _strategies)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(_strategyTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);

                var results = await strategy.DiscoverAsync(domain, linkedCts.Token);
                allCandidates.AddRange(results);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout - continue to next strategy
            }
            catch (Exception)
            {
                // Strategy failed - continue to next
            }
        }

        return allCandidates;
    }

    /// <summary>
    /// Cleans up expired cache entries.
    /// </summary>
    public Task CleanExpiredCacheAsync() => _registry.CleanExpiredAsync();

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        await _registry.DisposeAsync();
    }
}
