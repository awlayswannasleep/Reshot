using Reshot.Core.Document;
using SkiaSharp;
using Xunit;

namespace Reshot.Core.Tests;

// Verifies the soft-eraser coverage math: a greyscale coverage bitmap's luminance is
// turned into the fraction of layer alpha removed (Photoshop-style graded erasing).
public class SoftEraseTests
{
    private static SKBitmap OpaqueGray(int w, int h, byte v)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var c = new SKCanvas(bmp);
        c.Clear(new SKColor(v, v, v, 255));
        return bmp;
    }

    private static void FillOpaqueRed(SKBitmap layer)
    {
        using var c = new SKCanvas(layer);
        c.Clear(new SKColor(255, 0, 0, 255));
    }

    [Theory]
    [InlineData(255, 0)]     // full coverage -> fully erased
    [InlineData(0, 255)]     // no coverage -> untouched
    [InlineData(76, 179)]    // ~30% coverage -> ~70% alpha remains
    [InlineData(191, 64)]    // ~75% coverage -> ~25% alpha remains
    public void ErasePaintCoverage_reduces_alpha_by_coverage(byte coverageValue, int expectedAlpha)
    {
        using var doc = new CaptureDocument(8, 8);
        FillOpaqueRed(doc.PaintLayer);
        using var cov = OpaqueGray(8, 8, coverageValue);

        doc.ErasePaintCoverage(cov, 0, 0, null);

        var px = doc.PaintLayer.GetPixel(4, 4);
        Assert.InRange(px.Alpha, expectedAlpha - 2, expectedAlpha + 2);
        // Colour survives where not fully erased (only alpha changes).
        if (expectedAlpha > 0)
            Assert.Equal(255, px.Red);
    }

    [Fact]
    public void AbsoluteEraseCoverage_accumulates_toward_opaque()
    {
        using var doc = new CaptureDocument(8, 8);
        using var cov = OpaqueGray(8, 8, 76); // ~0.30 each pass

        doc.AbsoluteEraseCoverage(cov, 0, 0, null);
        var after1 = doc.AbsoluteMask.GetPixel(4, 4).Alpha;
        doc.AbsoluteEraseCoverage(cov, 0, 0, null);
        var after2 = doc.AbsoluteMask.GetPixel(4, 4).Alpha;

        Assert.True(after1 is >= 74 and <= 78);   // first pass ~0.30
        Assert.True(after2 > after1);              // second pass builds up
        Assert.True(doc.HasAbsolute);
    }
}
