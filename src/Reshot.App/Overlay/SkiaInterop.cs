using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using WpfGeometry = System.Windows.Media.Geometry;
using WpfPoint = System.Windows.Point;

namespace Reshot.App.Overlay;

/// <summary>Conversions between the WPF overlay world and the Skia editor canvas.</summary>
public static class SkiaInterop
{
    /// <summary>
    /// Flattens a WPF geometry (in DIP) to an <see cref="SKPath"/> scaled into
    /// physical pixels, used to clip brush strokes to the selection shape.
    /// </summary>
    public static SKPath ToSkPath(WpfGeometry geometry, double scaleX, double scaleY)
    {
        var flat = geometry.GetFlattenedPathGeometry();
        var path = new SKPath();

        foreach (var figure in flat.Figures)
        {
            var start = figure.StartPoint;
            path.MoveTo((float)(start.X * scaleX), (float)(start.Y * scaleY));

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case PolyLineSegment poly:
                        foreach (var p in poly.Points)
                            path.LineTo((float)(p.X * scaleX), (float)(p.Y * scaleY));
                        break;
                    case LineSegment line:
                        path.LineTo((float)(line.Point.X * scaleX), (float)(line.Point.Y * scaleY));
                        break;
                }
            }

            if (figure.IsClosed)
                path.Close();
        }

        return path;
    }

    /// <summary>Copies an SKBitmap (Bgra8888 premul) into a frozen WPF BitmapSource.</summary>
    public static BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        var writeable = new WriteableBitmap(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null);
        // Both are premultiplied BGRA of the same size, a straight pixel copy.
        writeable.WritePixels(
            new Int32Rect(0, 0, bitmap.Width, bitmap.Height),
            bitmap.GetPixels(),
            bitmap.ByteCount,
            bitmap.RowBytes);
        writeable.Freeze();
        return writeable;
    }

    /// <summary>Copies a WPF BitmapSource into an SKBitmap (Bgra8888 premul) for Skia encoding.</summary>
    public static SKBitmap ToSkBitmap(BitmapSource source)
    {
        var src = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

        var bitmap = new SKBitmap(new SKImageInfo(src.PixelWidth, src.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        src.CopyPixels(
            new Int32Rect(0, 0, src.PixelWidth, src.PixelHeight),
            bitmap.GetPixels(),
            bitmap.ByteCount,
            bitmap.RowBytes);
        return bitmap;
    }

    public static SKPoint ToSkPoint(WpfPoint p, double scaleX, double scaleY) =>
        new((float)(p.X * scaleX), (float)(p.Y * scaleY));
}
