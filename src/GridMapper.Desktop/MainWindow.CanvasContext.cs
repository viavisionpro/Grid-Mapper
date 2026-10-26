using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using GridMapper.Core;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    internal ContextMenu CanvasArrangementMenu { get; } = new() { Classes = { "arrangement" } };
    private void BuildCanvasContextMenu()
    {
        var entries = new[] { ("SendToBack", L.T("ui014")), ("SendBackward", L.T("ui015")),
            ("BringForward", L.T("ui016")), ("BringToFront", L.T("ui017")) };
        foreach (var (name, label) in entries)
        {
            var item = new MenuItem { Name = name, Header = label };
            item.Click += async (_, _) =>
            {
                if (!CanArrange(name)) return;
                await CanvasAction(p =>
                {
                    var count = p.Canvas.Placements.Count;
                    var index = p.Canvas.Placements.ToList().FindIndex(s => s.ScreenId == _screenId);
                    var target = name switch { "SendToBack" => 0, "SendBackward" => Math.Max(0, index - 1),
                        "BringForward" => Math.Min(count - 1, index + 1), _ => count - 1 };
                    return CanvasEditing.ReorderTo(p, _screenId, target);
                });
            };
            CanvasArrangementMenu.Items.Add(item);
        }
        _previewWorkspace.ContextRequested += (_, e) =>
        {
            if (!IsCanvas || _busy || HasPointerEdit) { e.Handled = true; return; }
            if (TryOpenImageMaskMenu(e)) return;
            CanvasImageMaskMenu.Close();
            if (e.TryGetPosition(_preview, out var point))
            {
                var rect = PreviewRasterRect(); var scale = rect.Width / PreviewWidth;
                var id = scale > 0 ? CanvasEditing.HitTestWorkspace(_document.Project,
                    (point.X - rect.X) / scale + PreviewOrigin.X, (point.Y - rect.Y) / scale + PreviewOrigin.Y) : null;
                if (id is { } hit && !SelectLayerForInput(hit)) { e.Handled = true; return; }
            }
            RefreshArrangementMenu(); CanvasArrangementMenu.Open(_previewWorkspace); e.Handled = true;
        };
        CanvasArrangementMenu.Opening += (_, _) => RefreshArrangementMenu();
        Closed += (_, _) => CanvasArrangementMenu.Close();
    }
    private bool CanArrange(string name) => IsCanvas && !_busy && !HasPointerEdit && _objectsSelected
        && _document.Project.Screens.Any(s => s.Id == _screenId)
        && _document.Project.Canvas.Placements.Count > (name is "SendToBack" or "BringToFront" ? 2 : 1);
    private void RefreshArrangementMenu()
    {
        foreach (var item in CanvasArrangementMenu.Items.OfType<MenuItem>())
        {
            item.Header = L.T(item.Name switch { "SendToBack" => "ui014", "SendBackward" => "ui015", "BringForward" => "ui016", _ => "ui017" });
            item.IsEnabled = CanArrange(item.Name!);
        }
    }
    private void UpdateCanvasSizeLabel()
    {
        _canvasSizeLabel.IsVisible = IsCanvas;
        _canvasSizeLabel.Text = $"Canvas · {_document.Project.Canvas.WidthPx} × {_document.Project.Canvas.HeightPx} px";
        if (_canvasSizeLabel.Parent is not Control parent) return;
        var origin = _workspaceRasters.TranslatePoint(_workspaceRasters.CanvasRect.TopLeft, parent);
        if (origin is { } p)
        {
            var y = Math.Max(8, p.Y - 22); var x = p.X + 6;
            // All three buttons, spacing and margins occupy more than 112 DIP.
            // Use their measured bounds so the Canvas caption cannot sit under them.
            var tools = parent.GetLogicalChildren().OfType<Control>().FirstOrDefault(c => c.Name == "WorkspaceTools");
            var toolsRight = Math.Max(158, (tools?.Bounds.Right ?? 0) + 12);
            if (y < Math.Max(52, tools?.Bounds.Bottom ?? 0) + 8 && x < toolsRight) x = toolsRight;
            _canvasSizeLabel.Margin = new Thickness(x, y, 0, 0);
        }
        // This overlay is outside the scaled raster: its 12 DIP text remains
        // readable at every zoom, just like the workspace's 0,0 marker.
    }
}
