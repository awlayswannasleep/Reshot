using System.Runtime.InteropServices;

namespace Reshot.App.Interop;

/// <summary>Thin P/Invoke surface for the Win32 calls Reshot needs in Phase 0.</summary>
internal static class NativeMethods
{
    /// <summary>Special parent handle that makes a window message-only (no UI, no Z-order).</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    /// <summary>Posted to a window's message loop when a registered hotkey fires.</summary>
    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- Overlay window placement (physical pixels, DPI-independent) -----------

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    // ---- Force foreground (so the overlay reliably gets keyboard focus) --------

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    // ---- Hold detection + cursor position (radial menu) ------------------------

    /// <summary>High bit set while the key is currently down.</summary>
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Whether the foreground window belongs to another process and covers its entire
    /// screen — in practice a fullscreen or borderless game.
    ///
    /// Everything that touches global input state is gated on this. Against an ordinary
    /// desktop window there is nothing to fight for: showing a topmost window is enough,
    /// and unlocking a cursor nobody locked, or attaching to an input queue nobody is
    /// holding, is pure cost — the kind the user sees as a blinking cursor.
    /// </summary>
    public static bool ForegroundIsFullscreenForeignWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(foreground, out var pid);
        if (pid == (uint)Environment.ProcessId)
            return false;

        if (!GetWindowRect(foreground, out var rect))
            return false;

        // Screen bounds, not the work area: a maximised window stops at the taskbar,
        // a game does not. That difference is the whole test.
        var screen = System.Windows.Forms.Screen.FromHandle(foreground).Bounds;
        return rect.Left <= screen.Left && rect.Top <= screen.Top &&
               rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
    }

    /// <summary>
    /// Puts a window back on top of the Z-order without activating it. Free of input-state
    /// side effects, so it is the one thing safe to repeat against a game that re-asserts
    /// its own topmost every frame.
    /// </summary>
    public static void AssertTopmost(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // ---- Games: ClipCursor / raw-input often leave the mouse stuck in the game ----

    /// <summary>Pass <see cref="IntPtr.Zero"/> to clear any cursor confinement.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    /// <summary>Display-count based: call until the return value is ≥ 0 to show the cursor.</summary>
    [DllImport("user32.dll")]
    public static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool bShow);

    public const uint ASFW_ANY = 0xFFFFFFFFu;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    /// <summary>
    /// Releases cursor confinement / capture that games apply, and makes the system cursor
    /// visible again. Call <b>once</b> before showing the overlay or radial menu: it touches
    /// global input state, and repeating it is visible to the user as a blinking cursor.
    /// </summary>
    public static void UnlockInputFromGame()
    {
        ReleaseCursorClip();
        ReleaseCapture();
        RestoreCursorVisibility();
    }

    /// <summary>
    /// Un-confines the pointer. Unlike the rest of the unlock this is free of side effects
    /// when nothing is confined, so it is safe to repeat while a game keeps re-clipping.
    /// </summary>
    public static void ReleaseCursorClip() => ClipCursor(IntPtr.Zero);

    /// <summary>
    /// Brings the cursor's display counter back to visible, and leaves it alone when it
    /// already is. ShowCursor is reference-counted and returns the <i>new</i> count, so
    /// merely asking costs an increment — which has to be handed back. Looping on it
    /// unconditionally left the counter one higher on every call, and the unlock runs
    /// several times per capture: that drift is what made the cursor blink and change
    /// shape in the moment before the overlay appeared.
    /// </summary>
    private static void RestoreCursorVisibility()
    {
        var count = ShowCursor(true);
        if (count > 0)
        {
            ShowCursor(false); // it was visible all along: undo the probe
            return;
        }

        // Games hide it many times over; climb back to zero, never above.
        for (var i = 0; i < 32 && count < 0; i++)
            count = ShowCursor(true);
    }

    /// <summary>
    /// Pulls <paramref name="hwnd"/> to the top and, if it can, to the foreground. Returns
    /// whether it holds the foreground afterwards.
    ///
    /// Two tiers, because they cost wildly different things. The polite tier — topmost,
    /// bring-to-top, SetForegroundWindow — touches nothing outside this process and always
    /// runs. The invasive tier attaches our input queue to the foreground window's and
    /// injects a synthetic Alt to defeat the foreground lock; that merges cursor state with
    /// a foreign process and serialises our thread against its input thread, which is
    /// cheap against Notepad and decidedly not cheap against a game mid-frame. So it is
    /// opt-in, and callers use it once per session rather than on a loop.
    /// </summary>
    public static bool ForceForegroundWindow(IntPtr hwnd, bool invasive = false)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        AssertTopmost(hwnd);

        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
            return true;

        AllowSetForegroundWindow(ASFW_ANY);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        SetFocus(hwnd);

        if (GetForegroundWindow() == hwnd || !invasive)
            return GetForegroundWindow() == hwnd;

        var thisThread = GetCurrentThreadId();
        var fgThread = GetWindowThreadProcessId(foreground, out _);

        var attached = fgThread != 0 && fgThread != thisThread &&
                       AttachThreadInput(fgThread, thisThread, true);
        try
        {
            // No synthetic Alt here. Injecting a global key event to defeat the foreground
            // lock is a blunt instrument: the whole desktop sees the keypress, games see it
            // as input, and it flashes. Sharing the input queue is what makes the call
            // below legal anyway — the Alt was belt and braces, and the belt was visible.

            // Mouse capture belongs to a thread, so the ReleaseCapture in the unlock can
            // only ever free our own. Here the queues are joined, which is the one moment
            // the game's capture is ours to drop — and while it holds it, every
            // WM_SETCURSOR keeps going to the game instead of to the window on top.
            if (attached)
                ReleaseCapture();

            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(fgThread, thisThread, false);
        }

        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Names the window that currently holds the foreground, for the log.</summary>
    public static string DescribeForegroundWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return "none";

        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return $"{process.ProcessName} (hwnd 0x{hwnd.ToInt64():X})";
        }
        catch
        {
            return $"pid {pid} (hwnd 0x{hwnd.ToInt64():X})";
        }
    }

    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
}
