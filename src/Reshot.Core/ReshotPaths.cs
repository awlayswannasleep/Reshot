namespace Reshot.Core;

/// <summary>
/// Central resolver for every path reshot writes to under the user profile.
/// Everything lives under <c>%AppData%\reshot\</c> (see SPEC §13).
/// </summary>
public static class ReshotPaths
{
    /// <summary><c>%AppData%\reshot</c> — root of all persisted state.</summary>
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "reshot");

    /// <summary><c>%AppData%\reshot\settings.json</c>.</summary>
    public static string SettingsFile { get; } = Path.Combine(AppDataDir, "settings.json");

    /// <summary><c>%AppData%\reshot\logs</c>.</summary>
    public static string LogsDir { get; } = Path.Combine(AppDataDir, "logs");

    /// <summary>Default screenshot output folder: <c>Pictures\reshot</c>.</summary>
    public static string DefaultScreenshotsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "reshot");

    /// <summary>Default video output folder: <c>Videos\reshot</c>.</summary>
    public static string DefaultVideosDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "reshot");

    /// <summary>Default audio-recording output folder: <c>Music\reshot</c>.</summary>
    public static string DefaultRecordsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "reshot");

    /// <summary>Creates the AppData + logs directories if they do not exist yet.</summary>
    public static void EnsureAppDataDir()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(LogsDir);
    }
}
