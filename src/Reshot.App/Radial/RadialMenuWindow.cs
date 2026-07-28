using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Reshot.App.Interop;
// WPF file; disambiguate from the System.Drawing / WinForms types <UseWindowsForms> adds.
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;
using ToolTip = System.Windows.Controls.ToolTip;

namespace Reshot.App.Radial;

/// <summary>Which slice of the radial menu was picked.</summary>
public enum RadialChoice { Record, Audio, Settings }

/// <summary>
/// The hold-to-open radial menu: three HL2-styled pie slices (quick record, quick audio,
/// screenshot) around a central cancel hub, drawn at the cursor over a dim scrim. Hovering
/// pulls a slice out and darkens it (0.4s); clicking flashes it teal then shrinks + fades
/// the whole wheel (0.8s) before raising <see cref="Chosen"/>. Cancel just fades out.
/// </summary>
public sealed class RadialMenuWindow : Window
{
    // HL2 / Source palette (base grey = the settings panel grey).
    private static readonly Color Base = Color.FromRgb(0x76, 0x76, 0x76);
    private static readonly Color Hover = Color.FromRgb(0x66, 0x66, 0x66); // slightly darker than base
    private static readonly Color Line = Color.FromRgb(0x88, 0x88, 0x88);  // light-grey outline + cuts
    private static readonly Color Teal = Color.FromRgb(0x3C, 0x98, 0x98);

    private const double OuterR = 132;
    private const double InnerR = 46;
    private const double ExtendPx = 14;
    private const double GapDeg = 0; // no gaps: the slices tile into one solid circle

    /// <summary>Raised after the close animation once a slice was chosen (not on cancel).</summary>
    public event Action<RadialChoice>? Chosen;

    private readonly Canvas _canvas = new();
    private readonly Rectangle _scrim = new();
    private readonly Canvas _wheel = new();
    private readonly ScaleTransform _scale = new(0.85, 0.85);

    private sealed class Slice
    {
        public required Path Path { get; init; }
        public required SolidColorBrush Brush { get; init; }
        public required TranslateTransform Extend { get; init; }
        public required double MidRad { get; init; }
        public required RadialChoice Choice { get; init; }
    }

    private readonly List<Slice> _slices = new();
    private RadialChoice? _result;
    private bool _closing;

    // Slice definitions: start angle (screen degrees, 0°=right, CW), label, icon builder.
    private readonly (double Start, RadialChoice Choice, string Name, Func<UIElement> Icon)[] _defs;

    public RadialMenuWindow()
    {
        _defs = new (double, RadialChoice, string, Func<UIElement>)[]
        {
            (210, RadialChoice.Record, "Quick screen recording", IconRecord),
            (330, RadialChoice.Audio, "Quick audio recording", IconAudio),
            (90, RadialChoice.Settings, "Settings", IconSettings),
        };

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Focusable = true;

        _scrim.Fill = Brushes.Transparent; // no dim; still catches clicks outside the wheel
        _scrim.Width = Width;
        _scrim.Height = Height;
        _scrim.MouseLeftButtonDown += (_, _) => Cancel();
        _canvas.Children.Add(_scrim);
        _canvas.Children.Add(_wheel);
        Content = _canvas;

        MouseRightButtonDown += (_, _) => Cancel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancel(); };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Activate();
        Focus();

        // Wheel centre = cursor, in this window's device-independent coordinates.
        NativeMethods.GetCursorPos(out var p);
        var c = PointFromScreen(new Point(p.X, p.Y));

        BuildWheel(c);

        // Grow + fade in.
        _wheel.RenderTransform = _scale;
        _scale.CenterX = c.X;
        _scale.CenterY = c.Y;
        _wheel.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1, Secs(0.18)) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, Secs(0.18)) { EasingFunction = ease });
        _wheel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Secs(0.18)));
    }

    private void BuildWheel(Point c)
    {
        for (int i = 0; i < _defs.Length; i++)
        {
            var def = _defs[i];
            double a0 = Deg2Rad(def.Start + GapDeg);
            double a1 = Deg2Rad(def.Start + 120 - GapDeg);
            double mid = Deg2Rad(def.Start + 60);

            var brush = new SolidColorBrush(Base);
            var extend = new TranslateTransform();
            var path = new Path
            {
                Data = SectorGeometry(c, InnerR, OuterR, a0, a1),
                Fill = brush,
                Stroke = new SolidColorBrush(Line),
                StrokeThickness = 2,
                RenderTransform = extend,
                Cursor = Cursors.Hand,
            };
            ToolTipService.SetInitialShowDelay(path, 1000);
            ToolTipService.SetShowDuration(path, 60000);
            ToolTipService.SetToolTip(path, Hl2Tip(def.Name));

            var slice = new Slice { Path = path, Brush = brush, Extend = extend, MidRad = mid, Choice = def.Choice };
            int index = i;
            path.MouseEnter += (_, _) => HoverSlice(slice, true);
            path.MouseLeave += (_, _) => HoverSlice(slice, false);
            path.MouseLeftButtonDown += (_, _) => Choose(index);

            _slices.Add(slice);
            _wheel.Children.Add(path);

            // Icon centred on the slice's mid-radius, riding the same extend transform.
            var icon = def.Icon();
            icon.IsHitTestVisible = false;
            icon.RenderTransform = extend;
            double ir = (InnerR + OuterR) / 2;
            const double iconSize = 40;
            Canvas.SetLeft(icon, c.X + Math.Cos(mid) * ir - iconSize / 2);
            Canvas.SetTop(icon, c.Y + Math.Sin(mid) * ir - iconSize / 2);
            _wheel.Children.Add(icon);
        }

        // Central cancel hub.
        var hub = new Ellipse
        {
            Width = InnerR * 2, Height = InnerR * 2,
            Fill = new SolidColorBrush(Base),
            Stroke = new SolidColorBrush(Line),
            StrokeThickness = 2,
            Cursor = Cursors.Hand,
        };
        Canvas.SetLeft(hub, c.X - InnerR);
        Canvas.SetTop(hub, c.Y - InnerR);

        double x = 12;
        var xBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)); // white by default
        var cross = new Path
        {
            Data = Geometry.Parse($"M {c.X - x},{c.Y - x} L {c.X + x},{c.Y + x} M {c.X + x},{c.Y - x} L {c.X - x},{c.Y + x}"),
            Stroke = xBrush,
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        hub.MouseEnter += (_, _) => AnimateColor(xBrush, Teal, 0.15);
        hub.MouseLeave += (_, _) => AnimateColor(xBrush, Color.FromRgb(0xFF, 0xFF, 0xFF), 0.15);
        hub.MouseLeftButtonDown += (_, _) => Cancel();
        ToolTipService.SetInitialShowDelay(hub, 1000);
        ToolTipService.SetToolTip(hub, Hl2Tip("Cancel"));

        _wheel.Children.Add(hub);
        _wheel.Children.Add(cross);
    }

    private void HoverSlice(Slice s, bool on)
    {
        if (_closing)
            return;
        AnimateColor(s.Brush, on ? Hover : Base, 0.4);
        double tx = on ? Math.Cos(s.MidRad) * ExtendPx : 0;
        double ty = on ? Math.Sin(s.MidRad) * ExtendPx : 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        s.Extend.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tx, Secs(0.4)) { EasingFunction = ease });
        s.Extend.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ty, Secs(0.4)) { EasingFunction = ease });
    }

    private void Choose(int i)
    {
        if (_closing)
            return;
        _closing = true;
        _result = _slices[i].Choice;

        // 0.15s teal flash, then (0.25s later) the close animation.
        AnimateColor(_slices[i].Brush, Teal, 0.15);
        var t = new System.Windows.Threading.DispatcherTimer { Interval = Secs(0.40) };
        t.Tick += (_, _) => { t.Stop(); PlayClose(); };
        t.Start();
    }

    private void Cancel()
    {
        if (_closing)
            return;
        _closing = true;
        _result = null;
        PlayClose();
    }

    private void PlayClose()
    {
        // 0.8s / 1.6 = 0.5s.
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var shrink = new DoubleAnimation(1, 0.6, Secs(0.5)) { EasingFunction = ease };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

        var fade = new DoubleAnimation(0, Secs(0.5));
        fade.Completed += (_, _) =>
        {
            var result = _result;
            Close();
            if (result is RadialChoice c)
                Chosen?.Invoke(c);
        };
        _wheel.BeginAnimation(OpacityProperty, fade);
    }

    // ---- helpers ---------------------------------------------------------------

    private static TimeSpan Secs(double s) => TimeSpan.FromSeconds(s);
    private static double Deg2Rad(double d) => d * Math.PI / 180.0;

    private static void AnimateColor(SolidColorBrush brush, Color to, double secs) =>
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, Secs(secs)));

    private static Geometry SectorGeometry(Point c, double inner, double outer, double a0, double a1)
    {
        Point P(double r, double a) => new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(inner, a0), isFilled: true, isClosed: true);
            ctx.LineTo(P(outer, a0), true, true);
            ctx.ArcTo(P(outer, a1), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(P(inner, a1), true, true);
            ctx.ArcTo(P(inner, a0), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    private static ToolTip Hl2Tip(string text) => new()
    {
        Background = Brushes.Transparent,
        BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        HasDropShadow = false,
        Padding = new Thickness(0),
        Content = new Border
        {
            Background = new SolidColorBrush(Base),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 5, 9, 5),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 13 },
        },
    };

    // ---- icons (scaled-up hotbar geometry, white) ------------------------------

    private static UIElement IconRecord()
    {
        var canvas = new Canvas { Width = 20, Height = 19 };
        canvas.Children.Add(new Rectangle
        {
            Width = 18, Height = 13, RadiusX = 1.5, RadiusY = 1.5,
            Stroke = Brushes.White, StrokeThickness = 1.6,
            Margin = default,
        });
        var rect = (Rectangle)canvas.Children[0];
        Canvas.SetLeft(rect, 1); Canvas.SetTop(rect, 1);
        var dot = new Ellipse { Width = 4.2, Height = 4.2, Fill = Brushes.White };
        Canvas.SetLeft(dot, 3.6); Canvas.SetTop(dot, 3.6);
        canvas.Children.Add(dot);
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M10,14 L10,17 M5.5,17.5 L14.5,17.5"),
            Stroke = Brushes.White, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round,
        });
        return Scaled(canvas, 20, 19);
    }

    private static UIElement IconAudio()
    {
        var canvas = new Canvas { Width = 18, Height = 20 };
        var body = new Rectangle
        {
            Width = 7, Height = 11, RadiusX = 3.5, RadiusY = 3.5,
            Stroke = Brushes.White, StrokeThickness = 1.6,
        };
        Canvas.SetLeft(body, 5.5); Canvas.SetTop(body, 1);
        canvas.Children.Add(body);
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M2.5,10 A6.5,6.5 0 0 0 15.5,10"),
            Stroke = Brushes.White, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round,
        });
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M9,16.5 L9,19"),
            Stroke = Brushes.White, StrokeThickness = 1.6,
        });
        return Scaled(canvas, 18, 20);
    }

    /// <summary>A white gear: eight teeth around a ring, hollow hub.</summary>
    private static UIElement IconSettings()
    {
        const double cx = 10, cy = 10;
        const double rOuter = 9.4, rInner = 6.6, rHub = 2.9;
        const int teeth = 8;
        const double half = 11 * Math.PI / 180; // half tooth width, radians

        var gear = new StreamGeometry();
        using (var ctx = gear.Open())
        {
            Point P(double r, double a) => new(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            double step = 2 * Math.PI / teeth;

            ctx.BeginFigure(P(rInner, -half - step / 2), isFilled: true, isClosed: true);
            for (int i = 0; i < teeth; i++)
            {
                double c = i * step;
                ctx.LineTo(P(rInner, c - half - step / 4), true, true);
                ctx.LineTo(P(rOuter, c - half), true, true);      // tooth rises
                ctx.LineTo(P(rOuter, c + half), true, true);      // tooth top
                ctx.LineTo(P(rInner, c + half + step / 4), true, true); // back down
            }

            // Hub as a counter-wound circle so it punches a hole (EvenOdd fill).
            ctx.BeginFigure(new Point(cx + rHub, cy), isFilled: true, isClosed: true);
            ctx.ArcTo(new Point(cx - rHub, cy), new Size(rHub, rHub), 0, false, SweepDirection.Clockwise, true, true);
            ctx.ArcTo(new Point(cx + rHub, cy), new Size(rHub, rHub), 0, false, SweepDirection.Clockwise, true, true);
        }
        gear.FillRule = FillRule.EvenOdd;
        gear.Freeze();

        var canvas = new Canvas { Width = 20, Height = 20 };
        canvas.Children.Add(new Path { Data = gear, Fill = Brushes.White });
        return Scaled(canvas, 20, 20);
    }

    private static UIElement Scaled(Canvas content, double w, double h) => new Viewbox
    {
        Width = 40, Height = 40 * h / w,
        Child = content,
    };
}
