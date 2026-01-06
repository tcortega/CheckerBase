using System.Net;
using System.Runtime.CompilerServices;
using CheckerBase.Core.Engine;
using CheckerBase.Core.Proxies;
using CheckerBase.Core.Results;

namespace CheckerBase.App;

/// <summary>
/// Example log entry representing a user:password combo.
/// Modify this record to match your input format.
/// </summary>
public record ComboEntry(string User, string Password);

/// <summary>
/// Example result data from processing.
/// Modify this record to match your expected results.
/// </summary>
public sealed record CheckResult(string Plan, DateTime? ExpiryDate);

/// <summary>
/// Example checker implementation demonstrating the IChecker interface.
///
/// TO CUSTOMIZE:
/// 1. Modify ComboEntry to match your input format
/// 2. Modify CheckResult to match your result data
/// 3. Update QuickValidate and Parse to handle your input format
/// 4. Implement your actual checking logic in ProcessAsync
/// </summary>
public sealed class ExampleChecker : IChecker<ComboEntry, CheckResult, HttpClient>
{
    /// <summary>
    /// Zero-allocation quick validation.
    /// Handles: user:pass, email:pass, user:pass | capture..., etc.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool QuickValidate(ReadOnlySpan<char> line)
    {
        if (line.IsEmpty)
            return false;

        var colonIdx = line.IndexOf(':');
        if (colonIdx <= 0)
            return false;

        var afterColon = line[(colonIdx + 1)..];
        var pipeIdx = afterColon.IndexOf('|');
        var password = pipeIdx >= 0 ? afterColon[..pipeIdx] : afterColon;

        password = password.Trim();
        return password.Length > 0;
    }

    /// <summary>
    /// Full parse - allocates strings.
    /// </summary>
    public ComboEntry? Parse(string line)
    {
        var span = line.AsSpan();

        var colonIdx = span.IndexOf(':');
        if (colonIdx <= 0)
            return null;

        var user = span[..colonIdx].Trim();
        var afterColon = span[(colonIdx + 1)..];

        var pipeIdx = afterColon.IndexOf('|');
        var password = pipeIdx >= 0 ? afterColon[..pipeIdx] : afterColon;
        password = password.Trim();

        if (user.IsEmpty || password.IsEmpty)
            return null;

        return new(user.ToString(), password.ToString());
    }

    /// <summary>
    /// Main processing logic.
    ///
    /// TODO: Replace this with your actual API call or validation logic.
    /// </summary>
    public async ValueTask<ProcessResult<CheckResult>> ProcessAsync(
        ComboEntry entry,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        // ============================================================
        // EXAMPLE IMPLEMENTATION - Replace with your actual logic
        // ============================================================
        //
        // Example API call:
        // var response = await client.PostAsync("https://api.example.com/login",
        //     new FormUrlEncodedContent(new Dictionary<string, string>
        //     {
        //         ["username"] = entry.User,
        //         ["password"] = entry.Password
        //     }), cancellationToken);
        //
        // if (!response.IsSuccessStatusCode)
        //     return ProcessResult<CheckResult>.Failed();
        //
        // var json = await response.Content.ReadAsStringAsync(cancellationToken);
        // var data = JsonSerializer.Deserialize<ApiResponse>(json);
        //
        // return ProcessResult<CheckResult>.Success(
        //     new CheckResult(data.Plan, data.ExpiryDate),
        //     new Capture("Plan", data.Plan));
        // ============================================================

        // Simulate network delay
        await Task.Delay(Random.Shared.Next(50, 150), cancellationToken);

        // Simulate 10% success rate
        var isValid = Random.Shared.NextDouble() < 0.1;
        if (!isValid)
            return ProcessResult<CheckResult>.Failed();

        var result = new CheckResult("Premium", DateTime.UtcNow.AddYears(1));

        return ProcessResult<CheckResult>.Success(
            result,
            new Capture("Plan", result.Plan),
            new Capture("Expiry", result.ExpiryDate?.ToString("yyyy-MM-dd") ?? "N/A"));
    }

    /// <summary>
    /// Creates an HttpClient configured with the provided proxy.
    /// </summary>
    public HttpClient CreateClient(Proxy? proxy)
    {
        var handler = new HttpClientHandler
        {
            // Disable automatic redirects if you need to handle them manually
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,

            // Set reasonable timeouts
            MaxConnectionsPerServer = 10
        };

        if (proxy != null)
        {
            // Determine proxy URL based on type
            var proxyScheme = proxy.Type switch
            {
                ProxyType.Http => "http",
                ProxyType.Https => "https",
                ProxyType.Socks4 => "socks4",
                ProxyType.Socks5 => "socks5",
                _ => "http"
            };

            var proxyUri = new Uri($"{proxyScheme}://{proxy.Host}:{proxy.Port}");

            handler.Proxy = new WebProxy(proxyUri)
            {
                // Set credentials if provided
                Credentials = !string.IsNullOrEmpty(proxy.Username)
                    ? new NetworkCredential(proxy.Username, proxy.Password)
                    : null
            };

            handler.UseProxy = true;
        }
        else
        {
            handler.UseProxy = false;
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Set default headers if needed
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        return client;
    }

    /// <summary>
    /// Determines if an exception is transient and should trigger a retry.
    /// Override this to customize retry behavior.
    /// </summary>
    public bool IsTransientException(Exception exception)
    {
        return exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException
            or IOException
            or WebException;
    }
}
