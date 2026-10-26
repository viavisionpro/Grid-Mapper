using GridMapper.Core;
using SkiaSharp;

namespace GridMapper.Rendering;

public static partial class SceneRenderer
{
    // Export always uses the requested pixel dimensions. The editor records the
    // same drawing commands so glyphs are rasterized at the display's real scale.
    public static byte[] RenderPng(ScreenScene scene, IReadOnlyDictionary<string, LogoImage>? logos = null, bool allowMissingLogos = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ModelLimits.ValidateRaster(scene.Width, scene.Height);
        RenderSafety.CheckMemory((long)scene.Width * scene.Height * 12);
        using var colorSpace = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(scene.Width, scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        if (bitmap.GetPixels() == 0) throw new OutOfMemoryException();
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var work = new RenderWork(cancellationToken, TimeSpan.FromSeconds(30));
        DrawScene(canvas, scene, logos, allowMissingLogos, work);
        var bytes = EncodePng(bitmap); work.Check(); return bytes;
    }
    public static byte[] RenderPng(SKPicture picture, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(picture); ModelLimits.ValidateRaster(width, height);
        RenderSafety.CheckMemory((long)width * height * 12);
        using var colorSpace = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        if (bitmap.GetPixels() == 0) throw new OutOfMemoryException();
        using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent); canvas.DrawPicture(picture);
        return EncodePng(bitmap);
    }
    private static byte[] EncodePng(SKBitmap bitmap)
    {
        // Lossless pixels; fast filtering/compression keeps live edits responsive.
        using var pixels = bitmap.PeekPixels();
        using var data = pixels.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.None, 1))
            ?? throw new InvalidOperationException("Не удалось создать PNG.");
        return data.ToArray();
    }

    public static SKPicture Record(ScreenScene scene, IReadOnlyDictionary<string, LogoImage>? logos = null, bool allowMissingLogos = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, scene.Width, scene.Height));
        DrawScene(canvas, scene, logos, allowMissingLogos, new RenderWork(default, TimeSpan.FromSeconds(30)));
        return recorder.EndRecording();
    }

    private static void DrawScene(SKCanvas canvas, ScreenScene scene, IReadOnlyDictionary<string, LogoImage>? logos, bool allowMissingLogos, RenderWork? work = null)
    {
        if (scene.Projection is { } projection) { DrawProjection(canvas, projection, work); return; }
        canvas.Save(); canvas.ClipRect(new SKRect(0, 0, scene.Width, scene.Height));
        using var paint = new SKPaint { IsAntialias = false };
        if (scene.OpaqueBackground) { paint.Color = SKColors.Black; canvas.DrawRect(0, 0, scene.Width, scene.Height, paint); }
        foreach (var layer in scene.Layers)
        {
            work?.Check();
            // Antialiased shapes and resampled central logos can differ by one channel when scan-converted
            // against a different clip. Composite their complete local raster so
            // workspace and exported crops retain exactly the same source pixels.
            if (layer.UnclippedSource is { } source && (layer.Decorations.Circle || layer.Decorations.CornerCircles || layer.Decorations.Diagonals
                || layer.Information.Enabled && layer.Logo.Enabled || layer.Information.IndependentParts && (layer.Information.HasParts || layer.Logo.Enabled)))
            {
                using var local = SKBitmap.Decode(RenderPng(new(source.Bounds.Width, source.Bounds.Height, [source], transparentMaskedPanels: scene.TransparentMaskedPanels), logos, allowMissingLogos));
                canvas.DrawBitmap(local, layer.Bounds.X, layer.Bounds.Y); continue;
            }
            canvas.Save();
            var bounds = layer.Bounds;
            canvas.ClipRect(new SKRect(Math.Max(0, bounds.X), Math.Max(0, bounds.Y),
                Math.Min(scene.Width, bounds.Right), Math.Min(scene.Height, bounds.Bottom)));
            if (scene.TransparentMaskedPanels)
                foreach (var masked in layer.Cabinets.Where(c => c.IsMasked))
                    canvas.ClipRect(new SKRect(masked.X, masked.Y, masked.X + masked.Width, masked.Y + masked.Height), SKClipOperation.Difference, false);
            paint.IsAntialias = false;
            foreach (var cabinet in layer.Cabinets)
            {
                var color = cabinet.Color;
                paint.Color = new SKColor(color.R, color.G, color.B, color.A);
                canvas.DrawRect(cabinet.X, cabinet.Y, cabinet.Width, cabinet.Height, paint);
            }
            DrawDecorations(canvas, layer);
            if (!layer.Information.IndependentParts && !layer.Information.Enabled && layer.Logo.Enabled && layer.Logo.SourcePath.Length > 0)
            {
                if (logos is not null && logos.TryGetValue(layer.Logo.SourcePath, out var logo)) DrawLogo(canvas, layer, logo);
                else if (!allowMissingLogos) throw new IOException("Не загружен файл логотипа: " + layer.Logo.SourcePath);
            }
            if (layer.CabinetLabels.Count > 0) DrawCabinetLabels(canvas, layer, EmbeddedTypefaces.Get(layer.Numbering.Font));
            if (layer.Information.IndependentParts) DrawApprovedInformation(canvas, layer, logos, allowMissingLogos);
            else if (layer.Information.Enabled) DrawInformation(canvas, layer, EmbeddedTypefaces.Get(layer.Information.Font), logos, allowMissingLogos);
            var textLines = layer.Text.Lines();
            if ((!layer.Information.Enabled || layer.Information.IndependentParts) && textLines.Count > 0)
            {
                using var font = new SKFont(EmbeddedTypefaces.Get(layer.Text.Font), layer.Text.FontSize);
                DrawTextBlock(canvas, textLines, font, bounds.X + layer.Text.X, bounds.Y + layer.Text.Y, layer.Text.Background);
            }
            // With the opaque Canvas / Single screen mode, masked panels cover
            // every label and decoration with black. Alpha mode clips them above.
            paint.IsAntialias = false; paint.Style = SKPaintStyle.Fill; paint.Color = SKColors.Black;
            foreach (var cabinet in layer.Cabinets.Where(c => c.IsMasked && !scene.TransparentMaskedPanels))
                canvas.DrawRect(cabinet.X, cabinet.Y, cabinet.Width, cabinet.Height, paint);
            canvas.Restore();
        }

        canvas.Restore();
    }

    private static class EmbeddedTypefaces
    {
        // Typefaces are immutable and safe to share between threads. Keep one
        // instance per embedded font for the process lifetime: unique faces on
        // every render caused measured native font-memory growth.
        private static readonly Lazy<SKTypeface> Sans = new(() => Load(EmbeddedFont.NotoSans));
        private static readonly Lazy<SKTypeface> Mono = new(() => Load(EmbeddedFont.NotoSansMono));
        public static SKTypeface Get(EmbeddedFont font)
        {
            EmbeddedFonts.Validate(font);
            return font == EmbeddedFont.NotoSansMono ? Mono.Value : Sans.Value;
        }
        private static SKTypeface Load(EmbeddedFont font)
        {
            var name = font == EmbeddedFont.NotoSansMono ? "NotoSansMono" : "NotoSans";
            using var stream = typeof(SceneRenderer).Assembly.GetManifestResourceStream($"GridMapper.Rendering.Assets.{name}.ttf")
                ?? throw new InvalidOperationException("Встроенный шрифт отсутствует.");
            return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("Встроенный шрифт не удалось прочитать.");
        }
    }

    private static void DrawLogo(SKCanvas canvas, SceneLayer layer, LogoImage logo)
    {
        var settings = layer.Logo; var bounds = layer.Bounds;
        var width = (float)(logo.Width * settings.ScalePercent / 100m);
        var height = (float)(logo.Height * settings.ScalePercent / 100m);
        var x = settings.Anchor switch
        {
            LogoAnchor.TopRight or LogoAnchor.BottomRight => bounds.Width - width - settings.OffsetX,
            LogoAnchor.Center => MathF.Floor((bounds.Width - width) / 2) + settings.OffsetX,
            _ => settings.OffsetX
        };
        var y = settings.Anchor switch
        {
            LogoAnchor.BottomLeft or LogoAnchor.BottomRight => bounds.Height - height - settings.OffsetY,
            LogoAnchor.Center => MathF.Floor((bounds.Height - height) / 2) + settings.OffsetY,
            _ => settings.OffsetY
        };
        var left = Math.Max(0, (int)MathF.Floor(x)); var top = Math.Max(0, (int)MathF.Floor(y));
        var right = Math.Min(bounds.Width, (int)MathF.Ceiling(x + width)); var bottom = Math.Min(bounds.Height, (int)MathF.Ceiling(y + height));
        if (left >= right || top >= bottom || settings.OpacityPercent == 0) return;
        using var bitmap = logo.Decode(); using var image = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(settings.OpacityPercent * 255m / 100m)) };
        // Resample in the screen's local raster so canvas placement cannot alter filter coordinates.
        using var raster = new SKBitmap(new SKImageInfo(right - left, bottom - top, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (raster.GetPixels() == 0) throw new OutOfMemoryException();
        using (var local = new SKCanvas(raster))
        {
            local.Clear(SKColors.Transparent); local.Translate(-left, -top);
            local.DrawImage(image, new SKRect(x, y, x + width, y + height), new SKSamplingOptions(SKFilterMode.Linear), paint);
        }
        canvas.DrawBitmap(raster, bounds.X + left, bounds.Y + top);
    }

    private static void DrawCabinetLabels(SKCanvas canvas, SceneLayer layer, SKTypeface typeface)
    {
        using var font = new SKFont(typeface, layer.Numbering.FontSize);
        using var paint = new SKPaint { IsAntialias = true };
        var metrics = font.Metrics;
        foreach (var label in layer.CabinetLabels)
        {
            var bounds = label.Bounds;
            var width = font.MeasureText(label.Text);
            var x = bounds.X + (bounds.Width - width) / 2f;
            var baseline = bounds.Y + bounds.Height / 2f - (metrics.Ascent + metrics.Descent) / 2f;
            canvas.Save();
            canvas.ClipRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
            if (layer.Numbering.Background)
            {
                paint.IsAntialias = false; paint.Color = new SKColor(0, 0, 0, 210);
                canvas.DrawRect(x - 4, baseline + metrics.Ascent - 3, width + 8, metrics.Descent - metrics.Ascent + 6, paint);
            }
            else
            {
                paint.IsAntialias = true; paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 2; paint.Color = SKColors.Black;
                canvas.DrawText(label.Text, x, baseline, font, paint);
            }
            paint.IsAntialias = true; paint.Style = SKPaintStyle.Fill; paint.Color = SKColors.White;
            canvas.DrawText(label.Text, x, baseline, font, paint);
            canvas.Restore();
        }
    }

    private static void DrawInformation(SKCanvas canvas, SceneLayer layer, SKTypeface typeface, IReadOnlyDictionary<string, LogoImage>? logos, bool allowMissingLogos)
    {
        var bounds = layer.Bounds; var settings = layer.Information;
        var unit = bounds.Height * settings.FontSize / 16f;
        var gap = Math.Max(2, bounds.Height * .008f);
        var maxWidth = bounds.Width * .84f;
        var blocks = new List<(string[] Lines, float FontSize, float MinWidth, EmbeddedFont Font)>();
        if (settings.ShowName && layer.Details is { } named) blocks.Add(([named.Name], Math.Clamp(unit * .075f, 8, bounds.Height * .12f), bounds.Width * .36f, settings.Font));
        if (layer.Text.Enabled && layer.Text.Lines().Count > 0) blocks.Add((layer.Text.Lines().ToArray(), Math.Clamp(unit * .024f * layer.Text.FontSize / 24f, 6, bounds.Height * .08f), bounds.Width * .46f, layer.Text.Font));
        var dimensions = new List<string>();
        if (settings.ShowPhysicalSize && layer.Details is { } physical) dimensions.Add(physical.PhysicalSize);
        if (settings.ShowResolution) dimensions.Add($"{bounds.Width} × {bounds.Height} px");
        if (settings.ShowCabinetCount) dimensions.Add(layer.InformationLines.FirstOrDefault(x => x.StartsWith("Кабинетов:", StringComparison.Ordinal)) ?? "");
        if (settings.ShowCoordinates) dimensions.AddRange(layer.InformationLines.Where(x => x.StartsWith("X/Y", StringComparison.Ordinal) || x.StartsWith("Offset", StringComparison.Ordinal)));
        if (dimensions.Count > 0) blocks.Add((dimensions.ToArray(), Math.Clamp(unit * .025f, 6, bounds.Height * .05f), bounds.Width * .18f, EmbeddedFont.NotoSansMono));
        var prepared = blocks.Select(block =>
        {
            var font = new SKFont(EmbeddedTypefaces.Get(block.Font), block.FontSize) { Embolden = settings.ShowName && layer.Details is not null && block.Lines.Length == 1 && block.Lines[0] == layer.Details.Name };
            var width = block.Lines.Max(line => font.MeasureText(line));
            if (width > maxWidth - gap * 4) font.Size *= Math.Max(1, maxWidth - gap * 4) / width;
            var lineHeight = font.Metrics.Descent - font.Metrics.Ascent + gap;
            return (block.Lines, Font: font, Width: Math.Min(maxWidth, Math.Max(block.MinWidth, block.Lines.Max(line => font.MeasureText(line)) + gap * 4)), Height: lineHeight * block.Lines.Length + gap * 2);
        }).ToArray();
        LogoImage? logo = null;
        if (layer.Logo.Enabled && layer.Logo.SourcePath.Length > 0)
        {
            if (logos is not null) logos.TryGetValue(layer.Logo.SourcePath, out logo);
            if (logo is null && !allowMissingLogos) throw new IOException("Не загружен файл логотипа: " + layer.Logo.SourcePath);
        }
        var logoHeight = logo is null ? 0 : Math.Min((float)(logo.Height * layer.Logo.ScalePercent / 100m), bounds.Height * .22f);
        var logoWidth = logo is null ? 0 : logoHeight * logo.Width / logo.Height;
        if (logoWidth > maxWidth) { logoHeight *= maxWidth / logoWidth; logoWidth = maxWidth; }
        var rampHeight = settings.Ramp ? Math.Max(2, bounds.Height * .055f) : 0;
        var totalHeight = prepared.Sum(b => b.Height + gap) + (logo is null ? 0 : logoHeight + gap * 2) + (settings.Ramp ? rampHeight + gap : 0);
        // Fit the stack as a whole, including multi-line captions on small screens.
        var shrink = Math.Min(1, bounds.Height * .78f / Math.Max(1, totalHeight));
        canvas.Save();
        canvas.Translate(bounds.X + bounds.Width / 2f, bounds.Y + (bounds.Height - totalHeight * shrink) / 2f);
        canvas.Scale(shrink);
        float y = 0;
        using var paint = new SKPaint { IsAntialias = true };
        if (logo is not null)
        {
            using var bitmap = logo.Decode(); using var image = SKImage.FromBitmap(bitmap);
            paint.Color = new SKColor(255, 255, 255, (byte)Math.Round(layer.Logo.OpacityPercent * 255m / 100m));
            canvas.DrawImage(image, new SKRect(-logoWidth / 2, y, logoWidth / 2, y + logoHeight), new SKSamplingOptions(SKFilterMode.Linear), paint);
            y += logoHeight + gap * 2;
        }
        foreach (var block in prepared)
        {
            if (settings.Background)
            {
                var color = settings.BackgroundColor ?? new RgbaColor(24, 24, 24);
                paint.Color = new SKColor(color.R, color.G, color.B, color.A); paint.IsAntialias = false;
                canvas.DrawRect(-block.Width / 2, y, block.Width, block.Height, paint);
            }
            paint.Color = SKColors.White; paint.IsAntialias = true;
            var baseline = y + gap - block.Font.Metrics.Ascent;
            foreach (var line in block.Lines)
            {
                canvas.DrawText(line, -block.Font.MeasureText(line) / 2, baseline, block.Font, paint);
                baseline += block.Font.Metrics.Descent - block.Font.Metrics.Ascent + gap;
            }
            block.Font.Dispose(); y += block.Height + gap;
        }
        if (settings.Ramp)
        {
            var width = bounds.Width * .56f;
            using var shader = SKShader.CreateLinearGradient(new SKPoint(-width / 2, 0), new SKPoint(width / 2, 0), [SKColors.Black, SKColors.White], null, SKShaderTileMode.Clamp);
            paint.Shader = shader; paint.Color = SKColors.White; paint.IsAntialias = false;
            canvas.DrawRect(-width / 2, y, width, rampHeight, paint);
        }
        canvas.Restore();
    }

    private static void DrawTextBlock(SKCanvas canvas, IReadOnlyList<string> lines, SKFont font, float x, float y, bool background, RgbaColor? backgroundColor = null)
    {
        using var paint = new SKPaint { IsAntialias = true };
        var metrics = font.Metrics;
        var lineHeight = metrics.Descent - metrics.Ascent + 4;
        var width = lines.Max(line => font.MeasureText(line));
        var height = lineHeight * lines.Count - 4;
        if (background)
        {
            var color = backgroundColor ?? new RgbaColor(0, 0, 0, 210);
            paint.IsAntialias = false; paint.Color = new SKColor(color.R, color.G, color.B, color.A);
            canvas.DrawRect(x - 4, y - 3, width + 8, height + 6, paint);
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var baseline = y - metrics.Ascent + i * lineHeight;
            paint.IsAntialias = true;
            if (!background)
            {
                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 2; paint.Color = SKColors.Black;
                canvas.DrawText(lines[i], x, baseline, font, paint);
            }
            paint.Style = SKPaintStyle.Fill; paint.Color = SKColors.White;
            canvas.DrawText(lines[i], x, baseline, font, paint);
        }
    }

    private static void DrawDecorations(SKCanvas canvas, SceneLayer layer)
    {
        var settings = layer.Decorations;
        if (!settings.Border && !settings.CornerMarkers && !settings.Diagonals && !settings.CenterPlus && !settings.Crosspoint && !settings.Raster && !settings.Circle && !settings.CornerCircles) return;
        if (layer.Decorations.Raster)
        {
            using var gridPaint = new SKPaint { Color = new SKColor(255, 255, 255, 180), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
            foreach (var cabinet in layer.Cabinets)
                canvas.DrawRect(cabinet.X + 0.5f, cabinet.Y + 0.5f, cabinet.Width - 1, cabinet.Height - 1, gridPaint);
        }
        var bounds = layer.Bounds;
        var left = bounds.X + .5f; var top = bounds.Y + .5f;
        var right = (float)bounds.Right - .5f; var bottom = (float)bounds.Bottom - .5f;
        if (settings.Circle || settings.CornerCircles)
        {
            using var circlePaint = new SKPaint { Color = new SKColor(255, 190, 215), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            if (settings.Circle) canvas.DrawCircle(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f, Math.Max(0, bounds.Height / 2f - .5f), circlePaint);
            if (settings.CornerCircles)
            {
                circlePaint.Color = new SKColor(255, 190, 215); circlePaint.StrokeWidth = 1.5f;
                var panelWidth = layer.Details?.PanelWidth ?? layer.Cabinets.FirstOrDefault()?.Width ?? 1;
                var panelHeight = layer.Details?.PanelHeight ?? layer.Cabinets.FirstOrDefault()?.Height ?? 1;
                // Calibration guides must remain circles for rectangular panels.
                // Use one diameter on both axes and fit it inside small screens.
                var diameter = Math.Min(Math.Min(panelWidth, panelHeight) * 2f, Math.Min(bounds.Width, bounds.Height));
                var inset = diameter / 2;
                foreach (var (x, y) in new[] { (bounds.X + inset, bounds.Y + inset), ((float)bounds.Right - inset, bounds.Y + inset), (bounds.X + inset, (float)bounds.Bottom - inset), ((float)bounds.Right - inset, (float)bounds.Bottom - inset) }.Distinct())
                    canvas.DrawCircle(x, y, Math.Max(0, inset - .5f), circlePaint);
            }
        }
        var lines = new List<(float X1, float Y1, float X2, float Y2)>();
        if (settings.Border)
        {
            lines.Add((left, top, right, top)); lines.Add((right, top, right, bottom));
            lines.Add((right, bottom, left, bottom)); lines.Add((left, bottom, left, top));
        }
        if (settings.CornerMarkers)
        {
            var length = Math.Clamp(Math.Min(bounds.Width, bounds.Height) / 6f, 2, 24);
            var dx = Math.Min(length, bounds.Width - 1); var dy = Math.Min(length, bounds.Height - 1);
            foreach (var (x, y, sx, sy) in new[] { (left, top, 1, 1), (right, top, -1, 1), (left, bottom, 1, -1), (right, bottom, -1, -1) })
            { lines.Add((x, y, x + sx * dx, y)); lines.Add((x, y, x, y + sy * dy)); }
        }
        if (settings.Diagonals)
        {
            using var diagonal = new SKPaint { Color = new SKColor(255, 190, 215), StrokeWidth = 1.5f, IsAntialias = true, StrokeCap = SKStrokeCap.Round, Style = SKPaintStyle.Stroke };
            canvas.DrawLine(left, top, right, bottom, diagonal); canvas.DrawLine(right, top, left, bottom, diagonal);
        }
        if (settings.Crosspoint)
        {
            var x = bounds.X + bounds.Width / 2 + .5f; var y = bounds.Y + bounds.Height / 2 + .5f;
            lines.Add((left, y, right, y)); lines.Add((x, top, x, bottom));
        }
        if (settings.CenterPlus)
        {
            // Choose an actual center pixel, including an even-sized raster.
            var x = bounds.X + bounds.Width / 2 + .5f; var y = bounds.Y + bounds.Height / 2 + .5f;
            var radius = Math.Clamp(Math.Min(bounds.Width, bounds.Height) / 12f, 2, 24);
            lines.Add((Math.Max(left, x - radius), y, Math.Min(right, x + radius), y));
            lines.Add((x, Math.Max(top, y - radius), x, Math.Min(bottom, y + radius)));
        }
        // A white line with black outline stays visible over both light and dark fills.
        using var stroke = new SKPaint { IsAntialias = false, StrokeCap = SKStrokeCap.Square, Style = SKPaintStyle.Stroke };
        foreach (var (color, width) in new[] { (SKColors.Black, 3f), (SKColors.White, 1f) })
        {
            stroke.Color = color; stroke.StrokeWidth = width;
            foreach (var line in lines) canvas.DrawLine(line.X1, line.Y1, line.X2, line.Y2, stroke);
        }
    }
}
