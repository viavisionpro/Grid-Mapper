using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GridMapper.Core;
using GridMapper.Rendering;

namespace GridMapper.Desktop;

// The editor displays complete screen rasters independently of the export crop.
// Its extent is virtual: distant coordinates never allocate a giant bitmap.
internal sealed class WorkspacePreview : Control
{
    internal sealed record Frame(Bitmap Bitmap, double X, double Y, ScreenModel Source, LogoImage? Logo, RecordedScreen Vector, bool TransparentMasks = false, bool IsProjection = false);
    public IReadOnlyList<Frame>? Frames { get; set; }
    public Frame? SingleFrame { get; set; }
    public Rect SceneBounds { get; set; }
    public bool TransparentCanvas { get; set; }
    public Size CanvasSize { get; set; }
    internal Rect CanvasRect => SceneBounds.Width > 0 && SceneBounds.Height > 0
        ? new Rect((0 - SceneBounds.X) * Scale + (Bounds.Width - SceneBounds.Width * Scale) / 2,
            (0 - SceneBounds.Y) * Scale + (Bounds.Height - SceneBounds.Height * Scale) / 2, CanvasSize.Width * Scale, CanvasSize.Height * Scale) : default;
    private double Scale => Math.Min(Bounds.Width / SceneBounds.Width, Bounds.Height / SceneBounds.Height);
    public override void Render(DrawingContext context)
    {
        if (Frames is null && SingleFrame is null) return;
        if (SceneBounds.Width <= 0 || SceneBounds.Height <= 0) return;
        var scale = Math.Min(Bounds.Width / SceneBounds.Width, Bounds.Height / SceneBounds.Height);
        var origin = new Point((Bounds.Width - SceneBounds.Width * scale) / 2, (Bounds.Height - SceneBounds.Height * scale) / 2);
        if (Frames is not null && !TransparentCanvas) context.DrawRectangle(Brushes.Black, null, CanvasRect);
        foreach (var frame in Frames ?? [SingleFrame!])
        {
            var size = frame.Bitmap.Size;
            var target = new Rect(origin.X + ((Frames is null ? 0 : frame.X) - SceneBounds.X) * scale,
                origin.Y + ((Frames is null ? 0 : frame.Y) - SceneBounds.Y) * scale, size.Width * scale, size.Height * scale);
            context.Custom(frame.Vector.Draw(target, size));
        }
        if (Frames is null) return;
        // Workspace-only export boundary; screen coordinates still use the Canvas origin.
        var boundary = CanvasRect;
        var strokeRect = boundary.Deflate(.5);
        context.DrawRectangle(null, new Pen(Brushes.Black, 3), strokeRect);
        context.DrawRectangle(null, new Pen(Brush.Parse("#C8C8D2"), 1, DashStyle.Dash), strokeRect);
        var label = new FormattedText("0, 0", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Inter"), 11, Brushes.White);
        var labelPosition = boundary.TopLeft + new Vector(6, 6);
        context.DrawRectangle(Brush.Parse("#CC17171B"), null, new Rect(labelPosition - new Vector(3, 2), new Size(label.Width + 6, label.Height + 4)));
        context.DrawText(label, labelPosition);
    }
    public void ReplaceFrames(IReadOnlyList<Frame>? frames, Bitmap? preserve = null)
    {
        var previous = Frames; Frames = frames;
        var keep = frames?.Select(f => f.Bitmap).ToHashSet() ?? [];
        if (preserve is not null) keep.Add(preserve);
        if (previous is not null) foreach (var frame in previous)
            if (!keep.Contains(frame.Bitmap)) { frame.Bitmap.Dispose(); frame.Vector.Dispose(); }
    }
    public void DisposeFrames() => ReplaceFrames(null);
}
