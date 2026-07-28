using Reshot.Core.Tools;
using SkiaSharp;

namespace Reshot.Core.Document;

/// <summary>
/// The editable state of a capture session (ARCHITECTURE §3). Layers, bottom→top:
/// EffectsLayer (blur/pixelize over the untouched base), PaintLayer (brush strokes
/// plus rasterized shapes/text — vectors bake to permanent ink on commit). Plus an
/// AbsoluteMask that punches the whole composite (incl. the base frame) to
/// transparency. Sized in physical pixels; Skia only.
/// </summary>
public sealed class CaptureDocument : IDisposable
{
    private readonly SKCanvas _paintCanvas;
    private readonly SKCanvas _effectsCanvas;
    private readonly SKCanvas _absoluteCanvas;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Blur/pixelize results over the base, transparent where untouched.</summary>
    public SKBitmap EffectsLayer { get; }

    /// <summary>Permanent brush strokes and rasterized shapes/text, transparent where nothing was drawn.</summary>
    public SKBitmap PaintLayer { get; }

    /// <summary>Coverage of the Absolute Eraser (opaque = punch to transparency on export).</summary>
    public SKBitmap AbsoluteMask { get; }

    public bool HasPaint { get; private set; }
    public bool HasEffects { get; private set; }
    public bool HasAbsolute { get; private set; }

    public CaptureDocument(int width, int height)
    {
        Width = width;
        Height = height;

        PaintLayer = NewLayer(width, height);
        EffectsLayer = NewLayer(width, height);
        AbsoluteMask = NewLayer(width, height);

        _paintCanvas = new SKCanvas(PaintLayer);
        _effectsCanvas = new SKCanvas(EffectsLayer);
        _absoluteCanvas = new SKCanvas(AbsoluteMask);
    }

    private static SKBitmap NewLayer(int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.Transparent);
        return bmp;
    }

    // ---- Brush -----------------------------------------------------------------

    public void CommitStroke(SKPath stroke, SKPaint paint, SKPath? clip)
    {
        _paintCanvas.Save();
        if (clip is not null)
            _paintCanvas.ClipPath(clip, antialias: true);
        _paintCanvas.DrawPath(stroke, paint);
        _paintCanvas.Restore();
        HasPaint = true;
    }

    /// <summary>
    /// Rasterizes a finished vector object (shape, text) into the paint layer.
    /// Vectors are baked to permanent ink on commit, so the eraser treats them
    /// exactly like brush strokes (pixel-wise, never whole-object).
    /// </summary>
    public void CommitVector(VectorObject v, SKPath? clip)
    {
        _paintCanvas.Save();
        if (clip is not null)
            _paintCanvas.ClipPath(clip, antialias: true);
        v.Draw(_paintCanvas);
        _paintCanvas.Restore();
        HasPaint = true;
    }

    // ---- Effects & erasers -----------------------------------------------------

    /// <summary>Stamps effect-source pixels (blurred/pixelized base) into the area.</summary>
    public void ApplyEffect(SKBitmap source, SKPath area, SKPath? selectionClip)
    {
        _effectsCanvas.Save();
        if (selectionClip is not null)
            _effectsCanvas.ClipPath(selectionClip, antialias: true);
        _effectsCanvas.ClipPath(area, antialias: true);
        _effectsCanvas.DrawBitmap(source, 0, 0);
        _effectsCanvas.Restore();
        HasEffects = true;
    }

    /// <summary>Filter Eraser: clears the effects layer in the area (returns the original).</summary>
    public void EraseEffects(SKPath area, SKPath? selectionClip) =>
        ClearArea(_effectsCanvas, area, selectionClip);

    /// <summary>Eraser: clears painted pixels in the area.</summary>
    public void ErasePaint(SKPath area, SKPath? selectionClip) =>
        ClearArea(_paintCanvas, area, selectionClip);

    /// <summary>Absolute Eraser: marks the area to be punched to transparency on export.</summary>
    public void AbsoluteErase(SKPath area, SKPath? selectionClip)
    {
        _absoluteCanvas.Save();
        if (selectionClip is not null)
            _absoluteCanvas.ClipPath(selectionClip, antialias: true);
        _absoluteCanvas.ClipPath(area, antialias: true);
        using var paint = new SKPaint { Color = SKColors.White };
        _absoluteCanvas.DrawPaint(paint); // DrawPaint respects the clip (Clear does not)
        _absoluteCanvas.Restore();
        HasAbsolute = true;
    }

    private static void ClearArea(SKCanvas canvas, SKPath area, SKPath? selectionClip)
    {
        canvas.Save();
        if (selectionClip is not null)
            canvas.ClipPath(selectionClip, antialias: true);
        canvas.ClipPath(area, antialias: true);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Clear };
        canvas.DrawPaint(paint); // clears only within the clip
        canvas.Restore();
    }

    // ---- Soft (Photoshop-style) erasers ---------------------------------------
    //
    // A "coverage" bitmap is an opaque greyscale image whose luminance is the per-pixel
    // erase strength (0 = keep, 1 = fully erase), with the eraser's Global-opacity already
    // baked in. CreateCoverageAlphaFilter turns that luminance into a pure alpha mask so a
    // single DstOut removes exactly that fraction of the layer — no compounding.

    /// <summary>Maps a greyscale coverage bitmap to a pure-alpha mask (alpha = red, rgb = 0).</summary>
    public static SKColorFilter CreateCoverageAlphaFilter() => SKColorFilter.CreateColorMatrix(new float[]
    {
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        1, 0, 0, 0, 0,
    });

    /// <summary>Eraser: removes painted pixels by the coverage strength (soft edge).</summary>
    public void ErasePaintCoverage(SKBitmap coverage, int dx, int dy, SKPath? selectionClip) =>
        ApplyCoverage(_paintCanvas, coverage, dx, dy, selectionClip, SKBlendMode.DstOut);

    /// <summary>Filter Eraser: removes effect pixels by the coverage strength (soft edge).</summary>
    public void EraseEffectsCoverage(SKBitmap coverage, int dx, int dy, SKPath? selectionClip) =>
        ApplyCoverage(_effectsCanvas, coverage, dx, dy, selectionClip, SKBlendMode.DstOut);

    /// <summary>Absolute Eraser: accumulates coverage into the punch-through mask (soft edge).</summary>
    public void AbsoluteEraseCoverage(SKBitmap coverage, int dx, int dy, SKPath? selectionClip)
    {
        ApplyCoverage(_absoluteCanvas, coverage, dx, dy, selectionClip, SKBlendMode.SrcOver);
        HasAbsolute = true;
    }

    private static void ApplyCoverage(SKCanvas canvas, SKBitmap coverage, int dx, int dy,
        SKPath? selectionClip, SKBlendMode blend)
    {
        canvas.Save();
        if (selectionClip is not null)
            canvas.ClipPath(selectionClip, antialias: true);
        using var filter = CreateCoverageAlphaFilter();
        using var paint = new SKPaint { BlendMode = blend, ColorFilter = filter };
        canvas.DrawBitmap(coverage, dx, dy, paint);
        canvas.Restore();
    }

    // ---- Undo snapshots (generic over any layer) -------------------------------

    /// <summary>Copies a rectangular region of a layer for an undo snapshot.</summary>
    public static SKBitmap SnapshotRegion(SKBitmap layer, SKRectI region)
    {
        var snap = new SKBitmap(new SKImageInfo(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(snap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(
            layer,
            SKRect.Create(region.Left, region.Top, region.Width, region.Height),
            SKRect.Create(0, 0, region.Width, region.Height));
        return snap;
    }

    /// <summary>Overwrites a region of a layer with a snapshot (undo/redo).</summary>
    public static void RestoreRegion(SKBitmap layer, SKRectI region, SKBitmap snapshot)
    {
        using var canvas = new SKCanvas(layer);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawBitmap(
            snapshot,
            SKRect.Create(0, 0, region.Width, region.Height),
            SKRect.Create(region.Left, region.Top, region.Width, region.Height),
            paint);
    }

    // ---- Composition -----------------------------------------------------------

    public bool HasContent => HasPaint || HasEffects || HasAbsolute;

    /// <summary>
    /// Full export composite: base → effects → paint, then the Absolute
    /// Eraser punches the whole thing (incl. the base) to transparency.
    /// </summary>
    public SKBitmap RenderComposite(SKBitmap baseBitmap)
    {
        var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(baseBitmap, 0, 0);
        canvas.DrawBitmap(EffectsLayer, 0, 0);
        canvas.DrawBitmap(PaintLayer, 0, 0);

        if (HasAbsolute)
        {
            using var punch = new SKPaint { BlendMode = SKBlendMode.DstOut };
            canvas.DrawBitmap(AbsoluteMask, 0, 0, punch);
        }
        return bmp;
    }

    /// <summary>
    /// Composites effects → paint into one frame-sized overlay bitmap
    /// (transparent where empty). The base frame and the absolute mask are applied
    /// by the exporter around this.
    /// </summary>
    public SKBitmap RenderOverlay()
    {
        var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(EffectsLayer, 0, 0);
        canvas.DrawBitmap(PaintLayer, 0, 0);
        return bmp;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _paintCanvas.Dispose();
        _effectsCanvas.Dispose();
        _absoluteCanvas.Dispose();
        PaintLayer.Dispose();
        EffectsLayer.Dispose();
        AbsoluteMask.Dispose();
    }
}
