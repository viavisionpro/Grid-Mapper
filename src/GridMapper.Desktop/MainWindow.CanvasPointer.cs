using Avalonia;
using Avalonia.Input;
using GridMapper.Core;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly CabinetOverlay _screenOverlay = new() { Name = "ScreenOverlay", IsHitTestVisible = false };
    private sealed record ScreenDrag(ProjectModel Project, Guid Id, Point Start, double Scale, int X, int Y)
    {
        public int ProposedX { get; set; } = X;
        public int ProposedY { get; set; } = Y;
        public bool Moved { get; set; }
        public IReadOnlyList<CanvasEditing.SnapGuide> Guides { get; set; } = [];
    }
    private ScreenDrag? _screenDrag;
    private IPointer? _screenPointer;
    private bool HasPointerEdit => _imageMaskGesture is not null || _maskStroke is not null || _cabinetDrag is not null || _screenDrag is not null || _screenResize is not null || _pan is not null || _layerDrag is not null;
    private void BuildCanvasPointer()
    {
        BuildScreenResize();
        _previewWorkspace.AddHandler(PointerPressedEvent, SelectionPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _previewWorkspace.AddHandler(PointerPressedEvent, ScreenPointerPressed, Avalonia.Interactivity.RoutingStrategies.Bubble);
        _previewWorkspace.AddHandler(PointerMovedEvent, ScreenPointerMoved, Avalonia.Interactivity.RoutingStrategies.Bubble);
        _previewWorkspace.AddHandler(PointerReleasedEvent, ScreenPointerReleased, Avalonia.Interactivity.RoutingStrategies.Bubble);
        _preview.PointerCaptureLost += (_, _) => { if (_screenDrag is not null) CancelScreenDrag(); };
        _preview.LayoutUpdated += (_, _) => UpdateScreenOverlay();
    }
    private Rect ScreenViewRect(ProjectModel project, Guid id, int? x = null, int? y = null)
    {
        var bounds = project.Screens.First(s => s.Id == id).Geometry.Bounds;
        var placement = project.Canvas.Placements.Single(p => p.ScreenId == id);
        var rect = PreviewRasterRect(); var scale = _pngWidth > 0 ? rect.Width / PreviewWidth : 0;
        var origin = _preview.TranslatePoint(rect.TopLeft, _screenOverlay) ?? rect.TopLeft;
        return new(origin.X + ((x ?? placement.X) - PreviewOrigin.X) * scale, origin.Y + ((y ?? placement.Y) - PreviewOrigin.Y) * scale, bounds.Width * scale, bounds.Height * scale);
    }
    private void UpdateScreenOverlay()
    {
        UpdateCanvasSizeLabel(); UpdateImageMaskOverlay();
        var project = _screenResize?.Candidate ?? _document.Project;
        Rect? selection = IsProjection || !HasScreens || !_objectsSelected ? null : IsCanvas ? !Placement.IsHidden ? ScreenViewRect(project, _screenId) : null
            : _cabinetMode.IsChecked != true ? SingleScreenViewRect() : null;
        Rect? moving = _screenDrag is { Moved: true } drag ? ScreenViewRect(drag.Project, drag.Id, drag.ProposedX, drag.ProposedY) : null;
        var handles = selection is not null && RegularGrid.Detect(project.Screens.First(s => s.Id == _screenId)) is not null
            && !_busy && !_maskEnabled ? ResizeHandles.Where(h => !Placement.IsLocked || h.X >= 0 && h.Y >= 0).ToArray() : [];
        var guides = new List<CabinetOverlay.GuideLine>();
        if (_screenDrag is { Moved: true } guideDrag)
        {
            var rect = PreviewRasterRect(); var scale = rect.Width / PreviewWidth;
            var origin = _preview.TranslatePoint(rect.TopLeft, _screenOverlay) ?? rect.TopLeft;
            Point World(double x, double y) => origin + new Vector((x - PreviewOrigin.X) * scale, (y - PreviewOrigin.Y) * scale);
            foreach (var guide in guideDrag.Guides)
                guides.Add(guide.Vertical
                    ? new(World(guide.Position, guide.From) - new Vector(0, 12), World(guide.Position, guide.To) + new Vector(0, 12))
                    : new(World(guide.From, guide.Position) - new Vector(12, 0), World(guide.To, guide.Position) + new Vector(12, 0)));
        }
        UpdateResizeCursor();
        if (_screenOverlay.Selection == selection && _screenOverlay.Moving == moving && _screenOverlay.Handles.SequenceEqual(handles) && _screenOverlay.Guides.SequenceEqual(guides)) return;
        _screenOverlay.Handles = handles;
        _screenOverlay.Selection = selection; _screenOverlay.Moving = moving; _screenOverlay.Guides = guides; _screenOverlay.InvalidateVisual();
        UpdateResizeCursor();
    }
    private void ScreenPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_busy || HasPointerEdit || !IsCanvas || !e.GetCurrentPoint(_preview).Properties.IsLeftButtonPressed) return;
        if (HasDraft && !ApplyAutomatic()) return;
        var rect = PreviewRasterRect(); if (rect.Width <= 0) return;
        var scale = rect.Width / PreviewWidth; var point = e.GetPosition(_preview);
        var id = CanvasEditing.HitTestWorkspace(_document.Project, (point.X - rect.X) / scale + PreviewOrigin.X, (point.Y - rect.Y) / scale + PreviewOrigin.Y);
        if (id is null) return;
        EndEditGroup(); _objectsSelected = true; _screenId = id.Value; LoadControls(); UpdatePreviewSummary(); UpdateScreenOverlay(); _preview.Focus();
        if (Placement.IsLocked) { SetStatus(L.T("ui174")); e.Handled = true; return; }
        _screenDrag = new(_document.Project, _screenId, point, scale, Placement.X, Placement.Y);
        _screenPointer = e.Pointer; e.Pointer.Capture(_preview); UpdateActions(); e.Handled = true;
    }
    private void ScreenPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_screenDrag is not { } drag) return;
        var point = e.GetPosition(_preview);
        if (!drag.Moved && Math.Abs(point.X - drag.Start.X) < 3 && Math.Abs(point.Y - drag.Start.Y) < 3) return;
        drag.Moved = true;
        var x = (int)Math.Clamp(drag.X + Math.Round((point.X - drag.Start.X) / drag.Scale, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
        var y = (int)Math.Clamp(drag.Y + Math.Round((point.Y - drag.Start.Y) / drag.Scale, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
        var snapped = CanvasEditing.SnapWithGuides(drag.Project, drag.Id, x, y, 8 / drag.Scale);
        drag.ProposedX = snapped.X; drag.ProposedY = snapped.Y; drag.Guides = snapped.Guides;
        UpdateScreenOverlay(); SetStatus(L.F("ui175", drag.ProposedX, drag.ProposedY)); e.Handled = true;
    }
    private void ScreenPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_screenDrag is not { } drag || e.InitialPressMouseButton != MouseButton.Left) return;
        ScreenPointerMoved(sender, e); _screenDrag = null; _screenPointer = null; e.Pointer.Capture(null);
        if (drag.Moved)
        {
            var placement = drag.Project.Canvas.Placements.Single(p => p.ScreenId == drag.Id);
            var next = CanvasEditing.Place(drag.Project, drag.Id, drag.ProposedX, drag.ProposedY, false, placement.IsHidden);
            try
            {
                var rendered = Render(next, next.Screens.First(s => s.Id == drag.Id));
                _document.Execute(new ReplaceCanvasProjectCommand(next)); LoadControls(); UsePreview(rendered);
            }
            catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException)
            { ResourceFailure(error); UpdateActions(); UpdateScreenOverlay(); e.Handled = true; return; }
        }
        UpdateActions(); UpdateScreenOverlay(); SetStatus(drag.Moved ? L.T("ui176") : L.T("ui008")); e.Handled = true;
    }
    private void CancelScreenDrag()
    {
        _screenDrag = null; var pointer = _screenPointer; _screenPointer = null; pointer?.Capture(null);
        UpdateActions(); UpdateScreenOverlay(); SetStatus(L.T("ui177"));
    }
    private bool HandleScreenKey(KeyEventArgs e)
    {
        if (_screenResize is not null)
        { if (e.Key == Key.Escape) CancelScreenResize(); e.Handled = true; return true; }
        if (_screenDrag is not null)
        { if (e.Key == Key.Escape) CancelScreenDrag(); e.Handled = true; return true; }
        if (!_objectsSelected || !IsCanvas || e.Source != _preview || e.KeyModifiers is not KeyModifiers.None and not KeyModifiers.Shift) return false;
        if (e.Key == Key.Delete) { CanvasAction(p => CanvasEditing.Remove(p, _screenId)).GetAwaiter().GetResult(); e.Handled = true; return true; }
        var step = e.KeyModifiers == KeyModifiers.Shift ? 10 : 1;
        var (dx, dy) = e.Key switch { Key.Left => (-step, 0), Key.Right => (step, 0), Key.Up => (0, -step), Key.Down => (0, step), _ => (0, 0) };
        if (dx == 0 && dy == 0) return false;
        CanvasAction(p => CanvasEditing.Place(p, _screenId, (int)Math.Clamp((long)Placement.X + dx, int.MinValue, int.MaxValue),
            (int)Math.Clamp((long)Placement.Y + dy, int.MinValue, int.MaxValue), Placement.IsLocked, Placement.IsHidden)).GetAwaiter().GetResult();
        e.Handled = true; return true;
    }
}
