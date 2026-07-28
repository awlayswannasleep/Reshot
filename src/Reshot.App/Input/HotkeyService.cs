using System.Runtime.InteropServices;
using System.Windows.Interop;
using Reshot.App.Interop;
using Reshot.Core.Diagnostics;
using Reshot.Core.Input;

namespace Reshot.App.Input;

/// <summary>
/// Owns the single global hotkey. Backed by a <b>message-only</b> HwndSource so
/// there is no visible window and no polling, Windows delivers WM_HOTKEY to the
/// message loop, which is exactly the "zero background cost" model from
/// ARCHITECTURE §7. Rebinding = unregister + register on the same window.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xB001; // arbitrary, unique within this window

    private readonly HwndSource _source;
    private bool _registered;
    private bool _disposed;

    /// <summary>Raised (on the UI thread) each time the hotkey is pressed.</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>The hotkey currently registered, if any.</summary>
    public HotkeyDefinition? Current { get; private set; }

    public HotkeyService()
    {
        // Parent = HWND_MESSAGE → a message-only window: invisible, no taskbar,
        // no Z-order, just a message pump target for RegisterHotKey.
        var parameters = new HwndSourceParameters("reshot.hotkey")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Registers <paramref name="hotkeyText"/>, replacing any current binding.
    /// Returns false (and logs) if parsing or the OS registration fails, e.g.
    /// the combo is already owned by another app.
    /// </summary>
    public bool Register(string hotkeyText)
    {
        if (!HotkeyDefinition.TryParse(hotkeyText, out var def, out var error))
        {
            Log.Error($"Hotkey: cannot parse '{hotkeyText}': {error}");
            return false;
        }

        return Register(def);
    }

    public bool Register(HotkeyDefinition def)
    {
        Unregister();

        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, (uint)def.Modifiers, def.VirtualKey))
        {
            var err = Marshal.GetLastWin32Error();
            Log.Error($"Hotkey: RegisterHotKey failed for '{def}' (Win32 error {err}; likely already in use).");
            return false;
        }

        _registered = true;
        Current = def;
        Log.Info($"Hotkey: registered '{def}'.");
        return true;
    }

    /// <summary>Releases the OS hotkey without disposing the service (used for Pause).</summary>
    public void Unregister()
    {
        if (!_registered)
            return;

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
        Log.Info($"Hotkey: unregistered '{Current}'.");
        Current = null;
    }

    public bool IsRegistered => _registered;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
