using SkiaSharp;

namespace Reshot.Core.Tools;

/// <summary>Vector draw objects (SPEC §6.3–6.4), movable, unlike raster brush strokes.</summary>
public enum VectorKind
{
    Circle,
    Square,
    Line,
    Triangle,
    Arrow,
    Text,
}

/// <summary>
/// One vector object in the VectorLayer. Shapes are defined by a bounding rect
/// (circle/square/triangle) or two endpoints (line/arrow); text by a baseline
/// origin + string. Rendered with Skia; stroked with the chosen color/thickness.
/// </summary>
public sealed class VectorObject
{
    public VectorKind Kind { get; set; }
    public SKRect Bounds { get; set; }      // circle / square / triangle
    public SKPoint P1 { get; set; }         // line / arrow start, or text origin
    public SKPoint P2 { get; set; }         // line / arrow end
    public SKColor Color { get; set; }
    public float Thickness { get; set; } = 4f;
    public string Text { get; set; } = string.Empty;
    public float FontSize { get; set; } = 32f;

    /// <summary>Triangle apex direction, up when dragged upward, down when downward.</summary>
    public bool PointsUp { get; set; } = true;

    public void Draw(SKCanvas canvas)
    {
        using var stroke = new SKPaint
        {
            Color = Color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Thickness,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        switch (Kind)
        {
            case VectorKind.Circle:
                canvas.DrawOval(Bounds, stroke);
                break;
            case VectorKind.Square:
                canvas.DrawRect(Bounds, stroke);
                break;
            case VectorKind.Triangle:
                canvas.DrawPath(TrianglePath(), stroke);
                break;
            case VectorKind.Line:
                canvas.DrawLine(P1, P2, stroke);
                break;
            case VectorKind.Arrow:
                DrawArrow(canvas, stroke);
                break;
            case VectorKind.Text:
                DrawText(canvas);
                break;
        }
    }

    private SKPath TrianglePath()
    {
        var path = new SKPath();
        if (PointsUp)
        {
            path.MoveTo(Bounds.MidX, Bounds.Top);       // apex at top
            path.LineTo(Bounds.Right, Bounds.Bottom);
            path.LineTo(Bounds.Left, Bounds.Bottom);
        }
        else
        {
            path.MoveTo(Bounds.MidX, Bounds.Bottom);    // apex at bottom
            path.LineTo(Bounds.Right, Bounds.Top);
            path.LineTo(Bounds.Left, Bounds.Top);
        }
        path.Close();
        return path;
    }

    private void DrawArrow(SKCanvas canvas, SKPaint stroke)
    {
        canvas.DrawLine(P1, P2, stroke);

        // Arrowhead: two barbs scaled to the line length (with sane min/max).
        var dx = P2.X - P1.X;
        var dy = P2.Y - P1.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1f)
            return;

        var head = Math.Clamp(len * 0.25f, Thickness * 3f, 40f);
        var angle = MathF.Atan2(dy, dx);
        const float spread = 0.5f; // radians

        var left = new SKPoint(
            P2.X - head * MathF.Cos(angle - spread),
            P2.Y - head * MathF.Sin(angle - spread));
        var right = new SKPoint(
            P2.X - head * MathF.Cos(angle + spread),
            P2.Y - head * MathF.Sin(angle + spread));

        canvas.DrawLine(P2, left, stroke);
        canvas.DrawLine(P2, right, stroke);
    }

    private void DrawText(SKCanvas canvas)
    {
        if (string.IsNullOrEmpty(Text))
            return;

        using var paint = new SKPaint
        {
            Color = Color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            TextSize = FontSize,
            Typeface = SKTypeface.FromFamilyName("Segoe UI"),
        };

        // P1 is the top-left; offset down by the ascent so text sits below the click.
        var metrics = paint.FontMetrics;
        var lines = Text.Split('\n');
        var y = P1.Y - metrics.Ascent;
        foreach (var line in lines)
        {
            canvas.DrawText(line, P1.X, y, paint);
            y += paint.FontSpacing;
        }
    }

    /// <summary>Axis-aligned bounds of the object (region estimate for rasterization).</summary>
    public SKRect ComputeBounds()
    {
        return Kind switch
        {
            VectorKind.Line or VectorKind.Arrow => SKRect.Create(
                Math.Min(P1.X, P2.X), Math.Min(P1.Y, P2.Y),
                Math.Abs(P2.X - P1.X), Math.Abs(P2.Y - P1.Y)),
            VectorKind.Text => ComputeTextBounds(),
            _ => Bounds,
        };
    }

    /// <summary>Rough multi-line text extent: ~0.6 em per glyph, ~1.4 em line pitch.</summary>
    private SKRect ComputeTextBounds()
    {
        var lines = (Text ?? string.Empty).Split('\n');
        var maxLen = 1;
        foreach (var line in lines)
            maxLen = Math.Max(maxLen, line.Length);
        return SKRect.Create(P1.X, P1.Y, FontSize * maxLen * 0.6f, FontSize * 1.4f * lines.Length);
    }
}
