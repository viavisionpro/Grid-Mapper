using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using GridMapper.Core;
using GridMapper.Rendering;
using GridMapper.Storage;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private bool IsPreviewInput(object? source) => source is Control control && (control == _previewWorkspace
        || control.GetVisualAncestors().Contains(_previewWorkspace));
    internal async Task<bool> CopyScreenAsync()
    {
        if (_busy || HasPointerEdit || !_objectsSelected) return false;
        SetBusy(true);
        try
        {
            if (!ApplyAutomatic()) return false;
            if (Clipboard is null) throw new NotSupportedException(L.T("ui253"));
            await Clipboard.SetTextAsync(ScreenClipboard.Serialize(SelectedScreen, Placement));
            SetStatus(L.T("ui254")); return true;
        }
        catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException) { ResourceFailure(error); return false; }
        catch (Exception error) when (IsFileError(error)) { SetStatus(L.F("ui255", error.Message)); return false; }
        finally { SetBusy(false); }
    }
    internal async Task<bool> PasteScreenAsync()
    {
        if (_busy || HasPointerEdit) return false;
        SetBusy(true);
        try
        {
            if (!ApplyAutomatic()) return false;
            if (Clipboard is null) throw new NotSupportedException(L.T("ui253"));
            var copied = ScreenClipboard.Deserialize(await Clipboard.TryGetTextAsync());
            var copy = LogoImages.ResolveScreenPath(CanvasEditing.Copy(copied.Screen, NextScreenName()), _filePath);
            var next = CanvasEditing.Add(_document.Project, copy, copied.X, copied.Y);
            var images = new Dictionary<string, LogoImage>(_logoImages);
            var problems = new Dictionary<string, string>(_logoProblems);
            var source = copy.Style.Logo.SourcePath;
            if (source.Length > 0 && !images.ContainsKey(source))
            {
                try
                {
                    var image = copy.Style.Logo.EmbeddedImage is { } embedded
                        ? await Task.Run(() => LogoImages.Decode(Convert.FromBase64String(embedded.DataBase64)))
                        : await LogoImages.LoadAsync(LogoImages.ResolvePath(source, _filePath));
                    if (images.Values.Sum(i => (long)i.ByteCount) + image.ByteCount > LogoImages.MaxCacheBytes)
                        throw new IOException(L.T("ui256"));
                    images.Add(source, image); problems.Remove(source);
                }
                catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException) { ResourceFailure(error); return false; }
        catch (Exception error) when (IsFileError(error)) { problems[source] = error.Message; }
            }
            var rendered = Render(next, copy, images);
            _document.Execute(new ReplaceCanvasProjectCommand(next)); _objectsSelected = true; _screenId = copy.Id;
            _logoImages = images; _logoProblems = problems; LoadControls(); UsePreview(rendered);
            SetStatus(source.Length > 0 && !images.ContainsKey(source) ? L.T("ui257") : L.T("ui258"));
            return true;
        }
        catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException) { ResourceFailure(error); return false; }
        catch (Exception error) when (IsFileError(error)) { SetStatus(L.F("ui259", error.Message)); return false; }
        finally { SetBusy(false); }
    }
}
