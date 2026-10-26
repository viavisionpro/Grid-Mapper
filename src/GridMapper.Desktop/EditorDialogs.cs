using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace GridMapper.Desktop;

public enum UnsavedDecision { Cancel, Save, Discard }
public enum EditorFileKind { Project, Png, Resolume }

public interface IEditorDialogs
{
    Task<string?> PickOpenAsync(Window owner);
    Task<string?> PickLogoAsync(Window owner);
    Task<string?> PickCanvasMaskAsync(Window owner) => PickLogoAsync(owner);
    Task<string?> PickSaveAsync(Window owner, EditorFileKind kind, string suggestedName);
    Task<UnsavedDecision> ConfirmUnsavedAsync(Window owner);
    Task<bool> ConfirmRemoveAllAsync(Window owner, int count) => RemoveAllConfirmation.Show(owner, count);
    Task ShowNoticeAsync(Window owner, string message) => EditorNotice.Show(owner, message);
}

internal static class EditorNotice
{
    public static async Task Show(Window owner, string message)
    {
        var close = new Button { Name = "CloseEditorNotice", Content = "OK", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        var body = new StackPanel { Spacing = 18, Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }); body.Children.Add(close);
        var dialog = new Window { Name = "EditorNotice", Title = L.T("notice.title"), Width = 460, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body, FontFamily = owner.FontFamily };
        close.Click += (_, _) => dialog.Close(); await dialog.ShowDialog(owner);
    }
}

internal static class RemoveAllConfirmation
{
    public static Task<bool> Show(Window owner, int count)
    {
        var dialog = new Window { Name = "RemoveAllConfirmation", Title = L.T("ui027"), Width = 400,
            SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = owner.FontFamily, Icon = owner.Icon };
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 18 };
        content.Children.Add(new TextBlock { Text = L.T("ui027"), FontSize = 19 });
        content.Children.Add(new TextBlock { Text = L.F("ui028", count), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Name = "CancelRemoveAll", Content = L.T("ui029"), IsDefault = true, IsCancel = true };
        var remove = new Button { Name = "ConfirmRemoveAll", Content = L.T("ui030"), Classes = { "danger" } };
        cancel.Click += (_, _) => dialog.Close(false); remove.Click += (_, _) => dialog.Close(true);
        actions.Children.Add(cancel); actions.Children.Add(remove); content.Children.Add(actions); dialog.Content = content;
        dialog.Opened += (_, _) => cancel.Focus();
        return dialog.ShowDialog<bool>(owner);
    }
}

public sealed class NativeEditorDialogs : IEditorDialogs
{
    private static FilePickerFileType ProjectOpenType => new(L.T("ui031")) { Patterns = ["*.gpm"] };
    private static FilePickerFileType ProjectSaveType => new(L.T("ui031")) { Patterns = ["*.gpm"] };
    private static readonly FilePickerFileType PngType = new("PNG") { Patterns = ["*.png"] };
    private static readonly FilePickerFileType ResolumeType = new("Resolume Arena XML") { Patterns = ["*.xml"] };
    private static FilePickerFileType LogoType => new(L.T("ui032")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] };

    public async Task<string?> PickCanvasMaskAsync(Window owner)
    {
        if (!owner.StorageProvider.CanOpen) throw new IOException(L.T("ui033"));
        var type = new FilePickerFileType(L.T("mask.images")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"], MimeTypes = ["image/png", "image/jpeg", "image/webp"] };
        var files = await owner.StorageProvider.OpenFilePickerAsync(new() { Title = L.T("mask.open"), AllowMultiple = false, FileTypeFilter = [type] });
        return files.Count == 0 ? null : LocalPath(files[0]);
    }

    public async Task<string?> PickLogoAsync(Window owner)
    {
        if (!owner.StorageProvider.CanOpen) throw new IOException(L.T("ui033"));
        var files = await owner.StorageProvider.OpenFilePickerAsync(new()
        { Title = L.T("ui034"), AllowMultiple = false, FileTypeFilter = [LogoType] });
        return files.Count == 0 ? null : LocalPath(files[0]);
    }

    public async Task<string?> PickOpenAsync(Window owner)
    {
        if (!owner.StorageProvider.CanOpen) throw new IOException(L.T("ui033"));
        var files = await owner.StorageProvider.OpenFilePickerAsync(ProjectOpenOptions());
        return files.Count == 0 ? null : LocalPath(files[0]);
    }

    public async Task<string?> PickSaveAsync(Window owner, EditorFileKind kind, string suggestedName)
    {
        if (!owner.StorageProvider.CanSave) throw new IOException(L.T("ui036"));
        var file = await owner.StorageProvider.SaveFilePickerAsync(SaveOptions(kind, suggestedName));
        return file is null ? null : LocalPath(file);
    }

    internal static FilePickerOpenOptions ProjectOpenOptions() => new()
    { Title = L.T("ui035"), AllowMultiple = false, FileTypeFilter = [ProjectOpenType] };

    internal static FilePickerSaveOptions SaveOptions(EditorFileKind kind, string suggestedName) => new()
    {
        Title = kind == EditorFileKind.Resolume ? L.T("resolume.export") : kind == EditorFileKind.Project ? L.T("ui037") : L.T("ui038"),
        SuggestedFileName = suggestedName, DefaultExtension = kind == EditorFileKind.Resolume ? "xml" : kind == EditorFileKind.Project ? "gpm" : "png",
        ShowOverwritePrompt = true, FileTypeChoices = [kind == EditorFileKind.Resolume ? ResolumeType : kind == EditorFileKind.Project ? ProjectSaveType : PngType]
    };

    private static string LocalPath(IStorageItem item) => item.TryGetLocalPath()
        ?? throw new IOException(L.T("ui039"));

    public Task<UnsavedDecision> ConfirmUnsavedAsync(Window owner)
    {
        var dialog = new Window { Name = "UnsavedChangesConfirmation", Title = L.T("ui040"), SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = owner.FontFamily };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        panel.Children.Add(new TextBlock { Text = L.T("ui041"), MaxWidth = 560, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (text, decision) in new[] { (L.T("ui042"), UnsavedDecision.Save), (L.T("ui043"), UnsavedDecision.Discard), (L.T("ui029"), UnsavedDecision.Cancel) })
        {
            var button = new Button { Name = "Unsaved" + decision, Content = text, IsDefault = decision == UnsavedDecision.Save, IsCancel = decision == UnsavedDecision.Cancel };
            button.Click += (_, _) => dialog.Close(decision);
            actions.Children.Add(button);
        }
        panel.Children.Add(actions);
        dialog.Content = panel;
        return dialog.ShowDialog<UnsavedDecision>(owner);
    }
}
