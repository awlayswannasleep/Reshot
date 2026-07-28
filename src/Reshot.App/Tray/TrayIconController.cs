using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Reshot.Core.Diagnostics;

namespace Reshot.App.Tray;

/// <summary>
/// The tray presence (SPEC §14): a NotifyIcon with a context menu
/// (Capture / Settings / Pause hotkey / Quit) and balloon feedback. Owns a
/// runtime-drawn placeholder icon; the real app icon arrives in Phase 5.
/// Raises intent events, it holds no app logic itself.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly NotifyIcon _notifyIcon;
    private Icon? _icon;
    private IntPtr _iconHandle;
    private bool _disposed;

    public event EventHandler? CaptureRequested;

    /// <summary>
    /// Right-click on the icon. The menu itself is a styled WPF window
    /// (<see cref="TrayMenuWindow"/>) owned by the App layer, not a
    /// <c>ContextMenuStrip</c>, so it can match the settings dialog.
    /// </summary>
    public event EventHandler? MenuRequested;

    /// <summary>Current "Pause hotkey" state, used to seed the menu when it opens.</summary>
    public bool IsPaused { get; private set; }

    public TrayIconController()
    {
        _icon = LoadAppIcon() ?? CreatePlaceholderIcon();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Visible = true,
            Text = "reshot",
        };

        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
                MenuRequested?.Invoke(this, EventArgs.Empty);
        };

        // Double-click the tray icon = capture, matching the primary action.
        _notifyIcon.DoubleClick += (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty);

        Log.Info("Tray: icon created.");
    }

    /// <summary>Records the current pause state so the next menu opens in sync.</summary>
    public void SetPaused(bool paused) => IsPaused = paused;

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(2500);
    }

    /// <summary>Loads the designed app icon from the embedded resource, or null on failure.</summary>
    private static Icon? LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/reshot.ico");
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info?.Stream is null)
                return null;
            using var stream = info.Stream;
            // 32px frame: crisp in the tray and scales down cleanly at any DPI.
            return new Icon(stream, new Size(32, 32));
        }
        catch (Exception ex)
        {
            Log.Warn($"Tray: could not load app icon, using placeholder: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Draws a simple dark-square-with-aperture placeholder at 32×32.
    /// Fallback if the designed .ico resource can't be loaded.
    /// </summary>
    private Icon CreatePlaceholderIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(255, 32, 34, 40));
            using var path = RoundedRect(new Rectangle(2, 2, 28, 28), 7);
            g.FillPath(bg, path);

            // Selection-frame motif: a bright rounded rectangle outline.
            using var pen = new Pen(Color.FromArgb(255, 60, 152, 152), 2.4f);
            g.DrawRectangle(pen, 10, 10, 12, 12);
            using var dot = new SolidBrush(Color.FromArgb(255, 60, 152, 152));
            g.FillEllipse(dot, 14, 14, 4, 4);
        }

        _iconHandle = bmp.GetHicon();
        return Icon.FromHandle(_iconHandle);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon?.Dispose();
        if (_iconHandle != IntPtr.Zero)
            DestroyIcon(_iconHandle);
    }
}
