using System.Runtime.CompilerServices;
using CheckerBase.Core.Engine;
using CheckerBase.Core.Proxies;
using CheckerBase.Core.Results;

namespace CheckerBase.App;

/// <summary>
/// Example log entry representing an user:password combo.
/// </summary>
public record ComboEntry(string User, string Password);

/// <summary>
/// Example result data from processing.
/// </summary>
public sealed record CheckResult(string Plan, DateTime? ExpiryDate);

/// <summary>
/// Example checker implementation demonstrating the IChecker interface.
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

    public async ValueTask<ProcessResult<CheckResult>> ProcessAsync(
        ComboEntry entry,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        // TODO: var (isValid, plan) = await client.CheckAsync(entry.Email, entry.Password, cancellationToken);

        var isValid = Random.Shared.NextDouble() < 0.1;
        if (!isValid)
            return ProcessResult<CheckResult>.Failed();

        var result = new CheckResult("Premium", DateTime.UtcNow.AddYears(1));

        return ProcessResult<CheckResult>.Success(
            result,
            new Capture("Plan", result.Plan),
            new Capture("Expiry", result.ExpiryDate?.ToString("yyyy-MM-dd") ?? "N/A"));
    }

    public HttpClient CreateClient(Proxy? proxy) => new();
}