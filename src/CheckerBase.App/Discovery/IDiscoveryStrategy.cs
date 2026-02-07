namespace CheckerBase.App.Discovery;

/// <summary>
/// Strategy interface for discovering IMAP server configuration.
/// </summary>
public interface IDiscoveryStrategy
{
    /// <summary>
    /// Priority of this strategy (lower = higher priority).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Name of the discovery source for logging.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Discovers all possible IMAP server configurations for the given domain.
    /// </summary>
    /// <param name="domain">The email domain to discover.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of discovered configs (may be empty if none found).</returns>
    Task<IReadOnlyList<ImapServerConfig>> DiscoverAsync(string domain, CancellationToken cancellationToken);
}
