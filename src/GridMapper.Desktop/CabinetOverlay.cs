using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GridMapper.Desktop;

internal sealed class CabinetOverlay : Control
{
    public Rect? Selection { get; set; }
    public Rect? Moving { get; set; }
    public bool Invalid { get; set; }
    public IReadOnlyList<(int X, int Y)> Handles { get; set; } = [];
    internal sealed record GuideLine(Point Start, Point End);
    public IReadOnlyList<GuideLine> Guides { get; set; } = [];
    internal const double HandleSize = 9;
    internal static Point HandleCenter(Rect rect, int x, int y) => new(
        x < 0 ? rect.Left : x > 0 ? rect.Right : rect.Center.X,
        y < 0 ? rect.Top : y > 0 ? rect.Bottom : rect.Center.Y);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Selection is { } selected)
        {
            context.DrawRectangle(null, new Pen(Brushes.Black, 4), selected);
            context.DrawRectangle(null, new Pen(Brush.Parse("#B8B8C4"), 2), selected);
        }
        if (Moving is { } moving)
        {
            var color = Invalid ? Colors.OrangeRed : Color.Parse("#B8B8C4");
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(64, color.R, color.G, color.B)), new Pen(new SolidColorBrush(color), 2), moving);
        }
        if (Selection is { } frame)
            foreach (var (x, y) in Handles)
            {
                var center = HandleCenter(frame, x, y);
                var handle = new Rect(center.X - HandleSize / 2, center.Y - HandleSize / 2, HandleSize, HandleSize);
                context.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 3), handle);
                context.DrawRectangle(null, new Pen(Brush.Parse("#B8B8C4"), 1), handle);
            }
        var guidePen = new Pen(Brush.Parse("#E75691"), 1);
        foreach (var guide in Guides)
        {
            context.DrawLine(guidePen, guide.Start, guide.End);
        }
    }
}
