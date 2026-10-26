using Avalonia;
using Avalonia.Media;

namespace GridMapper.Desktop;

// Keeps Image's layout/hit-test dimensions without allocating an invisible Canvas bitmap.
internal sealed class PreviewExtent(Size size) : IImage
{
    public Size Size { get; } = size;
    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) { }
}
