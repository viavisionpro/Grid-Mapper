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
    private sealed record CanvasLayerRow(Guid Id, string Label) { public override string ToString() => Label; }
    private readonly ListBox _canvasLayers = new() { Name = "CanvasLayers", MaxHeight = 160, Focusable = true };
    private readonly TextBlock _layerDrop = new() { Name = "LayerDrop", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private bool _syncLayers;
    private readonly LayerReorderOverlay _layerOverlay = new() { Name = "LayerReorderOverlay", IsHitTestVisible = false };
    private sealed record LayerGesture(ProjectModel Project, Guid Id, Point Start, int Index)
    {
        public int Target { get; set; } = Index;
        public bool Moved { get; set; }
        public Point Grab { get; set; }
        public Size Size { get; set; }
        public Point Point { get; set; }
    }
    private LayerGesture? _layerDrag;
    private IPointer? _layerPointer;
    private void BuildCanvasLayers()
    {
        _canvasLayers.SelectionChanged += (_, _) =>
        {
            if (_syncLayers || _syncingControls || _busy || HasPointerEdit || _canvasLayers.SelectedItem is not CanvasLayerRow row) return;
            if (HasDraft && !ApplyAutomatic()) { SyncCanvasLayers(); return; }
            if (_screenId == row.Id && _objectsSelected) return;
            _objectsSelected = true; _screenId = row.Id; LoadControls(); if (IsCanvas) { UpdatePreviewSummary(); UpdateScreenOverlay(); } else RefreshPreview(); SetStatus(L.T("ui008"));
        };
        _canvasLayers.AddHandler(PointerPressedEvent, LayerPressed, RoutingStrategies.Tunnel);
        PointerMoved += LayerMoved; PointerReleased += LayerReleased;
        PointerCaptureLost += (_, _) => { if (_layerDrag is not null) CancelLayerDrag(); };
        _canvasLayers.LayoutUpdated += (_, _) => { if (_layerDrag is { Moved: true } drag) UpdateLayerVisual(drag); };
        Closed += (_, _) => _layerOverlay.Clear();
    }
    private void SyncCanvasLayers()
    {
        if (_syncLayers) return;
        var screens = _document.Project.Screens.ToDictionary(s => s.Id);
        var rows = _document.Project.Canvas.Placements.Reverse().Select(p => new CanvasLayerRow(p.ScreenId, screens[p.ScreenId].Name
            + (p.IsHidden ? L.T("ui009") : "") + (p.IsLocked ? L.T("ui010") : ""))).ToArray();
        _syncLayers = true;
        try
        {
            if (_canvasLayers.ItemsSource is not CanvasLayerRow[] previous || !previous.SequenceEqual(rows)) { _layerEditors.Clear(); _canvasLayers.ItemsSource = rows; }
            foreach (var row in rows) UpdateLayerEditor(row.Id);
            _canvasLayers.SelectedIndex = _objectsSelected ? Array.FindIndex(rows, r => r.Id == _screenId) : -1;
        }
        finally { _syncLayers = false; }
        if (_layerDrag is null) _layerDrop.Text = L.T("ui011");
    }
    private void LayerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_busy || HasPointerEdit || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var item = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (item is null || e.GetPosition(item).X is < 0 or > 30) return;
        var index = _canvasLayers.IndexFromContainer(item);
        if (index < 0 || _canvasLayers.ItemsSource is not CanvasLayerRow[] rows) return;
        if (HasDraft && !ApplyAutomatic()) { e.Handled = true; return; }
        EndEditGroup(); _objectsSelected = true; _screenId = rows[index].Id; LoadControls(); if (!IsCanvas) RefreshPreview(); UpdatePreviewSummary(); UpdateScreenOverlay();
        _layerDrag = new(_document.Project, _screenId, e.GetPosition(this), index) { Grab = e.GetPosition(item), Size = item.Bounds.Size };
        _layerOverlay.Capture(item, RenderScaling);
        _layerPointer = e.Pointer; e.Pointer.Capture(this); _canvasLayers.Focus(); UpdateActions(); e.Handled = true;
    }
    private void LayerMoved(object? sender, PointerEventArgs e)
    {
        if (_layerDrag is not { } drag) return;
        var point = e.GetPosition(this);
        if (!drag.Moved && Math.Abs(point.X - drag.Start.X) < 4 && Math.Abs(point.Y - drag.Start.Y) < 4) return;
        drag.Moved = true;
        drag.Point = e.GetPosition(_layerOverlay); Cursor = ResizeCursors.Move; _canvasLayers.Cursor = ResizeCursors.Move;
        var realized = Enumerable.Range(0, _canvasLayers.ItemCount).Select(i => (Index: i, Container: _canvasLayers.ContainerFromIndex(i)))
            .Where(row => row.Container is not null).Select(row => (row.Index,
                Center: row.Container!.TranslatePoint(new Point(0, row.Container!.Bounds.Height / 2), this)?.Y)).Where(row => row.Center.HasValue).ToArray();
        if (realized.Length > 0) drag.Target = realized.MinBy(row => Math.Abs(row.Center!.Value - point.Y)).Index;
        var top = _canvasLayers.TranslatePoint(default, this)?.Y ?? point.Y;
        if (point.Y < top && drag.Target > 0) _canvasLayers.ScrollIntoView(--drag.Target);
        else if (point.Y > top + _canvasLayers.Bounds.Height && drag.Target < _canvasLayers.ItemCount - 1) _canvasLayers.ScrollIntoView(++drag.Target);
        _layerDrop.Text = L.F("ui012", drag.Target + 1, _canvasLayers.ItemCount);
        UpdateLayerVisual(drag);
        e.Handled = true;
    }
    private void LayerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_layerDrag is not { } drag || e.InitialPressMouseButton != MouseButton.Left) return;
        LayerMoved(sender, e); _layerDrag = null; _layerPointer = null; e.Pointer.Capture(null);
        ClearLayerVisual();
        if (drag.Moved) CanvasAction(_ => CanvasEditing.ReorderTo(drag.Project, drag.Id, drag.Project.Canvas.Placements.Count - 1 - drag.Target)).GetAwaiter().GetResult();
        UpdateActions(); SyncCanvasLayers(); _canvasLayers.Focus(); e.Handled = true;
    }
    private void CancelLayerDrag()
    {
        _layerDrag = null; var pointer = _layerPointer; _layerPointer = null; pointer?.Capture(null);
        ClearLayerVisual();
        UpdateActions(); SyncCanvasLayers(); _canvasLayers.Focus(); SetStatus(L.T("ui013"));
    }
    private void ClearLayerVisual()
    { _layerOverlay.Clear(); ClearValue(CursorProperty); _canvasLayers.ClearValue(CursorProperty); }
    private void UpdateLayerVisual(LayerGesture drag)
    {
        var origin = _canvasLayers.TranslatePoint(default, _layerOverlay) ?? default;
        _layerOverlay.ClipBounds = new Rect(origin, _canvasLayers.Bounds.Size);
        _layerOverlay.GhostBounds = new Rect(new Point(origin.X, drag.Point.Y - drag.Grab.Y), drag.Size);
        if (_canvasLayers.ContainerFromIndex(drag.Target) is Control target)
            _layerOverlay.InsertionY = target.TranslatePoint(new Point(0, drag.Target > drag.Index ? target.Bounds.Height : 0), _layerOverlay)?.Y;
        else _layerOverlay.InsertionY = null;
        if (_layerOverlay.InsertionY is { } y && _layerOverlay.ClipBounds is { } clip && clip.Height >= 4)
            _layerOverlay.InsertionY = Math.Clamp(y, clip.Top + 2, clip.Bottom - 2);
        _layerOverlay.InvalidateVisual();
    }
    private bool HandleLayerKey(KeyEventArgs e)
    {
        if (_layerDrag is null)
        {
            if ((e.Source as Visual)?.GetSelfAndVisualAncestors().Any(v => v is TextBox) == true) return false;
            if (!IsCanvas || !IsLayerInput(e.Source) || e.KeyModifiers != KeyModifiers.None) return false;
            if (e.Key == Key.Delete && !_objectsSelected) return false;
            if (e.Key == Key.Delete) CanvasAction(p => CanvasEditing.Remove(p, _screenId)).GetAwaiter().GetResult();
            else
            {
                var index = e.Key switch { Key.Up => _canvasLayers.SelectedIndex - 1, Key.Down => _canvasLayers.SelectedIndex + 1,
                    Key.Home => 0, Key.End => _canvasLayers.ItemCount - 1, _ => -1 };
                if (e.Key is not Key.Up and not Key.Down and not Key.Home and not Key.End) return false;
                _canvasLayers.SelectedIndex = Math.Clamp(index, 0, _canvasLayers.ItemCount - 1);
            }
            e.Handled = true; return true;
        }
        if (e.Key == Key.Escape) CancelLayerDrag(); e.Handled = true; return true;
    }
    private bool IsLayerInput(object? source) => source is Control control && (control == _canvasLayers || control.GetVisualAncestors().Contains(_canvasLayers));
}
