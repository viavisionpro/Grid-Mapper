using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using GridMapper.Core;
using GridMapper.Rendering;
using GridMapper.Storage;
using SkiaSharp;

namespace GridMapper.Desktop;

// Runs from the published executable, without GUI, SDK, external assets or network.
internal static class PackageSelfCheck
{
    public static async Task<int> RunAsync(string outputDirectory)
    {
        string? reportPath = null;
        try
        {
            var output = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(output);
            reportPath = Path.Combine(output, "package-self-check.json");
            await AtomicFiles.WriteAsync(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
                { passed = false, status = "running", startedAtUtc = DateTimeOffset.UtcNow }));
            var checks = 0;
            void Require(bool condition, string message)
            {
                checks++;
                if (!condition) throw new InvalidOperationException(message);
            }
            var installation = ApplicationPaths.InstallationDirectory;
            foreach (var resource in new[] { "README.md", "README.ru.md", "NDI/README.md", "Localizations/En-en.json", "Localizations/Ru-ru.json" })
                Require(File.Exists(Path.Combine(installation, resource)), "Application resource is missing: " + resource);
            var catalog = new LanguageCatalog();
            Require(catalog.Folder == Path.Combine(ApplicationPaths.CustomizationDirectory, "Localizations"), "Language folder differs from the installation folder.");
            Require(NdiRuntime.LibraryCandidates().First().StartsWith(Path.Combine(ApplicationPaths.CustomizationDirectory, "NDI") + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "Local NDI override must remain beside the launcher, outside Runtime.");
            var assembly = typeof(SceneRenderer).Assembly;
            foreach (var name in new[] { "NotoSans", "NotoSansMono" })
            {
                using var stream = assembly.GetManifestResourceStream($"GridMapper.Rendering.Assets.{name}.ttf")
                    ?? throw new InvalidOperationException("Embedded font is missing: " + name);
                using var typeface = SKTypeface.FromStream(stream)
                    ?? throw new InvalidOperationException("Embedded font cannot be loaded: " + name);
                Require(typeface.ContainsGlyphs("Grid Mapper Проверка 123 ×"), "Embedded Latin/Cyrillic glyphs: " + name);
            }
            using (var buffer = new HarfBuzzSharp.Buffer())
            {
                buffer.AddUtf8("Grid Mapper Проверка");
                Require(buffer.Length > 0, "Native HarfBuzz library did not process text.");
            }

            var type = new CabinetType(Guid.NewGuid(), "Контрольный кабинет", 500, 500, 128, 128);
            var style = new ScreenStyle(FillPattern.Checkerboard, new(240, 240, 240), new(32, 32, 32), false);
            var screen = ScreenModel.Regular("Проверка пакета", type, 2, 2, style);
            var placement = new ScreenPlacement(Guid.NewGuid(), screen.Id, -8, 16, false, false);
            var project = new ProjectModel(1, [screen], new(320, 288, [placement], transparentBackground: true));
            var projectPath = Path.Combine(output, "Проверка пакета.gpm");
            await ProjectFiles.SaveAsync(projectPath, project);
            var loaded = await ProjectFiles.LoadAsync(projectPath);
            Require(ProjectFiles.Serialize(loaded).SequenceEqual(ProjectFiles.Serialize(project)), "Project Save/Open differs.");
            var singlePng = SceneRenderer.RenderPng(SceneBuilder.ForScreen(loaded.Screens[0], loaded.Canvas.Placements[0]));
            var canvasPng = SceneRenderer.RenderPng(SceneBuilder.ForCanvas(loaded));
            using var single = SKBitmap.Decode(singlePng) ?? throw new IOException("Screen PNG cannot be decoded.");
            using var canvas = SKBitmap.Decode(canvasPng) ?? throw new IOException("Canvas PNG cannot be decoded.");
            Require(single.Width == 256 && single.Height == 256, "Wrong screen resolution.");
            Require(canvas.Width == 320 && canvas.Height == 288, "Wrong Canvas resolution.");
            for (var y = 0; y < single.Height; y++)
                for (var x = 0; x < single.Width; x++)
                {
                    var channel = (byte)(((x / 128 + y / 128) % 2 == 0) ? 240 : 32);
                    Require(single.GetPixel(x, y) == new SKColor(channel, channel, channel), "Cabinet pixel differs.");
                }
            for (var y = 0; y < canvas.Height; y++)
                for (var x = 0; x < canvas.Width; x++)
                {
                    var sourceX = x - placement.X; var sourceY = y - placement.Y;
                    var expected = sourceX >= 0 && sourceY >= 0 && sourceX < single.Width && sourceY < single.Height
                        ? single.GetPixel(sourceX, sourceY) : new SKColor(0, 0, 0, 0);
                    Require(canvas.GetPixel(x, y) == expected, "Canvas clipping/alpha differs.");
                }
            Require(loaded.Canvas.TransparentBackground, "Canvas transparency was not saved.");
            using (var opaque = SKBitmap.Decode(SceneRenderer.RenderPng(SceneBuilder.ForCanvas(CanvasEditing.SetTransparency(loaded, false)))))
                Require(opaque.GetPixel(319, 287) == SKColors.Black, "Opaque Canvas background differs.");
            var masked = CabinetEditing.SetMask(loaded.Screens[0], new HashSet<Guid> { loaded.Screens[0].Cabinets[0].Id }, true);
            var maskProject = new ProjectModel(loaded.FormatVersion, [masked], loaded.Canvas);
            using (var alphaMask = SKBitmap.Decode(SceneRenderer.RenderPng(SceneBuilder.ForCanvas(maskProject))))
                Require(alphaMask.GetPixel(10, 30).Alpha == 0 && alphaMask.GetPixel(200, 30).Alpha == 255, "Canvas mask alpha differs.");
            var decorated = new ScreenStyle(style.Pattern, style.PrimaryColor, style.SecondaryColor, false,
                new(true, true, true, true, true), new(enabled: true, font: EmbeddedFont.NotoSansMono),
                new(enabled: true, font: EmbeddedFont.NotoSans),
                new(enabled: true, content: "Grid Mapper\nПроверка 123", y: 160, font: EmbeddedFont.NotoSansMono));
            var decoratedScreen = new ScreenModel(screen.Id, screen.Name, screen.CabinetTypes, screen.Cabinets, decorated);
            var textPng = SceneRenderer.RenderPng(SceneBuilder.ForScreen(decoratedScreen));
            Require(!textPng.SequenceEqual(singlePng), "Embedded text/decorations were not rendered.");
            await AtomicFiles.WriteAsync(Path.Combine(output, "screen.png"), singlePng);
            await AtomicFiles.WriteAsync(Path.Combine(output, "canvas.png"), canvasPng);
            await AtomicFiles.WriteAsync(Path.Combine(output, "fonts.png"), textPng);
            var report = new
            {
                product = "Grid Mapper", version = typeof(PackageSelfCheck).Assembly.GetName().Version?.ToString(3),
                runtime = RuntimeInformation.FrameworkDescription, platform = RuntimeInformation.RuntimeIdentifier,
                installationDirectory = installation, runtimeDirectory = AppContext.BaseDirectory,
                checks, passed = true, status = "passed", completedAtUtc = DateTimeOffset.UtcNow,
                screen = "256x256", canvas = "320x288", fonts = new[] { "Noto Sans", "Noto Sans Mono" },
                screenPixelsSha256 = Convert.ToHexString(SHA256.HashData(single.Bytes)),
                canvasPixelsSha256 = Convert.ToHexString(SHA256.HashData(canvas.Bytes))
            };
            var json = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true });
            await AtomicFiles.WriteAsync(reportPath, json);
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(json));
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or DllNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            if (reportPath is not null)
            {
                try
                {
                    await AtomicFiles.WriteAsync(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
                        { passed = false, status = "failed", completedAtUtc = DateTimeOffset.UtcNow, error = error.Message }));
                }
                catch (Exception reportError) when (reportError is IOException or UnauthorizedAccessException or ArgumentException)
                { Console.Error.WriteLine("Failure report could not be written: " + reportError.Message); }
            }
            Console.Error.WriteLine("Package verification failed: " + error.Message);
            return 1;
        }
    }
}
