using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Reshot.Core.Input;
using Reshot.Core.Settings;
using WinForms = System.Windows.Forms;

namespace Reshot.App.Settings;

/// <summary>
/// The application settings window (SPEC §13). Fixed dark theme, English. Loads a
/// copy of the current settings into its controls and, on Save, builds a fresh
/// <see cref="AppSettings"/> exposed via <see cref="Result"/>; the caller persists
/// it and re-applies the hotkey / autostart. Cancel changes nothing.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _capturing;
    private bool _capturingAudio;
    private string _hotkey;
    private string _audioHotkey = string.Empty;

    public AppSettings? Result { get; private set; }

    /// <summary>Raised by "Apply", the caller applies the settings while the window stays open.</summary>
    public event Action<AppSettings>? Applied;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();

        _hotkey = string.IsNullOrWhiteSpace(current.Hotkey) ? "PrtScn" : current.Hotkey;
        _audioHotkey = current.AudioHotkey ?? string.Empty;
        LoadFrom(current);

        // Custom chrome: drag by the title bar, close via the ✕.
        TitleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        CloseBtn.Click += (_, _) => { DialogResult = false; };

        RebindBtn.Click += (_, _) => ToggleCapture();
        AudioRebindBtn.Click += (_, _) => ToggleCapture(audio: true);
        AudioClearBtn.Click += (_, _) => { _audioHotkey = string.Empty; AudioHotkeyBox.Text = "(none)"; };
        BrowseRecordsBtn.Click += (_, _) => Browse(RecordsBox);
        SaveBtn.Click += (_, _) => { if (TryBuildResult()) DialogResult = true; };
        ApplyBtn.Click += (_, _) => { if (TryBuildResult()) Applied?.Invoke(Result!); };
        CancelBtn.Click += (_, _) => { DialogResult = false; };

        BrowseShotsBtn.Click += (_, _) => Browse(ScreenshotsBox);
        BrowseVideosBtn.Click += (_, _) => Browse(VideosBox);

        DimSlider.ValueChanged += (_, _) => DimValue.Text = $"{(int)Math.Round(DimSlider.Value)}%";
        QualitySlider.ValueChanged += (_, _) => QualityValue.Text = $"{(int)Math.Round(QualitySlider.Value)}";
        CornerOpacitySlider.ValueChanged += (_, _) => CornerOpacityValue.Text = $"{(int)Math.Round(CornerOpacitySlider.Value)}%";
        DimColorBox.TextChanged += (_, _) => UpdateSwatch(DimColorBox.Text, DimColorSwatch);
        CornerColorBox.TextChanged += (_, _) => UpdateSwatch(CornerColorBox.Text, CornerColorSwatch);
    }

    private void LoadFrom(AppSettings s)
    {
        HotkeyBox.Text = _hotkey;
        AutostartCheck.IsChecked = s.Autostart;
        UpdateCheck.IsChecked = s.Update.Auto;

        DimSlider.Value = Math.Clamp(s.Dim.Opacity, 0, 1) * 100;
        DimValue.Text = $"{(int)Math.Round(DimSlider.Value)}%";
        DimColorBox.Text = s.Dim.Color;
        UpdateSwatch(s.Dim.Color, DimColorSwatch);

        ScreenshotsBox.Text = s.Paths.Screenshots;
        VideosBox.Text = s.Paths.Videos;
        RecordsBox.Text = s.Paths.Records;
        SelectByTag(FormatCombo, s.Format.Image);
        QualitySlider.Value = Math.Clamp(s.Format.Quality, 1, 100);
        QualityValue.Text = $"{(int)Math.Round(QualitySlider.Value)}";
        FilenameBox.Text = s.Filename.Template;

        SelectByTag(FpsCombo, s.Video.Fps.ToString());
        MicCheck.IsChecked = s.Video.Audio.Mic;
        SystemAudioCheck.IsChecked = s.Video.Audio.System;
        AskOnSaveCheck.IsChecked = s.Video.Audio.AskOnSave;
        CornersCheck.IsChecked = s.Video.Corners.Enabled;
        CornerColorBox.Text = s.Video.Corners.Color;
        UpdateSwatch(s.Video.Corners.Color, CornerColorSwatch);
        CornerOpacitySlider.Value = Math.Clamp(s.Video.Corners.Opacity, 0, 1) * 100;
        CornerOpacityValue.Text = $"{(int)Math.Round(CornerOpacitySlider.Value)}%";

        AudioSystemCheck.IsChecked = s.Audio.System;
        AudioMicCheck.IsChecked = s.Audio.Mic;
        PopulateMicrophones(s.Audio.MicDevice);
        AudioHotkeyBox.Text = string.IsNullOrWhiteSpace(_audioHotkey) ? "(none)" : _audioHotkey;
    }

    private void PopulateMicrophones(string selectedId)
    {
        MicDeviceCombo.Items.Clear();
        MicDeviceCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "System default", Tag = "default" });
        foreach (var (id, name) in Reshot.Recording.AudioRecorder.ListMicrophones())
            MicDeviceCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = name, Tag = id });
        SelectByTag(MicDeviceCombo, string.IsNullOrWhiteSpace(selectedId) ? "default" : selectedId);
    }

    /// <summary>Validates the inputs and builds <see cref="Result"/>; false if validation failed.</summary>
    private bool TryBuildResult()
    {
        if (!TryParseColor(DimColorBox.Text, out _))
        {
            Warn("Dim color is not a valid hex color (e.g. #000000).");
            return false;
        }
        if (!TryParseColor(CornerColorBox.Text, out _))
        {
            Warn("Corner color is not a valid hex color (e.g. #FF0000).");
            return false;
        }
        if (string.IsNullOrWhiteSpace(FilenameBox.Text))
        {
            Warn("Filename template cannot be empty.");
            return false;
        }
        if (!HotkeyDefinition.TryParse(_hotkey, out _, out var hotkeyError))
        {
            Warn($"Hotkey is invalid: {hotkeyError}");
            return false;
        }
        if (!string.IsNullOrWhiteSpace(_audioHotkey) && !HotkeyDefinition.TryParse(_audioHotkey, out _, out var audioErr))
        {
            Warn($"Audio hotkey is invalid: {audioErr}");
            return false;
        }

        Result = new AppSettings
        {
            Hotkey = _hotkey,
            AudioHotkey = _audioHotkey,
            Autostart = AutostartCheck.IsChecked == true,
            Dim = new DimSettings
            {
                Opacity = DimSlider.Value / 100.0,
                Color = DimColorBox.Text.Trim(),
            },
            Paths = new PathSettings
            {
                Screenshots = ScreenshotsBox.Text.Trim(),
                Videos = VideosBox.Text.Trim(),
                Records = RecordsBox.Text.Trim(),
            },
            Audio = new AudioSettings
            {
                System = AudioSystemCheck.IsChecked == true,
                Mic = AudioMicCheck.IsChecked == true,
                MicDevice = TagOf(MicDeviceCombo) ?? "default",
            },
            Format = new FormatSettings
            {
                Image = TagOf(FormatCombo) ?? "png",
                Quality = (int)Math.Round(QualitySlider.Value),
            },
            Filename = new FilenameSettings { Template = FilenameBox.Text.Trim() },
            Update = new UpdateSettings { Auto = UpdateCheck.IsChecked == true },
            Video = new VideoSettings
            {
                Fps = int.TryParse(TagOf(FpsCombo), out var fps) ? fps : 60,
                Audio = new VideoAudioSettings
                {
                    Mic = MicCheck.IsChecked == true,
                    System = SystemAudioCheck.IsChecked == true,
                    AskOnSave = AskOnSaveCheck.IsChecked == true,
                },
                Corners = new VideoCornersSettings
                {
                    Enabled = CornersCheck.IsChecked == true,
                    Color = CornerColorBox.Text.Trim(),
                    Opacity = CornerOpacitySlider.Value / 100.0,
                },
            },
        };

        return true;
    }

    // ---- Hotkey capture --------------------------------------------------------

    private void ToggleCapture(bool audio = false)
    {
        if (_capturing)
        {
            EndCapture();
            return;
        }
        _capturing = true;
        _capturingAudio = audio;
        (audio ? AudioRebindBtn : RebindBtn).Content = "Cancel";
        (audio ? AudioHotkeyBox : HotkeyBox).Text = "Press keys…";
        HotkeyHint.Text = "Press the key combination now (Esc to cancel).";
    }

    private void EndCapture()
    {
        _capturing = false;
        _capturingAudio = false;
        RebindBtn.Content = "Rebind";
        HotkeyBox.Text = _hotkey;
        AudioRebindBtn.Content = "Rebind";
        AudioHotkeyBox.Text = string.IsNullOrWhiteSpace(_audioHotkey) ? "(none)" : _audioHotkey;
        HotkeyHint.Text = "Click Rebind, then press the key combination.";
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            EndCapture();
            return;
        }
        if (IsModifierKey(key))
            return; // wait for the main key while modifiers are held

        var token = KeyToToken(key);
        if (token is null)
            return; // unmapped key, keep waiting

        var parts = new List<string>(4);
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(token);

        var text = string.Join("+", parts);
        if (HotkeyDefinition.TryParse(text, out var def, out _))
        {
            if (_capturingAudio)
                _audioHotkey = def.ToString();
            else
                _hotkey = def.ToString();
            EndCapture();
        }
    }

    private static bool IsModifierKey(Key k) => k
        is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    /// <summary>Maps a WPF key to a token <see cref="HotkeyDefinition"/> understands, or null.</summary>
    private static string? KeyToToken(Key k)
    {
        if (k is >= Key.A and <= Key.Z)
            return k.ToString();
        if (k is >= Key.D0 and <= Key.D9)
            return ((char)('0' + (k - Key.D0))).ToString();
        if (k is >= Key.NumPad0 and <= Key.NumPad9)
            return ((char)('0' + (k - Key.NumPad0))).ToString();
        if (k is >= Key.F1 and <= Key.F24)
            return "F" + (k - Key.F1 + 1);

        return k switch
        {
            Key.Snapshot or Key.PrintScreen => "PrtScn",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Enter => "Enter",
            Key.Back => "Backspace",
            Key.Pause => "Pause",
            Key.Scroll => "ScrollLock",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            _ => null,
        };
    }

    // ---- Helpers ---------------------------------------------------------------

    private static void Browse(System.Windows.Controls.TextBox target)
    {
        using var dialog = new WinForms.FolderBrowserDialog { InitialDirectory = target.Text };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            target.Text = dialog.SelectedPath;
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox combo, string tag)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static string? TagOf(System.Windows.Controls.ComboBox combo) =>
        (combo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();

    private static void UpdateSwatch(string hex, System.Windows.Controls.Border swatch)
    {
        if (TryParseColor(hex, out var color))
            swatch.Background = new SolidColorBrush(color);
    }

    private static bool TryParseColor(string? hex, out System.Windows.Media.Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        try
        {
            var parsed = System.Windows.Media.ColorConverter.ConvertFromString(hex.Trim());
            if (parsed is System.Windows.Media.Color c) { color = c; return true; }
        }
        catch
        {
            // not a valid color string
        }
        return false;
    }

    private void Warn(string message) =>
        System.Windows.MessageBox.Show(this, message, "Reshot: Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
}
