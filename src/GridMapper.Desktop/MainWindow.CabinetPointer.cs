using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GridMapper.Core;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly Grid _previewSurface = new();
    private readonly CabinetOverlay _cabinetOverlay = new() { Name = "CabinetOverlay", IsHitTestVisible = false };
    private readonly CheckBox _cabinetSnap = new() { Name = "CabinetSnap", Content = L.T("ui001"), IsChecked = true };
    private sealed record CabinetDrag(ScreenModel Screen, Guid Id, Point Start, double Scale, int X, int Y)
    {
        public ScreenModel? Candidate { get; set; }
        public int ProposedX { get; set; } = X;
        public int ProposedY { get; set; } = Y;
        public bool Moved { get; set; }
    }
    private CabinetDrag? _cabinetDrag;
    private IPointer? _cabinetPointer;
    private void BuildCabinetPointer()
    {
        _previewSurface.Children.Add(_preview); _previewSurface.Children.Add(_workspaceRasters); _previewSurface.Children.Add(_cabinetOverlay);
        _previewViewport.Content = _previewSurface; _preview.Focusable = true;
        _preview.PointerPressed += CabinetPointerPressed;
        _preview.PointerMoved += CabinetPointerMoved;
        _preview.PointerReleased += CabinetPointerReleased;
        _preview.PointerCaptureLost += (_, _) => { if (_cabinetDrag is not null) CancelCabinetDrag(); };
        _preview.SizeChanged += (_, _) => UpdateCabinetOverlay();
        _preview.LayoutUpdated += (_, _) => UpdateCabinetOverlay();
        _cabinetPanel.Children.Add(_cabinetSnap);
        _cabinetPanel.Children.Add(new TextBlock { Text = L.T("ui002"), FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
    }
    private Rect PreviewRasterRect()
    {
        if (_pngWidth == 0 || _pngHeight == 0) return default;
        var scale = Math.Min(_preview.Bounds.Width / PreviewWidth, _preview.Bounds.Height / PreviewHeight);
        return new((_preview.Bounds.Width - PreviewWidth * scale) / 2, (_preview.Bounds.Height - PreviewHeight * scale) / 2, PreviewWidth * scale, PreviewHeight * scale);
    }
    private Rect CabinetViewRect(ScreenModel screen, Guid id, int? x = null, int? y = null)
    {
        var cabinet = screen.Cabinets.First(c => c.Id == id); var type = screen.CabinetTypes.First(t => t.Id == cabinet.TypeId);
        var rect = PreviewRasterRect(); var scale = rect.Width / _pngWidth; var bounds = screen.Geometry.Bounds;
        var origin = _preview.TranslatePoint(rect.TopLeft, _cabinetOverlay) ?? rect.TopLeft;
        return new(origin.X + ((x ?? cabinet.X) - (double)bounds.X) * scale, origin.Y + ((y ?? cabinet.Y) - (double)bounds.Y) * scale,
            type.WidthPx * scale, type.HeightPx * scale);
    }
    private void UpdateCabinetOverlay()
    {
        Rect? selection = !IsProjection && _objectsSelected && !IsCanvas && _cabinetMode.IsChecked == true && _cabinetId is { } id && SelectedScreen.Cabinets.Any(c => c.Id == id)
            ? CabinetViewRect(SelectedScreen, id) : null;
        Rect? moving = _cabinetDrag is { Moved: true } drag ? CabinetViewRect(drag.Screen, drag.Id, drag.ProposedX, drag.ProposedY) : null;
        var invalid = _cabinetDrag is { Moved: true, Candidate: null };
        if (_cabinetOverlay.Selection == selection && _cabinetOverlay.Moving == moving && _cabinetOverlay.Invalid == invalid) return;
        _cabinetOverlay.Selection = selection; _cabinetOverlay.Moving = moving; _cabinetOverlay.Invalid = invalid;
        _cabinetOverlay.InvalidateVisual();
    }
    private void CabinetPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_busy || HasPointerEdit || IsProjection || IsCanvas || _cabinetMode.IsChecked != true || !e.GetCurrentPoint(_preview).Properties.IsLeftButtonPressed) return;
        if (HasDraft && !ApplyAutomatic()) return;
        var rect = PreviewRasterRect(); if (rect.Width <= 0) return;
        var point = e.GetPosition(_preview); var scale = rect.Width / _pngWidth;
        var hit = CabinetEditing.HitTest(SelectedScreen, (point.X - rect.X) / scale, (point.Y - rect.Y) / scale);
        if (hit is not { } id) return;
        EndEditGroup(); _objectsSelected = true; _cabinetId = id; SyncCabinetControls(); _preview.Focus();
        var cabinet = CurrentCabinet;
        _cabinetDrag = new(SelectedScreen, id, point, scale, cabinet.X, cabinet.Y) { Candidate = SelectedScreen };
        _cabinetPointer = e.Pointer; e.Pointer.Capture(_preview); UpdateActions(); UpdateCabinetOverlay(); e.Handled = true;
    }
    private void CabinetPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_cabinetDrag is not { } drag) return;
        var point = e.GetPosition(_preview);
        if (!drag.Moved && Math.Abs(point.X - drag.Start.X) < 3 && Math.Abs(point.Y - drag.Start.Y) < 3) return;
        drag.Moved = true;
        var x = (int)Math.Clamp(drag.X + Math.Round((point.X - drag.Start.X) / drag.Scale, MidpointRounding.AwayFromZero), 0, int.MaxValue);
        var y = (int)Math.Clamp(drag.Y + Math.Round((point.Y - drag.Start.Y) / drag.Scale, MidpointRounding.AwayFromZero), 0, int.MaxValue);
        if (_cabinetSnap.IsChecked == true) (x, y) = CabinetEditing.Snap(drag.Screen, drag.Id, x, y, 6 / drag.Scale);
        drag.ProposedX = x; drag.ProposedY = y;
        try { drag.Candidate = CabinetEditing.Move(drag.Screen, drag.Id, x, y); SetStatus(L.F("ui003", x, y)); }
        catch (ArgumentException error) { drag.Candidate = null; SetStatus(error.Message); }
        UpdateCabinetOverlay(); e.Handled = true;
    }
    private void CabinetPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_cabinetDrag is not { } drag || e.InitialPressMouseButton != MouseButton.Left) return;
        // The pointer's release can carry a final position without an intervening move event.
        CabinetPointerMoved(sender, e);
        _cabinetDrag = null; _cabinetPointer = null; e.Pointer.Capture(null);
        try { if (drag.Moved && drag.Candidate is { } candidate) ExecuteCabinetEdit(candidate); }
        catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException)
        { ResourceFailure(error); UpdateCabinetOverlay(); e.Handled = true; return; }
        SyncCabinetControls(); UpdateCabinetOverlay();
        SetStatus(!drag.Moved ? L.T("ui004") : drag.Candidate is null ? L.T("ui005") : L.T("ui006"));
        e.Handled = true;
    }
    private void CancelCabinetDrag()
    {
        _cabinetDrag = null; var pointer = _cabinetPointer; _cabinetPointer = null; pointer?.Capture(null); SyncCabinetControls(); UpdateCabinetOverlay(); SetStatus(L.T("ui007"));
    }
    private bool HandleCabinetKey(KeyEventArgs e)
    {
        if (IsProjection) return false;
        if (_cabinetDrag is not null && e.Key == Key.Escape) { CancelCabinetDrag(); e.Handled = true; return true; }
        if (_cabinetDrag is not null) { e.Handled = true; return true; }
        if (!_objectsSelected || IsCanvas || _cabinetMode.IsChecked != true || e.Source is not Control control || control != _preview && !control.GetVisualAncestors().Contains(_preview)) return false;
        if (e.KeyModifiers is not KeyModifiers.None and not KeyModifiers.Shift) return false;
        if (e.Key == Key.Delete) { CabinetAction(() => CabinetEditing.Remove(SelectedScreen, CurrentCabinet.Id)).GetAwaiter().GetResult(); e.Handled = true; return true; }
        var step = e.KeyModifiers == KeyModifiers.Shift ? 10 : 1;
        var (dx, dy) = e.Key switch { Key.Left => (-step, 0), Key.Right => (step, 0), Key.Up => (0, -step), Key.Down => (0, step), _ => (0, 0) };
        if (dx == 0 && dy == 0) return false;
        CabinetAction(() => CabinetEditing.Move(SelectedScreen, CurrentCabinet.Id, (int)Math.Clamp((long)CurrentCabinet.X + dx, 0, int.MaxValue), (int)Math.Clamp((long)CurrentCabinet.Y + dy, 0, int.MaxValue))).GetAwaiter().GetResult();
        e.Handled = true; return true;
    }
}
