using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace Reshot.App.Recording;

/// <summary>
/// The small "which audio do you want to keep?" panel that appears at the bottom-right
/// after a recording. Both tracks, one, or neither can be kept. "Never show this again"
/// writes back to <c>video.audio.askOnSave</c>, so the settings checkbox and this one are
/// the same switch.
///
/// Every control is dressed from <c>Theme/SourceVgui.xaml</c>, the shared Half-Life 2
/// vocabulary, so this dialog and the settings window read as one product. Nothing here
/// invents a colour or a bevel of its own.
/// </summary>
public sealed class AudioTrackPrompt : Window
{
    private readonly CheckBox _mic;
    private readonly CheckBox _system;
    private readonly CheckBox _never;

    /// <summary>Raised once with the user's choice; never fires twice.</summary>
    public event Action<bool /*keepSystem*/, bool /*keepMic*/, bool /*neverAskAgain*/>? Decided;

    private bool _done;

    public AudioTrackPrompt(bool hasSystem, bool hasMic, bool systemDefault, bool micDefault)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 300;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Source VGUI never antialiased its text, and the bevels are single pixels:
        // display-mode formatting and layout rounding keep both crisp.
        FontFamily = new FontFamily("Roboto, Segoe UI");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;

        _system = MakeCheck("System audio", hasSystem && systemDefault, hasSystem);
        _mic = MakeCheck("Microphone", hasMic && micDefault, hasMic);
        _never = MakeCheck("Never show this again", false, true);

        var stack = new StackPanel();

        // Titlebar: caption on the left, close glyph on the right, like the settings modal.
        var titlebar = new DockPanel();
        var close = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("VguiCloseButton"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        close.Click += (_, _) => Finish(_system.IsChecked == true, _mic.IsChecked == true);
        DockPanel.SetDock(close, Dock.Right);
        titlebar.Children.Add(close);
        titlebar.Children.Add(new TextBlock
        {
            Text = "AUDIO TRACKS",
            Style = (Style)FindResource("VguiDialogTitle"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(titlebar);

        stack.Children.Add(new TextBlock
        {
            Text = "Which audio do you want to keep?",
            Style = (Style)FindResource("VguiBody"),
            Margin = new Thickness(0, 12, 0, 6),
        });
        stack.Children.Add(_system);
        stack.Children.Add(_mic);

        stack.Children.Add(new Separator
        {
            Style = (Style)FindResource("VguiSeparator"),
            Margin = new Thickness(0, 12, 0, 6),
        });
        stack.Children.Add(_never);

        var save = new Button
        {
            Content = "Save",
            Style = (Style)FindResource("VguiDialogButton"),
        };
        save.Click += (_, _) => Finish(_system.IsChecked == true, _mic.IsChecked == true);
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { save },
        });

        Content = new Border
        {
            Background = (Brush)FindResource("VguiPanel"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Child = stack,
        };

        // Bottom-right of the primary monitor's working area (clear of the taskbar).
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 16;
            Top = wa.Bottom - ActualHeight - 16;
        };
    }

    /// <summary>Closing without pressing Save keeps the defaults (nothing is lost).</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Finish(_system.IsChecked == true, _mic.IsChecked == true);
    }

    private void Finish(bool keepSystem, bool keepMic)
    {
        if (_done)
            return;
        _done = true;
        var never = _never.IsChecked == true;
        Decided?.Invoke(keepSystem, keepMic, never);
        if (IsLoaded)
            Close();
    }

    /// <summary>
    /// A track that was never captured still gets a row, disabled: the absence of a
    /// microphone track is information, and a row that silently vanishes is not.
    /// </summary>
    private CheckBox MakeCheck(string text, bool isChecked, bool enabled) => new()
    {
        Content = text,
        Style = (Style)FindResource("VguiCheckBox"),
        IsChecked = isChecked,
        IsEnabled = enabled,
    };
}
