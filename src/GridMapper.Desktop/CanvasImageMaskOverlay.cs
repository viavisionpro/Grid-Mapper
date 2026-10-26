using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GridMapper.Desktop;

internal sealed class CanvasImageMaskOverlay : Control
{
    internal static readonly IBrush FrameBrush = Brush.Parse("#58D8C8");
    public Bitmap? Bitmap { get; set; }
    public Rect? Frame { get; set; }
    public double ImageOpacity { get; set; }
    public bool Selected { get; set; }
    public bool ShowFrame { get; set; } = true;
    public IReadOnlyList<CabinetOverlay.GuideLine> Guides { get; set; } = [];
    internal static readonly (int X, int Y)[] Corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)];
    public override void Render(DrawingContext context)
    {
        if (Frame is not { } frame || Bitmap is not { } bitmap) return;
        if (ImageOpacity > 0) using (context.PushOpacity(ImageOpacity)) context.DrawImage(bitmap, new Rect(bitmap.Size), frame);
        if (ShowFrame)
        {
            context.DrawRectangle(null, new Pen(Brushes.Black, 3), frame);
            context.DrawRectangle(null, new Pen(FrameBrush, 1.5), frame);
        }
        var guidePen = new Pen(Brush.Parse("#E75691"), 1);
        foreach (var guide in Guides) context.DrawLine(guidePen, guide.Start, guide.End);
        if (!Selected) return;
        foreach (var (x, y) in Corners)
        {
            var p = CabinetOverlay.HandleCenter(frame, x, y);
            var box = new Rect(p.X - CabinetOverlay.HandleSize / 2, p.Y - CabinetOverlay.HandleSize / 2, CabinetOverlay.HandleSize, CabinetOverlay.HandleSize);
            context.DrawRectangle(FrameBrush, new Pen(Brushes.Black, 3), box);
            context.DrawRectangle(null, new Pen(Brushes.White, 1), box);
        }
    }
}
