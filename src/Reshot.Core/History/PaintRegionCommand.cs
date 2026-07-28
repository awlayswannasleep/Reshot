using Reshot.Core.Document;
using SkiaSharp;

namespace Reshot.Core.History;

/// <summary>
/// Undo/redo for a raster edit to any layer (paint, effects, absolute mask):
/// stores before/after snapshots of just the affected region (ARCHITECTURE §5).
/// </summary>
public sealed class LayerRegionCommand : IUndoableCommand
{
    private readonly SKBitmap _layer;
    private readonly SKRectI _region;
    private readonly SKBitmap _before;
    private readonly SKBitmap _after;

    public LayerRegionCommand(SKBitmap layer, SKRectI region, SKBitmap before, SKBitmap after)
    {
        _layer = layer;
        _region = region;
        _before = before;
        _after = after;
    }

    public void Undo() => CaptureDocument.RestoreRegion(_layer, _region, _before);
    public void Redo() => CaptureDocument.RestoreRegion(_layer, _region, _after);
}
