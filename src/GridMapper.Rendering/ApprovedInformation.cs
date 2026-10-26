using GridMapper.Core;
using SkiaSharp;

namespace GridMapper.Rendering;

public sealed record InformationLayout(SKRect Name, SKRect Details, SKRect Ramp, SKRect Logo,
    float NameFontSize, float DetailsFontSize, string DetailsText);

public static partial class SceneRenderer
{
    public static InformationLayout InformationBounds(SceneLayer layer, LogoImage? logo)
    {
        var b = layer.Bounds; var settings = layer.Information; var h = b.Height;
        var padding = Math.Max(1, h * .012f); const float gap = 0; var maxWidth = b.Width * .84f;
        var nameSize = Math.Max(6, h * .06f * settings.FontSize / 16f);
        using var nameFont = new SKFont(EmbeddedTypefaces.Get(settings.Font), nameSize) { Embolden = true };
        var name = layer.Details?.Name ?? "";
        if (nameFont.MeasureText(name) > maxWidth - padding * 4) nameFont.Size *= Math.Max(1, maxWidth - padding * 4) / Math.Max(1, nameFont.MeasureText(name));
        var details = new List<string>();
        if (settings.ShowResolution) details.Add($"{b.Width}×{b.Height} px");
        if (settings.ShowPhysicalSize && layer.Details is { } physical) details.Add(physical.PhysicalSize);
        if (settings.ShowCabinetCount && layer.Details is { } count) details.Add($"{count.CabinetCount} panels");
        var detailText = string.Join(" / ", details);
        using var detailFont = new SKFont(EmbeddedTypefaces.Get(EmbeddedFont.NotoSansMono), Math.Max(12, h * .02f));
        if (detailFont.MeasureText(detailText) > maxWidth - padding * 4) detailFont.Size *= Math.Max(1, maxWidth - padding * 4) / Math.Max(1, detailFont.MeasureText(detailText));
        var width = Math.Min(maxWidth, Math.Max(b.Width * .25f, Math.Max(settings.ShowName ? nameFont.MeasureText(name) : 0,
            detailText.Length > 0 ? detailFont.MeasureText(detailText) : 0) + padding * 4));
        var cx = b.X + b.Width / 2f; var cy = b.Y + h / 2f;
        SKRect Box(float y, float height) => new(cx - width / 2, y, cx + width / 2, y + height);
        var nameHeight = nameFont.Metrics.Descent - nameFont.Metrics.Ascent + padding * 2;
        var nameBox = settings.ShowName ? Box(cy - nameHeight / 2, nameHeight) : SKRect.Empty;
        var nextY = nameBox.IsEmpty ? cy : nameBox.Bottom + gap;
        var detailsHeight = detailFont.Metrics.Descent - detailFont.Metrics.Ascent + padding * 2;
        var detailsBox = detailText.Length > 0 ? Box(nameBox.IsEmpty ? cy - detailsHeight / 2 : nextY, detailsHeight) : SKRect.Empty;
        if (!detailsBox.IsEmpty) nextY = detailsBox.Bottom + gap;
        var rampBox = settings.Ramp ? Box(nextY, Math.Max(2, h * .02f)) : SKRect.Empty;
        if (nameBox.IsEmpty && detailsBox.IsEmpty && !rampBox.IsEmpty) rampBox.Offset(0, -rampBox.Height / 2);
        var logoBox = SKRect.Empty;
        if (logo is not null && layer.Logo.Enabled)
        {
            var anchorTop = !nameBox.IsEmpty ? nameBox.Top : !detailsBox.IsEmpty ? detailsBox.Top : !rampBox.IsEmpty ? rampBox.Top : cy;
            var logoGap = Math.Max(4, h * .025f); var availableHeight = Math.Max(1, anchorTop - b.Y - h * .025f - logoGap);
            var scale = Math.Min(h * .22f * (float)Math.Clamp(layer.Logo.ScalePercent, 70, 130) / 100 / logo.Height, maxWidth / logo.Width);
            scale = Math.Min(scale, availableHeight / logo.Height);
            var lw = logo.Width * scale; var lh = logo.Height * scale;
            var hasLower = !nameBox.IsEmpty || !detailsBox.IsEmpty || !rampBox.IsEmpty;
            var bottom = hasLower ? anchorTop - logoGap : cy + lh / 2;
            logoBox = new(cx - lw / 2, bottom - lh, cx + lw / 2, bottom);
        }
        return new(nameBox, detailsBox, rampBox, logoBox, nameFont.Size, detailFont.Size, detailText);
    }

    private static void DrawApprovedInformation(SKCanvas canvas, SceneLayer layer, IReadOnlyDictionary<string, LogoImage>? logos, bool allowMissingLogos)
    {
        LogoImage? logo = null;
        if (layer.Logo.Enabled && layer.Logo.SourcePath.Length > 0)
        {
            logos?.TryGetValue(layer.Logo.SourcePath, out logo);
            if (logo is null && !allowMissingLogos) throw new IOException("Не загружен логотип: " + layer.Logo.SourcePath);
        }
        var layout = InformationBounds(layer, logo); var color = layer.Information.BackgroundColor ?? new RgbaColor(24, 24, 24);
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = false };
        if (layer.Logo.Background && !layout.Logo.IsEmpty) canvas.DrawRect(layout.Logo, paint);
        if (logo is not null && !layout.Logo.IsEmpty)
        {
            using var bitmap = logo.Decode(); using var image = SKImage.FromBitmap(bitmap);
            using var imagePaint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(layer.Logo.OpacityPercent * 255m / 100m)), IsAntialias = true };
            canvas.DrawImage(image, layout.Logo, new SKSamplingOptions(SKFilterMode.Linear), imagePaint);
        }
        void Text(SKRect box, string value, SKFont font)
        {
            if (box.IsEmpty) return;
            paint.Color = new SKColor(color.R, color.G, color.B, color.A); paint.IsAntialias = false;
            if (layer.Information.Background) canvas.DrawRect(box, paint);
            paint.Color = SKColors.White; paint.IsAntialias = true;
            var baseline = box.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
            canvas.DrawText(value, box.MidX - font.MeasureText(value) / 2, baseline, font, paint);
        }
        using var nameFont = new SKFont(EmbeddedTypefaces.Get(layer.Information.Font), layout.NameFontSize) { Embolden = true };
        using var detailsFont = new SKFont(EmbeddedTypefaces.Get(EmbeddedFont.NotoSansMono), layout.DetailsFontSize);
        Text(layout.Name, layer.Details?.Name ?? "", nameFont); Text(layout.Details, layout.DetailsText, detailsFont);
        if (!layout.Ramp.IsEmpty)
        {
            using var shader = SKShader.CreateLinearGradient(new(layout.Ramp.Left, 0), new(layout.Ramp.Right, 0), [SKColors.Black, SKColors.White], null, SKShaderTileMode.Clamp);
            paint.Shader = shader; paint.Color = SKColors.White; paint.IsAntialias = false; canvas.DrawRect(layout.Ramp, paint);
        }
    }
}
