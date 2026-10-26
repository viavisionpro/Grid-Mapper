using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    internal Window? SettingsWindow { get; private set; }
    private async Task<bool> ShowSettingsAsync()
    {
        if (_busy || HasPointerEdit) return false;
        if (HasDraft && !ApplyAutomatic()) return false;
        L.Reload(); L.Track(this);
        var dialog = new Window { Name = "SettingsWindow", Title = L.T("settings.title"), Width = 460,
            SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, Icon = Icon };
        NativeMenu.SetMenu(dialog, NativeMenu.GetMenu(this));
        SettingsWindow = dialog;
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        if (OperatingSystem.IsMacOS()) body.Children.Add(new NativeMenuBar { Name = "SettingsMenuBar" });
        body.Children.Add(new TextBlock { Text = L.T("settings.title"), FontSize = 23, FontWeight = FontWeight.SemiBold });
        body.Children.Add(Divider());
        body.Children.Add(new TextBlock { Text = L.T("settings.interface"), FontSize = 16, FontWeight = FontWeight.SemiBold });
        var languages = new ComboBox { Name = "SettingsLanguage", ItemsSource = L.Catalog.Languages,
            SelectedItem = L.Catalog.Find(L.Language), HorizontalAlignment = HorizontalAlignment.Stretch };
        AddField(body, L.T("settings.language"), languages);
        body.Children.Add(new TextBlock { Text = L.T("settings.languageHint"), FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        var folder = new Button { Name = "OpenLocalizations", Content = L.T("settings.openFolder"), FontSize = 12 };
        folder.Click += async (_, _) =>
        {
            try
            {
                L.Catalog.PrepareFolder();
                if (await StorageProvider.TryGetFolderFromPathAsync(new Uri(L.Catalog.Folder)) is { } directory && Launcher is { } launcher)
                    await launcher.LaunchFileAsync(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatus(L.F("settings.saveError", e.Message)); }
        };
        body.Children.Add(folder); body.Children.Add(Divider());
        body.Children.Add(new TextBlock { Text = "NDI", FontSize = 16, FontWeight = FontWeight.SemiBold });
        var rate = new ComboBox { Name = "SettingsNdiRate", ItemsSource = new[] { 50, 60 }, SelectedItem = Preferences.Current.NdiFrameRate, Width = 110 };
        AddField(body, L.T("settings.frameRate"), rate);
        body.Children.Add(new TextBlock { Text = L.T("settings.ndiHint"), FontSize = 12, Classes = { "muted" } });
        var runtime = new SelectableTextBlock { Name = "SettingsNdiRuntime", FontSize = 12, TextWrapping = TextWrapping.Wrap };
        try
        {
            var loaded = NdiRuntime.Get();
            runtime.Text = "NDI Runtime: " + loaded.Version + "\n" + loaded.LibraryPath;
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or BadImageFormatException)
        { runtime.Text = L.T("settings.ndiUnavailable"); }
        body.Children.Add(runtime);
        var ndiLink = new Button { Content = "ndi.video", FontSize = 12 };
        ndiLink.Click += async (_, _) => { if (Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri("https://ndi.video")); };
        body.Children.Add(ndiLink);
        body.Children.Add(new TextBlock { Text = L.T("settings.ndiRestart"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "NDI® is a registered trademark of Vizrt NDI AB.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var error = new TextBlock { Name = "SettingsError", Foreground = Brush.Parse("#F0AFC8"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        body.Children.Add(error);
        var close = new Button { Name = "CloseSettings", Content = L.T("ui132"), HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
        close.Click += (_, _) => dialog.Close(); body.Children.Add(close);
        dialog.Content = body; L.Track(dialog);
        var updating = false;
        void Change()
        {
            if (updating || languages.SelectedItem is not LanguageFile language || rate.SelectedItem is not int fps) return;
            updating = true;
            try
            {
                Preferences.Save(new(language.Id, fps));
                foreach (var output in _ndiOutputs.Values) output.FrameRate = fps;
                L.Track(this); L.Track(dialog); L.Select(language.Id);
                // Only presentation properties change; drafts, history, output pixels and names are preserved.
                _syncingControls = true;
                try { L.Apply(this); L.Apply(dialog); BuildMenu(); }
                finally { _syncingControls = false; }
                dialog.Title = L.T("settings.title");
                UpdateActions(); SyncDesign(); UpdateLogoInfo(); UpdatePreviewSummary();
                foreach (var id in _layerEditors.Keys) UpdateLayerEditor(id);
                SetStatus(L.T("settings.saved")); error.IsVisible = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error.Text = L.F("settings.saveError", e.Message); error.IsVisible = true;
                languages.SelectedItem = L.Catalog.Find(Preferences.Current.Language); rate.SelectedItem = Preferences.Current.NdiFrameRate;
            }
            finally { updating = false; }
        }
        languages.SelectionChanged += (_, _) => Change(); rate.SelectionChanged += (_, _) => Change();
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        SetBusy(true);
        try { await dialog.ShowDialog(this); return true; }
        finally { SettingsWindow = null; SetBusy(false); }
    }
}
