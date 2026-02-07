using DnsClient;

namespace CheckerBase.App.Discovery.Strategies;

/// <summary>
/// Discovers IMAP config by looking up MX records and deriving the provider domain.
/// Example: mycompany.com → MX: aspmx.l.google.com → google.com → ISPDB lookup
/// </summary>
public sealed class MxLookupStrategy : IDiscoveryStrategy
{
    private readonly HttpClient _httpClient;
    private readonly ILookupClient _dnsClient;

    public int Priority => 3;
    public string Name => "mx";

    public MxLookupStrategy(HttpClient httpClient, ILookupClient? dnsClient = null)
    {
        _httpClient = httpClient;
        _dnsClient = dnsClient ?? new LookupClient();
    }

    public async Task<IReadOnlyList<ImapServerConfig>> DiscoverAsync(string domain, CancellationToken cancellationToken)
    {
        var allResults = new List<ImapServerConfig>();

        try
        {
            // 1. Get MX records
            var mxResult = await _dnsClient.QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken);
            var mxRecords = mxResult.Answers.MxRecords()
                .OrderBy(mx => mx.Preference)
                .ToList();

            if (mxRecords.Count == 0)
                return [];

            // 2. Extract base domain from highest priority MX
            var mxHost = mxRecords[0].Exchange.Value.TrimEnd('.');
            var providerDomain = ExtractBaseDomain(mxHost);

            if (string.IsNullOrEmpty(providerDomain) || providerDomain == domain)
                return [];

            // 3. Try ISPDB lookup on provider domain
            var ispdbUrl = $"https://live.thunderbird.net/autoconfig/v1.1/{providerDomain}";
            var configs = await TryFetchAllConfigsAsync(ispdbUrl, cancellationToken);
            allResults.AddRange(configs);

            // 4. Try autoconfig URLs on provider domain
            var autoconfigUrls = new[]
            {
                $"https://autoconfig.{providerDomain}/mail/config-v1.1.xml",
                $"https://{providerDomain}/.well-known/autoconfig/mail/config-v1.1.xml"
            };

            foreach (var url in autoconfigUrls)
            {
                configs = await TryFetchAllConfigsAsync(url, cancellationToken);
                allResults.AddRange(configs);
            }
        }
        catch
        {
            // Return whatever we found so far
        }

        return allResults;
    }

    private async Task<List<ImapServerConfig>> TryFetchAllConfigsAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return [];

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return IspdbStrategy.ParseAllImapServers(xml, Name, Priority);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Extracts the base domain from an MX hostname.
    /// Example: aspmx.l.google.com → google.com
    /// </summary>
    private static string? ExtractBaseDomain(string hostname)
    {
        var parts = hostname.Split('.');

        if (parts.Length < 2)
            return null;

        // Return last two parts (handles .com, .net, .org, etc.)
        // For .co.uk style TLDs, this is imperfect but covers most cases
        return $"{parts[^2]}.{parts[^1]}";
    }
}
