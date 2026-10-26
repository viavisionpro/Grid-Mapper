using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace GridMapper.Desktop;

internal static class ResizeCursors
{
    internal static readonly Cursor Arrow = new(StandardCursorType.Arrow);
    internal static readonly Cursor Hand = new(OperatingSystem.IsMacOS() ? StandardCursorType.DragMove : StandardCursorType.Hand);
    // macOS maps SizeAll to a crosshair, so use actual arrowheads there.
    internal static readonly Cursor Move = OperatingSystem.IsMacOS() ? FourWay() : new(StandardCursorType.SizeAll);
    internal static readonly Cursor Horizontal = new(StandardCursorType.SizeWestEast);
    internal static readonly Cursor Vertical = new(StandardCursorType.SizeNorthSouth);
    // Avalonia's macOS corner cursors are crosshairs; provide actual two-headed arrows.
    internal static readonly Cursor NorthWestSouthEast = Diagonal(false);
    internal static readonly Cursor NorthEastSouthWest = Diagonal(true);

    private static Cursor FourWay()
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 32));
        surface.Canvas.Clear(SKColors.Transparent);
        using var path = new SKPath();
        path.MoveTo(16, 4); path.LineTo(16, 28); path.MoveTo(4, 16); path.LineTo(28, 16);
        path.MoveTo(11, 9); path.LineTo(16, 4); path.LineTo(21, 9);
        path.MoveTo(11, 23); path.LineTo(16, 28); path.LineTo(21, 23);
        path.MoveTo(9, 11); path.LineTo(4, 16); path.LineTo(9, 21);
        path.MoveTo(23, 11); path.LineTo(28, 16); path.LineTo(23, 21);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
        paint.Color = SKColors.White; paint.StrokeWidth = 5; surface.Canvas.DrawPath(path, paint);
        paint.Color = SKColors.Black; paint.StrokeWidth = 2; surface.Canvas.DrawPath(path, paint);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray()); using var bitmap = new Bitmap(stream);
        return new Cursor(bitmap, new PixelPoint(16, 16));
    }

    private static Cursor Diagonal(bool mirrored)
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 32));
        var canvas = surface.Canvas; canvas.Clear(SKColors.Transparent);
        if (mirrored) { canvas.Translate(32, 0); canvas.Scale(-1, 1); }
        using var path = new SKPath();
        path.MoveTo(8, 14); path.LineTo(8, 8); path.LineTo(14, 8);
        path.MoveTo(8, 8); path.LineTo(24, 24);
        path.MoveTo(18, 24); path.LineTo(24, 24); path.LineTo(24, 18);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Miter };
        paint.Color = SKColors.White; paint.StrokeWidth = 5; canvas.DrawPath(path, paint);
        paint.Color = SKColors.Black; paint.StrokeWidth = 2; canvas.DrawPath(path, paint);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray()); using var bitmap = new Bitmap(stream);
        return new Cursor(bitmap, new PixelPoint(16, 16));
    }
}
