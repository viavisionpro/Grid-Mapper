using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Media;
using Avalonia.Styling;

namespace GridMapper.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--verify-package")
        {
            if (args.Length != 2) { Console.Error.WriteLine("Usage: GridMapper.Desktop --verify-package OUTPUT_DIRECTORY"); return 2; }
            return PackageSelfCheck.RunAsync(args[1]).GetAwaiter().GetResult();
        }
        return App.ConfigurePlatform(AppBuilder.Configure<App>().UsePlatformDetect()).LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
    public App() { Name = "Grid Mapper"; Avalonia.Controls.NativeMenu.SetMenu(this, new Avalonia.Controls.NativeMenu()); }
    // A cache budget, not a cap on active textures/window surfaces. Keep unused GPU
    // resources bounded after switching large projects on both native platforms.
    public const long GpuResourceCacheBytes = 64L * 1024 * 1024;
    public static AppBuilder ConfigurePlatform(AppBuilder builder)
    {
        builder.With(new SkiaOptions { MaxGpuResourceSizeBytes = GpuResourceCacheBytes });
        if (OperatingSystem.IsMacOS() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
            builder.With(new AvaloniaNativePlatformOptions
            {
                // Pinned 12.1.3 backend with owned file-activation arrays released.
                AvaloniaNativeLibraryPath = Path.Combine(AppContext.BaseDirectory, "native", "osx-arm64", "libGridMapperAvaloniaNative.dylib")
            });
        return builder;
    }
    public override void Initialize()
    {
        var theme = new FluentTheme();
        theme.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse("#94949E") };
        Styles.Add(theme);
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://GridMapper.Desktop/"))
        { Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml") });
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://GridMapper.Desktop/"))
        { Source = new Uri("avares://GridMapper.Desktop/Graphite.axaml") });
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.Opened += async (_, _) => await window.OpenLaunchProjectAsync(desktop.Args ?? []);
            desktop.MainWindow = window;
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activation)
            {
                async void Activated(object? sender, ActivatedEventArgs args)
                {
                    if (args is FileActivatedEventArgs files) await window.OpenActivatedFilesAsync(files);
                }
                activation.Activated += Activated;
                window.Closed += (_, _) => activation.Activated -= Activated;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
