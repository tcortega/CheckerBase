using System.Xml.Linq;

namespace CheckerBase.App.Discovery.Strategies;

/// <summary>
/// Discovers IMAP config from Mozilla ISPDB (ISP Database).
/// URL: https://live.thunderbird.net/autoconfig/v1.1/{domain}
/// </summary>
public sealed class IspdbStrategy : IDiscoveryStrategy
{
    private readonly HttpClient _httpClient;

    public int Priority => 1;
    public string Name => "ispdb";

    public IspdbStrategy(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ImapServerConfig>> DiscoverAsync(string domain, CancellationToken cancellationToken)
    {
        var url = $"https://live.thunderbird.net/autoconfig/v1.1/{domain}";

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return [];

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseAllImapServers(xml, Name, Priority);
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (TaskCanceledException)
        {
            return [];
        }
    }

    /// <summary>
    /// Parses all IMAP servers from Mozilla autoconfig XML format.
    /// </summary>
    internal static List<ImapServerConfig> ParseAllImapServers(string xml, string source, int priority)
    {
        var results = new List<ImapServerConfig>();

        try
        {
            var doc = XDocument.Parse(xml);

            // Get ALL incomingServer elements with type="imap"
            foreach (var server in doc.Descendants("incomingServer")
                .Where(e => e.Attribute("type")?.Value == "imap"))
            {
                var config = ParseServerElement(server, source, priority);
                if (config != null)
                    results.Add(config);
            }
        }
        catch
        {
            // XML parsing failed, return empty list
        }

        return results;
    }

    private static ImapServerConfig? ParseServerElement(XElement server, string source, int priority)
    {
        var hostname = server.Element("hostname")?.Value;
        var portStr = server.Element("port")?.Value;
        var socketType = server.Element("socketType")?.Value;
        var username = server.Element("username")?.Value;

        if (string.IsNullOrEmpty(hostname) || !int.TryParse(portStr, out var port))
            return null;

        var security = socketType?.ToUpperInvariant() switch
        {
            "SSL" => SecurityType.Ssl,
            "STARTTLS" => SecurityType.StartTls,
            _ => SecurityType.None
        };

        var usernameFormat = username switch
        {
            "%EMAILLOCALPART%" => UsernameFormat.LocalPart,
            _ => UsernameFormat.Email
        };

        return new ImapServerConfig
        {
            Hostname = hostname,
            Port = port,
            Security = security,
            UsernameFormat = usernameFormat,
            Source = source,
            Priority = priority
        };
    }
}
