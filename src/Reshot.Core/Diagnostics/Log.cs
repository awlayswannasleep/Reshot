using System.Diagnostics;
using System.Text;

namespace Reshot.Core.Diagnostics;

/// <summary>
/// Minimal thread-safe file logger. reshot spends most of its life asleep, so
/// there is no logging framework and no background flush thread — every call
/// appends synchronously to <c>%AppData%\reshot\logs\reshot.log</c> and mirrors
/// to the debugger. Cheap, dependency-free, good enough for a tray utility.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _logFile;

    public enum Level { Info, Warn, Error }

    /// <summary>Must be called once at startup after the AppData dir exists.</summary>
    public static void Init()
    {
        ReshotPaths.EnsureAppDataDir();
        _logFile = Path.Combine(ReshotPaths.LogsDir, "reshot.log");
        TrimIfLarge();
        Info($"=== reshot session started (pid {Environment.ProcessId}) ===");
    }

    public static void Info(string message) => Write(Level.Info, message);
    public static void Warn(string message) => Write(Level.Warn, message);
    public static void Error(string message) => Write(Level.Error, message);

    public static void Error(string message, Exception ex) =>
        Write(Level.Error, $"{message}{Environment.NewLine}{ex}");

    private static void Write(Level level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] {message}";
        Debug.WriteLine(line);

        var file = _logFile;
        if (file is null)
            return;

        lock (Gate)
        {
            try
            {
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never crash the app. Swallow disk/IO errors.
            }
        }
    }

    /// <summary>Keeps the log from growing unbounded across many sessions (~1 MB cap).</summary>
    private static void TrimIfLarge()
    {
        try
        {
            if (_logFile is { } file && File.Exists(file) && new FileInfo(file).Length > 1_000_000)
                File.WriteAllText(file, string.Empty);
        }
        catch
        {
            // ignored
        }
    }
}
