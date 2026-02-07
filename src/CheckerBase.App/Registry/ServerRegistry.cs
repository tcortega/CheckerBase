using CheckerBase.App.Discovery;
using Microsoft.Data.Sqlite;

namespace CheckerBase.App.Registry;

/// <summary>
/// Persistent SQLite-backed registry for discovered IMAP server configurations.
/// Uses two tables:
/// - verified_configs: Confirmed working configs (one per domain, updated on successful auth)
/// - server_candidates: All discovered candidates (multiple per domain)
/// </summary>
public sealed class ServerRegistry : IAsyncDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private bool _initialized;

    public ServerRegistry(string? databasePath = null)
    {
        var path = databasePath ?? GetDefaultDatabasePath();
        _connectionString = $"Data Source={path};Mode=ReadWriteCreate;Cache=Shared";
    }

    private static string GetDefaultDatabasePath()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".checkerbase");
        Directory.CreateDirectory(configDir);
        return Path.Combine(configDir, "server_registry.db");
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync();

        // Enable WAL mode for better concurrent performance
        await using var walCmd = _connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        await walCmd.ExecuteNonQueryAsync();

        // Create tables if not exist
        await using var createCmd = _connection.CreateCommand();
        createCmd.CommandText = """
            -- Verified working configs (one per domain, updated on successful auth)
            CREATE TABLE IF NOT EXISTS verified_configs (
                domain TEXT PRIMARY KEY,
                hostname TEXT NOT NULL,
                port INTEGER NOT NULL,
                security TEXT NOT NULL,
                username_format TEXT NOT NULL,
                source TEXT NOT NULL,
                verified_at TEXT NOT NULL,
                expires_at TEXT NOT NULL
            );

            -- All discovered candidates (multiple per domain)
            CREATE TABLE IF NOT EXISTS server_candidates (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                domain TEXT NOT NULL,
                hostname TEXT NOT NULL,
                port INTEGER NOT NULL,
                security TEXT NOT NULL,
                username_format TEXT NOT NULL,
                source TEXT NOT NULL,
                priority INTEGER NOT NULL,
                discovered_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                UNIQUE(domain, hostname, port)
            );
            CREATE INDEX IF NOT EXISTS idx_candidates_domain ON server_candidates(domain);
            CREATE INDEX IF NOT EXISTS idx_candidates_expires ON server_candidates(expires_at);
            CREATE INDEX IF NOT EXISTS idx_verified_expires ON verified_configs(expires_at);
            """;
        await createCmd.ExecuteNonQueryAsync();

        _initialized = true;
    }

    /// <summary>
    /// Gets the verified working configuration for a domain (fast path).
    /// </summary>
    public async Task<ImapServerConfig?> GetVerifiedAsync(string domain)
    {
        await EnsureInitializedAsync();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT hostname, port, security, username_format, source
            FROM verified_configs
            WHERE domain = $domain AND expires_at > $now
            """;
        cmd.Parameters.AddWithValue("$domain", domain.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new ImapServerConfig
        {
            Hostname = reader.GetString(0),
            Port = reader.GetInt32(1),
            Security = Enum.Parse<SecurityType>(reader.GetString(2)),
            UsernameFormat = Enum.Parse<UsernameFormat>(reader.GetString(3)),
            Source = reader.GetString(4),
            Priority = 0 // Verified configs get highest priority
        };
    }

    /// <summary>
    /// Marks a configuration as verified (auth succeeded).
    /// </summary>
    public async Task SetVerifiedAsync(string domain, ImapServerConfig config, TimeSpan ttl)
    {
        await EnsureInitializedAsync();

        var now = DateTime.UtcNow;
        var expiresAt = now.Add(ttl);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO verified_configs
            (domain, hostname, port, security, username_format, source, verified_at, expires_at)
            VALUES ($domain, $hostname, $port, $security, $username_format, $source, $verified_at, $expires_at)
            """;
        cmd.Parameters.AddWithValue("$domain", domain.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$hostname", config.Hostname);
        cmd.Parameters.AddWithValue("$port", config.Port);
        cmd.Parameters.AddWithValue("$security", config.Security.ToString());
        cmd.Parameters.AddWithValue("$username_format", config.UsernameFormat.ToString());
        cmd.Parameters.AddWithValue("$source", config.Source);
        cmd.Parameters.AddWithValue("$verified_at", now.ToString("O"));
        cmd.Parameters.AddWithValue("$expires_at", expiresAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Gets all cached candidates for a domain, sorted by priority.
    /// </summary>
    public async Task<IReadOnlyList<ImapServerConfig>> GetCandidatesAsync(string domain)
    {
        await EnsureInitializedAsync();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT hostname, port, security, username_format, source, priority
            FROM server_candidates
            WHERE domain = $domain AND expires_at > $now
            ORDER BY priority ASC
            """;
        cmd.Parameters.AddWithValue("$domain", domain.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        var results = new List<ImapServerConfig>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new ImapServerConfig
            {
                Hostname = reader.GetString(0),
                Port = reader.GetInt32(1),
                Security = Enum.Parse<SecurityType>(reader.GetString(2)),
                UsernameFormat = Enum.Parse<UsernameFormat>(reader.GetString(3)),
                Source = reader.GetString(4),
                Priority = reader.GetInt32(5)
            });
        }

        return results;
    }

    /// <summary>
    /// Stores all discovered candidates for a domain.
    /// </summary>
    public async Task SetCandidatesAsync(string domain, IReadOnlyList<ImapServerConfig> candidates, TimeSpan ttl)
    {
        await EnsureInitializedAsync();

        var normalizedDomain = domain.ToLowerInvariant();
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(ttl);

        // Delete existing candidates for this domain first
        await using var deleteCmd = _connection!.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM server_candidates WHERE domain = $domain";
        deleteCmd.Parameters.AddWithValue("$domain", normalizedDomain);
        await deleteCmd.ExecuteNonQueryAsync();

        // Insert new candidates
        foreach (var config in candidates)
        {
            await using var insertCmd = _connection.CreateCommand();
            insertCmd.CommandText = """
                INSERT OR IGNORE INTO server_candidates
                (domain, hostname, port, security, username_format, source, priority, discovered_at, expires_at)
                VALUES ($domain, $hostname, $port, $security, $username_format, $source, $priority, $discovered_at, $expires_at)
                """;
            insertCmd.Parameters.AddWithValue("$domain", normalizedDomain);
            insertCmd.Parameters.AddWithValue("$hostname", config.Hostname);
            insertCmd.Parameters.AddWithValue("$port", config.Port);
            insertCmd.Parameters.AddWithValue("$security", config.Security.ToString());
            insertCmd.Parameters.AddWithValue("$username_format", config.UsernameFormat.ToString());
            insertCmd.Parameters.AddWithValue("$source", config.Source);
            insertCmd.Parameters.AddWithValue("$priority", config.Priority);
            insertCmd.Parameters.AddWithValue("$discovered_at", now.ToString("O"));
            insertCmd.Parameters.AddWithValue("$expires_at", expiresAt.ToString("O"));

            await insertCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Checks if there are any non-expired candidates for a domain.
    /// </summary>
    public async Task<bool> HasCandidatesAsync(string domain)
    {
        await EnsureInitializedAsync();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM server_candidates
            WHERE domain = $domain AND expires_at > $now
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$domain", domain.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        return await cmd.ExecuteScalarAsync() != null;
    }

    /// <summary>
    /// Removes all expired entries from both tables.
    /// </summary>
    public async Task CleanExpiredAsync()
    {
        await EnsureInitializedAsync();

        var now = DateTime.UtcNow.ToString("O");

        await using var cmd1 = _connection!.CreateCommand();
        cmd1.CommandText = "DELETE FROM verified_configs WHERE expires_at <= $now";
        cmd1.Parameters.AddWithValue("$now", now);
        await cmd1.ExecuteNonQueryAsync();

        await using var cmd2 = _connection.CreateCommand();
        cmd2.CommandText = "DELETE FROM server_candidates WHERE expires_at <= $now";
        cmd2.Parameters.AddWithValue("$now", now);
        await cmd2.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
