using System.Text.Json;

namespace CheckerBase.App.Configuration;

/// <summary>
/// Manages persistence of application settings to JSON file.
/// </summary>
public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;

    /// <summary>
    /// Creates a new settings manager with the default settings path.
    /// </summary>
    public SettingsManager() : this(GetDefaultSettingsPath())
    {
    }

    /// <summary>
    /// Creates a new settings manager with a custom settings path.
    /// </summary>
    public SettingsManager(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    /// <summary>
    /// Gets the default settings file path (~/.checkerbase/settings.json).
    /// </summary>
    public static string GetDefaultSettingsPath()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".checkerbase");

        return Path.Combine(configDir, "settings.json");
    }

    /// <summary>
    /// Loads settings from disk. Returns default settings if file doesn't exist or is corrupted.
    /// </summary>
    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);

            return settings ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupted file - return defaults
            return new AppSettings();
        }
        catch (IOException)
        {
            // File access error - return defaults
            return new AppSettings();
        }
    }

    /// <summary>
    /// Saves settings to disk.
    /// </summary>
    public async Task SaveAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Write to temp file first, then move (atomic operation)
        var tempPath = _settingsPath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        }

        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    /// <summary>
    /// Saves a checkpoint (resume position) to settings.
    /// </summary>
    public async Task SaveCheckpointAsync(AppSettings settings, long bytePosition)
    {
        settings.ResumeBytePosition = bytePosition;
        settings.ResumeInputPath = settings.InputFilePath;
        settings.ResumeTimestamp = DateTime.UtcNow;

        await SaveAsync(settings);
    }

    /// <summary>
    /// Clears the checkpoint from settings.
    /// </summary>
    public async Task ClearCheckpointAsync(AppSettings settings)
    {
        settings.ClearResumeState();
        await SaveAsync(settings);
    }

    /// <summary>
    /// Checks if the settings file exists.
    /// </summary>
    public bool SettingsExist => File.Exists(_settingsPath);
}
