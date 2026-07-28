using System.Diagnostics;
using Microsoft.Win32;
using Reshot.Core.Diagnostics;

namespace Reshot.App.Platform;

/// <summary>
/// Manages the Windows "run at logon" entry under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Per-user, no
/// elevation required. The command is quoted so paths with spaces survive.
/// </summary>
public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "reshot";

    /// <summary>Full path to the running executable (the .exe host, not the .dll).</summary>
    private static string ExecutablePath =>
        Process.GetCurrentProcess().MainModule?.FileName
        ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot determine executable path.");

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex)
        {
            Log.Error("Autostart: failed to read Run key", ex);
            return false;
        }
    }

    public static void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, $"\"{ExecutablePath}\"");
            Log.Info("Autostart: enabled.");
        }
        catch (Exception ex)
        {
            Log.Error("Autostart: failed to enable", ex);
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Autostart: disabled.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Autostart: failed to disable", ex);
        }
    }

    /// <summary>Aligns the registry entry with the desired state from settings.</summary>
    public static void Apply(bool enabled)
    {
        if (enabled)
            Enable();
        else
            Disable();
    }
}
