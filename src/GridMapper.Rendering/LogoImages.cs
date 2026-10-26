using GridMapper.Core;
using SkiaSharp;
using System.Text;
using System.Runtime.InteropServices;

namespace GridMapper.Rendering;

public sealed class LogoImage
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] _png;
    private readonly byte[] _thumbnail;
    internal LogoImage(int width, int height, byte[] png, byte[] thumbnail) { Width = width; Height = height; _png = png; _thumbnail = thumbnail; }
    public int ByteCount => _png.Length + _thumbnail.Length;
    public Stream OpenThumbnail() => new MemoryStream(_thumbnail, writable: false);
    internal EmbeddedLogo Embed(string fileName) => new(Convert.ToBase64String(_png), fileName);
    internal int EncodedBytes => _png.Length;
    internal SKBitmap Decode() => SKBitmap.Decode(_png) ?? throw new IOException("Не удалось прочитать нормализованный логотип.");
}

public sealed record LoadedLogoImages(IReadOnlyDictionary<string, LogoImage> Images, IReadOnlyDictionary<string, string> Problems);

public static class LogoImages
{
    public const int MaxFileBytes = 8 * 1024 * 1024;
    public const int MaxDimension = 4096;
    public const long MaxPixels = 4_194_304;
    public const int MaxCacheBytes = 64 * 1024 * 1024;

    public static Task<LogoImage> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadCoreAsync(path, cancellationToken), cancellationToken);

    private static async Task<LogoImage> LoadCoreAsync(string path, CancellationToken cancellationToken) => Decode(await ReadCoreAsync(path, cancellationToken));

    public static Task<(LogoImage Image, EmbeddedLogo Asset)> ImportAsync(string path, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        var bytes = await ReadCoreAsync(path, cancellationToken);
        return (Decode(bytes), new EmbeddedLogo(Convert.ToBase64String(bytes), Path.GetFileName(path)));
    }, cancellationToken);

    private static async Task<byte[]> ReadCoreAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (!input.CanSeek) throw new IOException("Выберите обычный файл логотипа PNG или JPEG.");
        if (input.Length is <= 0 or > MaxFileBytes) throw new IOException("Логотип должен быть непустым файлом до 8 MiB.");
        using var buffer = new MemoryStream(); var chunk = new byte[81920];
        while (true)
        {
            var count = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (buffer.Length + count > MaxFileBytes) throw new IOException("Логотип превышает 8 MiB.");
            buffer.Write(chunk, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return buffer.ToArray();
    }

    public static LogoImage Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is <= 0 or > MaxFileBytes) throw new IOException("Логотип должен быть файлом до 8 MiB.");
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new IOException("Файл логотипа повреждён или не поддерживается.");
        if (codec.EncodedFormat is not SKEncodedImageFormat.Png and not SKEncodedImageFormat.Jpeg and not SKEncodedImageFormat.Webp || codec.FrameCount > 1)
            throw new IOException("Выберите статический PNG, JPEG или WebP.");
        var width = codec.Info.Width; var height = codec.Info.Height;
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new IOException("Логотип: до 4096 px на ось и 4 194 304 пикселей.");
        using var colorSpace = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace);
        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success) throw new IOException("Файл логотипа повреждён или не завершён.");
        var origin = codec.EncodedOrigin;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var oriented = new SKBitmap(new SKImageInfo(swap ? height : width, swap ? width : height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        if (origin == SKEncodedOrigin.TopLeft)
        {
            using var canvas = new SKCanvas(oriented); canvas.Clear(SKColors.Transparent); canvas.DrawBitmap(decoded, 0, 0);
        }
        else
        {
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var (dx, dy) = origin switch
                    {
                        SKEncodedOrigin.TopRight => (width - 1 - x, y),
                        SKEncodedOrigin.BottomRight => (width - 1 - x, height - 1 - y),
                        SKEncodedOrigin.BottomLeft => (x, height - 1 - y),
                        SKEncodedOrigin.LeftTop => (y, x),
                        SKEncodedOrigin.RightTop => (height - 1 - y, x),
                        SKEncodedOrigin.RightBottom => (height - 1 - y, width - 1 - x),
                        SKEncodedOrigin.LeftBottom => (y, width - 1 - x),
                        _ => throw new IOException("Неизвестная ориентация логотипа.")
                    };
                    oriented.SetPixel(dx, dy, decoded.GetPixel(x, y));
                }
        }
        using var image = SKImage.FromBitmap(oriented);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new IOException("Не удалось подготовить логотип.");
        var factor = Math.Min(1d, 96d / Math.Max(oriented.Width, oriented.Height));
        using var thumbnail = new SKBitmap(new SKImageInfo(Math.Max(1, (int)Math.Round(oriented.Width * factor)), Math.Max(1, (int)Math.Round(oriented.Height * factor)), SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        using (var canvas = new SKCanvas(thumbnail)) { canvas.Clear(SKColors.Transparent); canvas.DrawImage(image, new SKRect(0, 0, thumbnail.Width, thumbnail.Height), new SKSamplingOptions(SKFilterMode.Linear)); }
        using var thumbnailImage = SKImage.FromBitmap(thumbnail);
        using var thumbnailPng = thumbnailImage.Encode(SKEncodedImageFormat.Png, 100) ?? throw new IOException("Не удалось подготовить миниатюру логотипа.");
        return new(oriented.Width, oriented.Height, encoded.ToArray(), thumbnailPng.ToArray());
    }

    public static string ResolvePath(string sourcePath, string? projectPath) => Path.GetFullPath(Path.IsPathRooted(sourcePath)
        ? sourcePath : Path.Combine(projectPath is null ? Environment.CurrentDirectory : Path.GetDirectoryName(Path.GetFullPath(projectPath))!, sourcePath));

    public static ProjectModel ResolveProjectPaths(ProjectModel project, string projectPath)
    {
        var screens = project.AllScreens.Select(screen => ResolveScreenPath(screen, projectPath)).ToArray();
        return ReplaceScreenLayouts(project, screens);
    }

    public static Task<ProjectModel> EmbedProjectAsync(ProjectModel project, IReadOnlyDictionary<string, LogoImage> prepared, string? projectPath) => Task.Run(async () =>
    {
        var assets = new Dictionary<string, EmbeddedLogo>(); var screens = new List<ScreenModel>();
        foreach (var screen in project.AllScreens)
        {
            var logo = screen.Style.Logo;
            if (logo.EmbeddedImage is not null || logo.SourcePath.Length == 0) { screens.Add(screen); continue; }
            if (!assets.TryGetValue(logo.SourcePath, out var asset))
            {
                if (prepared.TryGetValue(logo.SourcePath, out var image) && image.EncodedBytes <= MaxFileBytes)
                    asset = image.Embed(Path.GetFileName(logo.SourcePath));
                else
                {
                    try { asset = (await ImportAsync(ResolvePath(logo.SourcePath, projectPath))).Asset; }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        // A missing legacy reference must not prevent saving unrelated work
                        // or silently remove the user's retained source and parameters.
                        screens.Add(screen); continue;
                    }
                }
                assets[logo.SourcePath] = asset;
            }
            screens.Add(screen.WithStyle(screen.Style.WithLogo(new(logo.Enabled, asset.Key, logo.Anchor, logo.OffsetX, logo.OffsetY,
                logo.ScalePercent, logo.OpacityPercent, asset, logo.BackgroundOverride))));
        }
        return ReplaceScreenLayouts(project, screens);
    });

    private static ProjectModel ReplaceScreenLayouts(ProjectModel project, IEnumerable<ScreenModel> screens)
    {
        var byId = screens.ToDictionary(s => s.Id);
        var inactive = project.InactiveLayout is { } other
            ? new ModeLayout(other.Screens.Select(s => byId[s.Id]).ToArray(), other.Canvas) : null;
        return new(project.FormatVersion, project.Screens.Select(s => byId[s.Id]).ToArray(), project.Canvas, project.Mode, inactive);
    }

    public static ScreenModel ResolveScreenPath(ScreenModel screen, string? projectPath)
    {
        var logo = screen.Style.Logo;
        if (logo.EmbeddedImage is not null || logo.SourcePath.Length == 0) return screen;
        var resolved = IsForeignWindowsPath(logo.SourcePath) ? logo.SourcePath : ResolvePath(logo.SourcePath, projectPath);
        return resolved == logo.SourcePath ? screen : screen.WithStyle(screen.Style.WithLogo(
            new(logo.Enabled, resolved, logo.Anchor, logo.OffsetX, logo.OffsetY, logo.ScalePercent, logo.OpacityPercent, logo.EmbeddedImage, logo.BackgroundOverride)));
    }

    // Leave the live model/cache/history unchanged. Only the file
    // snapshot uses paths relative to its own destination, including Save As.
    // Relative clipboard input still refers to the current project (or CWD).
    public static ProjectModel PrepareProjectForSave(ProjectModel project, string projectPath, string? currentProjectPath = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        long? macCaseSensitive = null;
        var screens = project.AllScreens.Select(screen =>
        {
            var logo = screen.Style.Logo;
            if (logo.EmbeddedImage is not null || logo.SourcePath.Length == 0 || IsForeignWindowsPath(logo.SourcePath)) return screen;
            var source = ResolvePath(logo.SourcePath, currentProjectPath);
            // macOS treats composed/decomposed names as the same file, but
            // GetRelativePath compares characters. AppKit and CLI can differ.
            string relative;
            if (OperatingSystem.IsMacOS())
            {
                // .NET's Mac default ignores case even on case-sensitive APFS.
                // Query once per destination; unknown filesystem semantics keep
                // the absolute reference rather than choosing another image.
                macCaseSensitive ??= PathConf(directory, MacPathConfCaseSensitive);
                var normalizedDirectory = directory.Normalize(NormalizationForm.FormC);
                var normalizedSource = source.Normalize(NormalizationForm.FormC);
                relative = macCaseSensitive switch
                {
                    0 => Path.GetRelativePath(normalizedDirectory, normalizedSource),
                    1 => RelativeMacPathOrdinal(normalizedDirectory, normalizedSource),
                    _ => source
                };
            }
            else relative = Path.GetRelativePath(directory, source);
            // Different Windows volumes cannot be represented relatively.
            // Use forward slashes for portable relative paths in JSON.
            if (!Path.IsPathRooted(relative) && OperatingSystem.IsWindows()) relative = relative.Replace('\\', '/');
            return relative == logo.SourcePath ? screen : screen.WithStyle(screen.Style.WithLogo(
                new(logo.Enabled, relative, logo.Anchor, logo.OffsetX, logo.OffsetY, logo.ScalePercent, logo.OpacityPercent, logo.EmbeddedImage, logo.BackgroundOverride)));
        }).ToArray();
        return ReplaceScreenLayouts(project, screens);
    }

    private static string RelativeMacPathOrdinal(string directory, string source)
    {
        var from = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var to = source.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < from.Length && common < to.Length && from[common] == to[common]) common++;
        var parts = Enumerable.Repeat("..", from.Length - common).Concat(to.Skip(common));
        var relative = string.Join('/', parts);
        return relative.Length == 0 ? "." : relative;
    }

    // Darwin SDK sys/unistd.h; this constant is not portable to other OSes.
    private const int MacPathConfCaseSensitive = 11;
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "pathconf")]
    private static extern long PathConf([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int name);

    private static bool IsForeignWindowsPath(string path) => !OperatingSystem.IsWindows()
        && (path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/');

    public static async Task<LoadedLogoImages> LoadProjectAsync(ProjectModel project, string? projectPath)
    {
        var images = new Dictionary<string, LogoImage>(); var problems = new Dictionary<string, string>(); long total = 0;
        foreach (var settings in project.AllScreens.Select(x => x.Style.Logo).Where(x => x.SourcePath.Length > 0).DistinctBy(x => x.SourcePath))
        {
            var path = settings.SourcePath;
            try
            {
                var image = settings.EmbeddedImage is { } embedded
                    ? await Task.Run(() => Decode(Convert.FromBase64String(embedded.DataBase64)))
                    : await LoadAsync(ResolvePath(path, projectPath));
                if (total + image.ByteCount > MaxCacheBytes) throw new IOException("Суммарный размер подготовленных логотипов превышает 64 MiB.");
                images.Add(path, image); total += image.ByteCount;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { problems.Add(path, error.Message); }
        }
        return new(images, problems);
    }
}
