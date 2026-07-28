using System.Runtime.InteropServices;
using System.Text;

namespace Reshot.Recording;

/// <summary>What the audio recorder should capture (mic + a loopback selection).</summary>
public sealed class AudioSources
{
    public bool Mic { get; set; }
    public string? MicDevice { get; set; }

    /// <summary>Capture the whole system mix (WASAPI loopback).</summary>
    public bool SystemFull { get; set; }

    /// <summary>Capture only these processes (Windows process loopback, include mode).</summary>
    public int[] IncludePids { get; set; } = Array.Empty<int>();

    public bool HasAnyAudio => Mic || SystemFull || IncludePids.Length > 0;
}

/// <summary>A top-level window that can be picked as an audio source.</summary>
public readonly record struct WindowAudioSource(int ProcessId, string ProcessName, string Title);

/// <summary>Enumerates visible top-level windows (one entry per process) for the source picker.</summary>
public static class WindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const uint GwOwner = 4;

    public static IReadOnlyList<WindowAudioSource> List()
    {
        var byPid = new Dictionary<int, WindowAudioSource>();
        var self = Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) == 0)
                return true;
            if (GetWindow(hwnd, GwOwner) != IntPtr.Zero) // skip owned/dialog windows
                return true;
            if ((GetWindowLong(hwnd, GwlExStyle) & WsExToolWindow) != 0) // skip tool windows
                return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || (int)pid == self || byPid.ContainsKey((int)pid))
                return true;

            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            string process;
            try { process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
            catch { return true; }

            byPid[(int)pid] = new WindowAudioSource((int)pid, process, title);
            return true;
        }, IntPtr.Zero);

        return byPid.Values.OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
