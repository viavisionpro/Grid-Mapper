using System.Text.Json;

namespace GridMapper.Desktop;

internal sealed record ApplicationPreferences(string Language = "En-en", int NdiFrameRate = 60);

internal sealed class PreferencesStore(string? path)
{
    internal ApplicationPreferences Load()
    {
        try
        {
            if (path is null || !File.Exists(path) || new FileInfo(path).Length > 4096) return new();
            var value = JsonSerializer.Deserialize<ApplicationPreferences>(File.ReadAllText(path));
            return value is null ? new() : value with { Language = value.Language ?? "En-en", NdiFrameRate = value.NdiFrameRate is 50 or 60 ? value.NdiFrameRate : 60 };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal void Save(ApplicationPreferences value)
    {
        if (value.NdiFrameRate is not (50 or 60)) throw new ArgumentOutOfRangeException(nameof(value));
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true });
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal static class Preferences
{
    private static PreferencesStore _store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GridMapper", "settings.json"));
    internal static ApplicationPreferences Current { get; private set; } = _store.Load();
    internal static void Save(ApplicationPreferences value) { _store.Save(value); Current = value; }
    // Tests explicitly inject an isolated store; running verification never changes user preferences.
    internal static void Configure(PreferencesStore store) { _store = store; Current = store.Load(); }
}
