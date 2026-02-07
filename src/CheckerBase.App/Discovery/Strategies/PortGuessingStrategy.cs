using MailKit.Net.Imap;
using MailKit.Security;

namespace CheckerBase.App.Discovery.Strategies;

/// <summary>
/// Fallback strategy that probes common IMAP hostnames and ports.
/// Hostnames: imap.{domain}, mail.{domain}, {domain}
/// Ports: 993 (SSL), 143 (STARTTLS)
/// </summary>
public sealed class PortGuessingStrategy : IDiscoveryStrategy
{
    private readonly TimeSpan _connectionTimeout;

    public int Priority => 4;
    public string Name => "guess";

    public PortGuessingStrategy(TimeSpan? connectionTimeout = null)
    {
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<IReadOnlyList<ImapServerConfig>> DiscoverAsync(string domain, CancellationToken cancellationToken)
    {
        var hostnames = new[]
        {
            $"imap.{domain}",
            $"mail.{domain}",
            domain
        };

        var portConfigs = new (int Port, SecureSocketOptions Options, SecurityType Security)[]
        {
            (993, SecureSocketOptions.SslOnConnect, SecurityType.Ssl),
            (143, SecureSocketOptions.StartTls, SecurityType.StartTls)
        };

        var results = new List<ImapServerConfig>();

        // Try all combinations, collect all that work
        foreach (var hostname in hostnames)
        {
            foreach (var (port, options, security) in portConfigs)
            {
                var config = await TryConnectAsync(hostname, port, options, security, cancellationToken);
                if (config != null)
                    results.Add(config);
            }
        }

        return results;
    }

    private async Task<ImapServerConfig?> TryConnectAsync(
        string hostname,
        int port,
        SecureSocketOptions options,
        SecurityType security,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(_connectionTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(hostname, port, options, linkedCts.Token);

            // Connection successful - server is valid IMAP
            await client.DisconnectAsync(true, linkedCts.Token);

            return new ImapServerConfig
            {
                Hostname = hostname,
                Port = port,
                Security = security,
                UsernameFormat = UsernameFormat.Email, // Default to full email
                Source = Name,
                Priority = Priority
            };
        }
        catch
        {
            return null;
        }
    }
}
