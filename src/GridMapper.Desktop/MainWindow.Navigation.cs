using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private double _viewFactor = double.NaN;
    internal const double MinZoom = .1;
    internal const double MaxZoom = 2;
    private bool _syncZoom;
    private bool _spaceDown;
    private sealed record ViewAnchor(Point Scene, Point View);
    private ViewAnchor? _viewAnchor;
    private sealed record PanGesture(Point Start, Point Scene, double Factor, Vector Offset, MouseButton Button);
    private PanGesture? _pan;
    private IPointer? _panPointer;
    private void BuildNavigation()
    {
        _workspaceRasters.UseLayoutRounding = _preview.UseLayoutRounding = _previewSurface.UseLayoutRounding = _previewViewport.UseLayoutRounding = false;
        _previewViewport.Focusable = true;
        _previewViewport.Background = _previewSurface.Background = Brushes.Transparent;
        _previewViewport.BringIntoViewOnFocusChange = false;
        _previewWorkspace.ClipToBounds = _previewViewport.ClipToBounds = true;
        // Include the workspace padding and blank areas, and intercept before ScrollViewer.
        _previewWorkspace.AddHandler(PointerWheelChangedEvent, ZoomWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        _previewWorkspace.AddHandler(PointerTouchPadGestureMagnifyEvent, ZoomMagnify, RoutingStrategies.Bubble, handledEventsToo: true);
        _previewWorkspace.AddHandler(PointerPressedEvent, PanPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _previewWorkspace.AddHandler(PointerMovedEvent, PanMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _previewWorkspace.AddHandler(PointerReleasedEvent, PanReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _previewWorkspace.PointerCaptureLost += (_, _) => { if (_pan is not null) CancelPan(); };
        _preview.LayoutUpdated += (_, _) => ApplyViewAnchor();
        _previewViewport.ScrollChanged += (_, e) =>
        {
            if (e.ViewportDelta != default) ApplyZoom();
            ApplyViewAnchor();
            UpdateScreenOverlay();
        };
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Space) { _spaceDown = false; UpdateNavigationCursor(); } }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => { _spaceDown = false; CancelPointerGestures(); UpdateNavigationCursor(); };
        ScalingChanged += (_, _) => CancelPointerGestures();
        _previewViewport.SizeChanged += (_, _) => { CancelPointerGestures(); ApplyZoom(); };
    }
    private void CancelPointerGestures()
    {
        if (_imageMaskGesture is not null) CancelImageMaskGesture();
        if (_maskStroke is not null) CancelMask();
        if (_cabinetDrag is not null) CancelCabinetDrag();
        if (_screenDrag is not null) CancelScreenDrag();
        if (_screenResize is not null) CancelScreenResize();
        if (_layerDrag is not null) CancelLayerDrag();
        if (_pan is not null) CancelPan();
    }
    private Point ViewCenter => new(_previewViewport.Viewport.Width / 2, _previewViewport.Viewport.Height / 2);
    private Point SceneAtView(Point view)
    {
        var imagePoint = _previewViewport.TranslatePoint(view, _preview) ?? default;
        var rect = PreviewRasterRect(); var scale = rect.Width / PreviewWidth;
        return scale > 0 ? new((imagePoint.X - rect.X) / scale, (imagePoint.Y - rect.Y) / scale) : new(PreviewWidth / 2d, PreviewHeight / 2d);
    }
    private void SelectZoom()
    {
        if (_syncZoom || HasPointerEdit) return;
        var center = ViewCenter; var scene = SceneAtView(center);
        _viewFactor = _zoom.SelectedIndex switch { 1 => 1d, 2 => 2d, 3 => _viewFactor, _ => double.NaN };
        _viewAnchor = double.IsNaN(_viewFactor) ? null : new(scene, center);
        ApplyZoom();
    }
    private void SetZoom(double factor, Point scene, Point view)
    {
        _viewFactor = Math.Clamp(factor, MinZoom, MaxZoom);
        _syncZoom = true;
        try
        {
            _zoom.ItemsSource = new[] { "Fit", "100%", "200%", (_viewFactor * 100).ToString("0", CultureInfo.InvariantCulture) + "%" };
            _zoom.SelectedIndex = _viewFactor == 1 ? 1 : _viewFactor == 2 ? 2 : 3;
        }
        finally { _syncZoom = false; }
        _viewAnchor = new(scene, view); ApplyZoom();
    }
    private void ApplyZoom()
    {
        if (_pngWidth == 0 || _previewViewport.Viewport.Width <= 0 || _previewViewport.Viewport.Height <= 0) return;
        var fit = double.IsNaN(_viewFactor);
        var factor = fit ? Math.Min(_previewViewport.Viewport.Width / Math.Max(1, PreviewWidth), _previewViewport.Viewport.Height / Math.Max(1, PreviewHeight)) : _viewFactor;
        var bounded = Math.Clamp(factor, MinZoom, MaxZoom);
        factor = bounded;
        _previewViewport.HorizontalScrollBarVisibility = _previewViewport.VerticalScrollBarVisibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _workspaceRasters.Width = _preview.Width = PreviewWidth * factor; _workspaceRasters.Height = _preview.Height = PreviewHeight * factor;
        _workspaceRasters.HorizontalAlignment = _preview.HorizontalAlignment = HorizontalAlignment.Center; _workspaceRasters.VerticalAlignment = _preview.VerticalAlignment = VerticalAlignment.Center;
        _previewSurface.Width = !fit ? PreviewWidth * factor + _previewViewport.Viewport.Width : double.NaN;
        _previewSurface.Height = !fit ? PreviewHeight * factor + _previewViewport.Viewport.Height : double.NaN;
        if (fit) { _viewAnchor = null; _previewViewport.Offset = default; }
        UpdateCabinetOverlay(); UpdateScreenOverlay();
    }
    private void ApplyViewAnchor()
    {
        if (_viewAnchor is not { } anchor || double.IsNaN(_viewFactor) || _pngWidth == 0
            || Math.Abs(_preview.Bounds.Width - PreviewWidth * _viewFactor) > 0.01
            || Math.Abs(_previewSurface.Bounds.Width - _previewSurface.Width) > 0.01
            || Math.Abs(_previewViewport.Extent.Width - _previewSurface.Width) > 0.01
            || Math.Abs(_previewViewport.Extent.Height - _previewSurface.Height) > 0.01) return;
        _viewAnchor = null;
        var current = _preview.TranslatePoint(new(anchor.Scene.X * _viewFactor, anchor.Scene.Y * _viewFactor), _previewSurface);
        if (current is { } point) _previewViewport.Offset = new Vector(point.X - anchor.View.X, point.Y - anchor.View.Y);
    }
    private void ZoomWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_busy || HasPointerEdit) { e.Handled = true; return; }
        if (e.Delta.Y == 0 || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        ZoomAt(e.GetPosition(_previewViewport), Math.Pow(1.2, Math.Clamp(e.Delta.Y, -10, 10)));
        e.Handled = true;
    }
    private void ZoomMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (_busy || HasPointerEdit) { e.Handled = true; return; }
        // Native macOS magnification is a fractional increment (0.25 = +25%).
        ZoomAt(e.GetPosition(_previewViewport), 1 + e.Delta.X);
        e.Handled = true;
    }
    private void ZoomAt(Point view, double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier <= 0) return;
        var scene = _viewAnchor is { } pending ? new Point(pending.Scene.X + (view.X - pending.View.X) / _viewFactor,
            pending.Scene.Y + (view.Y - pending.View.Y) / _viewFactor) : SceneAtView(view);
        var factor = double.IsNaN(_viewFactor) ? PreviewRasterRect().Width / PreviewWidth : _viewFactor;
        SetZoom(factor * multiplier, scene, view);
    }
    private void PanPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_busy || HasPointerEdit) return;
        var buttons = e.GetCurrentPoint(_previewViewport).Properties;
        var button = buttons.IsMiddleButtonPressed ? MouseButton.Middle : _spaceDown
            && buttons.IsLeftButtonPressed ? MouseButton.Left : MouseButton.None;
        if (button == MouseButton.None)
        {
            if (buttons.IsLeftButtonPressed) _previewViewport.Focus();
            return;
        }
        var view = e.GetPosition(_previewViewport); var scene = SceneAtView(view);
        _pan = new(view, scene, _viewFactor, _previewViewport.Offset, button);
        if (double.IsNaN(_viewFactor)) SetZoom(PreviewRasterRect().Width / PreviewWidth, scene, view);
        _panPointer = e.Pointer; e.Pointer.Capture(_previewWorkspace); _previewViewport.Focus(); UpdateActions(); UpdateNavigationCursor(); e.Handled = true;
    }
    private void PanMoved(object? sender, PointerEventArgs e)
    {
        if (_pan is not { } pan) return;
        _viewAnchor = new(pan.Scene, e.GetPosition(_previewViewport)); ApplyViewAnchor(); e.Handled = true;
    }
    private void PanReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pan is not { } pan || e.InitialPressMouseButton != pan.Button) return;
        PanMoved(sender, e); _pan = null; _panPointer = null; e.Pointer.Capture(null);
        UpdateActions(); UpdateNavigationCursor(); e.Handled = true;
    }
    private void CancelPan()
    {
        if (_pan is not { } pan) return;
        _pan = null; var pointer = _panPointer; _panPointer = null; pointer?.Capture(null);
        if (double.IsNaN(pan.Factor)) { _syncZoom = true; _zoom.SelectedIndex = 0; _syncZoom = false; _viewFactor = double.NaN; _viewAnchor = null; ApplyZoom(); }
        else { _viewAnchor = null; _previewViewport.Offset = pan.Offset; }
        UpdateActions(); UpdateNavigationCursor();
    }
    private bool HandleNavigationKey(KeyEventArgs e)
    {
        if (_pan is not null) { if (e.Key == Key.Escape) CancelPan(); e.Handled = true; return true; }
        if (HasPointerEdit) return false;
        var control = e.Source as Control;
        if (control != _previewWorkspace && (control is null || !control.GetVisualAncestors().Contains(_previewWorkspace))) return false;
        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None) return false;
        _spaceDown = true; UpdateNavigationCursor(); e.Handled = true; return true;
    }
    private void UpdateNavigationCursor() => UpdateResizeCursor();
}
