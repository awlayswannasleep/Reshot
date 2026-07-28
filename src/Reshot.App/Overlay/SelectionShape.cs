using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace Reshot.App.Overlay;

/// <summary>The selection sub-tools from SPEC §4.2.</summary>
public enum ShapeKind
{
    Rectangle,
    Ellipse,
    Lasso,
    Polygon,
    Triangle,
}

/// <summary>
/// One selection: a shape plus its bounding box. Bounding-box shapes (rectangle,
/// ellipse, triangle) are fully described by <see cref="Bounds"/>. Freeform shapes
/// (lasso, polygon) also keep their outline as points normalized to [0,1] within
/// the bounds, so resizing the box just re-maps them. Geometry is produced on
/// demand so the same shape can render in DIP (overlay) or physical px (export).
/// </summary>
public sealed class Selection
{
    public ShapeKind Kind { get; set; }
    public Rect Bounds { get; set; }

    /// <summary>Outline points in [0,1] space relative to <see cref="Bounds"/> (lasso/polygon only).</summary>
    public IReadOnlyList<Point>? NormalizedPoints { get; set; }

    public bool IsFreeform => Kind is ShapeKind.Lasso or ShapeKind.Polygon;

    /// <summary>Builds the shape geometry positioned within the current bounds.</summary>
    public Geometry BuildGeometry() => BuildGeometry(Bounds);

    /// <summary>
    /// Builds the shape geometry mapped into an arbitrary rect. Passing an origin
    /// rect like (0,0,w,h) yields crop-local geometry for export masking.
    /// </summary>
    public Geometry BuildGeometry(Rect b)
    {
        Geometry geometry = Kind switch
        {
            ShapeKind.Rectangle => new RectangleGeometry(b),
            ShapeKind.Ellipse => new EllipseGeometry(b),
            ShapeKind.Triangle => TriangleGeometry(b),
            ShapeKind.Lasso or ShapeKind.Polygon => PointsGeometry(b),
            _ => new RectangleGeometry(b),
        };
        geometry.Freeze();
        return geometry;
    }

    private static Geometry TriangleGeometry(Rect b)
    {
        // Apex at top-center, base along the bottom edge — inscribed in the box.
        var apex = new Point(b.Left + b.Width / 2, b.Top);
        var figure = new PathFigure { StartPoint = apex, IsClosed = true };
        figure.Segments.Add(new LineSegment(new Point(b.Right, b.Bottom), true));
        figure.Segments.Add(new LineSegment(new Point(b.Left, b.Bottom), true));
        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return geo;
    }

    private Geometry PointsGeometry(Rect b)
    {
        if (NormalizedPoints is not { Count: >= 2 } pts)
            return new RectangleGeometry(b);

        Point Map(Point n) => new(b.Left + n.X * b.Width, b.Top + n.Y * b.Height);

        var figure = new PathFigure { StartPoint = Map(pts[0]), IsClosed = true };
        for (var i = 1; i < pts.Count; i++)
            figure.Segments.Add(new LineSegment(Map(pts[i]), true));

        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return geo;
    }

    /// <summary>Computes the bounding box of a set of absolute points.</summary>
    public static Rect BoundsOf(IEnumerable<Point> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        foreach (var p in points)
        {
            any = true;
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
        return any ? new Rect(minX, minY, maxX - minX, maxY - minY) : Rect.Empty;
    }

    /// <summary>Normalizes absolute points into [0,1] space relative to <paramref name="bounds"/>.</summary>
    public static IReadOnlyList<Point> Normalize(IEnumerable<Point> points, Rect bounds)
    {
        var w = bounds.Width <= 0 ? 1 : bounds.Width;
        var h = bounds.Height <= 0 ? 1 : bounds.Height;
        return points.Select(p => new Point((p.X - bounds.Left) / w, (p.Y - bounds.Top) / h)).ToList();
    }
}
