using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace Reshot.App.Recording;

/// <summary>
/// The small "which audio do you want to keep?" panel that appears at the bottom-right
/// after a recording. Both tracks, one, or neither can be kept. "Never show this again"
/// writes back to <c>video.audio.askOnSave</c>, so the settings checkbox and this one are
/// the same switch. HL2/Source dress, matching the tray menu and settings window.
/// </summary>
public sealed class AudioTrackPrompt : Window
{
    private static readonly Color PanelGrey = Color.FromRgb(0x76, 0x76, 0x76);
    private static readonly Color Bevel = Color.FromRgb(0xC0, 0xBF, 0xBF);
    private static readonly Color BevelDark = Color.FromRgb(0x45, 0x45, 0x43);
    private static readonly Color Teal = Color.FromRgb(0x3C, 0x98, 0x98);

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
        Width = 320;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _system = MakeCheck("System audio", hasSystem && systemDefault, hasSystem);
        _mic = MakeCheck("Microphone", hasMic && micDefault, hasMic);
        _never = MakeCheck("Never show this again", false, true);

        var stack = new StackPanel();

        // Header: title + a close (×) button, like the settings window's chrome.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = new Button
        {
            Content = "✕",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
        };
        close.MouseEnter += (_, _) => close.Foreground = new SolidColorBrush(Teal);
        close.MouseLeave += (_, _) => close.Foreground = Brushes.White;
        close.Click += (_, _) => Finish(_system.IsChecked == true, _mic.IsChecked == true);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock
        {
            Text = "Which audio do you want to keep?",
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(header);
        stack.Children.Add(_system);
        stack.Children.Add(_mic);
        stack.Children.Add(Rule());
        stack.Children.Add(_never);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var save = MakeButton("Save");
        save.Click += (_, _) => Finish(_system.IsChecked == true, _mic.IsChecked == true);
        buttons.Children.Add(save);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, PanelGrey.R, PanelGrey.G, PanelGrey.B)),
            BorderBrush = new SolidColorBrush(Bevel),
            BorderThickness = new Thickness(1, 1, 0, 0),
            Child = new Border
            {
                BorderBrush = new SolidColorBrush(BevelDark),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(14),
                Child = stack,
            },
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

    private static CheckBox MakeCheck(string text, bool isChecked, bool enabled) => new()
    {
        Content = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 13 },
        IsChecked = isChecked,
        IsEnabled = enabled,
        Opacity = enabled ? 1.0 : 0.45,
        Margin = new Thickness(0, 3, 0, 3),
    };

    private static UIElement Rule() => new StackPanel
    {
        Margin = new Thickness(0, 8, 0, 6),
        Children =
        {
            new Border { Height = 1, Background = new SolidColorBrush(BevelDark) },
            new Border { Height = 1, Background = new SolidColorBrush(Bevel) },
        },
    };

    /// <summary>A raised Source-style button: light top-left bevel over a dark bottom-right one.</summary>
    private static Button MakeButton(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var btn = new Button
        {
            MinWidth = 86,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            // WPF can't colour border sides individually, so the bevel is two nested Borders.
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x7B, 0x7B, 0x7B)),
                BorderBrush = new SolidColorBrush(Bevel),
                BorderThickness = new Thickness(1, 1, 0, 0),
                Child = new Border
                {
                    BorderBrush = new SolidColorBrush(BevelDark),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(18, 5, 18, 5),
                    Child = label,
                },
            },
        };
        btn.MouseEnter += (_, _) => label.Foreground = new SolidColorBrush(Teal);
        btn.MouseLeave += (_, _) => label.Foreground = Brushes.White;
        return btn;
    }
}
