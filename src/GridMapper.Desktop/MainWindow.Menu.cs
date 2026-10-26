using GridMapper.Core;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Input;
using Avalonia.Layout;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly List<MenuAction> _menuActions = [];
    private readonly Dictionary<string, NativeMenuItem> _menuItems = [];
    internal IReadOnlyDictionary<string, NativeMenuItem> MenuItems => _menuItems;

    private static readonly List<NativeMenuItemBase> ApplicationItems = [];
    private NativeMenuBar BuildMenu()
    {
        _menuActions.Clear(); _menuItems.Clear();
        // Keep the exported root identity; Avalonia cannot replace it after native export.
        var root = NativeMenu.GetMenu(this) ?? new NativeMenu(); root.Items.Clear();
        var modifiers = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        NativeMenu Section(string title)
        {
            var menu = new NativeMenu(); root.Items.Add(new NativeMenuItem(title) { Menu = menu }); return menu;
        }
        void Action(NativeMenu section, string id, string title, Func<Task<bool>> run,
            Func<bool>? available = null, KeyGesture? gesture = null)
        {
            var command = new MenuAction(run, () => !_busy && !HasPointerEdit && (available?.Invoke() ?? true));
            var item = new NativeMenuItem(title) { Command = command, Gesture = gesture };
            _menuActions.Add(command); _menuItems.Add(id, item); section.Items.Add(item);
        }
        Task<bool> Immediate(System.Action run) { run(); return Task.FromResult(true); }
        var file = Section(L.T("ui116"));
        Action(file, "New", L.T("ui095"), NewProjectAsync, gesture: new(Key.N, modifiers));
        Action(file, "Open", L.T("ui117"), OpenProjectAsync, gesture: new(Key.O, modifiers));
        file.Items.Add(new NativeMenuItemSeparator());
        Action(file, "Save", L.T("ui042"), () => SaveProjectAsync(false), gesture: new(Key.S, modifiers));
        Action(file, "SaveAs", L.T("ui118"), () => SaveProjectAsync(true), gesture: new(Key.S, modifiers | KeyModifiers.Shift));
        Action(file, "Export", L.T("ui119"), ExportAsync, () => HasScreens, gesture: new(Key.E, modifiers));
        Action(file, "ExportResolume", L.T("resolume.export"), ExportResolumeAsync, () => CanExportResolume);
        file.Items.Add(new NativeMenuItemSeparator());
        if (!OperatingSystem.IsMacOS())
        {
            Action(file, "Settings", L.T("settings.title") + "…", ShowSettingsAsync);
            file.Items.Add(new NativeMenuItemSeparator());
        }
        Action(file, "Close", L.T("ui120"), () => Immediate(Close), gesture: new(Key.W, modifiers));
        var edit = Section(L.T("ui121"));
        // Project commands are explicit; native text-field Cmd/Ctrl+Z/C/V keep their usual meaning.
        Action(edit, "Undo", L.T("ui122"), () => Immediate(Undo), () => _document.CanUndo || HasDraft);
        Action(edit, "Redo", L.T("ui123"), () => Immediate(Redo), () => _document.CanRedo && !HasDraft);
        edit.Items.Add(new NativeMenuItemSeparator());
        Action(edit, "CopyScreen", L.T("ui124"), CopyScreenAsync, () => _objectsSelected);
        Action(edit, "PasteScreen", L.T("ui125"), PasteScreenAsync);
        var view = Section(L.T("ui126"));
        var patternMode=new NativeMenu();view.Items.Add(new NativeMenuItem(L.T("projection.mode")){Menu=patternMode});
        Action(patternMode,"ModeLed","LED",()=>Immediate(()=>{SetProjectionMode(ProjectMode.Led);UpdateMenuActions();}));
        Action(patternMode,"ModeProjection","Projection",()=>Immediate(()=>{SetProjectionMode(ProjectMode.Projection);UpdateMenuActions();}));
        _menuItems["ModeLed"].ToggleType=_menuItems["ModeProjection"].ToggleType=MenuItemToggleType.Radio;
        view.Items.Add(new NativeMenuItemSeparator());
        Action(view, "Fit", L.T("ui127"), () => Immediate(() => _zoom.SelectedIndex = 0));
        Action(view, "Zoom100", L.T("ui128"), () => Immediate(() => _zoom.SelectedIndex = 1));
        Action(view, "Zoom200", L.T("ui129"), () => Immediate(() => _zoom.SelectedIndex = 2));
        var help = Section(L.T("ui130"));
        Action(help, "Readme", "Readme", OpenReadmeAsync);
        if (OperatingSystem.IsMacOS() && Application.Current is { } application)
        {
            var appMenu = NativeMenu.GetMenu(application) ?? new NativeMenu();
            foreach (var item in ApplicationItems) appMenu.Items.Remove(item);
            ApplicationItems.Clear();
            // A probe or an older framework initialization may have created its default About item.
            foreach (var item in appMenu.Items.OfType<NativeMenuItem>().Where(x => x.Header == "About Avalonia").ToArray())
                appMenu.Items.Remove(item);
            var own = new NativeMenu();
            Action(own, "About", AboutText.Title, ShowAboutAsync);
            Action(own, "Settings", L.T("settings.title") + "…", ShowSettingsAsync,
                gesture: new(Key.OemComma, KeyModifiers.Meta));
            var index = 0;
            foreach (var item in own.Items.ToArray())
            { own.Items.Remove(item); appMenu.Items.Insert(index++, item); ApplicationItems.Add(item); }
            NativeMenu.SetMenu(application, appMenu);
        }
        else Action(help, "About", AboutText.Title, ShowAboutAsync);
        NativeMenu.SetMenu(this, root);
        UpdateMenuActions();
        return new NativeMenuBar { Name = "MainMenuBar" };
    }
    private void UpdateMenuActions()
    {
        foreach (var command in _menuActions) command.Refresh();
        if(_menuItems.TryGetValue("ModeLed",out var led))led.IsChecked=!IsProjection;
        if(_menuItems.TryGetValue("ModeProjection",out var projection))projection.IsChecked=IsProjection;
    }
    private async Task<bool> ShowAboutAsync()
    {
        if (_busy || HasPointerEdit) return false;
        SetBusy(true);
        try
        {
            var version = DisplayVersion;
            var close = new Button { Name = "CloseAbout", Content = AboutText.Close, HorizontalAlignment = HorizontalAlignment.Right };
            var content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            using var image = AssetLoader.Open(new Uri("avares://GridMapper.Desktop/Assets/GridMapper.png"));
            var logo = new Bitmap(image);
            heading.Children.Add(new Image { Name = "AboutLogo", Source = logo, Width = 64, Height = 64 });
            var title = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "Grid Mapper", FontSize = 26 });
            title.Children.Add(new TextBlock { Name = "AboutVersion", Text = version });
            heading.Children.Add(title); content.Children.Add(heading);
            content.Children.Add(new SelectableTextBlock { Name = "AboutDescription", Text = AboutText.Description, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Name = "AboutLicense", Text = "GNU General Public License v3.0 (GPL-3.0-only)" });
            var website = new Button { Content = "VIAVISION — viavision.pro" };
            website.Click += async (_, _) => { if (Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri("https://viavision.pro")); };
            content.Children.Add(website);
            content.Children.Add(new SelectableTextBlock { Name = "AboutComponents", Text = AboutText.Components, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var ndi = new Button { Content = "NDI® — ndi.video" };
            ndi.Click += async (_, _) => { if (Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri("https://ndi.video")); };
            content.Children.Add(ndi);
            var licenses = new Button { Content = AboutText.Licenses };
            licenses.Click += async (_, _) => await OpenPackagedFileAsync(Path.Combine("Licenses", "INDEX.txt"));
            content.Children.Add(licenses); content.Children.Add(close);
            var dialog = new Window { Title = AboutText.Title, Width = 560, MaxHeight = 800, SizeToContent = SizeToContent.Height,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer { Content = content }, FontFamily = FontFamily, Icon = Icon };
            dialog.Closed += (_, _) => logo.Dispose();
            close.Click += (_, _) => dialog.Close();
            dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
            await dialog.ShowDialog(this);
            return true;
        }
        finally { SetBusy(false); }
    }
    internal string ReadmePath => Path.Combine(ApplicationPaths.InstallationDirectory, AboutText.Russian ? "README.ru.md" : "README.md");
    private Task<bool> OpenReadmeAsync() => OpenPackagedFileAsync(Path.GetFileName(ReadmePath));
    private async Task<bool> OpenPackagedFileAsync(string relative)
    {
        try
        {
            var path = Path.Combine(ApplicationPaths.InstallationDirectory, relative);
            if (!File.Exists(path)) { SetStatus(L.T("readme.missing")); return false; }
            if (await StorageProvider.TryGetFileFromPathAsync(new Uri(path)) is { } file && Launcher is { } launcher)
                return await launcher.LaunchFileAsync(file);
            SetStatus(L.T("readme.missing")); return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { SetStatus(L.Error(error.Message)); return false; }
    }
    private sealed class MenuAction(Func<Task<bool>> run, Func<bool> available) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => available();
        public async void Execute(object? parameter) { if (CanExecute(parameter)) await run(); }
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
