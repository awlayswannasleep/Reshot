using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Reshot.App.Tray;

/// <summary>
/// The tray context menu, drawn as a borderless window instead of a
/// <c>ContextMenuStrip</c> so it can carry the same Half-Life 2 styling as the
/// settings dialog. Raises intent events; it holds no app logic itself.
/// </summary>
public partial class TrayMenuWindow : Window
{
    // Blur-behind, so the translucent grey panel sits on a blurred desktop the
    // way the reference modal's backdrop-filter does.
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private bool _closing;

    public event EventHandler? CaptureRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? QuitRequested;

    /// <summary>Fires when the user toggles "Pause hotkey"; arg = paused.</summary>
    public event EventHandler<bool>? PauseHotkeyToggled;

    public TrayMenuWindow(bool paused)
    {
        InitializeComponent();

        PauseCheck.IsChecked = paused;

        CaptureBtn.Click += (_, _) => Fire(CaptureRequested);
        SettingsBtn.Click += (_, _) => Fire(SettingsRequested);
        QuitBtn.Click += (_, _) => Fire(QuitRequested);

        PauseCheck.Click += (_, _) =>
        {
            var paused = PauseCheck.IsChecked == true;
            CloseOnce();
            PauseHotkeyToggled?.Invoke(this, paused);
        };

        // Clicking anywhere else dismisses the menu, like a real context menu.
        Deactivated += (_, _) => CloseOnce();
    }

    /// <summary>
    /// Closes at most once. Closing the window also deactivates it, which re-enters
    /// the Deactivated handler — and WPF throws if <c>Close</c> is called while a
    /// close is already in progress.
    /// </summary>
    private void CloseOnce()
    {
        if (_closing)
            return;

        _closing = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    private void Fire(EventHandler? handler)
    {
        CloseOnce();
        handler?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var accent = new AccentPolicy { AccentState = AccentEnableBlurBehind, AccentFlags = 2 };
        var size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = ptr,
                SizeOfData = size,
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Shows the menu near the mouse, kept inside the work area of the screen it
    /// was summoned on. The tray lives in a corner, so the menu normally opens
    /// up and to the left of the cursor.
    /// </summary>
    public void ShowAtCursor()
    {
        // Measure before positioning: SizeToContent means the size is unknown
        // until the layout pass runs.
        Show();
        UpdateLayout();

        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var work = screen.WorkingArea;

        var source = PresentationSource.FromVisual(this);
        var toDip = source?.CompositionTarget?.TransformFromDevice
                    ?? System.Windows.Media.Matrix.Identity;

        var cursorDip = toDip.Transform(new System.Windows.Point(cursor.X, cursor.Y));
        var workTopLeft = toDip.Transform(new System.Windows.Point(work.Left, work.Top));
        var workBottomRight = toDip.Transform(new System.Windows.Point(work.Right, work.Bottom));

        // Prefer opening up-left of the cursor; flip to the other side when the
        // tray sits at the top or left of the screen instead.
        var left = cursorDip.X - ActualWidth;
        if (left < workTopLeft.X)
            left = cursorDip.X;

        var top = cursorDip.Y - ActualHeight;
        if (top < workTopLeft.Y)
            top = cursorDip.Y;

        Left = Math.Min(left, workBottomRight.X - ActualWidth);
        Top = Math.Min(top, workBottomRight.Y - ActualHeight);

        Activate();
    }
}
