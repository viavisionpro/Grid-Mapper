namespace GridMapper.Desktop;

/// <summary>Resources live beside Runtime (Contents/Resources on macOS, launcher folder on Windows).</summary>
internal static class ApplicationPaths
{
    internal const string LayoutProperty = "GridMapper.DistributionLayout";
    internal const string RuntimeLayout = "runtime-subdirectory-v1";
    internal const string MacBundleLayout = "macos-resources-v1";
    internal static string CustomizationDirectory => AppContext.GetData(LayoutProperty) as string == MacBundleLayout
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GridMapper") : InstallationDirectory;

    internal static string InstallationDirectory => ResolveInstallationDirectory(
        AppContext.BaseDirectory, AppContext.GetData(LayoutProperty) as string);

    internal static string ResolveInstallationDirectory(string baseDirectory, string? layout)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        if (layout is not (RuntimeLayout or MacBundleLayout)) return directory;
        if (!string.Equals(Path.GetFileName(directory), "Runtime", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid Grid Mapper runtime directory.");
        return Path.GetDirectoryName(directory)!;
    }
}
