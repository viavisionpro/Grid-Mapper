using System.Globalization;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using GridMapper.Core;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly CheckBox _cabinetMode = new() { Name = "CabinetMode", Content = L.T("ui260") };
    private readonly ComboBox _cabinetList = new() { Name = "Cabinets", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox _cabinetType = new() { Name = "CabinetType", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBox _typeName = Field("CabinetTypeName");
    private readonly TextBox _typeWidth = Field("CabinetTypeWidth");
    private readonly TextBox _typeHeight = Field("CabinetTypeHeight");
    private readonly TextBox _typeWidthMm = Field("CabinetTypeWidthMm");
    private readonly TextBox _typeHeightMm = Field("CabinetTypeHeightMm");
    private readonly TextBox _cabinetX = Field("CabinetX");
    private readonly TextBox _cabinetY = Field("CabinetY");
    private readonly TextBlock _cabinetDescription = new() { Name = "CabinetDescription", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _cabinetPanel = new() { Spacing = 8 };
    private Guid? _cabinetId;
    private readonly HashSet<Guid> _cabinetEditorScreens = [];
    private TextBox[] CabinetFields() => [_typeName, _typeWidth, _typeHeight, _cabinetX, _cabinetY, _typeWidthMm, _typeHeightMm];
    private CabinetInstance CurrentCabinet => SelectedScreen.Cabinets.FirstOrDefault(c => c.Id == _cabinetId) ?? SelectedScreen.Cabinets[0];
    private CabinetType CurrentType => SelectedScreen.CabinetTypes.First(t => t.Id == CurrentCabinet.TypeId);

    private void BuildCabinetEditor(StackPanel? target = null)
    {
        target ??= _editor;
        target.Children.Add(_cabinetMode);
        AddField(_cabinetPanel, L.T("ui261"), _cabinetList);
        // Legacy mixed projects remain readable, but new panel types are composed
        // as separate screens on Canvas rather than mixed within one screen.
        _cabinetType.IsVisible = false; _cabinetPanel.Children.Add(_cabinetType);
        var actions = new WrapPanel();
        AddAction(actions, "AddCabinet", L.T("ui263"), () => CabinetAction(() =>
        {
            var screen = SelectedScreen; var type = CurrentType; var position = CabinetEditing.NextPosition(screen, type.Id); var id = Guid.NewGuid();
            var next = CabinetEditing.Add(screen, id, type.Id, position.X, position.Y); _cabinetId = id; return next;
        }));
        AddAction(actions, "DeleteCabinet", L.T("ui264"), () => CabinetAction(() => CabinetEditing.Remove(SelectedScreen, CurrentCabinet.Id)));
        AddAction(actions, "NewCabinetType", L.T("ui265"), () => CabinetAction(() =>
        {
            var type = CurrentType; var copy = new CabinetType(Guid.NewGuid(), L.F("ui266", SelectedScreen.CabinetTypes.Count + 1), type.WidthMm, type.HeightMm, type.WidthPx, type.HeightPx);
            return CabinetEditing.AssignType(CabinetEditing.AddType(SelectedScreen, copy), CurrentCabinet.Id, copy.Id);
        }));
        ExistingAction("NewCabinetType").IsVisible = false;
        _cabinetPanel.Children.Add(actions);
        AddPair(_cabinetPanel, L.T("ui267"), _cabinetX, L.T("ui268"), _cabinetY);
        AddField(_cabinetPanel, L.T("ui269"), _typeName);
        AddPair(_cabinetPanel, L.T("ui270"), _typeWidth, L.T("ui271"), _typeHeight);
        AddPair(_cabinetPanel, L.T("ui272"), _typeWidthMm, L.T("ui273"), _typeHeightMm);
        _cabinetPanel.Children.Add(_cabinetDescription);
        _cabinetPanel.Children.Add(new TextBlock { Text = L.T("ui274"), FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        target.Children.Add(new Expander { Name = "CabinetSection", Header = L.T("ui275"), Content = _cabinetPanel, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch });
        _cabinetMode.IsCheckedChanged += (_, _) =>
        {
            if (_syncingControls || _busy) return;
            EndEditGroup();
            if (_cabinetMode.IsChecked != true && RegularGrid.Detect(SelectedScreen) is null)
            {
                _syncingControls = true; _cabinetMode.IsChecked = true; _syncingControls = false;
                SetStatus(L.T("ui276")); return;
            }
            if (_cabinetMode.IsChecked == true) _cabinetEditorScreens.Add(_screenId); else _cabinetEditorScreens.Remove(_screenId);
            LoadControls(); SetStatus(L.T("ui277"));
        };
        _cabinetList.SelectionChanged += (_, _) =>
        {
            if (_syncingControls || _busy || _cabinetList.SelectedIndex < 0) return;
            var id = SelectedScreen.Cabinets[_cabinetList.SelectedIndex].Id;
            if (_cabinetId == id) return;
            EndEditGroup(); ApplyAutomatic(); _cabinetId = id; SyncCabinetControls();
        };
        _cabinetType.SelectionChanged += (_, _) =>
        {
            if (_syncingControls || _busy || _cabinetType.SelectedIndex < 0) return;
            var id = SelectedScreen.CabinetTypes[_cabinetType.SelectedIndex].Id;
            CabinetAction(() => CabinetEditing.AssignType(SelectedScreen, CurrentCabinet.Id, id)).GetAwaiter().GetResult();
        };
    }
    private void LoadCabinetControls()
    {
        _cabinetMode.IsChecked = _cabinetEditorScreens.Contains(_screenId) || RegularGrid.Detect(SelectedScreen) is null;
        _cabinetPanel.IsEnabled = _cabinetMode.IsChecked == true;
        _cabinetPanel.IsVisible = _cabinetMode.IsChecked == true;
        _gridPanel.IsVisible = RegularGrid.Detect(SelectedScreen) is not null && _cabinetMode.IsChecked != true;
        if (_objectsSelected && (_cabinetId is null || !SelectedScreen.Cabinets.Any(c => c.Id == _cabinetId))) _cabinetId = SelectedScreen.Cabinets[0].Id;
        UpdateCabinetChoices();
        for (var i = 0; i < 7; i++) CabinetFields()[i].Text = CabinetValue(i);
        UpdateCabinetDescription();
    }
    private string? CabinetValue(int index) => index switch
    {
        0 => CurrentType.Name, 1 => CurrentType.WidthPx.ToString(CultureInfo.InvariantCulture),
        2 => CurrentType.HeightPx.ToString(CultureInfo.InvariantCulture), 3 => CurrentCabinet.X.ToString(CultureInfo.InvariantCulture),
        4 => CurrentCabinet.Y.ToString(CultureInfo.InvariantCulture),
        5 => CurrentType.WidthMm.ToString(CultureInfo.InvariantCulture), 6 => CurrentType.HeightMm.ToString(CultureInfo.InvariantCulture), _ => null
    };
    private void SyncCabinetControls()
    {
        _syncingControls = true;
        try
        {
            LoadCabinetControls();
            for (var i = 0; i < 7; i++) _loadedText[19 + i] = CabinetFields()[i].Text ?? "";
        }
        finally { _syncingControls = false; }
        UpdateActions(); UpdateCabinetOverlay();
    }
    private void UpdateCabinetChoices()
    {
        var syncing = _syncingControls; _syncingControls = true;
        try
        {
            var types = SelectedScreen.CabinetTypes.ToDictionary(t => t.Id);
            var names = SelectedScreen.Cabinets.Select((c, i) => $"{i + 1} · {types[c.TypeId].Name} · {c.X}/{c.Y}").ToArray();
            if (_cabinetList.ItemsSource is not string[] oldNames || !oldNames.SequenceEqual(names)) _cabinetList.ItemsSource = names;
            _cabinetList.SelectedIndex = _objectsSelected ? SelectedScreen.Cabinets.ToList().FindIndex(c => c.Id == CurrentCabinet.Id) : -1;
            var typeNames = SelectedScreen.CabinetTypes.Select(t => $"{t.Name} · {t.WidthPx} × {t.HeightPx} px").ToArray();
            if (_cabinetType.ItemsSource is not string[] oldTypes || !oldTypes.SequenceEqual(typeNames)) _cabinetType.ItemsSource = typeNames;
            _cabinetType.SelectedIndex = SelectedScreen.CabinetTypes.ToList().FindIndex(t => t.Id == CurrentCabinet.TypeId);
            _editor.GetLogicalDescendants().OfType<TextBlock>().Single(c => c.Name == "MixedNote").IsVisible = RegularGrid.Detect(SelectedScreen) is null;
        }
        finally { _syncingControls = syncing; }
    }
    private void UpdateCabinetDescription()
    {
        if (_cabinetId is null) return;
        UpdateCabinetChoices(); UpdateCabinetOverlay();
        var type = CurrentType;
        _cabinetDescription.Text = L.F("ui278", type.WidthMm, type.HeightMm, SelectedScreen.Cabinets.Count(c => c.TypeId == type.Id), SelectedScreen.Geometry.Bounds.X, SelectedScreen.Geometry.Bounds.Y);
    }
    private Task<bool> CabinetAction(Func<ScreenModel> action)
    {
        if (_busy || _cabinetMode.IsChecked != true) return Task.FromResult(false);
        EndEditGroup(); ApplyAutomatic();
        var previousId = _cabinetId;
        try
        {
            var next = action(); ExecuteCabinetEdit(next); SyncCabinetControls(); SetStatus(L.T("ui279")); return Task.FromResult(true);
        }
        catch (Exception error) when (error is GridMapper.Rendering.RenderLimitException or OutOfMemoryException)
        { _cabinetId = previousId; ResourceFailure(error); return Task.FromResult(false); }
        catch (ArgumentException error) { _cabinetId = previousId; SyncCabinetControls(); SetStatus(error.Message); return Task.FromResult(false); }
    }
    private void ExecuteCabinetEdit(ScreenModel screen, object? mergeKey = null)
    {
        var next = new ReplaceCabinetScreenCommand(screen).Apply(_document.Project);
        if (ReferenceEquals(next, _document.Project)) return;
        var rendered = Render(next, screen); _document.Execute(new PreparedEdit(next), mergeKey); UsePreview(rendered);
    }
    private ProjectModel ApplyCabinetDraft(ProjectModel project, List<string> problems, bool[] accepted)
    {
        if (_cabinetMode.IsChecked != true) { Array.Fill(accepted, true); return project; }
        var screen = project.Screens.First(s => s.Id == _screenId); var cabinet = screen.Cabinets.First(c => c.Id == _cabinetId);
        var type = screen.CabinetTypes.First(t => t.Id == cabinet.TypeId);
        var name = _typeName.Text ?? "";
        try { ModelLimits.ValidateName(name); accepted[0] = true; }
        catch (ModelValidationException e) { problems.Add(e.Message); name = type.Name; }
        var width = ReadBoundedInt(_typeWidth, L.T("ui280"), 1, ModelLimits.MaxDimension, type.WidthPx, problems, out accepted[1]);
        var height = ReadBoundedInt(_typeHeight, L.T("ui281"), 1, ModelLimits.MaxDimension, type.HeightPx, problems, out accepted[2]);
        var widthMm = ReadBoundedDecimal(_typeWidthMm, L.T("ui282"), ModelLimits.MinMillimeters, ModelLimits.MaxMillimeters, type.WidthMm, problems, out accepted[5]);
        var heightMm = ReadBoundedDecimal(_typeHeightMm, L.T("ui283"), ModelLimits.MinMillimeters, ModelLimits.MaxMillimeters, type.HeightMm, problems, out accepted[6]);
        try { screen = CabinetEditing.EditType(screen, type.Id, name, width, height, widthMm, heightMm); }
        catch (ArgumentException e)
        {
            problems.Add(e.Message);
            screen = CabinetEditing.EditType(screen, type.Id, name, type.WidthPx, type.HeightPx, type.WidthMm, type.HeightMm);
            try { screen = CabinetEditing.EditType(screen, type.Id, name, width, type.HeightPx, widthMm, type.HeightMm); }
            catch (ArgumentException) { accepted[1] = accepted[5] = false; }
            var effectiveType = screen.CabinetTypes.First(t => t.Id == type.Id);
            try { screen = CabinetEditing.EditType(screen, type.Id, name, effectiveType.WidthPx, height, effectiveType.WidthMm, heightMm); }
            catch (ArgumentException) { accepted[2] = accepted[6] = false; }
        }
        var x = ReadBoundedInt(_cabinetX, L.T("ui284"), 0, int.MaxValue, cabinet.X, problems, out accepted[3]);
        var y = ReadBoundedInt(_cabinetY, L.T("ui285"), 0, int.MaxValue, cabinet.Y, problems, out accepted[4]);
        try { screen = CabinetEditing.Move(screen, cabinet.Id, x, y); }
        catch (ArgumentException e)
        {
            problems.Add(e.Message);
            try { screen = CabinetEditing.Move(screen, cabinet.Id, x, cabinet.Y); } catch (ArgumentException) { accepted[3] = false; }
            var effectiveCabinet = screen.Cabinets.First(c => c.Id == cabinet.Id);
            try { screen = CabinetEditing.Move(screen, cabinet.Id, effectiveCabinet.X, y); } catch (ArgumentException) { accepted[4] = false; }
        }
        return new ReplaceCabinetScreenCommand(screen).Apply(project);
    }
    private static bool SameCabinetGeometry(ScreenModel a, ScreenModel b) =>
        a.Cabinets.Count == b.Cabinets.Count && a.CabinetTypes.Count == b.CabinetTypes.Count
        && a.Cabinets.Zip(b.Cabinets).All(p => p.First.Id == p.Second.Id && p.First.TypeId == p.Second.TypeId && p.First.X == p.Second.X && p.First.Y == p.Second.Y && p.First.IsMasked == p.Second.IsMasked)
        && a.CabinetTypes.Zip(b.CabinetTypes).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name && p.First.WidthPx == p.Second.WidthPx && p.First.HeightPx == p.Second.HeightPx && p.First.WidthMm == p.Second.WidthMm && p.First.HeightMm == p.Second.HeightMm);
}
