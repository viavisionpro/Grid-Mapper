using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using GridMapper.Core;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private static readonly (int X, int Y)[] ResizeHandles = [(-1, -1), (1, -1), (1, 1), (-1, 1), (0, -1), (1, 0), (0, 1), (-1, 0)];
    private sealed record ScreenResize(ProjectModel Project, RegularGrid Grid, ScreenGridResize Edit,
        (int X, int Y) Handle, Point Start, double Scale, Point Origin, double ViewFactor, Vector Offset)
    {
        public ProjectModel Candidate { get; set; } = Project;
        public bool ViewFrozen { get; set; }
    }
    private ScreenResize? _screenResize;
    private IPointer? _resizePointer;
    private Point? _resizeHover;

    private void BuildScreenResize()
    {
        _previewWorkspace.AddHandler(PointerPressedEvent, ScreenResizePressed, RoutingStrategies.Tunnel);
        _previewWorkspace.AddHandler(PointerMovedEvent, ScreenResizeMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _previewWorkspace.AddHandler(PointerReleasedEvent, ScreenResizeReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _previewWorkspace.PointerCaptureLost += (_, _) => { if (_screenResize is not null) CancelScreenResize(); };
        _previewWorkspace.PointerExited += (_, _) => { _resizeHover = null; UpdateResizeCursor(); };
    }
    private Rect SingleScreenViewRect()
    {
        var raster = PreviewRasterRect();
        return new(_preview.TranslatePoint(raster.TopLeft, _screenOverlay) ?? raster.TopLeft, raster.Size);
    }
    private (int X, int Y)? ResizeHandleAt(Point point)
    {
        if (_screenOverlay.Selection is not { } rect) return null;
        // The corner wins if a very small screen makes adjacent handles overlap.
        foreach (var handle in _screenOverlay.Handles)
        {
            var center = CabinetOverlay.HandleCenter(rect, handle.X, handle.Y);
            if (Math.Abs(point.X - center.X) <= CabinetOverlay.HandleSize / 2 + 2
                && Math.Abs(point.Y - center.Y) <= CabinetOverlay.HandleSize / 2 + 2) return handle;
        }
        return null;
    }
    private void UpdateResizeCursor()
    {
        if (_spaceDown || _pan is not null) { _previewWorkspace.Cursor = ResizeCursors.Hand; return; }
        if (_maskEnabled) { _previewWorkspace.Cursor = MaskCursor.Value; return; }
        if (_screenDrag is { Moved: true }) { _previewWorkspace.Cursor = ResizeCursors.Move; return; }
        if (_imageMaskGesture is { Corner: null, Moved: true }) { _previewWorkspace.Cursor = ResizeCursors.Move; return; }
        var imageCorner = _imageMaskGesture?.Corner;
        if (imageCorner is null && !_busy && !HasPointerEdit && _resizeHover is { } imageHover)
            imageCorner = ImageMaskCornerAt(_previewWorkspace.TranslatePoint(imageHover, _imageMaskOverlay) ?? imageHover);
        if (imageCorner is { } corner) { _previewWorkspace.Cursor = corner.X == corner.Y ? ResizeCursors.NorthWestSouthEast : ResizeCursors.NorthEastSouthWest; return; }
        var handle = _screenResize?.Handle;
        if (handle is null && !_busy && !HasPointerEdit && _resizeHover is { } hover)
            handle = ResizeHandleAt(_previewWorkspace.TranslatePoint(hover, _screenOverlay) ?? hover);
        var cursor = handle switch
        {
            { X: 0 } => ResizeCursors.Vertical,
            { Y: 0 } => ResizeCursors.Horizontal,
            { X: var x, Y: var y } when x == y => ResizeCursors.NorthWestSouthEast,
            not null => ResizeCursors.NorthEastSouthWest,
            _ => ResizeCursors.Arrow
        };
        _previewWorkspace.Cursor = cursor;
    }
    private static readonly Lazy<Cursor> MaskCursor = new(() => new(StandardCursorType.Cross));
    private void ScreenResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_busy || HasPointerEdit || _spaceDown || !e.GetCurrentPoint(_previewWorkspace).Properties.IsLeftButtonPressed) return;
        var handle = ResizeHandleAt(e.GetPosition(_screenOverlay));
        if (handle is null) return;
        if (HasDraft && !ApplyAutomatic()) { e.Handled = true; return; }
        var grid = RegularGrid.Detect(SelectedScreen); var rect = PreviewRasterRect();
        if (grid is null || rect.Width <= 0) return;
        EndEditGroup();
        var origin = _preview.TranslatePoint(rect.TopLeft, _previewViewport) ?? rect.TopLeft;
        _screenResize = new(_document.Project, grid, new(_document.Project, _screenId), handle.Value,
            e.GetPosition(_previewWorkspace), rect.Width / PreviewWidth, origin, _viewFactor, _previewViewport.Offset);
        _resizePointer = e.Pointer; e.Pointer.Capture(_previewWorkspace); _preview.Focus();
        UpdateActions(); UpdateResizeCursor(); e.Handled = true;
    }
    private void ScreenResizeMoved(object? sender, PointerEventArgs e)
    {
        _resizeHover = e.GetPosition(_previewWorkspace);
        if (_screenResize is not { } drag) { UpdateResizeCursor(); return; }
        var delta = e.GetPosition(_previewWorkspace) - drag.Start;
        var columns = drag.Handle.X == 0 ? drag.Grid.Columns : (int)Math.Clamp(drag.Grid.Columns
            + Math.Round(delta.X / drag.Scale / drag.Grid.Type.WidthPx * drag.Handle.X, MidpointRounding.AwayFromZero), 1, ModelLimits.MaxCabinets);
        var rows = drag.Handle.Y == 0 ? drag.Grid.Rows : (int)Math.Clamp(drag.Grid.Rows
            + Math.Round(delta.Y / drag.Scale / drag.Grid.Type.HeightPx * drag.Handle.Y, MidpointRounding.AwayFromZero), 1, ModelLimits.MaxCabinets);
        var current = RegularGrid.Detect(drag.Candidate.Screens.First(s => s.Id == _screenId))!;
        if (columns != current.Columns || rows != current.Rows)
        {
            try
            {
                var next = drag.Edit.Resize(columns, rows, drag.Handle.X < 0, drag.Handle.Y < 0);
                var rendered = Render(next, next.Screens.First(s => s.Id == _screenId));
                drag.Candidate = next;
                if (IsCanvas && !drag.ViewFrozen && double.IsNaN(_viewFactor))
                {
                    var view = _previewWorkspace.TranslatePoint(drag.Start, _previewViewport) ?? ViewCenter;
                    SetZoom(drag.Scale, SceneAtView(view), view); drag.ViewFrozen = true;
                }
                UsePreview(rendered);
                _syncingControls = true;
                try
                {
                    _gridFields[4].Text = columns.ToString(CultureInfo.InvariantCulture);
                    _gridFields[5].Text = rows.ToString(CultureInfo.InvariantCulture);
                    var placement = next.Canvas.Placements.Single(p => p.ScreenId == _screenId);
                    _placementX.Text = placement.X.ToString(CultureInfo.InvariantCulture);
                    _placementY.Text = placement.Y.ToString(CultureInfo.InvariantCulture);
                }
                finally { _syncingControls = false; }
                var g = next.Screens.First(s => s.Id == _screenId).Geometry;
                SetStatus(L.F("ui023", columns, rows, g.Bounds.Width, g.Bounds.Height));
            }
            catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException)
            { CancelScreenResize(); ResourceFailure(error); e.Handled = true; return; }
            catch (ArgumentException error) { SetStatus(error.Message); }
        }
        UpdateResizeCursor(); e.Handled = true;
    }
    private void ScreenResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_screenResize is not { } drag || e.InitialPressMouseButton != MouseButton.Left) return;
        ScreenResizeMoved(sender, e);
        if (_screenResize is null) return;
        _screenResize = null; _resizePointer = null; e.Pointer.Capture(null);
        if (!ReferenceEquals(drag.Candidate, drag.Project)) _document.Execute(new ReplaceCanvasProjectCommand(drag.Candidate));
        else if (drag.ViewFrozen) { RestoreResizeView(drag); RefreshPreview(); _previewViewport.Offset = drag.Offset; }
        LoadControls(); UpdateActions(); UpdatePreviewSummary(); UpdateScreenOverlay(); UpdateResizeCursor();
        SetStatus(ReferenceEquals(drag.Candidate, drag.Project) ? L.T("ui024") : L.T("ui025"));
        e.Handled = true;
    }
    private void CancelScreenResize()
    {
        if (_screenResize is not { } drag) return;
        _screenResize = null; var pointer = _resizePointer; _resizePointer = null; pointer?.Capture(null);
        RestoreResizeView(drag);
        RefreshPreview(); _previewViewport.Offset = drag.Offset;
        LoadControls(); UpdateActions(); UpdateResizeCursor(); SetStatus(L.T("ui026"));
    }
    private void RestoreResizeView(ScreenResize drag)
    {
        if (drag.ViewFrozen)
        {
            _viewFactor = drag.ViewFactor; _viewAnchor = null;
            _syncZoom = true;
            try { _zoom.SelectedIndex = double.IsNaN(drag.ViewFactor) ? 0 : drag.ViewFactor == 1 ? 1 : drag.ViewFactor == 2 ? 2 : 3; }
            finally { _syncZoom = false; }
        }
    }
}
