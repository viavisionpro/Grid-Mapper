using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using GridMapper.Core;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly Button _maskButton = new() { Name = "PanelMask", Width = 34, Padding = new Thickness(5), Classes = { "mode" } };
    private readonly Border _maskStripe = new() { Name = "MaskModeStripe", BorderThickness = new Thickness(3), BorderBrush = Brush.Parse("#F5BBD1"), IsHitTestVisible = false, IsVisible = false };
    private readonly MaskOutlineOverlay _maskOverlay = new() { Name = "MaskOutlineOverlay", IsHitTestVisible = false };
    private bool _maskEnabled;
    private sealed record MaskPanel(Guid Screen, Guid Cabinet, Rect Bounds);
    private sealed class MaskStroke(ProjectModel project, IReadOnlyList<MaskPanel> panels, bool masked, Point start)
    {
        public ProjectModel Original { get; } = project;
        public ProjectModel Candidate { get; set; } = project;
        public IReadOnlyList<MaskPanel> Panels { get; } = panels;
        public bool Masked { get; } = masked;
        public Point Previous { get; set; } = start;
        public Dictionary<Guid, HashSet<Guid>> Visited { get; } = [];
    }
    private MaskStroke? _maskStroke;
    private IPointer? _maskPointer;
    private ProjectModel? _maskOutlineProject;
    private Guid _maskOutlineScreen;
    private bool _maskOutlineCanvas;
    private IReadOnlyList<MaskPanel> _maskedPanels = [];
    private IReadOnlyList<(Point Start, Point End)> _maskEdges = [];
    private (Point Origin, double Scale, Point Offset, bool Active)? _maskOutlineTransform;
    private void BuildMask()
    {
        _maskButton.Content = ActionIcon("Eraser"); DescribeIcon(_maskButton, L.T("ui239"));
        _maskButton.Click += (_, _) => { if (_busy || HasPointerEdit || !HasScreens) return; _maskEnabled = !_maskEnabled; UpdateMaskState(); UpdateScreenOverlay(); };
        _previewWorkspace.AddHandler(PointerPressedEvent, MaskPressed, RoutingStrategies.Tunnel);
        _previewWorkspace.AddHandler(PointerMovedEvent, MaskMoved, RoutingStrategies.Tunnel);
        _previewWorkspace.AddHandler(PointerReleasedEvent, MaskReleased, RoutingStrategies.Tunnel);
        _previewWorkspace.PointerCaptureLost += (_, _) => { if (_maskStroke is not null) CancelMask(); };
        _preview.LayoutUpdated += (_, _) => UpdateMaskOverlay();
        AddHandler(KeyDownEvent, (_, e) => { if (_maskStroke is not null && e.Key == Key.Escape) { CancelMask(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    private void UpdateMaskState()
    {
        if (!HasScreens || IsProjection) _maskEnabled = false;
        _maskStripe.IsVisible = _maskEnabled;
        _maskButton.IsEnabled = HasScreens && !_busy && _maskStroke is null;
        _maskButton.Classes.Set("active", _maskEnabled);
        _maskButton.Background = Brush.Parse(_maskEnabled ? "#674752" : "#34343B");
        UpdateMaskOverlay(); UpdateNavigationCursor();
    }
    private IReadOnlyList<MaskPanel> MaskPanels(ProjectModel project)
    {
        var result = new List<MaskPanel>();
        foreach (var placement in project.Canvas.Placements.Where(p => !IsCanvas ? p.ScreenId == _screenId : !p.IsHidden))
        {
            var screen = project.Screens.First(s => s.Id == placement.ScreenId); var types = screen.CabinetTypes.ToDictionary(t => t.Id);
            foreach (var cabinet in screen.Cabinets)
            {
                var type = types[cabinet.TypeId]; var bounds = screen.Geometry.Bounds;
                result.Add(new(screen.Id, cabinet.Id, new(cabinet.X - bounds.X + (IsCanvas ? placement.X : 0),
                    cabinet.Y - bounds.Y + (IsCanvas ? placement.Y : 0), type.WidthPx, type.HeightPx)));
            }
        }
        return result;
    }
    private Point MaskWorld(PointerEventArgs e)
    {
        var rect = PreviewRasterRect(); var scale = rect.Width / PreviewWidth; var point = e.GetPosition(_preview);
        return new((point.X - rect.X) / scale + (IsCanvas ? PreviewOrigin.X : 0), (point.Y - rect.Y) / scale + (IsCanvas ? PreviewOrigin.Y : 0));
    }
    private void MaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_maskEnabled || _busy || HasPointerEdit || _spaceDown || !HasScreens || !e.GetCurrentPoint(_previewWorkspace).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Avalonia.Controls.Primitives.ScrollBar>().Any() == true) return;
        e.Handled = true;
        if (HasDraft && !ApplyAutomatic()) return;
        var point = MaskWorld(e); var panels = MaskPanels(_document.Project);
        var hit = panels.LastOrDefault(p => p.Bounds.Contains(point)); if (hit is null) return;
        EndEditGroup(); var screen = _document.Project.Screens.First(s => s.Id == hit.Screen);
        _maskStroke = new(_document.Project, panels, !screen.Cabinets.First(c => c.Id == hit.Cabinet).IsMasked, point);
        _maskPointer = e.Pointer; e.Pointer.Capture(_previewWorkspace);
        PaintMask(point); UpdateActions();
    }
    private void MaskMoved(object? sender, PointerEventArgs e)
    {
        if (_maskStroke is null) return;
        PaintMask(MaskWorld(e)); e.Handled = true;
    }
    private void PaintMask(Point point)
    {
        if (_maskStroke is not { } stroke) return;
        // Intersect the complete segment with panel edges. Fast pointer moves
        // cannot skip panels; each visible panel is visited once per stroke.
        var delta = point - stroke.Previous; var cuts = new SortedSet<double> { 0, 1 };
        foreach (var panel in stroke.Panels)
        {
            double from = 0, to = 1;
            bool Axis(double start, double direction, double low, double high)
            {
                if (Math.Abs(direction) < 1e-12) return start >= low && start < high;
                var a = (low - start) / direction; var b = (high - start) / direction;
                from = Math.Max(from, Math.Min(a, b)); to = Math.Min(to, Math.Max(a, b)); return from <= to;
            }
            if (Axis(stroke.Previous.X, delta.X, panel.Bounds.Left, panel.Bounds.Right)
                && Axis(stroke.Previous.Y, delta.Y, panel.Bounds.Top, panel.Bounds.Bottom) && from <= 1 && to >= 0)
            { cuts.Add(Math.Clamp(from, 0, 1)); cuts.Add(Math.Clamp(to, 0, 1)); }
        }
        var times = cuts.ToArray(); var changed = false;
        foreach (var t in times.Zip(times.Skip(1), (a, b) => (a + b) / 2).Append(1))
        {
            var p = stroke.Previous + delta * t; var hit = stroke.Panels.LastOrDefault(panel => panel.Bounds.Contains(p));
            if (hit is null) continue;
            if (!stroke.Visited.TryGetValue(hit.Screen, out var ids)) stroke.Visited[hit.Screen] = ids = [];
            changed |= ids.Add(hit.Cabinet);
        }
        stroke.Previous = point; if (!changed) return;
        var screens = stroke.Original.Screens.Select(s => stroke.Visited.TryGetValue(s.Id, out var ids) ? CabinetEditing.SetMask(s, ids, stroke.Masked) : s).ToArray();
        stroke.Candidate = stroke.Original.With(screens: screens);
        try { UsePreview(Render(stroke.Candidate, screens.First(s => s.Id == _screenId))); }
        catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException)
        { CancelMask(); ResourceFailure(error); return; }
        UpdateMaskOverlay(); SetStatus(stroke.Masked ? L.T("ui240") : L.T("ui241"));
    }
    private void MaskReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_maskStroke is not { } stroke || e.InitialPressMouseButton != MouseButton.Left) return;
        PaintMask(MaskWorld(e)); if (_maskStroke is null) return; _maskStroke = null; _maskPointer = null; e.Pointer.Capture(null);
        _document.Execute(new ReplaceCanvasProjectCommand(stroke.Candidate)); LoadControls(); UpdateActions(); UpdateMaskOverlay(); e.Handled = true;
    }
    private void CancelMask()
    {
        _maskStroke = null; var pointer = _maskPointer; _maskPointer = null; pointer?.Capture(null);
        RefreshPreview(); UpdateActions(); UpdateMaskOverlay(); SetStatus(L.T("ui242"));
    }
    private void UpdateMaskOverlay()
    {
        var project = _maskStroke?.Candidate ?? _document.Project;
        var changed = !ReferenceEquals(_maskOutlineProject, project) || _maskOutlineScreen != _screenId || _maskOutlineCanvas != IsCanvas;
        if (changed)
        {
            var masked = project.Screens.SelectMany(s => s.Cabinets.Where(c => c.IsMasked).Select(c => c.Id)).ToHashSet();
            _maskedPanels = masked.Count == 0 ? [] : MaskPanels(project).Where(p => masked.Contains(p.Cabinet)).ToArray();
            _maskEdges = VisibleMaskEdges(project);
            _maskOutlineProject = project; _maskOutlineScreen = _screenId; _maskOutlineCanvas = IsCanvas;
        }
        var rect = PreviewRasterRect(); var scale = rect.Width / PreviewWidth;
        var origin = _preview.TranslatePoint(rect.TopLeft, _maskOverlay) ?? default;
        var offset = IsCanvas ? PreviewOrigin : default;
        var transform = (origin, scale, offset, _maskEnabled);
        if (!changed && _maskOutlineTransform == transform) return;
        _maskOutlineTransform = transform;
        _maskOverlay.Outlines = _maskedPanels.Select(p =>
            new Rect(origin.X + (p.Bounds.X - offset.X) * scale, origin.Y + (p.Bounds.Y - offset.Y) * scale, p.Bounds.Width * scale, p.Bounds.Height * scale)).ToArray();
        Point View(Point p) => new(origin.X + (p.X - offset.X) * scale, origin.Y + (p.Y - offset.Y) * scale);
        _maskOverlay.Edges = _maskEdges.Select(e => (View(e.Start), View(e.End))).ToArray();
        _maskOverlay.Active = _maskEnabled; _maskOverlay.InvalidateVisual();
    }
    private IReadOnlyList<(Point Start, Point End)> VisibleMaskEdges(ProjectModel project)
    {
        if (_maskedPanels.Count == 0) return [];
        var panels = MaskPanels(project).GroupBy(p => p.Screen).ToArray();
        var transparentMasks = IsCanvas && project.Canvas.TransparentBackground
            ? project.Screens.SelectMany(s => s.Cabinets.Where(c => c.IsMasked)).Select(c => c.Id).ToHashSet() : [];
        var edges = new List<(Point, Point)>();
        for (var index = 0; index < panels.Length; index++)
        {
            // Actual panel rectangles preserve the gaps in mixed assemblies.
            var front = panels.Skip(index + 1).SelectMany(g => g).Where(p => !transparentMasks.Contains(p.Cabinet)).Select(p => p.Bounds).ToArray();
            foreach (var panel in _maskedPanels.Where(p => p.Screen == panels[index].Key))
            {
                var r = panel.Bounds;
                Edge(true, r.Top, r.Left, r.Right); Edge(true, r.Bottom, r.Left, r.Right);
                Edge(false, r.Left, r.Top, r.Bottom); Edge(false, r.Right, r.Top, r.Bottom);
            }
            void Edge(bool horizontal, double fixedPosition, double start, double end)
            {
                var intervals = new List<(double Start, double End)> { (start, end) };
                foreach (var occluder in front)
                {
                    if (fixedPosition < (horizontal ? occluder.Top : occluder.Left)
                        || fixedPosition > (horizontal ? occluder.Bottom : occluder.Right)) continue;
                    var low = horizontal ? occluder.Left : occluder.Top; var high = horizontal ? occluder.Right : occluder.Bottom;
                    var remaining = new List<(double, double)>();
                    foreach (var (a, b) in intervals)
                    {
                        if (high <= a || low >= b) remaining.Add((a, b));
                        else { if (low > a) remaining.Add((a, low)); if (high < b) remaining.Add((high, b)); }
                    }
                    intervals = remaining; if (intervals.Count == 0) break;
                }
                foreach (var (a, b) in intervals)
                    edges.Add(horizontal ? (new(a, fixedPosition), new(b, fixedPosition)) : (new(fixedPosition, a), new(fixedPosition, b)));
            }
        }
        return edges;
    }
}

internal sealed class MaskOutlineOverlay : Control
{
    public IReadOnlyList<Rect> Outlines { get; set; } = [];
    public IReadOnlyList<(Point Start, Point End)> Edges { get; set; } = [];
    public bool Active { get; set; }
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Brush.Parse(Active ? "#F5BBD1" : "#73737E"), Active ? 1.2 : .7);
        foreach (var edge in Edges) context.DrawLine(pen, edge.Start, edge.End);
    }
}
