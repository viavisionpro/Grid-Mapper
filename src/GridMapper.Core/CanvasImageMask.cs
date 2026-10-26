using System.Text.Json.Serialization;

namespace GridMapper.Core;

// Project-owned reference art. Render/export scenes deliberately never consume it.
// One width plus the original oriented dimensions makes nonuniform scaling impossible.
public sealed record CanvasImageMask
{
    public EmbeddedLogo? Image { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public bool Enabled { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public int TransparencyPercent { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsLocked { get; }
    [JsonIgnore] public double Height => Width * PixelHeight / PixelWidth;

    public CanvasImageMask(EmbeddedLogo? image = null, int pixelWidth = 1, int pixelHeight = 1,
        bool enabled = false, double x = 0, double y = 0, double width = 1, int transparencyPercent = 50, bool isLocked = false)
    {
        if (pixelWidth is < 1 or > 16384 || pixelHeight is < 1 or > 16384
            || !double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 100_000_000 || Math.Abs(y) > 100_000_000
            || !double.IsFinite(width) || width < .0001 || width > 1_000_000 || width * pixelHeight / pixelWidth > 1_000_000
            || transparencyPercent is < 0 or > 100)
            throw new ArgumentException("Invalid Canvas image-mask dimensions or settings.");
        Image = image; PixelWidth = pixelWidth; PixelHeight = pixelHeight; Enabled = enabled;
        X = x; Y = y; Width = width; TransparencyPercent = transparencyPercent; IsLocked = isLocked;
    }
    public CanvasImageMask With(bool? enabled = null, double? x = null, double? y = null,
        double? width = null, int? transparencyPercent = null, bool? isLocked = null) => new(Image, PixelWidth, PixelHeight,
            enabled ?? Enabled, x ?? X, y ?? Y, width ?? Width, transparencyPercent ?? TransparencyPercent, isLocked ?? IsLocked);
}
