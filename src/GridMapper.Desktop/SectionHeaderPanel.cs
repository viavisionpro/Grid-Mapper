using Avalonia;
using Avalonia.Controls;

namespace GridMapper.Desktop;

// Measure title and rule in sequence so long translations never overlap either.
public sealed class SectionHeaderPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 3) return default;
        Children[0].Measure(new Size(12, 12));
        Children[1].Measure(new Size(Math.Max(0, availableSize.Width - 18), availableSize.Height));
        Children[2].Measure(new Size(0, 1));
        return new Size(18 + Children[1].DesiredSize.Width, Math.Max(12, Children[1].DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3) return finalSize;
        Children[0].Arrange(new Rect(0, (finalSize.Height - 12) / 2, 12, 12));
        var title = Math.Min(Children[1].DesiredSize.Width, Math.Max(0, finalSize.Width - 18));
        Children[1].Arrange(new Rect(18, 0, title, finalSize.Height));
        var start = Math.Min(finalSize.Width, 18 + title + 12);
        Children[2].Arrange(new Rect(start, (finalSize.Height - 1) / 2, Math.Max(0, finalSize.Width - start), 1));
        return finalSize;
    }
}
