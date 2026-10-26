using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GridMapper.Desktop;

internal sealed class LayerReorderOverlay : Control
{
    private RenderTargetBitmap? _ghost;
    public Rect? GhostBounds { get; set; }
    public Rect? ClipBounds { get; set; }
    public double? InsertionY { get; set; }
    public void Capture(Control row, double scaling)
    {
        Clear();
        _ghost = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)Math.Ceiling(row.Bounds.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(row.Bounds.Height * scaling))), new Vector(96 * scaling, 96 * scaling));
        _ghost.Render(row);
    }
    public void Clear()
    { _ghost?.Dispose(); _ghost = null; GhostBounds = ClipBounds = null; InsertionY = null; InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        if (ClipBounds is not { } clip) return;
        using var clipped = context.PushClip(clip);
        if (_ghost is not null && GhostBounds is { } rect)
        {
            using (context.PushOpacity(.94))
            {
                context.DrawRectangle(Brush.Parse("#34343B"), null, rect);
                context.DrawImage(_ghost, new Rect(0, 0, _ghost.PixelSize.Width, _ghost.PixelSize.Height), rect);
            }
            context.DrawRectangle(null, new Pen(Brush.Parse("#AEADB9"), 1), rect);
        }
        if (InsertionY is { } y)
        {
            var brush = Brush.Parse("#E75691");
            context.DrawLine(new Pen(brush, 2), new Point(clip.Left + 4, y), new Point(clip.Right - 4, y));
            context.DrawEllipse(brush, null, new Point(clip.Left + 4, y), 3, 3);
        }
    }
}
