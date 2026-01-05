using CheckerBase.Core.Collections;

namespace CheckerBase.Core.Proxies;

/// <summary>
/// Thread-safe round-robin proxy rotator.
/// </summary>
public sealed class ProxyRotator
{
    private readonly RoundRobinRotator<Proxy>? _rotator;

    /// <summary>
    /// Gets the number of proxies in the rotator.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets whether this rotator has any proxies.
    /// </summary>
    public bool HasProxies => _rotator is not null;

    /// <summary>
    /// Creates a new proxy rotator with the given proxies.
    /// </summary>
    /// <param name="proxies">The proxies to rotate through.</param>
    public ProxyRotator(Proxy[] proxies)
    {
        ArgumentNullException.ThrowIfNull(proxies);
        Count = proxies.Length;
        _rotator = proxies.Length > 0 ? new RoundRobinRotator<Proxy>(proxies) : null;
    }

    /// <summary>
    /// Gets the next proxy using round-robin.
    /// </summary>
    /// <returns>The next proxy, or null if no proxies are configured.</returns>
    public Proxy? Next() => _rotator?.Next();

    /// <summary>
    /// Creates an empty proxy rotator.
    /// </summary>
    public static ProxyRotator Empty { get; } = new([]);
}