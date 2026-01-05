namespace CheckerBase.Core.Proxies;

public sealed class Proxy
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public ProxyType Type { get; init; } = ProxyType.Http;

    public static Proxy Parse(string input, ProxyType defaultType = ProxyType.Http)
    {
        if (!TryParse(input, out var proxy, defaultType))
            throw new FormatException($"Invalid proxy format: {input}");

        return proxy;
    }

    public static bool TryParse(string? input, out Proxy proxy, ProxyType defaultType = ProxyType.Http)
    {
        proxy = null!;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var type = defaultType;

        if (input.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
        {
            type = ProxyType.Socks5;
            input = input[9..];
        }
        else if (input.StartsWith("socks4://", StringComparison.OrdinalIgnoreCase))
        {
            type = ProxyType.Socks4;
            input = input[9..];
        }
        else if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            type = ProxyType.Https;
            input = input[8..];
        }
        else if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            type = ProxyType.Http;
            input = input[7..];
        }

        string? user = null, pass = null;
        if (input.Contains('@'))
        {
            var atIdx = input.IndexOf('@');
            var authPart = input[..atIdx];
            input = input[(atIdx + 1)..];

            var colonIdx = authPart.IndexOf(':');
            if (colonIdx > 0)
            {
                user = authPart[..colonIdx];
                pass = authPart[(colonIdx + 1)..];
            }
        }

        var parts = input.Split(':');
        if (parts.Length < 2)
            return false;

        var host = parts[0];
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (!int.TryParse(parts[1], out var port) || port is < 1 or > 65535)
            return false;

        // Format: host:port:user:pass
        if (parts.Length >= 4 && user is null)
        {
            user = parts[2];
            pass = parts[3];
        }

        proxy = new Proxy
        {
            Host = host,
            Port = port,
            Username = user,
            Password = pass,
            Type = type
        };

        return true;
    }
}