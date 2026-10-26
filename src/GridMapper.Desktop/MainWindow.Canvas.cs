using System.Globalization;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using GridMapper.Core;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly ComboBox _viewMode = new() { Name = "ViewMode", ItemsSource = new[] { L.T("ui139"), L.T("ui140") }, SelectedIndex = 0 };
    private readonly TextBox _canvasWidth = Field("CanvasWidth");
    private readonly TextBox _canvasHeight = Field("CanvasHeight");
    private readonly TextBox _placementX = Field("PlacementX");
    private readonly TextBox _placementY = Field("PlacementY");
    private readonly CheckBox _placementLocked = new() { Name = "PlacementLocked", Content = L.T("ui141") };
    private readonly CheckBox _placementHidden = new() { Name = "PlacementHidden", Content = L.T("ui142") };
    private readonly TextBlock _canvasWarnings = new() { Name = "CanvasWarnings", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Expander _canvasSection = new() { Name = "CanvasSection", Header = L.T("ui143"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly CheckBox _screenSnap = new() { Name = "CanvasSnap", Content = L.T("ui144") };
    private readonly TextBox _snapGrid = Field("CanvasSnapGrid");
    private readonly StackPanel _canvasTools = new() { Name = "CanvasTools", Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 6) };
    private readonly Button _edgeSnapButton = new() { Name = "ScreenEdgeSnap", Width = 34, Padding = new Thickness(5), Classes = { "mode" } };
    private bool _canvasViewInitialized;
    private bool _firstScreenCreated;
    private readonly Button _canvasAlphaButton = new() { Name = "CanvasTransparency", Width = 34, Padding = new Thickness(5), Classes = { "mode" } };
    private bool IsCanvas => !IsProjection && _viewMode.SelectedIndex == 1;
    private TextBox[] CanvasFields() => [_canvasWidth, _canvasHeight, _placementX, _placementY, _snapGrid];
    private ScreenPlacement Placement => _document.Project.Canvas.Placements.FirstOrDefault(p => p.ScreenId == _screenId) ?? EmptyEditorPlacement;
    private void BuildCanvasEditor()
    {
        var panel = new StackPanel { Spacing = 8 };
        var presets = new WrapPanel();
        AddAction(presets, "CanvasHD", "HD 1920×1080", () => CanvasAction(p => CanvasEditing.Resize(p, 1920, 1080)));
        AddAction(presets, "CanvasUHD", "UHD 3840×2160", () => CanvasAction(p => CanvasEditing.Resize(p, 3840, 2160)));
        panel.Children.Add(presets);
        AddPair(panel, L.T("ui145"), _placementX, L.T("ui146"), _placementY);
        panel.Children.Add(_placementLocked); panel.Children.Add(_placementHidden);
        var legacySnap = new StackPanel { IsVisible = false }; legacySnap.Children.Add(_screenSnap); legacySnap.Children.Add(_snapGrid); panel.Children.Add(legacySnap);
        panel.Children.Add(new TextBlock { Text = L.T("ui147"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
        var actions = new WrapPanel();
        AddAction(actions, "AddScreen", L.T("ui148"), () => CanvasAction(p =>
        {
            var screen = ScreenModel.Regular(NextScreenName(), new(Guid.NewGuid(), L.T("ui149"), 500, 500, 128, 128), 9, 5, NextColourStyle(p));
            return CanvasEditing.Add(p, screen);
        }, selectLast: true));
        AddAction(actions, "DuplicateScreen", L.T("ui150"), () => CanvasAction(p => CanvasEditing.Add(p, CanvasEditing.Copy(SelectedScreen, NextScreenName()), Placement.X, Placement.Y), selectLast: true));
        AddAction(actions, "CopyScreen", L.T("ui124"), CopyScreenAsync);
        AddAction(actions, "PasteScreen", L.T("ui125"), PasteScreenAsync);
        AddAction(actions, "DeleteScreen", L.T("ui151"), () => CanvasAction(p => CanvasEditing.Remove(p, _screenId)));
        AddAction(actions, "ScreenUp", L.T("ui152"), () => CanvasAction(p => CanvasEditing.Reorder(p, _screenId, 1)));
        AddAction(actions, "ScreenDown", L.T("ui153"), () => CanvasAction(p => CanvasEditing.Reorder(p, _screenId, -1)));
        panel.Children.Add(actions); panel.Children.Add(_canvasWarnings);
        panel.Children.Add(new TextBlock { Text = L.T("ui154"), FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        _canvasSection.Content = panel; _editor.Children.Add(_canvasSection);
        _viewMode.SelectionChanged += (_, _) =>
        {
            if (_syncingControls || _busy || HasPointerEdit) return;
            EndEditGroup(); ApplyAutomatic(); UpdateCanvasControls();
            try { UsePreview(Render(_document.Project, SelectedScreen)); }
            catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException)
            {
                _syncingControls = true;
                try { _viewMode.SelectedIndex = IsCanvas ? 0 : 1; }
                finally { _syncingControls = false; }
                ResourceFailure(error); return;
            }
            if (IsCanvas && !_canvasViewInitialized)
            {
                _canvasViewInitialized = true;
                var canvas = _document.Project.Canvas;
                var factor = Math.Min(Math.Max(1, _previewViewport.Viewport.Width - 48) / canvas.WidthPx,
                    Math.Max(1, _previewViewport.Viewport.Height - 48) / canvas.HeightPx);
                SetZoom(factor, new(canvas.WidthPx / 2d - PreviewOrigin.X, canvas.HeightPx / 2d - PreviewOrigin.Y), ViewCenter);
            }
            else if (IsCanvas && !double.IsNaN(_viewFactor)) _viewAnchor = new(new(PreviewWidth / 2, PreviewHeight / 2), ViewCenter);
        };
        _placementLocked.IsCheckedChanged += (_, _) => DraftChanged(_placementLocked);
        _screenSnap.IsCheckedChanged += (_, _) => DraftChanged(_screenSnap);
        _placementHidden.IsCheckedChanged += (_, _) => DraftChanged(_placementHidden);
    }
    private Control BuildCanvasTools()
    {
        _canvasAlphaButton.Content = ActionIcon("Transparency");
        DescribeIcon(_canvasAlphaButton, L.T("ui155"));
        _canvasAlphaButton.Click += async (_, _) => await CanvasAction(p => CanvasEditing.SetTransparency(p, !p.Canvas.TransparentBackground));
        _edgeSnapButton.Content = ActionIcon("Magnet");
        DescribeIcon(_edgeSnapButton, L.T("ui156"));
        _edgeSnapButton.Click += (_, _) => { if (!_busy && !HasPointerEdit) _screenSnap.IsChecked = _screenSnap.IsChecked != true; };
        _canvasWidth.Width = _canvasHeight.Width = 58;
        foreach (var field in new[] { _canvasWidth, _canvasHeight })
        {
            field.Height = field.MinHeight = 32; field.Padding = new Thickness(6, 4);
            field.FontSize = 13; field.VerticalContentAlignment = VerticalAlignment.Center;
            field.VerticalAlignment = VerticalAlignment.Center;
        }
        ToolTip.SetTip(_canvasWidth, L.T("ui157")); ToolTip.SetTip(_canvasHeight, L.T("ui158"));
        _canvasTools.Children.Add(_canvasWidth);
        _canvasTools.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
        _canvasTools.Children.Add(_canvasHeight);
        _canvasTools.Children.Add(new TextBlock { Text = "px", VerticalAlignment = VerticalAlignment.Center });
        return _canvasTools;
    }
    private async Task<bool> RemoveAllScreensAsync()
    {
        if (_busy || HasPointerEdit || !HasScreens) return false;
        var before = _document.Project;
        SetBusy(true);
        try { if (!await _dialogs.ConfirmRemoveAllAsync(this, before.Screens.Count)) { SetStatus(L.T("ui159")); return false; } }
        finally { SetBusy(false); }
        if (!ReferenceEquals(before, _document.Project)) return false;
        EndEditGroup();
        var next = new ProjectModel(before.FormatVersion, [], new(before.Canvas.WidthPx, before.Canvas.HeightPx, [], before.Canvas.Snap, before.Canvas.TransparentBackground, before.Canvas.ImageMask), before.Mode, before.InactiveLayout);
        var rendered = Render(next, EmptyEditorScreen);
        _document.Execute(new ReplaceCanvasProjectCommand(next)); _objectsSelected = false; _screenId = Guid.Empty;
        LoadControls(); UsePreview(rendered); SetStatus(L.T("ui160")); return true;
    }
    private string NextScreenName()
    {
        var names = _document.Project.Screens.Select(s => s.Name).ToHashSet(); var n = 1;
        var key = IsProjection ? "screen.projectionName" : "screen.ledName";
        while (names.Contains(L.F(key, n))) n++;
        return L.F(key, n);
    }
    private void LoadCanvasControls()
    {
        for (var i = 0; i < 5; i++) CanvasFields()[i].Text = CanvasValue(i);
        _screenSnap.IsChecked = _document.Project.Canvas.Snap.Enabled;
        _placementLocked.IsChecked = Placement.IsLocked; _placementHidden.IsChecked = Placement.IsHidden;
        UpdateCanvasControls();
    }
    private string? CanvasValue(int index) => (index switch { 0 => _document.Project.Canvas.WidthPx, 1 => _document.Project.Canvas.HeightPx,
        2 => Placement.X, 3 => Placement.Y, 4 => _document.Project.Canvas.Snap.GridSize, _ => 0 }).ToString(CultureInfo.InvariantCulture);
    private void UpdateCanvasControls()
    {
        _canvasSection.IsVisible = false;
        _screens.IsVisible = IsCanvas || _document.Project.Screens.Count > 1;
        _placementX.IsEnabled = _placementY.IsEnabled = !Placement.IsLocked;
        var warnings = _document.Project.LayoutWarnings;
        _canvasWarnings.Text = warnings.Count == 0 ? L.T("ui162")
            : L.F("ui163", warnings.Count(w => w.Kind == LayoutWarningKind.OutsideCanvas), warnings.Count(w => w.Kind == LayoutWarningKind.ScreenOverlap));
        _cabinetMode.IsEnabled = !IsCanvas;
        UpdateCabinetOverlay(); UpdateScreenOverlay(); SyncCanvasLayers();
    }
    private ProjectModel ApplyCanvasDraft(ProjectModel project, List<string> problems, bool[] accepted)
    {
        var canvas = project.Canvas;
        var width = ReadBoundedInt(_canvasWidth, L.T("ui164"), 1, ModelLimits.MaxDimension, canvas.WidthPx, problems, out accepted[0]);
        var height = ReadBoundedInt(_canvasHeight, L.T("ui165"), 1, ModelLimits.MaxDimension, canvas.HeightPx, problems, out accepted[1]);
        try { project = CanvasEditing.Resize(project, width, height); }
        catch (ArgumentException e)
        {
            problems.Add(e.Message);
            try { project = CanvasEditing.Resize(project, width, canvas.HeightPx); } catch (ArgumentException) { accepted[0] = false; }
            try { project = CanvasEditing.Resize(project, project.Canvas.WidthPx, height); } catch (ArgumentException) { accepted[1] = false; }
        }
        var placement = project.Canvas.Placements.SingleOrDefault(p => p.ScreenId == _screenId);
        if (placement is null) return CanvasEditing.SetSnap(project, new(_screenSnap.IsChecked == true, canvas.Snap.GridSize));
        var x = ReadBoundedInt(_placementX, L.T("ui166"), int.MinValue, int.MaxValue, placement.X, problems, out accepted[2]);
        var y = ReadBoundedInt(_placementY, L.T("ui167"), int.MinValue, int.MaxValue, placement.Y, problems, out accepted[3]);
        if (placement.IsLocked && (x != placement.X || y != placement.Y))
        { problems.Add(L.T("ui168")); accepted[2] = accepted[3] = false; x = placement.X; y = placement.Y; }
        project = CanvasEditing.Place(project, _screenId, x, y, _placementLocked.IsChecked == true, _placementHidden.IsChecked == true);
        var grid = ReadBoundedInt(_snapGrid, L.T("ui169"), 1, ModelLimits.MaxDimension, project.Canvas.Snap.GridSize, problems, out accepted[4]);
        return CanvasEditing.SetSnap(project, new(_screenSnap.IsChecked == true, grid));
    }
    private bool ApplyEmptyCanvasDraft(object? mergeKey)
    {
        if (!HasDraft) return true;
        var problems = new List<string>(); var accepted = new bool[5];
        var next = ApplyCanvasDraft(_document.Project, problems, accepted);
        if (!ReferenceEquals(next, _document.Project))
        {
            var rendered = Render(next, EmptyEditorScreen);
            _document.Execute(new ReplaceCanvasProjectCommand(next), mergeKey); UsePreview(rendered);
        }
        for (var i = 0; i < 2; i++) if (accepted[i]) _loadedText[26 + i] = CanvasFields()[i].Text ?? "";
        UpdateActions(); SetStatus(problems.Count == 0 ? L.T("ui072") : problems[0]);
        return problems.Count == 0;
    }
    private static bool SameCanvas(CanvasModel a, CanvasModel b) => a.WidthPx == b.WidthPx && a.HeightPx == b.HeightPx && a.Snap == b.Snap && a.TransparentBackground == b.TransparentBackground && a.ImageMask == b.ImageMask
        && a.Placements.Count == b.Placements.Count && a.Placements.Zip(b.Placements).All(p => p.First.Id == p.Second.Id
            && p.First.ScreenId == p.Second.ScreenId && p.First.X == p.Second.X && p.First.Y == p.Second.Y
            && p.First.IsLocked == p.Second.IsLocked && p.First.IsHidden == p.Second.IsHidden);
    private Task<bool> CanvasAction(Func<ProjectModel, ProjectModel> action, bool selectLast = false)
    {
        if (_busy || HasPointerEdit) return Task.FromResult(false);
        EndEditGroup(); if (!ApplyAutomatic()) return Task.FromResult(false);
        try
        {
            var previousCanvas = _document.Project.Canvas;
            var next = action(_document.Project);
            if (ReferenceEquals(next, _document.Project)) { SetStatus(L.T("ui170")); return Task.FromResult(true); }
            var firstCreated = !_firstScreenCreated && _document.Project.Screens.Count == 0 && next.Screens.Count > 0;
            var id = next.Screens.Count == 0 ? Guid.Empty : selectLast ? next.Screens[^1].Id : next.Screens.Any(s => s.Id == _screenId) ? _screenId : next.Screens[0].Id;
            var rendered = Render(next, next.Screens.FirstOrDefault(s => s.Id == id) ?? EmptyEditorScreen);
            _document.Execute(new ReplaceCanvasProjectCommand(next)); _objectsSelected = next.Screens.Count > 0 && (_objectsSelected || selectLast); _screenId = id; LoadControls(); UsePreview(rendered);
            if (firstCreated) { _firstScreenCreated = true; SetZoom(.75, new(PreviewWidth / 2d, PreviewHeight / 2d), ViewCenter); }
            SetStatus(L.T("ui171"));
            if (next.Canvas.WidthPx != previousCanvas.WidthPx || next.Canvas.HeightPx != previousCanvas.HeightPx) WarnCanvasCrop();
            return Task.FromResult(true);
        }
        catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException)
        { ResourceFailure(error); return Task.FromResult(false); }
        catch (ArgumentException error) { SetStatus(error.Message); ShowNotice(error.Message); return Task.FromResult(false); }
    }
}
