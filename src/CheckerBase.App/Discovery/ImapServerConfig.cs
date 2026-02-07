namespace CheckerBase.App.Discovery;

/// <summary>
/// Discovered IMAP server configuration.
/// </summary>
public sealed record ImapServerConfig
{
    public required string Hostname { get; init; }
    public required int Port { get; init; }
    public required SecurityType Security { get; init; }
    public required UsernameFormat UsernameFormat { get; init; }

    /// <summary>
    /// Discovery source: "ispdb", "autoconfig", "mx", "guess"
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Priority for trying this config. Lower = try first.
    /// ispdb=1, autoconfig=2, mx=3, guess=4
    /// </summary>
    public int Priority { get; init; }
}

/// <summary>
/// Connection security type.
/// </summary>
public enum SecurityType
{
    /// <summary>SSL/TLS on connect (port 993).</summary>
    Ssl,

    /// <summary>STARTTLS upgrade (port 143).</summary>
    StartTls,

    /// <summary>No encryption (not recommended).</summary>
    None
}

/// <summary>
/// Username format for authentication.
/// </summary>
public enum UsernameFormat
{
    /// <summary>Full email address (user@domain.com).</summary>
    Email,

    /// <summary>Local part only (user).</summary>
    LocalPart
}