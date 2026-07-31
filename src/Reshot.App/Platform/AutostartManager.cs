using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using Reshot.Core.Diagnostics;

namespace Reshot.App.Platform;

/// <summary>
/// Manages "run at logon", in one of two lanes.
///
/// <b>Plain</b> is the <c>HKCU\...\Run</c> value: per-user, no elevation, nothing to
/// approve. <b>Elevated</b> is a scheduled task registered with
/// <c>RunLevel=HighestAvailable</c>, which is the only way Windows will start a program
/// with administrator rights at logon <i>without</i> a UAC prompt every single time — the
/// consent is given once, when the task is created, and the scheduler carries it from then
/// on. Reshot needs that lane because it cannot bring its overlay over an application that
/// is itself running elevated (ARCHITECTURE §6).
///
/// The two lanes are mutually exclusive: running both would start Reshot twice, and the
/// single-instance guard would kill one of them for no reason.
/// </summary>
public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "reshot";

    /// <summary>Deliberately plain and top-level, so it is easy to find and delete by hand.</summary>
    private const string TaskName = "Reshot";

    /// <summary>Full path to the running executable (the .exe host, not the .dll).</summary>
    private static string ExecutablePath =>
        Process.GetCurrentProcess().MainModule?.FileName
        ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot determine executable path.");

    /// <summary>Whether the elevated lane is currently registered and points at this build.</summary>
    public static bool IsElevatedTaskCurrent() => ReadTaskCommand() is { } cmd &&
        string.Equals(cmd, ExecutablePath, StringComparison.OrdinalIgnoreCase);

    public static bool ElevatedTaskExists() => RunSchtasks($"/Query /TN \"{TaskName}\"", out _);

    /// <summary>
    /// Aligns Windows with the desired state and reports which elevation actually took.
    /// A caller that gets back <c>false</c> for <paramref name="elevated"/> should persist
    /// that: the checkbox would otherwise claim something Windows never agreed to.
    /// </summary>
    /// <param name="allowPrompt">
    /// Whether this call may raise UAC. True when the user just changed the setting and is
    /// expecting it; false at startup, where a prompt on every boot would be intolerable —
    /// there, a mismatch is reported rather than fixed.
    /// </param>
    public static bool Apply(bool enabled, bool elevated, bool allowPrompt)
    {
        var elevatedActive = enabled && elevated && EnsureTask(allowPrompt);

        // Only ever removed when it exists, so a lane that was never created cannot cost a
        // second prompt after the first one was declined.
        if (!elevatedActive && ElevatedTaskExists())
            RemoveTask(allowPrompt);

        ApplyRunKey(enabled && !elevatedActive);
        return elevatedActive;
    }

    // ---- plain lane: HKCU\...\Run ----------------------------------------------

    public static bool IsRunKeyEnabled()
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

    private static void ApplyRunKey(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                key.SetValue(ValueName, $"\"{ExecutablePath}\"");
                Log.Info("Autostart: Run key enabled.");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key?.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    Log.Info("Autostart: Run key disabled.");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Autostart: failed to {(enabled ? "enable" : "disable")} the Run key", ex);
        }
    }

    // ---- elevated lane: scheduled task -----------------------------------------

    /// <summary>Creates or refreshes the task; returns whether it is in place afterwards.</summary>
    private static bool EnsureTask(bool allowPrompt)
    {
        if (IsElevatedTaskCurrent())
            return true;

        if (!allowPrompt)
        {
            // Startup. Say what is wrong and leave it alone: a UAC prompt every boot would
            // be worse than autostart quietly running unelevated for one more session.
            Log.Warn(ElevatedTaskExists()
                ? $"Autostart: the '{TaskName}' task points at another build; re-toggle " +
                  "\"Start as administrator\" in Settings to update it."
                : $"Autostart: elevated start is enabled but the '{TaskName}' task is missing; " +
                  "re-toggle \"Start as administrator\" in Settings to recreate it.");
            return false;
        }

        var xmlFile = Path.Combine(Path.GetTempPath(), $"reshot-autostart-{Guid.NewGuid():N}.xml");
        try
        {
            // Registering from XML rather than switches: /RU with a logon trigger otherwise
            // drags in credential handling, and the defaults that matter here (no battery
            // stop, no execution time limit) are not reachable from the command line.
            File.WriteAllText(xmlFile, BuildTaskXml(), Encoding.Unicode);

            if (!RunSchtasksElevated($"/Create /TN \"{TaskName}\" /XML \"{xmlFile}\" /F"))
                return false;

            Log.Info($"Autostart: registered the elevated '{TaskName}' task → {ExecutablePath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Autostart: failed to register the elevated task", ex);
            return false;
        }
        finally
        {
            try { File.Delete(xmlFile); } catch { /* temp file; nothing to do */ }
        }
    }

    private static void RemoveTask(bool allowPrompt)
    {
        if (!allowPrompt)
        {
            Log.Warn($"Autostart: the elevated '{TaskName}' task is still registered; " +
                     "it will be removed the next time the setting is changed.");
            return;
        }

        if (RunSchtasksElevated($"/Delete /TN \"{TaskName}\" /F"))
            Log.Info($"Autostart: removed the elevated '{TaskName}' task.");
    }

    /// <summary>The command the registered task runs, or null if there is no task.</summary>
    private static string? ReadTaskCommand()
    {
        if (!RunSchtasks($"/Query /TN \"{TaskName}\" /XML ONE", out var xml) || xml is null)
            return null;

        var open = xml.IndexOf("<Command>", StringComparison.Ordinal);
        var close = xml.IndexOf("</Command>", StringComparison.Ordinal);
        if (open < 0 || close < open)
        {
            Log.Warn("Autostart: could not read the task's command; skipping the staleness check.");
            return null;
        }

        open += "<Command>".Length;
        return xml[open..close].Trim().Trim('"');
    }

    private static string BuildTaskXml()
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>{user}</Author>
            <Description>Starts Reshot at logon with administrator rights, so its overlay can appear over elevated applications.</Description>
            <URI>\{TaskName}</URI>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{user}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{user}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>false</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <IdleSettings>
              <StopOnIdleEnd>false</StopOnIdleEnd>
              <RestartOnIdle>false</RestartOnIdle>
            </IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <WakeToRun>false</WakeToRun>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{ExecutablePath}</Command>
            </Exec>
          </Actions>
        </Task>
        """;
    }

    // ---- schtasks plumbing ------------------------------------------------------

    /// <summary>Runs schtasks unelevated and captures its output. Queries need no rights.</summary>
    private static bool RunSchtasks(string arguments, out string? output)
    {
        output = null;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Deliberately not overriding StandardOutputEncoding: /XML comes back in
                // the console encoding, and forcing UTF-16 turns it into noise. A parse
                // that fails anyway costs only the staleness check, never a wrong answer.
            });
            if (process is null)
                return false;

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error($"Autostart: schtasks {arguments} failed", ex);
            return false;
        }
    }

    /// <summary>Whether this process already holds administrator rights.</summary>
    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            Log.Error("Autostart: could not determine elevation", ex);
            return false;
        }
    }

    /// <summary>
    /// Runs schtasks with administrator rights, asking for them only if we do not already
    /// have them. Once the elevated lane is in use Reshot itself starts elevated, so
    /// turning the setting back off — or moving the executable and re-registering — costs
    /// no prompt at all. The prompt is the price of the first change, not of every change.
    /// </summary>
    private static bool RunSchtasksElevated(string arguments)
    {
        if (IsProcessElevated())
        {
            Log.Info("Autostart: already elevated; running schtasks without a prompt.");
            return RunSchtasks(arguments, out _);
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return false;

            process.WaitForExit(60_000);
            if (process.ExitCode == 0)
                return true;

            Log.Error($"Autostart: schtasks exited with {process.ExitCode} for '{arguments}'.");
            return false;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            Log.Warn("Autostart: the administrator prompt was declined.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Autostart: elevated schtasks {arguments} failed", ex);
            return false;
        }
    }
}
