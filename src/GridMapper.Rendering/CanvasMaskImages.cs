using GridMapper.Core;
using SkiaSharp;

namespace GridMapper.Rendering;

public sealed record CanvasMaskPreview(int Width, int Height, byte[] Png);
public sealed record ImportedCanvasMask(EmbeddedLogo Asset, CanvasMaskPreview Preview);

public static class CanvasMaskImages
{
    public const int MaxFileBytes = 8 * 1024 * 1024;
    public const int MaxDimension = 16384;
    public const int PreviewDimension = 2048;
    public static bool SupportedExtension(string name) => Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";

    public static Task<ImportedCanvasMask> ImportAsync(string path, CancellationToken token = default) => Task.Run(async () =>
    {
        if (!SupportedExtension(path)) throw new IOException("mask.format");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length is <= 0 or > MaxFileBytes) throw new IOException("mask.fileLimit");
        using var buffer = new MemoryStream(); var chunk = new byte[81920];
        while (true)
        {
            var n = await input.ReadAsync(chunk, token).ConfigureAwait(false); if (n == 0) break;
            if (buffer.Length + n > MaxFileBytes) throw new IOException("mask.fileLimit");
            buffer.Write(chunk, 0, n);
        }
        var bytes = buffer.ToArray(); var preview = Decode(bytes, token);
        return new ImportedCanvasMask(new(Convert.ToBase64String(bytes), Path.GetFileName(path)), preview);
    }, token);

    public static Task<CanvasMaskPreview> DecodeAsync(EmbeddedLogo image, CancellationToken token = default) =>
        Task.Run(() => Decode(Convert.FromBase64String(image.DataBase64), token), token);

    public static CanvasMaskPreview Decode(byte[] bytes, CancellationToken token = default)
    {
        if (bytes.Length is <= 0 or > MaxFileBytes) throw new IOException("mask.fileLimit");
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new IOException("mask.invalid");
        if (codec.EncodedFormat is not SKEncodedImageFormat.Png and not SKEncodedImageFormat.Jpeg and not SKEncodedImageFormat.Webp
            || codec.FrameCount > 1 || IsAnimatedWebp(bytes)) throw new IOException("mask.format");
        var width = codec.Info.Width; var height = codec.Info.Height;
        if (width is < 1 or > MaxDimension || height is < 1 or > MaxDimension) throw new IOException("mask.dimensionLimit");
        token.ThrowIfCancellationRequested();
        using var colorSpace = SKColorSpace.CreateSrgb();
        using var decoded = DecodePixels(codec, bytes, colorSpace, token);
        var origin = codec.EncodedOrigin;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var oriented = new SKBitmap(new SKImageInfo(swap ? decoded.Height : decoded.Width, swap ? decoded.Width : decoded.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        using (var canvas = new SKCanvas(oriented))
        {
            canvas.Clear(SKColors.Transparent);
            var w = decoded.Width; var h = decoded.Height;
            var matrix = origin switch
            {
                SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
                SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
                SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
                SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
                SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
                SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
                _ => SKMatrix.Identity
            };
            canvas.SetMatrix(matrix); canvas.DrawBitmap(decoded, 0, 0);
        }
        token.ThrowIfCancellationRequested();
        using var image = SKImage.FromBitmap(oriented);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new IOException("mask.invalid");
        return new(swap ? height : width, swap ? width : height, png.ToArray());
    }
    private static SKBitmap DecodePixels(SKCodec codec, byte[] bytes, SKColorSpace space, CancellationToken token)
    {
        var factor = Math.Min(1f, (float)PreviewDimension / Math.Max(codec.Info.Width, codec.Info.Height));
        // Native JPEG/WebP sampling is bounded. PNG has no native scaled GetPixels;
        // stream its rows into the small preview instead of allocating a 1 GiB raster.
        if (codec.EncodedFormat == SKEncodedImageFormat.Png)
        {
            using var sourceSpace = codec.Info.ColorSpace;
            return SampledPng.Decode(bytes, PreviewDimension, sourceSpace ?? space, token);
        }
        var size = codec.GetScaledDimensions(factor);
        for (var i = 0; i < 4 && (size.Width > PreviewDimension || size.Height > PreviewDimension); i++) size = codec.GetScaledDimensions(factor *= .5f);
        if (size.Width > PreviewDimension || size.Height > PreviewDimension) throw new IOException("mask.invalid");
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul, space);
        var bitmap = new SKBitmap(info);
        try
        {
            if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) throw new IOException("mask.invalid");
            token.ThrowIfCancellationRequested(); return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
    private static bool IsAnimatedWebp(byte[] bytes)
    {
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return false;
        for (long offset = 12; offset + 8 <= bytes.Length;)
        {
            var n = (int)offset;
            if (bytes.AsSpan(n, 4).SequenceEqual("ANIM"u8) || bytes.AsSpan(n, 4).SequenceEqual("ANMF"u8)) return true;
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(n + 4, 4));
            offset += 8L + length + (length & 1);
        }
        return false;
    }
}
