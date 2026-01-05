namespace CheckerBase.Core.Proxies;

/// <summary>
/// Specifies the protocol type for a proxy connection.
/// </summary>
public enum ProxyType
{
    /// <summary>HTTP proxy (CONNECT method for HTTPS tunneling).</summary>
    Http,

    /// <summary>HTTPS proxy with TLS encryption to the proxy server.</summary>
    Https,

    /// <summary>SOCKS4 proxy protocol.</summary>
    Socks4,

    /// <summary>SOCKS5 proxy protocol with authentication support.</summary>
    Socks5
}