using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using GridMapper.Core;
using GridMapper.Rendering;
using GridMapper.Storage;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly Button _imageMaskButton = new() { Name = "CanvasImageMask", Width = 34, Height = 32, Padding = new Thickness(5), Classes = { "mode" } };
    private readonly Button _imageMaskLoad = new() { Name = "LoadCanvasImageMask", MinHeight = 0, Height = 22, Width = 64, FontSize = 10, Padding = new Thickness(5, 1) };
    private readonly Slider _imageMaskTransparency = new() { Name = "CanvasImageMaskTransparency", Width = 76, Height = 20, MinHeight = 0, Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 1, IsSnapToTickEnabled = true, Classes = { "mask-slider" } };
    private readonly Button _imageMaskLock = new() { Name = "CanvasImageMaskLock", Width = 22, Height = 22, MinHeight = 0, Padding = new Thickness(3), Classes = { "mode" } };
    internal ContextMenu CanvasImageMaskMenu { get; } = new() { Name = "CanvasImageMaskMenu", Classes = { "arrangement" } };
    private readonly MenuItem _imageMaskLockItem = new() { Name = "ToggleCanvasImageMaskLock" };
    private readonly TextBlock _imageMaskPercentage = new() { Name = "CanvasImageMaskPercentage", Width = 30, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly CanvasImageMaskOverlay _imageMaskOverlay = new() { Name = "CanvasImageMaskOverlay", IsHitTestVisible = false };
    private Border? _imageMaskControls;
    private Bitmap? _imageMaskBitmap;
    private string? _imageMaskDecodedKey, _imageMaskRequestedKey;
    private CancellationTokenSource? _imageMaskDecodeCancellation;
    private bool _imageMaskSyncing, _imageMaskSelected, _imageMaskClosed;
    private object? _imageMaskSliderKey;
    internal bool ImageMaskReady => _document.Project.Canvas.ImageMask?.Image is not { } image || _imageMaskDecodedKey == image.Key;
    private sealed record ImageMaskGesture(CanvasImageMask Original, Point Start, double Scale, (int X, int Y)? Corner)
    {
        public CanvasImageMask Candidate { get; set; } = Original;
        public IReadOnlyList<CanvasEditing.SnapGuide> Guides { get; set; } = [];
        public bool Moved { get; set; }
    }
    private ImageMaskGesture? _imageMaskGesture;
    private IPointer? _imageMaskPointer;

    private Control BuildWorkspaceTools()
    {
        _imageMaskButton.Content = ActionIcon("ImageMask"); DescribeIcon(_imageMaskButton, L.T("mask.toggle"));
        _imageMaskLoad.Content = L.T("mask.load");
        _imageMaskButton.Click += (_, _) =>
        {
            if (_busy || HasPointerEdit || !IsCanvas || HasDraft && !ApplyAutomatic()) return;
            var mask = _document.Project.Canvas.ImageMask ?? new();
            _imageMaskSelected = !mask.Enabled && mask.Image is not null && !mask.IsLocked; if (_imageMaskSelected) _objectsSelected = false;
            CommitImageMask(mask.With(enabled: !mask.Enabled), render: true);
        };
        _imageMaskLoad.Click += async (_, _) => await ImportCanvasImageMaskAsync();
        _imageMaskLock.Click += (_, _) => ToggleImageMaskLock();
        _imageMaskLockItem.Click += (_, _) => ToggleImageMaskLock();
        CanvasImageMaskMenu.Items.Add(_imageMaskLockItem);
        CanvasImageMaskMenu.Opening += (_, _) => SyncImageMaskLock();
        Closed += (_, _) => CanvasImageMaskMenu.Close();
        _imageMaskTransparency.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || _imageMaskSyncing || _busy || HasPointerEdit || _document.Project.Canvas.ImageMask is not { } mask) return;
            CommitImageMask(mask.With(transparencyPercent: (int)Math.Round(_imageMaskTransparency.Value)), mergeKey: _imageMaskSliderKey);
        };
        _imageMaskTransparency.AddHandler(PointerPressedEvent, (_, _) => _imageMaskSliderKey = new object(), RoutingStrategies.Tunnel);
        _imageMaskTransparency.AddHandler(PointerReleasedEvent, (_, _) => _imageMaskSliderKey = null, RoutingStrategies.Bubble, handledEventsToo: true);
        _imageMaskTransparency.PointerCaptureLost += (_, _) => _imageMaskSliderKey = null;
        _imageMaskTransparency.LostFocus += (_, _) => _imageMaskSliderKey = null;
        var rail = new StackPanel { Name = "WorkspaceToolRail", Spacing = 8 };
        rail.Children.Add(_maskButton); rail.Children.Add(_edgeSnapButton); rail.Children.Add(_canvasAlphaButton);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(_imageMaskLoad);
        controls.Children.Add(new TextBlock { Text = L.T("mask.transparency"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        controls.Children.Add(_imageMaskTransparency); controls.Children.Add(_imageMaskPercentage); controls.Children.Add(_imageMaskLock);
        _imageMaskControls = new Border { Name = "CanvasImageMaskControls", Background = Brush.Parse("#29292F"), BorderBrush = Brush.Parse("#45454D"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 3), Margin = new Thickness(7, 0, 0, 0), Height = 30, Child = controls };
        var extension = new StackPanel { Name = "CanvasImageMaskTools", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
        extension.Children.Add(_imageMaskButton); extension.Children.Add(_imageMaskControls);
        var tools = new Grid { Name = "WorkspaceTools", ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16) };
        tools.Children.Add(rail); Grid.SetColumn(extension, 1); tools.Children.Add(extension);
        _previewWorkspace.AddHandler(PointerMovedEvent, ImageMaskMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _previewWorkspace.AddHandler(PointerReleasedEvent, ImageMaskReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _previewWorkspace.PointerCaptureLost += (_, _) => { if (_imageMaskGesture is not null) CancelImageMaskGesture(); };
        AddHandler(KeyDownEvent, (_, e) => { if (_imageMaskGesture is not null && e.Key == Key.Escape) { CancelImageMaskGesture(); e.Handled = true; } }, RoutingStrategies.Tunnel);
        Closed += (_, _) => { _imageMaskClosed = true; DisposeImageMask(); };
        return tools;
    }
    internal async Task<bool> ImportCanvasImageMaskAsync()
    {
        if (_busy || HasPointerEdit || !IsCanvas || _document.Project.Canvas.ImageMask?.IsLocked == true || HasDraft && !ApplyAutomatic()) return false;
        SetBusy(true);
        try
        {
            var path = await _dialogs.PickCanvasMaskAsync(this); if (path is null) return false;
            SetStatus(L.T("mask.loading"));
            var imported = await CanvasMaskImages.ImportAsync(path);
            var old = _document.Project.Canvas.ImageMask;
            var width = imported.Preview.Width * Math.Min(1, Math.Min(_document.Project.Canvas.WidthPx * .8 / imported.Preview.Width, _document.Project.Canvas.HeightPx * .8 / imported.Preview.Height));
            var asset = old?.Image?.Key == imported.Asset.Key ? old.Image : imported.Asset;
            var mask = new CanvasImageMask(asset, imported.Preview.Width, imported.Preview.Height, true,
                (_document.Project.Canvas.WidthPx - width) / 2, (_document.Project.Canvas.HeightPx - width * imported.Preview.Height / imported.Preview.Width) / 2,
                width, old?.TransparencyPercent ?? 50);
            var next = CanvasEditing.SetImageMask(_document.Project, mask);
            await Task.Run(() => ProjectFiles.Serialize(next)); // Enforce the complete project's existing 16 MiB budget before changing it.
            var rendered = Render(next, SelectedScreen);
            InstallImageMask(imported.Preview, mask);
            EndEditGroup(); _document.Execute(new PreparedEdit(next)); _imageMaskSelected = true; _objectsSelected = false;
            LoadControls(); UsePreview(rendered); SetStatus(L.F("mask.loaded", Path.GetFileName(path))); return true;
        }
        catch (Exception e) when (e is RenderLimitException or OutOfMemoryException) { ResourceFailure(e); return false; }
        catch (Exception e) when (IsFileError(e)) { var text = L.F("mask.error", ImageMaskError(e)); SetStatus(text); ShowNotice(text); return false; }
        finally { SetBusy(false); }
    }
    private void SyncImageMaskLock()
    {
        var mask = _document.Project.Canvas.ImageMask;
        var locked = mask?.IsLocked == true;
        _imageMaskLock.IsEnabled = _imageMaskLockItem.IsEnabled = IsCanvas && mask?.Image is not null && !_busy && !HasPointerEdit;
        _imageMaskLock.Content = ActionIcon(locked ? "Lock" : "Unlock");
        _imageMaskLock.Classes.Set("active", locked);
        var label = L.T(locked ? "mask.unlock" : "mask.lock");
        DescribeIcon(_imageMaskLock, label);
        _imageMaskLockItem.Header = label;
        _imageMaskLockItem.Icon = ActionIcon(locked ? "Lock" : "Unlock");
    }
    private void ToggleImageMaskLock()
    {
        if (_busy || HasPointerEdit || !IsCanvas || _document.Project.Canvas.ImageMask is not { Image: not null } mask
            || HasDraft && !ApplyAutomatic()) return;
        EndEditGroup();
        _imageMaskSelected = mask.IsLocked;
        if (_imageMaskSelected) _objectsSelected = false;
        CommitImageMask(mask.With(isLocked: !mask.IsLocked));
        LoadControls(); UpdateScreenOverlay();
    }
    private bool TryOpenImageMaskMenu(ContextRequestedEventArgs e)
    {
        if (_document.Project.Canvas.ImageMask is not { Enabled: true, Image: not null }
            || _imageMaskOverlay.Frame is not { } frame) return false;
        var hit = e.TryGetPosition(_imageMaskOverlay, out var point) ? frame.Inflate(5).Contains(point) : _imageMaskSelected;
        if (!hit) return false;
        CanvasArrangementMenu.Close(); SyncImageMaskLock(); CanvasImageMaskMenu.Open(_previewWorkspace);
        e.Handled = true; return true;
    }
    private static string ImageMaskError(Exception error) => error.Message.StartsWith("mask.", StringComparison.Ordinal) ? L.T(error.Message) : L.Error(error.Message);
    private static async Task<CanvasMaskPreview?> ReadImageMask(CanvasImageMask? mask)
    {
        if (mask?.Image is not { } image) return null;
        try
        {
            var preview = await CanvasMaskImages.DecodeAsync(image);
            if (preview.Width != mask.PixelWidth || preview.Height != mask.PixelHeight) throw new IOException("mask.invalid");
            return preview;
        }
        catch (IOException e) { throw new IOException(ImageMaskError(e), e); }
    }
    private void InstallImageMask(CanvasMaskPreview? preview, CanvasImageMask? mask)
    {
        Bitmap? bitmap = null;
        if (preview is not null) { using var stream = new MemoryStream(preview.Png, writable: false); bitmap = new Bitmap(stream); }
        _imageMaskDecodeCancellation?.Cancel(); _imageMaskDecodeCancellation?.Dispose(); _imageMaskDecodeCancellation = null;
        _imageMaskOverlay.Bitmap = null; var previous = _imageMaskBitmap; _imageMaskBitmap = bitmap;
        _imageMaskDecodedKey = _imageMaskRequestedKey = mask?.Image?.Key; previous?.Dispose();
    }
    private void DisposeImageMask()
    {
        _imageMaskDecodeCancellation?.Cancel(); _imageMaskDecodeCancellation?.Dispose(); _imageMaskDecodeCancellation = null;
        _imageMaskOverlay.Bitmap = null; _imageMaskOverlay.Frame = null;
        _imageMaskBitmap?.Dispose(); _imageMaskBitmap = null; _imageMaskDecodedKey = _imageMaskRequestedKey = null;
    }
    private void SyncImageMaskState()
    {
        if (_imageMaskClosed || _imageMaskControls is null) return;
        var mask = _document.Project.Canvas.ImageMask;
        _imageMaskButton.IsVisible = IsCanvas;
        _imageMaskControls.IsVisible = IsCanvas && mask?.Enabled == true;
        _imageMaskButton.IsEnabled = !_busy && !HasPointerEdit;
        _imageMaskLoad.IsEnabled = !_busy && !HasPointerEdit && mask?.IsLocked != true;
        SyncImageMaskLock();
        _imageMaskTransparency.IsEnabled = !_busy && !HasPointerEdit && mask?.Image is not null;
        _imageMaskButton.Background = Brush.Parse(mask?.Enabled == true ? "#4D4D59" : "#34343B");
        _imageMaskSyncing = true;
        try { _imageMaskTransparency.Value = mask?.TransparencyPercent ?? 50; _imageMaskPercentage.Text = (mask?.TransparencyPercent ?? 50) + "%"; }
        finally { _imageMaskSyncing = false; }
        ToolTip.SetTip(_imageMaskLoad, mask?.Image is { } asset ? asset.FileName : L.T("mask.open"));
        if (mask?.Image is not { } image) { if (_imageMaskBitmap is not null || _imageMaskRequestedKey is not null) DisposeImageMask(); }
        else if (_imageMaskDecodedKey != image.Key && _imageMaskRequestedKey != image.Key) _ = LoadImageMaskForHistory(mask);
        UpdateImageMaskOverlay();
    }
    private async Task LoadImageMaskForHistory(CanvasImageMask mask)
    {
        _imageMaskDecodeCancellation?.Cancel(); _imageMaskDecodeCancellation?.Dispose();
        var cancellation = new CancellationTokenSource(); _imageMaskDecodeCancellation = cancellation;
        var token = cancellation.Token; _imageMaskRequestedKey = mask.Image!.Key;
        try
        {
            var preview = await CanvasMaskImages.DecodeAsync(mask.Image, token);
            if (preview.Width != mask.PixelWidth || preview.Height != mask.PixelHeight) throw new IOException("mask.invalid");
            if (_imageMaskClosed || token.IsCancellationRequested || _document.Project.Canvas.ImageMask?.Image?.Key != mask.Image.Key) return;
            InstallImageMask(preview, mask); UpdateImageMaskOverlay();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (IsFileError(e) || e is OutOfMemoryException)
        { if (!_imageMaskClosed && !token.IsCancellationRequested) { SetStatus(L.F("mask.error", ImageMaskError(e))); UpdateImageMaskOverlay(); } }
    }
    private void CommitImageMask(CanvasImageMask mask, bool render = false, object? mergeKey = null)
    {
        var next = CanvasEditing.SetImageMask(_document.Project, mask); if (ReferenceEquals(next, _document.Project)) return;
        try
        {
            var rendered = render ? Render(next, SelectedScreen) : null;
            _document.Execute(new PreparedEdit(next), mergeKey);
            if (rendered is not null) UsePreview(rendered);
        }
        catch (Exception e) when (e is RenderLimitException or OutOfMemoryException) { ResourceFailure(e); }
        UpdateActions(); UpdateScreenOverlay();
    }
    private Rect ImageMaskViewRect(CanvasImageMask mask)
    {
        var raster = PreviewRasterRect(); var scale = raster.Width / PreviewWidth;
        var origin = _preview.TranslatePoint(raster.TopLeft, _imageMaskOverlay) ?? default;
        return new(origin.X + (mask.X - PreviewOrigin.X) * scale, origin.Y + (mask.Y - PreviewOrigin.Y) * scale, mask.Width * scale, mask.Height * scale);
    }
    private void UpdateImageMaskOverlay()
    {
        if (_objectsSelected) _imageMaskSelected = false;
        var mask = _imageMaskGesture?.Candidate ?? _document.Project.Canvas.ImageMask;
        var visible = IsCanvas && mask is { Enabled: true, Image: not null } && mask.Image.Key == _imageMaskDecodedKey && _imageMaskBitmap is not null && _pngWidth > 0;
        var frame = visible ? ImageMaskViewRect(mask!) : (Rect?)null;
        var bitmap = visible ? _imageMaskBitmap : null;
        var opacity = 1 - (mask?.TransparencyPercent ?? 100) / 100d;
        var selected = _imageMaskSelected && mask?.IsLocked != true && !_maskEnabled && !_busy;
        var showFrame = mask?.IsLocked != true;
        var guides = new List<CabinetOverlay.GuideLine>();
        if (visible && _imageMaskGesture is { Moved: true } gesture)
        {
            var raster = PreviewRasterRect(); var scale = raster.Width / PreviewWidth;
            var origin = _preview.TranslatePoint(raster.TopLeft, _imageMaskOverlay) ?? default;
            Point World(double x, double y) => origin + new Vector((x - PreviewOrigin.X) * scale, (y - PreviewOrigin.Y) * scale);
            foreach (var guide in gesture.Guides)
                guides.Add(guide.Vertical
                    ? new(World(guide.Position, guide.From) - new Vector(0, 12), World(guide.Position, guide.To) + new Vector(0, 12))
                    : new(World(guide.From, guide.Position) - new Vector(12, 0), World(guide.To, guide.Position) + new Vector(12, 0)));
        }
        if (_imageMaskOverlay.Frame == frame && ReferenceEquals(_imageMaskOverlay.Bitmap, bitmap)
            && _imageMaskOverlay.ImageOpacity == opacity && _imageMaskOverlay.Selected == selected
            && _imageMaskOverlay.ShowFrame == showFrame && _imageMaskOverlay.Guides.SequenceEqual(guides)) return;
        _imageMaskOverlay.Frame = frame;
        _imageMaskOverlay.Bitmap = bitmap;
        _imageMaskOverlay.ImageOpacity = opacity;
        _imageMaskOverlay.Selected = selected;
        _imageMaskOverlay.ShowFrame = showFrame;
        _imageMaskOverlay.Guides = guides;
        _imageMaskOverlay.InvalidateVisual();
    }
    private (int X, int Y)? ImageMaskCornerAt(Point point)
    {
        if (!_imageMaskOverlay.Selected || _imageMaskOverlay.Frame is not { } frame) return null;
        foreach (var corner in CanvasImageMaskOverlay.Corners)
        {
            var center = CabinetOverlay.HandleCenter(frame, corner.X, corner.Y);
            if (Math.Abs(center.X - point.X) <= 7 && Math.Abs(center.Y - point.Y) <= 7) return corner;
        }
        return null;
    }
    private void ImageMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_busy || HasPointerEdit || !IsCanvas || _spaceDown || _maskEnabled || !e.GetCurrentPoint(_previewWorkspace).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Avalonia.Controls.Primitives.ScrollBar>().Any() == true) return;
        var point = e.GetPosition(_imageMaskOverlay); var corner = ImageMaskCornerAt(point);
        var frame = _imageMaskOverlay.Frame;
        if (_document.Project.Canvas.ImageMask is not { Image: not null, IsLocked: false } mask || frame is null) return;
        var world = MaskWorld(e);
        var borderHit = frame.Value.Inflate(5).Contains(point) && !frame.Value.Deflate(5).Contains(point);
        // A deselected mask is click-through over screens; its coloured frame
        // remains selectable. The eraser always paints the underlying panels.
        var hit = corner is not null || borderHit || frame.Value.Contains(point) && (_imageMaskSelected
            || CanvasEditing.HitTestWorkspace(_document.Project, world.X, world.Y) is null);
        if (!hit) { _imageMaskSelected = false; UpdateImageMaskOverlay(); return; }
        if (HasDraft && !ApplyAutomatic()) { e.Handled = true; return; }
        EndEditGroup(); _objectsSelected = false; _imageMaskSelected = true; LoadControls();
        var scale = PreviewRasterRect().Width / PreviewWidth;
        _imageMaskGesture = new(mask, e.GetPosition(_previewWorkspace), scale, corner);
        _imageMaskPointer = e.Pointer; e.Pointer.Capture(_previewWorkspace); _previewViewport.Focus();
        UpdateActions(); UpdateScreenOverlay(); e.Handled = true;
    }
    private void ImageMaskMoved(object? sender, PointerEventArgs e)
    {
        if (_imageMaskGesture is not { } gesture) return;
        var delta = (e.GetPosition(_previewWorkspace) - gesture.Start) / gesture.Scale;
        if (Math.Abs(delta.X * gesture.Scale) + Math.Abs(delta.Y * gesture.Scale) < 3 && !gesture.Moved) return;
        gesture.Moved = true;
        if (gesture.Corner is { } corner)
        {
            var original = gesture.Original; var ratio = original.Height / original.Width;
            var dx = corner.X * delta.X; var dy = corner.Y * delta.Y;
            var maxWidth = Math.Min(1_000_000, 1_000_000 / ratio);
            // Keep the fixed opposite corner inside the model's finite coordinate budget.
            if (corner.X < 0) maxWidth = Math.Min(maxWidth, original.X + original.Width + 100_000_000);
            if (corner.Y < 0) maxWidth = Math.Min(maxWidth, (original.Y + original.Height + 100_000_000) / ratio);
            var width = Math.Clamp(original.Width + (dx + dy * ratio) / (1 + ratio * ratio), .0001, maxWidth);
            gesture.Candidate = original.With(width: width, x: original.X + (corner.X < 0 ? original.Width - width : 0),
                y: original.Y + (corner.Y < 0 ? original.Height - width * ratio : 0));
        }
        else gesture.Candidate = gesture.Original.With(x: Math.Clamp(gesture.Original.X + delta.X, -100_000_000, 100_000_000), y: Math.Clamp(gesture.Original.Y + delta.Y, -100_000_000, 100_000_000));
        var snapped = CanvasImageMaskSnapping.Snap(_document.Project, gesture.Candidate, 8 / gesture.Scale, gesture.Corner);
        gesture.Candidate = snapped.Mask; gesture.Guides = snapped.Guides;
        UpdateImageMaskOverlay(); UpdateResizeCursor(); e.Handled = true;
    }
    private void ImageMaskReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_imageMaskGesture is not { } gesture || e.InitialPressMouseButton != MouseButton.Left) return;
        ImageMaskMoved(sender, e); _imageMaskGesture = null; _imageMaskPointer = null; e.Pointer.Capture(null);
        if (gesture.Moved) CommitImageMask(gesture.Candidate, render: true);
        UpdateActions(); UpdateScreenOverlay(); e.Handled = true;
    }
    private void CancelImageMaskGesture()
    {
        _imageMaskGesture = null; var pointer = _imageMaskPointer; _imageMaskPointer = null; pointer?.Capture(null);
        UpdateActions(); UpdateScreenOverlay();
    }
}
