namespace Reshot.Capture;

/// <summary>
/// One frozen snapshot of the whole virtual desktop, as tightly-packed BGRA32
/// pixels (stride == Width*4). Coordinates are in virtual-desktop space so the
/// overlay can map screen points straight into the buffer. UI-agnostic on purpose
/// (Reshot.Core / Reshot.Capture never depend on WPF).
/// </summary>
public sealed class CapturedFrame
{
    public required byte[] PixelsBgra { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Bytes per row. Always <c>Width * 4</c>, rows are compacted on capture.</summary>
    public int Stride => Width * 4;

    /// <summary>X of the virtual desktop's top-left in screen coordinates (can be negative).</summary>
    public required int VirtualLeft { get; init; }

    /// <summary>Y of the virtual desktop's top-left in screen coordinates (can be negative).</summary>
    public required int VirtualTop { get; init; }

    /// <summary>The monitors that make up this frame, in screen coordinates.</summary>
    public required IReadOnlyList<CapturedMonitor> Monitors { get; init; }
}

/// <summary>A single monitor's placement within the virtual desktop.</summary>
public sealed record CapturedMonitor(int Left, int Top, int Width, int Height, bool IsPrimary)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}
