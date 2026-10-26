using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GridMapper.Core;
using GridMapper.Rendering;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<ProjectMode, Guid> _modeSelections = [];
    private bool IsProjection => _document.Project.Mode == ProjectMode.Projection;
    private readonly Button _ledMode = new() { Name="LedMode", Content="LED", Classes={"mode"} };
    private readonly Button _projectionMode = new() { Name="ProjectionMode", Content="Projection", Classes={"mode"} };
    private readonly TextBox _projectionWidthPx = Field("ProjectionWidthPx"), _projectionHeightPx = Field("ProjectionHeightPx"),
        _projectionWidthM = Field("ProjectionWidthM"), _projectionHeightM = Field("ProjectionHeightM"), _projectionGridM = Field("ProjectionGridM");
    private readonly ColorPicker _projectionGridColor = Picker("ProjectionGridColor"), _projectionCircleColor = Picker("ProjectionCircleColor"), _projectionFrameColor = Picker("ProjectionFrameColor");
    private readonly CheckBox _projectionGridEnabled = new(){Name="ProjectionGridEnabled",Content=L.T("projection.grid")},
        _projectionFrameEnabled = new(){Name="ProjectionFrameEnabled",Content=L.T("projection.frame")},
        _projectionIds = new(){Name="ProjectionIds",Content="ID"},
        _projectionChecker = new(){Name="ProjectionChecker",Content=L.T("projection.checker")},
        _projectionCircles = new(){Name="ProjectionCircles",Content=L.T("projection.circles")},
        _projectionInformation = new(){Name="ProjectionInformation",Content=L.T("ui198")};
    private readonly Slider _projectionRows = new(){Name="ProjectionCircleRows",Minimum=1,Maximum=10,Value=1,TickFrequency=1,IsSnapToTickEnabled=true,MinHeight=24,Height=24,Classes={"logo-slider"}};
    private readonly TextBlock _projectionRowsValue = new(){Name="ProjectionCircleRowsValue",Width=28,VerticalAlignment=VerticalAlignment.Center};
    private StackPanel? _projectionPanel;
    private Dictionary<Control,bool> _ledInspectorParts=[];
    private bool _projectionSync;
    private bool _projectionDesignActive;
    private string[] _projectionLoaded=[];
    private ProjectionSettings _projectionLoadedSettings=ProjectionSettings.Default;
    private ProjectionSettings Projection => SelectedScreen.Projection ?? ProjectionSettings.Default;
    private TextBox[] ProjectionFields()=>[_projectionWidthPx,_projectionHeightPx,_projectionWidthM,_projectionGridM];
    private string[] ProjectionText()=>[_name.Text??"",..ProjectionFields().Select(f=>f.Text??"")];
    private bool HasProjectionDraft => HasScreens && _projectionLoaded.Length>0 && (!ProjectionText().SequenceEqual(_projectionLoaded)
        || _projectionGridColor.Color!=ToColor(_projectionLoadedSettings.GridColor) || _projectionCircleColor.Color!=ToColor(_projectionLoadedSettings.CircleColor) || _projectionFrameColor.Color!=ToColor(_projectionLoadedSettings.FrameColor)
        || _projectionGridEnabled.IsChecked!=_projectionLoadedSettings.GridEnabled || _projectionFrameEnabled.IsChecked!=_projectionLoadedSettings.FrameEnabled
        || _projectionIds.IsChecked!=_projectionLoadedSettings.ShowIds || _projectionChecker.IsChecked!=_projectionLoadedSettings.Checkerboard
        || _projectionCircles.IsChecked!=_projectionLoadedSettings.Circles || _projectionInformation.IsChecked!=_projectionLoadedSettings.ShowInformation
        || (int)Math.Round(_projectionRows.Value)!=_projectionLoadedSettings.CircleRows);

    private Control BuildProjectionModeSwitch()
    {
        var row=new StackPanel {Name="PatternModeSwitch",Orientation=Orientation.Horizontal,Spacing=0,Margin=new Thickness(40,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        foreach(var button in new[]{_ledMode,_projectionMode}){button.Height=32;button.FontSize=12;button.Padding=new Thickness(10,5);row.Children.Add(button);}
        _ledMode.CornerRadius=new CornerRadius(3,0,0,3);_projectionMode.CornerRadius=new CornerRadius(0,3,3,0);
        _projectionMode.BorderThickness=new Thickness(0,1,1,1);
        _ledMode.Click+=(_,_)=>SetProjectionMode(ProjectMode.Led);_projectionMode.Click+=(_,_)=>SetProjectionMode(ProjectMode.Projection);
        return row;
    }
    private void BuildProjectionInspector()
    {
        L.Bind(_projectionGridEnabled,ContentControl.ContentProperty,"projection.grid");
        L.Bind(_projectionFrameEnabled,ContentControl.ContentProperty,"projection.frame");
        _ledInspectorParts=_editor.Children.Skip(3).ToDictionary(c=>c,c=>c.IsVisible);
        _projectionPanel=new StackPanel{Name="ProjectionInspector",Spacing=10,IsVisible=false};
        var geometry=new StackPanel{Spacing=8};
        AddProjectionResolutionPair(geometry);
        AddPair(geometry,L.T("projection.widthM"),_projectionWidthM,L.T("projection.heightM"),_projectionHeightM);
        _projectionHeightM.IsReadOnly=true;_projectionHeightM.IsEnabled=false;
        ToolTip.SetTip(_projectionHeightM,L.T("projection.heightCalculated"));
        _projectionPanel.Children.Add(Section("ProjectionGeometrySection",L.T("ui216"),geometry,true));_projectionPanel.Children.Add(Divider());
        var appearance=new StackPanel{Spacing=6};
        appearance.Children.Add(ProjectionLayerRow(_projectionGridEnabled,_projectionGridColor,"projection.gridColor"));
        var scale=new Grid{ColumnDefinitions=new("*,96"),ColumnSpacing=8};
        var gridLabel=Caption(L.T("projection.gridScale"));gridLabel.VerticalAlignment=VerticalAlignment.Center;scale.Children.Add(gridLabel);
        Grid.SetColumn(_projectionGridM,1);scale.Children.Add(_projectionGridM);appearance.Children.Add(scale);
        var checks=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=8};checks.Children.Add(_projectionIds);Grid.SetColumn(_projectionChecker,1);checks.Children.Add(_projectionChecker);appearance.Children.Add(checks);
        appearance.Children.Add(ProjectionDivider());
        appearance.Children.Add(ProjectionLayerRow(_projectionCircles,_projectionCircleColor,"projection.circleColor"));
        var rows=new Grid{ColumnDefinitions=new("Auto,*,24"),ColumnSpacing=8};
        var rowLabel=Caption(L.T("projection.circleRows"));rowLabel.VerticalAlignment=VerticalAlignment.Center;rows.Children.Add(rowLabel);
        Grid.SetColumn(_projectionRows,1);rows.Children.Add(_projectionRows);Grid.SetColumn(_projectionRowsValue,2);rows.Children.Add(_projectionRowsValue);appearance.Children.Add(rows);
        appearance.Children.Add(ProjectionDivider());
        appearance.Children.Add(ProjectionLayerRow(_projectionFrameEnabled,_projectionFrameColor,"projection.frameColor"));
        appearance.Children.Add(ProjectionDivider());appearance.Children.Add(_projectionInformation);
        foreach(var toggle in new[]{_projectionGridEnabled,_projectionIds,_projectionChecker,_projectionCircles,_projectionFrameEnabled,_projectionInformation})toggle.Margin=new Thickness(0);
        _projectionPanel.Children.Add(Section("ProjectionAppearanceSection",L.T("ui217"),appearance,true));
        _editor.Children.Add(_projectionPanel);_objectInputs=[.._objectInputs,_projectionPanel];
        foreach(var field in ProjectionFields())
        {
            field.Tag=(Action<bool>)(normalize=>{ApplyProjectionAutomatic(_editKey,normalize);EndEditGroup();});
            field.TextChanged+=(_,_)=>ProjectionChanged(field);
            field.LostFocus+=(_,_)=>{if(!_projectionSync&&!_syncingControls&&IsProjection&&!_busy){ApplyProjectionAutomatic(_editKey,true);EndEditGroup();}};
        }
        foreach(var toggle in new[]{_projectionGridEnabled,_projectionIds,_projectionChecker,_projectionCircles,_projectionFrameEnabled,_projectionInformation})toggle.IsCheckedChanged+=(_,_)=>ProjectionChanged(toggle);
        _projectionGridColor.ColorChanged+=(_,_)=>ProjectionChanged(_projectionGridColor);_projectionCircleColor.ColorChanged+=(_,_)=>ProjectionChanged(_projectionCircleColor);
        _projectionFrameColor.ColorChanged+=(_,_)=>ProjectionChanged(_projectionFrameColor);
        _projectionRows.ValueChanged+=(_,_)=>ProjectionChanged(_projectionRows);
        _projectionRows.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent,(_,_)=>EndEditGroup(),Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    private static Control ProjectionLayerRow(CheckBox toggle,ColorPicker colour,string colourKey)
    {
        var row=new Grid{ColumnDefinitions=new("132,*"),ColumnSpacing=8};row.Children.Add(toggle);
        colour.HorizontalAlignment=HorizontalAlignment.Left;colour.Width=140;colour.MinHeight=32;
        ToolTip.SetTip(colour,L.T(colourKey));Avalonia.Automation.AutomationProperties.SetName(colour,L.T(colourKey));
        Grid.SetColumn(colour,1);row.Children.Add(colour);return row;
    }
    private static Control ProjectionDivider()=>new Border { Height=1,Background=Brush.Parse("#3C3C44"),Margin=new Thickness(0,8) };
    private void SetProjectionMode(ProjectMode mode)
    {
        if(_busy||HasPointerEdit||_document.Project.Mode==mode)return;
        var applied=ApplyAutomatic();
        // Projection edits already publish their valid state live. A rejected
        // draft must not trap the user in this mode; keep the last applied model.
        var recoverDraft=!applied && IsProjection && HasProjectionDraft;
        if(!applied && !recoverDraft)return;
        EndEditGroup();_maskEnabled=false;
        var next=_document.Project.With(mode:mode);
        var selected = next.Screens.FirstOrDefault(s => s.Id == _modeSelections.GetValueOrDefault(mode)) ?? next.Screens.FirstOrDefault();
        try
        {
            var rendered=Render(next,selected ?? EmptyEditorScreen);
            _modeSelections[_document.Project.Mode] = _screenId;
            _document.Execute(new PreparedEdit(next));
            _screenId = selected?.Id ?? Guid.Empty; _objectsSelected = selected is not null;
            _imageMaskSelected = false; _cabinetId = null;
            LoadControls();UsePreview(rendered);
            _viewFactor=double.NaN;_viewAnchor=null;ApplyZoom();
            if(recoverDraft)SetStatus(L.T("projection.validFallback"));
        }
        catch(Exception e) when(e is RenderLimitException or OutOfMemoryException){ResourceFailure(e);}
    }
    private void ProjectionChanged(Control source)
    {
        if(_projectionSync||_syncingControls||_busy||HasPointerEdit||!IsProjection||!HasScreens)return;
        if(source is TextBox or Slider)
        {if(!ReferenceEquals(_editSource,source)){EndEditGroup();_editSource=source;_editKey=new object();}}
        else EndEditGroup();
        ApplyProjectionAutomatic(_editKey);
    }
    private bool ApplyProjectionAutomatic(object? mergeKey=null,bool normalize=false)
    {
        if(!HasScreens)return true;
        try
        {
            var previous=Projection;
            var p=previous.With(widthPx:NumericExpression.EvaluateInt32(_projectionWidthPx.Text),heightPx:NumericExpression.EvaluateInt32(_projectionHeightPx.Text),
                widthMeters:NumericExpression.Evaluate((_projectionWidthM.Text??"").Replace(',','.')),gridMeters:NumericExpression.Evaluate((_projectionGridM.Text??"").Replace(',','.')),
                gridColor:ToRgba(_projectionGridColor.Color),circleColor:ToRgba(_projectionCircleColor.Color),showIds:_projectionIds.IsChecked==true,
                checkerboard:_projectionChecker.IsChecked==true,circles:_projectionCircles.IsChecked==true,circleRows:(int)Math.Round(_projectionRows.Value),showInformation:_projectionInformation.IsChecked==true,gridEnabled:_projectionGridEnabled.IsChecked==true,frameEnabled:_projectionFrameEnabled.IsChecked==true,frameColor:ToRgba(_projectionFrameColor.Color));
            var name=_name.Text??"";ModelLimits.ValidateName(name);
            if(p!=previous||name!=SelectedScreen.Name)
            {
                var screen=SelectedScreen.WithName(name).WithProjection(p);var next=_document.Project.With(screens:_document.Project.Screens.Select(s=>s.Id==screen.Id?screen:s).ToArray());
                var rendered=Render(next,screen);_document.Execute(new PreparedEdit(next),mergeKey);UsePreview(rendered);UpdateScreenNames();
            }
            _projectionSync=true;
            try{if(normalize)WriteProjectionNumbers(p);_projectionHeightM.Text=p.HeightMeters.ToString("0.########",CultureInfo.InvariantCulture);_projectionLoaded=ProjectionText();_projectionLoadedSettings=p;}
            finally{_projectionSync=false;}
            _projectionRowsValue.Text=p.CircleRows.ToString(CultureInfo.InvariantCulture);UpdateActions();SetStatus(L.T("ui072"));return true;
        }
        catch(Exception e) when(e is not RenderLimitException && e is (ArgumentException or NumericExpressionException or InvalidOperationException or OverflowException))
        {SetStatus(e.Message.StartsWith("projection.",StringComparison.Ordinal)?L.T(e.Message):e.Message);UpdateActions();return false;}
        catch(Exception e) when(e is RenderLimitException or OutOfMemoryException){ResourceFailure(e);return false;}
    }
    private void WriteProjectionNumbers(ProjectionSettings p)
    {
        _projectionWidthPx.Text=p.WidthPx.ToString(CultureInfo.InvariantCulture);_projectionHeightPx.Text=p.HeightPx.ToString(CultureInfo.InvariantCulture);
        _projectionWidthM.Text=p.WidthMeters.ToString(CultureInfo.InvariantCulture);_projectionGridM.Text=p.GridMeters.ToString(CultureInfo.InvariantCulture);
    }
    private void LoadProjectionControls()
    {
        _projectionSync=true;
        try
        {
            var p=Projection;WriteProjectionNumbers(p);_projectionHeightM.Text=p.HeightMeters.ToString("0.########",CultureInfo.InvariantCulture);
            _projectionGridColor.Color=ToColor(p.GridColor);_projectionCircleColor.Color=ToColor(p.CircleColor);_projectionFrameColor.Color=ToColor(p.FrameColor);
            _projectionGridEnabled.IsChecked=p.GridEnabled;_projectionFrameEnabled.IsChecked=p.FrameEnabled;
            _projectionIds.IsChecked=p.ShowIds;_projectionChecker.IsChecked=p.Checkerboard;_projectionCircles.IsChecked=p.Circles;_projectionInformation.IsChecked=p.ShowInformation;
            _projectionRows.Value=p.CircleRows;_projectionRowsValue.Text=p.CircleRows.ToString(CultureInfo.InvariantCulture);_projectionLoaded=ProjectionText();_projectionLoadedSettings=p;
        }
        finally{_projectionSync=false;}
    }
    private void SyncProjectionDesign()
    {
        if(_projectionPanel is null)return;
        if (_projectionDesignActive != IsProjection)
        {
            if (IsProjection)
            {
                foreach (var control in _ledInspectorParts.Keys.ToArray()) _ledInspectorParts[control] = control.IsVisible;
                foreach (var control in _ledInspectorParts.Keys) control.IsVisible = false;
            }
            else foreach (var (control, visible) in _ledInspectorParts) control.IsVisible = visible;
            _projectionDesignActive = IsProjection;
        }
        _projectionPanel.IsVisible=IsProjection;_projectionHeightM.IsEnabled=false;
        _projectionGridM.IsEnabled=_projectionGridColor.IsEnabled=_projectionIds.IsEnabled=_projectionChecker.IsEnabled=Projection.GridEnabled;
        _projectionRows.IsEnabled=_projectionCircleColor.IsEnabled=Projection.Circles;_projectionFrameColor.IsEnabled=Projection.FrameEnabled;
        _ledMode.Classes.Set("active",!IsProjection);_projectionMode.Classes.Set("active",IsProjection);
        _ledMode.IsEnabled=_projectionMode.IsEnabled=!_busy&&!HasPointerEdit;
        _singleModeButton.IsVisible=_canvasModeButton.IsVisible=!IsProjection;
        ExistingAction("ExportCanvas").IsVisible=!IsProjection;
        _maskButton.IsVisible=!IsProjection;
        _workspaceRasters.IsVisible=HasScreens||!IsProjection;
        if(IsProjection)_layerDrop.IsVisible=false;
        var labels=this.GetLogicalDescendants().OfType<Grid>().FirstOrDefault(g=>g.Name=="ScreenColumnLabels");
        if(labels is not null){labels.ColumnDefinitions[3].Width=labels.ColumnDefinitions[4].Width=new GridLength(IsProjection?0:80);foreach(var c in labels.Children.Where(c=>c.Name is "OffsetXHeading" or "OffsetYHeading"))c.IsVisible=!IsProjection;}
        foreach(var row in _layerEditors.Values)
        {
            row.X.IsVisible=row.Y.IsVisible=!IsProjection;
            if(row.X.Parent is Grid grid)grid.ColumnDefinitions[3].Width=grid.ColumnDefinitions[4].Width=new GridLength(IsProjection?0:80);
        }
    }
    private static RenderedPreview RenderProjection(ScreenModel screen)
    {
        var p=screen.Projection??ProjectionSettings.Default;RenderSafety.CheckPreparedBytes((long)p.WidthPx*p.HeightPx*4);
        var scene=SceneBuilder.ForProjection(p);var vector=new RecordedScreen(SceneRenderer.Record(scene));
        Bitmap? bitmap=null;
        try
        {
            var png=vector.RenderPng(p.WidthPx,p.HeightPx);using var stream=new MemoryStream(png);bitmap=new Bitmap(stream);
            var frame=new WorkspacePreview.Frame(bitmap,0,0,screen,null,vector,IsProjection:true);
            return new(png,bitmap,bitmap,p.WidthPx,p.HeightPx,new(0,0,p.WidthPx,p.HeightPx),null,frame);
        }
        catch{bitmap?.Dispose();vector.Dispose();throw;}
    }
}
