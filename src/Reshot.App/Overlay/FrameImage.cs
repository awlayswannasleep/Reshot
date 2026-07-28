using System.Windows.Media;
using System.Windows.Media.Imaging;
using Reshot.Capture;

namespace Reshot.App.Overlay;

/// <summary>Bridges a capture-layer <see cref="CapturedFrame"/> into a WPF image.</summary>
public static class FrameImage
{
    /// <summary>
    /// Wraps the frozen BGRA buffer as a frozen <see cref="BitmapSource"/>. Uses
    /// Bgr32 (the alpha byte is ignored) so the frame is fully opaque regardless of
    /// what the capture API put in the alpha channel. Frozen for zero-copy display.
    /// </summary>
    public static BitmapSource ToBitmapSource(CapturedFrame frame)
    {
        var bmp = BitmapSource.Create(
            frame.Width, frame.Height,
            96, 96,
            PixelFormats.Bgr32,
            palette: null,
            frame.PixelsBgra,
            frame.Stride);
        bmp.Freeze();
        return bmp;
    }
}
