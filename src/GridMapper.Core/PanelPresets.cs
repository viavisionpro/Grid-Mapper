namespace GridMapper.Core;

public sealed record PanelSizePreset(string Id, decimal WidthMm, decimal HeightMm);
public sealed record PanelResolutionPreset(string Id, string Pitch, int WidthPx, int HeightPx);

public static class PanelPresets
{
    public static IReadOnlyList<PanelSizePreset> Sizes { get; } = Array.AsReadOnly(new[] {
        new PanelSizePreset("500x500", 500, 500), new("500x1000", 500, 1000), new("960x960", 960, 960),
        new("600x337-5", 600, 337.5m), new("610x343", 610, 343), new("480x270", 480, 270) });

    public static IReadOnlyList<PanelResolutionPreset> Resolutions(decimal widthMm, decimal heightMm)
    {
        if (widthMm == 960 && heightMm == 960) return Array.AsReadOnly(new[] {
            new PanelResolutionPreset("p10", "10.0", 96, 96), new("p8", "8.0", 120, 120),
            new("p6-67", "6.67", 144, 144), new("p5", "5.0", 192, 192) });
        if (widthMm == 500 && heightMm is 500 or 1000)
        {
            var factor = heightMm == 1000 ? 2 : 1;
            return Array.AsReadOnly(new[] { new PanelResolutionPreset("p4-81", "4.81", 104, 104 * factor),
                new("p3-91", "3.91", 128, 128 * factor), new("p2-97", "2.97", 168, 168 * factor), new("p2-6", "2.6", 192, 192 * factor),
                new("p2-5", "2.5", 200, 200 * factor), new("p1-95", "1.95", 256, 256 * factor) });
        }
        if (widthMm == 600 && heightMm == 337.5m) return Array.AsReadOnly(new[] {
            new PanelResolutionPreset("p1-875", "1.875", 320, 180), new("p1-56", "1.56", 384, 216),
            new("p1-25", "1.25", 480, 270), new("p0-93", "0.93", 640, 360) });
        return Array.Empty<PanelResolutionPreset>();
    }

    public static GridSettings ForSize(GridSettings previous, PanelSizePreset size)
    {
        if (!Sizes.Contains(size)) throw new ArgumentException("Unknown panel size preset.", nameof(size));
        var choices = Resolutions(size.WidthMm, size.HeightMm);
        // Preserve a compatible module resolution (including 500x500 -> 500x1000).
        var selected = choices.FirstOrDefault(p => p.WidthPx == previous.WidthPx) ?? choices.FirstOrDefault();
        int width, height;
        if (selected is not null) { width = selected.WidthPx; height = selected.HeightPx; }
        else
        {
            // No unconfirmed product presets: choose a custom integer raster with
            // the exact physical aspect ratio, as close as possible to the old pitch.
            var w = decimal.ToInt32(size.WidthMm * 10); var h = decimal.ToInt32(size.HeightMm * 10);
            static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
            var gcd = Gcd(w, h); w /= gcd; h /= gcd;
            var requestedScale = decimal.Round(size.WidthMm * previous.WidthPx / previous.WidthMm / w);
            var maxScale = Math.Max(1, Math.Min(ModelLimits.MaxDimension / w, ModelLimits.MaxDimension / h));
            var scale = decimal.ToInt32(Math.Clamp(requestedScale, 1m, maxScale));
            width = checked(w * scale); height = checked(h * scale);
        }
        return previous with { WidthMm = size.WidthMm, HeightMm = size.HeightMm, WidthPx = width, HeightPx = height };
    }
}
