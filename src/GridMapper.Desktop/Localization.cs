using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace GridMapper.Desktop;

internal sealed record LanguageFile(string Id, string DisplayName, Dictionary<string, string> Strings)
{
    public override string ToString() => DisplayName;
}

internal sealed class LanguageCatalog
{
    internal string Folder { get; }
    internal IReadOnlyList<LanguageFile> Languages { get; }
    internal LanguageFile English { get; }
    internal LanguageFile OriginalRussian { get; }
    private readonly Dictionary<string, LanguageFile> _languages;
    internal LanguageCatalog(string? folder = null)
    {
        Folder = folder ?? Path.Combine(ApplicationPaths.CustomizationDirectory, "Localizations");
        var assembly = typeof(LanguageCatalog).Assembly;
        LanguageFile Embedded(string id)
        {
            using var stream = assembly.GetManifestResourceStream("GridMapper.Desktop.Localizations." + id + ".json")!;
            return JsonSerializer.Deserialize<LanguageFile>(stream, JsonOptions)!;
        }
        English = Embedded("En-en");
        OriginalRussian = Embedded("Ru-ru");
        _languages = new(StringComparer.OrdinalIgnoreCase) { [English.Id] = English, ["Ru-ru"] = OriginalRussian };
        try
        {
            foreach (var file in Directory.EnumerateFiles(Folder, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    if (new FileInfo(file).Length > 1024 * 1024) continue;
                    var language = JsonSerializer.Deserialize<LanguageFile>(File.ReadAllText(file), JsonOptions);
                    if (language is null || string.IsNullOrWhiteSpace(language.Id) || !Regex.IsMatch(language.Id, "^[A-Za-z]{2,8}(-[A-Za-z0-9]{2,8})+$")
                        || !string.Equals(language.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(language.DisplayName) || language.DisplayName.Length > 80 || language.Strings is null || language.Strings.Count > 2000) continue;
                    var valid = new Dictionary<string, string>();
                    foreach (var (key, text) in language.Strings)
                    {
                        if (text is null || text.Length > 16384) continue;
                        try
                        {
                            var format = CompositeFormat.Parse(text);
                            if (English.Strings.TryGetValue(key, out var original) && format.MinimumArgumentCount != CompositeFormat.Parse(original).MinimumArgumentCount) continue;
                            valid[key] = text;
                        }
                        catch (FormatException) { /* A broken entry falls back to English. */ }
                    }
                    _languages[language.Id] = language with { Strings = valid };
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Languages = _languages.Values.OrderBy(x => x.Id.Equals("En-en", StringComparison.OrdinalIgnoreCase) ? 0 : x.Id.Equals("Ru-ru", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal void PrepareFolder()
    {
        Directory.CreateDirectory(Folder);
        var bundled = Path.Combine(ApplicationPaths.InstallationDirectory, "Localizations");
        if (Path.GetFullPath(Folder) == Path.GetFullPath(bundled)) return;
        foreach (var file in Directory.EnumerateFiles(bundled))
        {
            var destination = Path.Combine(Folder, Path.GetFileName(file));
            if (!File.Exists(destination)) File.Copy(file, destination, overwrite: false);
        }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    internal LanguageFile Find(string id) => _languages.GetValueOrDefault(id) ?? English;
    internal string Get(string language, string key) => Find(language).Strings.GetValueOrDefault(key) ?? English.Strings.GetValueOrDefault(key) ?? key;
}

internal static class L
{
    internal static LanguageCatalog Catalog { get; private set; } = new();
    internal static string Language { get; private set; } = Catalog.Find(Preferences.Current.Language).Id;
    private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, string>> Bindings = new();
    private static readonly ConditionalWeakTable<ComboBox, string[]> Choices = new();
    internal static string T(string key) => Catalog.Get(Language, key);
    internal static string F(string key, params object?[] arguments)
    {
        try { return string.Format(CultureInfo.InvariantCulture, T(key), arguments); }
        catch (FormatException) { return string.Format(CultureInfo.InvariantCulture, Catalog.English.Strings.GetValueOrDefault(key) ?? key, arguments); }
    }
    internal static string Error(string text)
    {
        // Model and storage APIs retain their stable validation messages. Translate
        // at the UI boundary, leaving project data and rendered captions untouched.
        if (!text.Any(c => c is >= '\u0400' and <= '\u04ff')) return text;
        foreach (var (key, source) in Catalog.OriginalRussian.Strings.Where(p => p.Key.StartsWith("error", StringComparison.Ordinal)))
        {
            if (text == source) return T(key);
            if (source.EndsWith(": ", StringComparison.Ordinal) && text.StartsWith(source, StringComparison.Ordinal)) return T(key) + text[source.Length..];
            if (!source.Contains('{')) continue;
            var escaped = Regex.Escape(source);
            var pattern = "^" + Regex.Replace(escaped, @"\\\{\d+}", "(.+?)") + "$";
            var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success) return F(key, match.Groups.Cast<Group>().Skip(1).Select(g => (object?)g.Value).ToArray());
        }
        var colon = text.IndexOf(": ", StringComparison.Ordinal);
        if (colon >= 0)
        {
            var tail = text[(colon + 2)..]; var translated = Error(tail);
            if (tail != translated) return text[..(colon + 2)] + translated;
        }
        return text;
    }
    internal static void Select(string id) => Language = Catalog.Find(id).Id;
    internal static void Reload(string? folder = null) { Catalog = new(folder); Select(Language); }

    // Explicit keys disambiguate identical labels that mean different things.
    internal static void Bind(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        Bindings.GetOrCreateValue(target)[property] = key;
        target.SetValue(property, T(key));
    }

    // Track only presentation properties, never editable text, screen names or project content.
    // Weak ownership means closed windows and recycled rows are not retained by localization.
    internal static void Track(Control root)
    {
        var reverse = Catalog.English.Strings.Keys.GroupBy(T).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        void Register(AvaloniaObject target, AvaloniaProperty property)
        {
            if (target.GetValue(property) is not string value) return;
            if (Bindings.TryGetValue(target, out var known) && known.TryGetValue(property, out var existing) && T(existing) == value) return;
            if (reverse.TryGetValue(value, out var key)) Bindings.GetOrCreateValue(target)[property] = key;
        }
        foreach (var control in Walk(root))
        {
            if (control.Name?.StartsWith("LayerName_", StringComparison.Ordinal) == true) continue;
            if (control is TextBlock) Register(control, TextBlock.TextProperty);
            if (control is SelectableTextBlock) Register(control, SelectableTextBlock.TextProperty);
            if (control is ContentControl) Register(control, ContentControl.ContentProperty);
            if (control is HeaderedContentControl) Register(control, HeaderedContentControl.HeaderProperty);
            Register(control, ToolTip.TipProperty); Register(control, AutomationProperties.NameProperty);
            if (control is ComboBox choice && choice.ItemsSource is string[] strings)
            {
                Choices.Remove(choice);
                Choices.Add(choice, strings.Select(s => reverse.TryGetValue(s, out var key) ? key : "=" + s).ToArray());
            }
        }
        if (root is Window window && NativeMenu.GetMenu(window) is { } menu) TrackMenu(menu);
        void TrackMenu(NativeMenu menu)
        {
            foreach (var item in menu.Items.OfType<NativeMenuItem>())
            { Register(item, NativeMenuItem.HeaderProperty); if (item.Menu is { } child) TrackMenu(child); }
        }
    }
    internal static void Apply(Control root)
    {
        void Update(AvaloniaObject target)
        {
            if (Bindings.TryGetValue(target, out var values)) foreach (var (property, key) in values) target.SetValue(property, T(key));
        }
        foreach (var control in Walk(root))
        {
            Update(control);
            if (control is ComboBox choice && Choices.TryGetValue(choice, out var keys))
            {
                var index = choice.SelectedIndex;
                choice.ItemsSource = keys.Select(k => k.StartsWith('=') ? k[1..] : T(k)).ToArray(); choice.SelectedIndex = index;
            }
        }
        if (root is Window window && NativeMenu.GetMenu(window) is { } menu) UpdateMenu(menu);
        void UpdateMenu(NativeMenu menu)
        {
            foreach (var item in menu.Items.OfType<NativeMenuItem>()) { Update(item); if (item.Menu is { } child) UpdateMenu(child); }
        }
    }
    private static IEnumerable<Control> Walk(Control root)
    {
        var pending = new Stack<Control>(); var seen = new HashSet<Control>(); pending.Push(root);
        while (pending.TryPop(out var next))
        {
            if (!seen.Add(next)) continue;
            yield return next;
            foreach (var child in next.GetLogicalChildren().OfType<Control>()) pending.Push(child);
            if (next is Button { Flyout: Flyout { Content: Control content } }) pending.Push(content);
        }
    }
}
