using GridMapper.Storage;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private bool CanExportResolume => IsCanvas && !_busy && !HasPointerEdit
        && _document.Project.Canvas.Placements.Any(p => !p.IsHidden);

    internal async Task<bool> ExportResolumeAsync()
    {
        if (!CanExportResolume) return false;
        SetBusy(true);
        try
        {
            if (!ApplyAutomatic()) return false;
            var project = _document.Project;
            var name = Path.GetFileNameWithoutExtension(_filePath ?? "project.gpm");
            var bytes = ResolumeExport.Serialize(project, name);
            var path = await _dialogs.PickSaveAsync(this, EditorFileKind.Resolume,
                ExportFileNames.Resolume(name, project.Canvas.WidthPx, project.Canvas.HeightPx));
            if (path is null) return false;
            await AtomicFiles.WriteAsync(path, bytes, CancellationToken.None);
            SetStatus(L.F("resolume.saved", Path.GetFileName(path), project.Canvas.WidthPx, project.Canvas.HeightPx));
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        { SetStatus(L.F("ui115", error.Message)); ShowNotice(L.F("ui115", error.Message)); return false; }
        finally { SetBusy(false); }
    }
}
