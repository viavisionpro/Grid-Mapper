namespace GridMapper.Core;

public readonly record struct ViewTransform
{
    public double Scale { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }

    public ViewTransform(double scale, double offsetX, double offsetY)
    {
        if (!double.IsFinite(scale) || scale <= 0
            || !double.IsFinite(offsetX) || !double.IsFinite(offsetY))
            throw new ArgumentOutOfRangeException(nameof(scale));
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public (double X, double Y) ToView(double x, double y) =>
        (x * Scale + OffsetX, y * Scale + OffsetY);
    public (double X, double Y) ToScene(double x, double y) =>
        ((x - OffsetX) / Scale, (y - OffsetY) / Scale);
}
