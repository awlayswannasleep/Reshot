using System.Text.Json;
using System.Text.Json.Serialization;
using Reshot.Core.Diagnostics;

namespace Reshot.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as pretty-printed camelCase JSON in
/// <c>%AppData%\reshot\settings.json</c>. On a missing or corrupt file it falls
/// back to defaults and (re)writes a clean file, so the app always starts.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _filePath;

    public SettingsService() : this(ReshotPaths.SettingsFile) { }

    /// <summary>Overload for tests: point at an arbitrary settings file.</summary>
    public SettingsService(string filePath) => _filePath = filePath;

    public AppSettings Current { get; private set; } = new();

    /// <summary>
    /// Loads settings from disk, or creates the defaults file on first run.
    /// Resolves empty output-folder paths to their per-user defaults.
    /// </summary>
    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
                Current = ResolveDefaults(settings);
                Save();
                Log.Info($"Settings: created defaults at {_filePath}");
                return Current;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Settings: failed to read {_filePath}, using defaults", ex);
            settings = new AppSettings();
        }

        Current = ResolveDefaults(settings);
        return Current;
    }

    /// <summary>Replaces the current settings (e.g. from the Settings window) and saves.</summary>
    public void Update(AppSettings settings)
    {
        Current = ResolveDefaults(settings);
        Save();
    }

    /// <summary>Persists <see cref="Current"/> to disk (creating the folder if needed).</summary>
    public void Save()
    {
        try
        {
            ReshotPaths.EnsureAppDataDir();
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Settings: failed to write {_filePath}", ex);
        }
    }

    /// <summary>Fills machine-specific defaults that can't be baked into the POCO.</summary>
    private static AppSettings ResolveDefaults(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Paths.Screenshots))
            settings.Paths.Screenshots = ReshotPaths.DefaultScreenshotsDir;
        if (string.IsNullOrWhiteSpace(settings.Paths.Videos))
            settings.Paths.Videos = ReshotPaths.DefaultVideosDir;
        if (string.IsNullOrWhiteSpace(settings.Paths.Records))
            settings.Paths.Records = ReshotPaths.DefaultRecordsDir;
        return settings;
    }
}
