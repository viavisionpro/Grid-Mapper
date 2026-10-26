using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using GridMapper.Core;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private void SelectionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsProjection) ImageMaskPressed(sender, e);
        if (e.Handled) return;
        if (_busy || HasPointerEdit || e.Handled || !e.GetCurrentPoint(_previewWorkspace).Properties.IsLeftButtonPressed) return;
        if (e.Source is Control control && control.GetSelfAndVisualAncestors().OfType<ScrollBar>().Any()) return;
        // Selection handles can extend beyond the screen's raster bounds.
        if (ResizeHandleAt(e.GetPosition(_screenOverlay)) is not null)
        { ScreenResizePressed(sender, e); e.Handled = true; return; }
        var point = e.GetPosition(_preview); var rect = PreviewRasterRect(); var scale = rect.Width / PreviewWidth;
        // The empty project's layout extent is not a selectable screen.
        var hit = HasScreens && scale > 0 && rect.Contains(point);
        if (hit)
        {
            var x = (point.X - rect.X) / scale; var y = (point.Y - rect.Y) / scale;
            hit = IsCanvas ? CanvasEditing.HitTestWorkspace(_document.Project, x + PreviewOrigin.X, y + PreviewOrigin.Y) is not null
                : IsProjection || _cabinetMode.IsChecked != true || CabinetEditing.HitTest(SelectedScreen, x, y) is not null;
        }
        if (hit)
        {
            if (!IsCanvas && (IsProjection || _cabinetMode.IsChecked != true) && !_objectsSelected)
            { _objectsSelected = true; LoadControls(); UpdateScreenOverlay(); }
            return;
        }
        EndEditGroup(); _objectsSelected = false; _cabinetId = null;
        LoadControls(); UpdateCabinetOverlay(); UpdateScreenOverlay();
        _previewViewport.Focus(); SetStatus(L.T("ui178")); e.Handled = true;
    }
}
