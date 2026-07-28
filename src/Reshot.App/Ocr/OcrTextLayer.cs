using System.Text;
using SkiaSharp;

namespace Reshot.App.Ocr;

/// <summary>
/// The interactive text layer produced by OCR: recognised words positioned over the
/// frozen frame, with a click-drag selection model (like selecting text on a PDF /
/// Google Translate image). Selection spans words in reading order; copy joins them
/// with spaces, breaking lines with newlines.
/// </summary>
public sealed class OcrTextLayer
{
    private readonly List<OcrWord> _words;
    private int _anchor = -1;   // where a drag began (reading-order index)
    private int _focus = -1;    // where it currently is

    private static readonly SKColor Accent = new(0x3C, 0x98, 0x98); // app teal

    public OcrTextLayer(IReadOnlyList<OcrWord> words) => _words = words.ToList();

    public bool HasWords => _words.Count > 0;
    public bool HasSelection => _anchor >= 0 && _focus >= 0;

    /// <summary>Nearest word to a point (by distance to its box), for forgiving hit-testing.</summary>
    private int NearestWord(SKPoint p)
    {
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < _words.Count; i++)
        {
            var r = _words[i].Rect;
            float cx = Math.Clamp(p.X, r.Left, r.Right);
            float cy = Math.Clamp(p.Y, r.Top, r.Bottom);
            float dx = p.X - cx, dy = p.Y - cy;
            float d = dx * dx + dy * dy;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    public void StartSelect(SKPoint p) => _anchor = _focus = NearestWord(p);

    public void ExtendSelect(SKPoint p)
    {
        int i = NearestWord(p);
        if (i >= 0)
            _focus = i;
    }

    public void SelectAll()
    {
        if (_words.Count == 0)
            return;
        _anchor = 0;
        _focus = _words.Count - 1;
    }

    public void ClearSelection() => _anchor = _focus = -1;

    public string SelectedText() =>
        HasSelection ? Join(Math.Min(_anchor, _focus), Math.Max(_anchor, _focus)) : string.Empty;

    public string AllText() => _words.Count == 0 ? string.Empty : Join(0, _words.Count - 1);

    private string Join(int a, int b)
    {
        var sb = new StringBuilder();
        int line = _words[a].Line;
        for (int i = a; i <= b; i++)
        {
            if (_words[i].Line != line)
            {
                sb.Append('\n');
                line = _words[i].Line;
            }
            else if (i > a)
            {
                sb.Append(' ');
            }
            sb.Append(_words[i].Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Draws a tight teal outline (not a fill, so the letters stay uncovered) around each
    /// recognised word; the selected span gets a bolder, opaque outline. Canvas coordinates.
    /// </summary>
    public void Render(SKCanvas canvas)
    {
        if (_words.Count == 0)
            return;

        int a = HasSelection ? Math.Min(_anchor, _focus) : -1;
        int b = HasSelection ? Math.Max(_anchor, _focus) : -1;

        using var outline = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f,
            Color = Accent.WithAlpha(110), IsAntialias = true,
        };
        using var selected = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f,
            Color = Accent, IsAntialias = true,
        };

        for (int i = 0; i < _words.Count; i++)
        {
            var r = SKRect.Inflate(_words[i].Rect, 1.5f, 1.5f);
            canvas.DrawRect(r, i >= a && i <= b ? selected : outline);
        }
    }
}
