namespace CheckerBase.App.Discovery.Strategies;

/// <summary>
/// Discovers IMAP config from ISP autoconfig URLs.
/// Tries:
///   1. https://autoconfig.{domain}/mail/config-v1.1.xml
///   2. https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml
/// </summary>
public sealed class AutoconfigStrategy : IDiscoveryStrategy
{
    private readonly HttpClient _httpClient;

    public int Priority => 2;
    public string Name => "autoconfig";

    public AutoconfigStrategy(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ImapServerConfig>> DiscoverAsync(string domain, CancellationToken cancellationToken)
    {
        var urls = new[]
        {
            $"https://autoconfig.{domain}/mail/config-v1.1.xml",
            $"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml"
        };

        var allResults = new List<ImapServerConfig>();

        foreach (var url in urls)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    continue;

                var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                var configs = IspdbStrategy.ParseAllImapServers(xml, Name, Priority);
                allResults.AddRange(configs);
            }
            catch (HttpRequestException)
            {
                // Try next URL
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        return allResults;
    }
}
