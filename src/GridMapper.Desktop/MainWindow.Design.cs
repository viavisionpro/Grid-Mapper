using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using GridMapper.Core;
using GridMapper.Rendering;
using Avalonia.Media.Imaging;

namespace GridMapper.Desktop;

public sealed partial class MainWindow
{
    private readonly Button _singleModeButton = new() { Name = "SingleScreenMode", Content = L.T("ui139") };
    private readonly Button _canvasModeButton = new() { Name = "CanvasMode", Content = "Canvas" };
    private readonly TextBlock _canvasSizeLabel = new() { Name = "CanvasSizeLabel", Classes = { "muted" }, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Dictionary<Guid, (TextBox X, TextBox Y, Button Visible, Border Chip, Button Ndi)> _layerEditors = [];
    private readonly CheckBox _showDetails = new() { Name = "InformationDetails", Content = L.T("ui179") };
    private readonly Slider _logoScaleSlider = new() { Name = "LogoScaleSlider", Minimum = 70, Maximum = 130, Value = 100, TickFrequency = 1, MinHeight = 24, Height = 24, Classes = { "logo-slider" } };
    private readonly Slider _logoOpacitySlider = new() { Name = "LogoOpacitySlider", Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 1, MinHeight = 24, Height = 24, Classes = { "logo-slider" } };
    private readonly CheckBox _logoBackground = new() { Name = "LogoBackground", Content = L.T("ui180") };
    private readonly CheckBox _circle = new() { Name = "Circle", Content = L.T("ui181") };
    private readonly CheckBox _cornerCircles = new() { Name = "CornerCircles", Content = L.T("ui182") };
    private readonly CheckBox _ramp = new() { Name = "InformationRamp", Content = L.T("ui183") };
    private readonly CheckBox _raster = new() { Name = "Raster", Content = L.T("ui184") };
    private readonly ColorPicker _infoBackgroundColor = Picker("InformationBackgroundColor");
    private readonly Slider _infoBackgroundOpacity = new() { Name = "InformationBackgroundOpacity", Minimum = 0, Maximum = 100, TickFrequency = 1 };
    private readonly TextBlock _infoOpacityValue = new() { Name = "InformationOpacityValue", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _compactGeometry = new() { Name = "GeometryCompactSummary", FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    internal Flyout? SolidProfileFlyout { get; private set; }
    private bool _syncDesign;
    private readonly List<(Button Button, Color First, Color Second)> _profileButtons = [];
    private Action<double>? _adaptScreenTable;
    private readonly TextBlock _screenCount = new() { Name = "ScreenCount", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    private readonly Image _logoThumbnail = new() { Name = "LogoThumbnail", Width = 76, Height = 56, Stretch = Stretch.Uniform };
    private Bitmap? _logoThumbnailBitmap;
    private LogoImage? _thumbnailSource;
    private readonly StackPanel _welcome = new() { Name = "EmptyPreferences", Spacing = 20, Margin = new Thickness(24, 60) };
    private readonly TextBlock _emptyMessage = new() { Name = "EmptyWorkspaceMessage", Text = L.T("ui185"), FontSize = 22, Foreground = Brush.Parse("#96969F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };

    private static T Take<T>(T control) where T : Control
    {
        switch (control.Parent)
        {
            case Panel panel: panel.Children.Remove(control); break;
            case Decorator decorator: decorator.Child = null; break;
            case ContentControl content when ReferenceEquals(content.Content, control): content.Content = null; break;
        }
        return control;
    }
    private Button ExistingAction(string name) => _fileButtons.Single(b => b.Name == name);
    private static TextBlock Caption(string text) => new() { Text = text, Classes = { "muted" }, Margin = new Thickness(0, 8, 0, 2) };
    private static Expander Section(string name, string title, Control content, bool open = false) => new()
    { Name = name, Header = title, Content = content, IsExpanded = open, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Border Divider() => new() { Height = 1, Background = Brush.Parse("#3A3A41"), Margin = new Thickness(0, 4, 0, 8) };

    private void BuildOpenLines()
    {
        Width = 1440; Height = 960;
        var menu = BuildMenu();
        var toolbar = new StackPanel { Name = "HeaderActions", Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        _undo.Content = ActionIcon("Undo"); _redo.Content = ActionIcon("Redo");
        _undo.Width = _redo.Width = 34; _undo.Padding = _redo.Padding = new Thickness(5);
        DescribeIcon(_undo, L.T("ui070")); DescribeIcon(_redo, L.T("ui071"));
        toolbar.Children.Add(_undo); toolbar.Children.Add(_redo);
        AddAction(toolbar, "New", L.T("ui186"), NewProjectAsync);
        AddAction(toolbar, "Open", L.T("ui117"), OpenProjectAsync);
        AddAction(toolbar, "Save", L.T("ui042"), () => SaveProjectAsync(false));
        AddAction(toolbar, "SaveAs", L.T("ui118"), () => SaveProjectAsync(true));
        AddAction(toolbar, "Settings", L.T("settings.title"), ShowSettingsAsync);
        var settings = ExistingAction("Settings"); settings.Content = ActionIcon("Settings"); settings.Width = 34; settings.Padding = new Thickness(5); DescribeIcon(settings, L.T("settings.title"));
        AddAction(toolbar, "Export", L.T("ui119"), ExportAsync);
        _undo.Click += (_, _) => Undo(); _redo.Click += (_, _) => Redo();
        var numberingPanel = new StackPanel { Spacing = 8 };
        AddField(numberingPanel, L.T("ui187"), _numberFormat);
        AddField(numberingPanel, L.T("ui188"), _numberPrefix);
        AddPair(numberingPanel, L.T("ui073"), _numberStart,
            L.T("ui189"), _numberFontSize);
        AddField(numberingPanel, L.T("ui190"), _numberFont);
        AddField(numberingPanel, L.T("ui191"), _numberCorner);
        AddField(numberingPanel, L.T("ui192"), _numberTraversal);
        numberingPanel.Children.Add(_numberBackground);
        numberingPanel.Children.Add(new TextBlock { Text = L.T("ui193"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var numbering = new Expander { Name = "NumberingSection", Header = L.T("ui194"), Content = numberingPanel, HorizontalAlignment = HorizontalAlignment.Stretch };
        var informationPanel = new StackPanel { Spacing = 12 };
        informationPanel.Children.Add(_information);
        informationPanel.Children.Add(_ramp);
        var details = new StackPanel { Spacing = 8 };
        foreach (var toggle in new[] { _infoName, _infoResolution, _infoPhysical, _infoCount, _infoCoordinates }) details.Children.Add(toggle);
        AddField(details, L.T("ui189"), _infoFontSize); AddField(details, L.T("ui190"), _infoFont);
        _infoCorner.IsVisible = false; details.Children.Add(_infoCorner);
        informationPanel.Children.Add(_infoBackground);
        AddField(informationPanel, L.T("ui195"), _infoBackgroundColor);
        var opacityRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,46"), ColumnSpacing = 10 };
        opacityRow.Children.Add(_infoBackgroundOpacity); Grid.SetColumn(_infoOpacityValue, 1); opacityRow.Children.Add(_infoOpacityValue);
        informationPanel.Children.Add(Caption(L.T("ui196"))); informationPanel.Children.Add(opacityRow);
        informationPanel.Children.Add(Section("InformationDetailsSection", L.T("ui197"), details));
        var information = Section("InformationSection", L.T("ui198"), informationPanel);
        var textPanel = new StackPanel { Spacing = 8 };
        textPanel.Children.Add(_customText);
        AddField(textPanel, L.T("ui199"), _customContent);
        AddPair(textPanel, L.T("ui200"), _customX, L.T("ui201"), _customY);
        AddField(textPanel, L.T("ui189"), _customFont);
        AddField(textPanel, L.T("ui190"), _customFontFamily);
        textPanel.Children.Add(_customBackground);
        textPanel.Children.Add(new TextBlock { Text = L.T("ui202"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var text = new Expander { Name = "CustomTextSection", IsVisible = false, Header = L.T("ui203"), Content = textPanel, HorizontalAlignment = HorizontalAlignment.Stretch };
        var logoPanel = new StackPanel { Spacing = 8 };
        var logoChecks = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        _logo.Content = L.T("ui204"); _logoBackground.Content = L.T("ui205");
        logoChecks.Children.Add(_logo); Grid.SetColumn(_logoBackground, 1); logoChecks.Children.Add(_logoBackground); logoPanel.Children.Add(logoChecks);
        _logoBackground.IsCheckedChanged += (_, _) => DraftChanged(_logoBackground);
        var logoChoice = new Grid { ColumnDefinitions = new ColumnDefinitions("86,*"), ColumnSpacing = 8 };
        var logoFrame = new Grid(); logoFrame.Children.Add(new TransparencyBackground()); logoFrame.Children.Add(_logoThumbnail);
        logoChoice.Children.Add(new Border { Child = logoFrame, Height = 64, CornerRadius = new CornerRadius(3), ClipToBounds = true });
        var logoActions = new StackPanel { Spacing = 6 };
        AddAction(logoActions, "ImportLogo", L.T("ui206"), ImportLogoAsync);
        AddAction(logoActions, "RemoveLogo", L.T("ui207"), RemoveLogoAsync);
        Grid.SetColumn(logoActions, 1); logoChoice.Children.Add(logoActions); logoPanel.Children.Add(logoChoice);
        _logoInfo.FontSize = 11; _logoInfo.Classes.Add("muted"); logoPanel.Children.Add(_logoInfo);
        var logoPlacement = new StackPanel { Spacing = 8 };
        AddField(logoPlacement, L.T("ui208"), _logoAnchor);
        AddPair(logoPlacement, L.T("ui209"), _logoX, L.T("ui210"), _logoY);
        var legacyLogoValues = new StackPanel { IsVisible = false }; legacyLogoValues.Children.Add(_logoScale); legacyLogoValues.Children.Add(_logoOpacity); logoPanel.Children.Add(legacyLogoValues);
        var logo = Section("LogoSection", L.T("ui204"), logoPanel, true);
        logo.Classes.Add("inspector-subsection");
        var mixedNote = new TextBlock { Name = "MixedNote", Text = L.T("ui211"), TextWrapping = TextWrapping.Wrap };
        AddGeometryPair(L.T("ui212"), _gridFields[0], L.T("ui213"), _gridFields[1], true);
        AddGeometryPair(L.T("ui214"), _gridFields[2], L.T("ui215"), _gridFields[3], false);
        AddGeometryPair(L.T("ui088"), _gridFields[4], L.T("ui089"), _gridFields[5], null);
        var geometry = new StackPanel { Spacing = 12 };
        geometry.Children.Add(Take(_gridPanel));
        geometry.Children.Add(Take(_summary)); _summary.FontSize = 12; _summary.Foreground = Brush.Parse("#BEBEC7");
        // Preserve file compatibility without restoring the retired cabinet inspector.
        var legacyCabinets = new StackPanel { Name = "LegacyCabinetControls", IsVisible = false };
        BuildCabinetEditor(legacyCabinets); legacyCabinets.Children.Add(mixedNote); geometry.Children.Add(legacyCabinets);
        _cabinetMode.IsVisible = false;
        var geometrySection = Section("GeometrySection", L.T("ui216"), geometry, true);
        var geometryGroup = new StackPanel { Spacing = 2 }; geometryGroup.Children.Add(geometrySection); geometryGroup.Children.Add(_compactGeometry);
        _compactGeometry.IsVisible = false;
        geometrySection.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _compactGeometry.IsVisible = !geometrySection.IsExpanded; };
        var appearance = new StackPanel { Spacing = 12 };
        appearance.Children.Add(BuildPaletteStrip());
        _numbering.Content = "ID"; _raster.Content = L.T("ui184");
        var checks = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        var toggles = new[] { _numbering, _raster, _diagonals, _circle, _cornerCircles };
        for (var i = 0; i < toggles.Length; i++) { Take(toggles[i]); Grid.SetRow(toggles[i], i / 2); Grid.SetColumn(toggles[i], i % 2); checks.Children.Add(toggles[i]); }
        appearance.Children.Add(checks);
        // Retain legacy values for file compatibility, without presenting retired controls.
        var legacyAppearance = new StackPanel { IsVisible = false };
        foreach (var control in new Control[] { _pattern, _primary, _secondary, _border, _corners, _centerPlus, _crosspoint, _label, numbering }) legacyAppearance.Children.Add(control);
        appearance.Children.Add(legacyAppearance);
        var appearanceSection = Section("AppearanceSection", L.T("ui217"), appearance, true);
        var infoContent = (StackPanel)information.Content!;
        var legacyInformation = new StackPanel { IsVisible = false };
        foreach (var control in infoContent.Children.ToArray()) legacyInformation.Children.Add(Take(control));
        _infoName.Content = L.T("ui218"); _showDetails.Content = L.T("ui219"); _ramp.Content = L.T("grayscale");
        var infoChecks = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8 };
        var infoFlags = new[] { _infoName, _showDetails, _ramp };
        for (var i = 0; i < infoFlags.Length; i++) { Take(infoFlags[i]); Grid.SetColumn(infoFlags[i], i % 2); Grid.SetRow(infoFlags[i], i / 2); infoChecks.Children.Add(infoFlags[i]); }
        infoContent.Spacing = 6; infoContent.Children.Add(infoChecks);
        infoContent.Children.Add(Divider()); infoContent.Children.Add(logo);
        infoContent.Children.Add(legacyInformation);
        Grid LogoSliderRow(string caption, Slider slider, TextBlock value)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("98,*,38"), ColumnSpacing = 8, MinHeight = 28 };
            var label = Caption(caption); label.FontSize = 11; label.VerticalAlignment = VerticalAlignment.Center;
            value.FontSize = 12; value.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(label); Grid.SetColumn(slider, 1); row.Children.Add(slider); Grid.SetColumn(value, 2); row.Children.Add(value);
            return row;
        }
        var sliderValue = new TextBlock { Name = "LogoScaleValue", VerticalAlignment = VerticalAlignment.Center };
        var logoOpacityValue = new TextBlock { Name = "LogoOpacityValue", VerticalAlignment = VerticalAlignment.Center };
        logoPanel.Children.Add(LogoSliderRow(L.T("ui220"), _logoScaleSlider, sliderValue));
        logoPanel.Children.Add(LogoSliderRow(L.T("ui081"), _logoOpacitySlider, logoOpacityValue));
        _logoOpacitySlider.ValueChanged += (_, _) =>
        {
            var opacity = Math.Round(_logoOpacitySlider.Value);
            logoOpacityValue.Text = opacity.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
            if (!_syncDesign && !_syncingControls && !_busy) _logoOpacity.Text = opacity.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        };
        ((StackPanel)logo.Content!).Children.Add(Section("LogoPlacementSection", L.T("ui221"), logoPlacement));
        _logoScaleSlider.ValueChanged += (_, _) =>
        {
            sliderValue.Text = _logoScaleSlider.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
            if (!_syncDesign && !_syncingControls && !_busy) _logoScale.Text = _logoScaleSlider.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        };
        _infoBackgroundColor.ColorChanged += (_, _) => DraftChanged(_infoBackgroundColor);
        _infoBackgroundOpacity.ValueChanged += (_, _) =>
        {
            if (_syncDesign || _syncingControls || _busy) return;
            var current = _infoBackgroundColor.Color;
            _infoBackgroundColor.Color = Color.FromArgb((byte)Math.Round(_infoBackgroundOpacity.Value * 255 / 100), current.R, current.G, current.B);
        };
        _showDetails.IsCheckedChanged += (_, _) =>
        {
            if (_syncDesign || _syncingControls || _busy) return;
            _syncingControls = true;
            try
            {
                foreach (var flag in new[] { _infoResolution, _infoPhysical, _infoCount }) flag.IsChecked = _showDetails.IsChecked;
                if (_showDetails.IsChecked != true) _infoCoordinates.IsChecked = false;
            }
            finally { _syncingControls = false; }
            DraftChanged(_showDetails);
        };
        logo.Content = null;
        logo.Content = new Border { BorderBrush = Brush.Parse("#3A3A41"), BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(12, 0, 0, 0), Child = logoPanel };
        _editor.Classes.Add("compact-inspector"); _editor.Margin = new Thickness(24, 16, 24, 20); _editor.Spacing = 10;
        _editor.Children.Add(new TextBlock { Text = L.T("ui222"), FontSize = 19, FontWeight = FontWeight.SemiBold });
        AddField(_editor, L.T("ui062"), Take(_name));
        _editor.Children.Add(geometryGroup); _editor.Children.Add(Divider());
        _editor.Children.Add(appearanceSection); _editor.Children.Add(Divider());
        information.Header = L.T("ui198"); _editor.Children.Add(information);
        _editor.Children.Add(text); BuildCanvasEditor();
        _objectInputs = [_name, geometryGroup, appearanceSection, information, text];

        BuildProjectionInspector();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(20, 8, 16, 8) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new TextBlock { Text = "GRID MAPPER", FontSize = 17, FontWeight = FontWeight.SemiBold });
        header.Children.Add(brand);
        var modeSwitch=BuildProjectionModeSwitch();Grid.SetColumn(modeSwitch,1);header.Children.Add(modeSwitch);
        Take(toolbar); toolbar.Margin = default;
        foreach (var button in toolbar.Children.OfType<Button>()) button.Margin = default;
        ExistingAction("New").Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(toolbar, 2); header.Children.Add(toolbar);
        Take(ExistingAction("Export"));
        var workspaceTools = BuildWorkspaceTools();
        BuildMask(); BuildCabinetPointer(); BuildCanvasPointer(); BuildNavigation(); BuildCanvasLayers(); BuildCanvasContextMenu();
        var previewLayer = new Grid { ClipToBounds = true }; previewLayer.Children.Add(_previewViewport); previewLayer.Children.Add(_screenOverlay); _previewWorkspace.Child = previewLayer;
        previewLayer.Children.Add(_maskOverlay); previewLayer.Children.Add(_imageMaskOverlay);
        var workspace = new Grid { Name = "WorkspaceClip", ClipToBounds = true };
        workspace.Children.Add(new TransparencyBackground { IsHitTestVisible = false });
        workspace.Children.Add(Take(_previewWorkspace)); workspace.Children.Add(_emptyMessage);
        workspace.Children.Add(_canvasSizeLabel);
        workspace.Children.Add(workspaceTools); workspace.Children.Add(_maskStripe);
        _previewWorkspace.Background = Brushes.Transparent; _previewWorkspace.Padding = default;
        var preview = new Grid(); preview.Children.Add(workspace);
        var right = new Grid { Name = "WorkspaceAndScreens", RowDefinitions = new RowDefinitions("*,10,300") }; right.Children.Add(preview);
        right.RowDefinitions[0].MinHeight = 180;
        right.RowDefinitions[2].MinHeight = 200;
        var splitter = new GridSplitter { Name = "ScreenPanelResize", ResizeDirection = GridResizeDirection.Rows,
            HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
        AutomationProperties.SetName(splitter, L.T("screens.resize"));
        ToolTip.SetTip(splitter, L.T("screens.resize"));
        Grid.SetRow(splitter, 1); right.Children.Add(splitter);
        var tableLine = new Border { Name = "ScreenTableDivider", Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = Brush.Parse("#3A3A41"), IsHitTestVisible = false };
        Grid.SetRow(tableLine, 1); right.Children.Add(tableLine);
        var grip = new Grid { Name = "ScreenPanelGrip", ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 3, RowSpacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        for (var i = 0; i < 6; i++)
        {
            var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 2, Height = 2, Fill = Brush.Parse("#92929D") };
            Grid.SetRow(dot, i / 3); Grid.SetColumn(dot, i % 3); grip.Children.Add(dot);
        }
        Grid.SetRow(grip, 1); right.Children.Add(grip);
        var table = BuildScreenTable(); Grid.SetRow(table, 2); right.Children.Add(table);
        right.SizeChanged += (_, _) =>
        {
            // Keep a user-resized height until the window itself makes it impossible.
            var maximum = Math.Max(200, right.Bounds.Height - 190);
            if (right.RowDefinitions[2].Height.Value > maximum)
                right.RowDefinitions[2].Height = new GridLength(maximum);
        };
        _welcome.Children.Add(new TextBlock { Text = L.T("ui095"), FontSize = 19 });
        _welcome.Children.Add(new TextBlock { Text = L.T("ui225"), TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
        var welcomeAdd = new Button { Name = "WelcomeAddScreen", Content = L.T("ui226") };
        welcomeAdd.Click += (_, _) => ExistingAction("AddScreen").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); _welcome.Children.Add(welcomeAdd);
        var preferenceContent = new Grid(); preferenceContent.Children.Add(_editor); preferenceContent.Children.Add(_welcome);
        var sidebar = new ScrollViewer { Name = "PreferencesScroll", Content = preferenceContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var sidebarEdge = new Border { Name = "PreferencesBoundary", BorderBrush = Brush.Parse("#3A3A41"), BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("330,*") }; body.Children.Add(sidebarEdge); Grid.SetColumn(right, 1); body.Children.Add(right);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        var headerStrip = new Border { Name = "HeaderStrip", Background = Brush.Parse("#18181C"), BorderBrush = Brush.Parse("#3A3A41"), BorderThickness = new Thickness(0, 0, 0, 1), Child = header };
        Take(menu); root.Children.Add(menu); Grid.SetRow(headerStrip, 1); root.Children.Add(headerStrip); Grid.SetRow(body, 2); root.Children.Add(body);
        Take(_status); _status.Margin = new Thickness(20, 5); _status.FontSize = 11; _status.Foreground = Brush.Parse("#BEBEC7");
        var statusContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        statusContent.Children.Add(_status);
        var version = new TextBlock { Name = "StatusVersion", Text = "v." + DisplayVersion,
            FontSize = 11, Foreground = Brush.Parse("#BEBEC7"), Margin = new Thickness(0, 5, 16, 5),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(version, 1); statusContent.Children.Add(version);
        var statusStrip = new Border { Name = "StatusStrip", Background = Brush.Parse("#18181C"), BorderBrush = Brush.Parse("#35353C"), BorderThickness = new Thickness(0, 1, 0, 0), MinHeight = 26, Child = statusContent };
        Grid.SetRow(statusStrip, 3); root.Children.Add(statusStrip);
        var preserved = new StackPanel { IsVisible = false }; preserved.Children.Add(Take(_canvasSection)); preserved.Children.Add(Take(_screens)); preserved.Children.Add(Take(_viewMode)); preserved.Children.Add(Take(ExistingAction("Export"))); root.Children.Add(preserved);
        Content = root;
        SizeChanged += (_, _) =>
        {
            body.ColumnDefinitions[0].Width = new GridLength(Bounds.Width < 1000 ? 280 : 330);
            ExistingAction("SaveAs").IsVisible = Bounds.Width >= 1000;
            _adaptScreenTable?.Invoke(Bounds.Width);
        };
        _canvasLayers.MaxHeight = double.PositiveInfinity;
        _canvasLayers.ItemTemplate = new FuncDataTemplate<CanvasLayerRow>((row, _) => BuildLayerEditor(row));
        _viewMode.SelectionChanged += (_, _) => SyncDesign();
    }

    private static readonly (string Name, string First, string Second)[] ColourProfiles =
        [("Amber", "#754A2C", "#AE723C"), ("Blue", "#0F3854", "#086AA5"), ("Violet", "#412168", "#7435AF"),
         ("Green", "#244D39", "#47986D"), ("Red", "#642C35", "#B94D60"), ("Teal", "#235456", "#409C9E"),
         ("Gold", "#665B28", "#BBA64D"), ("Pink", "#603B5D", "#AB6AA5"), ("Slate", "#3B4859", "#71879F"),
         ("Coral", "#703E39", "#C97164"), ("Indigo", "#303D6D", "#667ED1"), ("Lime", "#41502B", "#88A650"),
         ("Cyan", "#1F5864", "#45A5BA"), ("Silver", "#44444A", "#919198")];

    private Button ProfileButton(string name, string first, string second, bool expanded = false)
    {
        var button = new Button { Name = (expanded ? "ExpandedPalette" : "Palette") + name, Padding = new Thickness(4), Width = expanded ? 72 : 58, Height = expanded ? 72 : 58 };
        ToolTip.SetTip(button, L.T("profile." + name));
        var size = expanded ? 56 : 42;
        var swatch = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*,*"), Width = size, Height = size };
        for (var i = 0; i < 4; i++) { var tile = new Border { Background = Brush.Parse(i is 0 or 3 ? first : second) }; Grid.SetRow(tile, i / 2); Grid.SetColumn(tile, i % 2); swatch.Children.Add(tile); }
        button.Content = swatch;
        _profileButtons.Add((button, Color.Parse(first), Color.Parse(second)));
        button.Click += (_, _) =>
        {
            if (_busy || !_objectsSelected) return;
            EndEditGroup(); _syncingControls = true;
            try { _primary.Color = Color.Parse(first); _secondary.Color = Color.Parse(second); _pattern.SelectedIndex = 0; }
            finally { _syncingControls = false; }
            ApplyAutomatic();
        };
        return button;
    }
    private Control BuildPaletteStrip()
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        var profiles = new StackPanel { Spacing = 12 };
        profiles.Children.Add(new TextBlock { Text = L.T("ui227"), FontSize = 17 });
        var choices = new WrapPanel { Orientation = Orientation.Horizontal, Width = 320 };
        var flyout = new Flyout { Content = profiles, Placement = PlacementMode.Right };
        foreach (var (name, first, second) in ColourProfiles)
        {
            if (Array.FindIndex(ColourProfiles, p => p.Name == name) < 3) strip.Children.Add(ProfileButton(name, first, second));
            var choice = ProfileButton(name, first, second, expanded: true);
            choice.Click += (_, _) => flyout.Hide();
            choice.Margin = new Thickness(4); choices.Children.Add(choice);
        }
        var solidChip = new Border { Width = 56, Height = 56, Background = new SolidColorBrush(_primary.Color) };
        var solid = new Button { Name = "PaletteSolid", Content = solidChip, Width = 72, Height = 72, Padding = new Thickness(4), Margin = new Thickness(4) };
        DescribeIcon(solid, L.T("ui228")); choices.Children.Add(solid);
        flyout.Opened += (_, _) => solidChip.Background = new SolidColorBrush(_primary.Color);
        profiles.Children.Add(new ScrollViewer { Content = choices, MaxHeight = 400, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var solidPicker = new CompactColourEditor();
        var solidFlyout = new Flyout { Content = solidPicker, Placement = PlacementMode.Right }; SolidProfileFlyout = solidFlyout;
        Guid editingScreen = Guid.Empty;
        solidPicker.Cancelled += (_, _) => solidFlyout.Hide();
        solidPicker.Applied += (_, _) =>
        {
            if (_busy || HasPointerEdit || !_objectsSelected || _screenId != editingScreen) { solidFlyout.Hide(); return; }
            EndEditGroup(); _syncingControls = true;
            try { _primary.Color = solidPicker.Color; _pattern.SelectedIndex = 1; }
            finally { _syncingControls = false; }
            ApplyAutomatic(); solidChip.Background = new SolidColorBrush(_primary.Color); solidFlyout.Hide();
        };
        solid.Click += (_, _) =>
        {
            if (_busy || HasPointerEdit || !_objectsSelected) return;
            L.Apply(solidPicker);
            editingScreen = _screenId; solidPicker.Color = _primary.Color;
            flyout.Hide(); solidFlyout.ShowAt(ExistingPaletteAnchor());
        };
        L.Track(solidPicker);
        Control ExistingPaletteAnchor() => this.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "MoreColourProfiles");
        var more = new Button { Name = "MoreColourProfiles", Content = ActionIcon("Profiles"), Width = 38, Padding = new Thickness(8), Height = 58, Flyout = flyout };
        ToolTip.SetTip(more, L.T("ui227")); strip.Children.Add(more);
        return strip;
    }
    private void UpdateLogoThumbnail()
    {
        _logoImages.TryGetValue(SelectedScreen.Style.Logo.SourcePath, out var image);
        if (ReferenceEquals(_thumbnailSource, image)) return;
        _logoThumbnail.Source = null; _logoThumbnailBitmap?.Dispose(); _logoThumbnailBitmap = null; _thumbnailSource = image;
        if (image is null) return;
        using var stream = image.OpenThumbnail();
        _logoThumbnailBitmap = new Bitmap(stream); _logoThumbnail.Source = _logoThumbnailBitmap;
    }

    private Control BuildScreenTable()
    {
        var panel = new Grid { Name = "ScreenTablePanel", ClipToBounds = true, ColumnDefinitions = new ColumnDefinitions("*"), RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = default };
        var heading = new Grid { Name = "ScreenTableHeading", ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 4 };
        var addGroup = new StackPanel { Name = "ScreenAddTools", Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var add = Take(ExistingAction("AddScreen")); add.Content = L.T("ui230"); add.Margin = default; addGroup.Children.Add(add); heading.Children.Add(addGroup);
        var modes = new StackPanel { Name = "ScreenModeTools", Orientation = Orientation.Horizontal, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Left };
        _singleModeButton.Classes.Add("mode"); _canvasModeButton.Classes.Add("mode"); modes.Children.Add(_singleModeButton); modes.Children.Add(_canvasModeButton);
        _singleModeButton.CornerRadius = new CornerRadius(3, 0, 0, 3);
        _canvasModeButton.CornerRadius = new CornerRadius(0, 3, 3, 0);
        _canvasModeButton.BorderThickness = new Thickness(0, 1, 1, 1);
        _singleModeButton.Click += (_, _) => _viewMode.SelectedIndex = 0; _canvasModeButton.Click += (_, _) => _viewMode.SelectedIndex = 1;
        _canvasTools.Margin = new Thickness(6, 0, 0, 0); modes.Children.Add(BuildCanvasTools());
        var modeArea = new Grid { Name = "ScreenModeArea", VerticalAlignment = VerticalAlignment.Center }; modeArea.Children.Add(modes);
        Grid.SetColumn(modeArea, 1); heading.Children.Add(modeArea);
        var viewTools = new StackPanel { Name = "ZoomTools", Orientation = Orientation.Horizontal, Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var minus = new Button { Name = "ZoomOut", Content = "−", Width = 34 };
        var plus = new Button { Name = "ZoomIn", Content = "+", Width = 34 };
        minus.Click += (_, _) => ZoomStep(1 / 1.15); plus.Click += (_, _) => ZoomStep(1.15);
        viewTools.Children.Add(minus); viewTools.Children.Add(Take(_zoom)); viewTools.Children.Add(plus);
        var fitH = new Button { Name = "FitWidth", Content = "Fit H" };
        var fitV = new Button { Name = "FitHeight", Content = "Fit V" };
        ToolTip.SetTip(fitH, L.T("ui223")); ToolTip.SetTip(fitV, L.T("ui224"));
        fitH.Click += (_, _) => FitAxis(true); fitV.Click += (_, _) => FitAxis(false);
        viewTools.Children.Add(fitH); viewTools.Children.Add(fitV);
        foreach (var button in heading.GetLogicalDescendants().OfType<Button>().Concat(viewTools.Children.OfType<Button>()))
        { button.FontSize = 12; button.Padding = new Thickness(8, 5); button.Height = 32; }
        _zoom.Width = _zoom.MinWidth = _zoom.MaxWidth = 74; _zoom.FontSize = 12; _zoom.Padding = new Thickness(6, 4);
        ToolTip.SetTip(_singleModeButton, L.T("ui139"));
        Grid.SetColumn(viewTools, 3); heading.Children.Add(viewTools);
        var toolbarBackground = new Border { Name = "ScreenToolbarBackground", Background = Brush.Parse("#29292F"),
            BorderBrush = Brush.Parse("#3A3A41"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 7), Child = heading };
        panel.Children.Add(toolbarBackground);
        var table = new Grid { Name = "ScreenTable", ClipToBounds = true, RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16, 0), MinWidth = 400 };
        table.RowDefinitions[1].MinHeight = 64;
        var labels = new Grid { Name = "ScreenColumnLabels", ColumnDefinitions = new ColumnDefinitions("24,30,30,80,80,*,36,28"), ColumnSpacing = 3, Margin = new Thickness(10, 4, 10, 2) };
        foreach (var (label, column) in new[] { ("Offset X", 3), ("Offset Y", 4) })
        { var c = Caption(label); c.Name = column == 3 ? "OffsetXHeading" : "OffsetYHeading"; c.Margin = new Thickness(2, 0); Grid.SetColumn(c, column); labels.Children.Add(c); }
        Grid.SetColumnSpan(_screenCount, 3); labels.Children.Add(_screenCount);
        table.Children.Add(labels);
        Take(_canvasLayers); _canvasLayers.ClipToBounds = true; ScrollViewer.SetHorizontalScrollBarVisibility(_canvasLayers, ScrollBarVisibility.Disabled); Grid.SetRow(_canvasLayers, 1); table.Children.Add(_canvasLayers);
        Take(_layerDrop); _layerDrop.FontSize = 10; _layerDrop.Foreground = Brush.Parse("#96969F");
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 }; footer.Children.Add(_layerDrop);
        _layerDrop.TextWrapping = TextWrapping.NoWrap; _layerDrop.TextTrimming = TextTrimming.CharacterEllipsis;
        var ndiNotice = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var ndiLink = new Button { Name = "NdiWebsite", Content = "NDI®", FontSize = 10, MinHeight = 16, Padding = new Thickness(0), Classes = { "icon" } };
        ToolTip.SetTip(ndiLink, L.T("ui232"));
        ndiLink.Click += async (_, _) => { if (Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri("https://ndi.video/")); };
        ndiNotice.Children.Add(ndiLink); ndiNotice.Children.Add(new TextBlock { Text = "is a registered trademark of Vizrt NDI AB", FontSize = 9, Foreground = Brush.Parse("#96969F"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(ndiNotice, 1); footer.Children.Add(ndiNotice); Grid.SetRow(footer, 2); table.Children.Add(footer);
        var layerArea = new Grid { ClipToBounds = true }; layerArea.Children.Add(table); layerArea.Children.Add(_layerOverlay);
        Grid.SetRow(layerArea, 1); panel.Children.Add(layerArea);
        var actions = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
        AddAction(actions, "ExportSelectedScreen", L.T("ui233"), () => ExportForViewAsync(false));
        AddAction(actions, "ExportCanvas", L.T("ui234"), () => ExportForViewAsync(true));
        AddAction(actions, "ExportResolume", L.T("resolume.export"), ExportResolumeAsync);
        var retired = new StackPanel { IsVisible = false };
        foreach (var name in new[] { "DuplicateScreen", "CopyScreen", "ScreenDown", "ScreenUp", "DeleteScreen" }) retired.Children.Add(Take(ExistingAction(name)));
        actions.Children.Add(retired);
        AddAction(retired, "RemoveAllScreens", L.T("ui235"), RemoveAllScreensAsync);
        var removeAll = Take(ExistingAction("RemoveAllScreens")); removeAll.Classes.Add("danger"); removeAll.HorizontalAlignment = HorizontalAlignment.Stretch;
        foreach (var action in actions.Children.OfType<Button>()) { action.HorizontalAlignment = HorizontalAlignment.Stretch; action.Width = 136; }
        var actionDock = new Grid { Name = "ExportActionsDock", ClipToBounds = true, ColumnDefinitions = new ColumnDefinitions("*,12,Auto"), Margin = new Thickness(16, 8, 16, 8) };
        var actionScroll = new ScrollViewer { Name = "ExportActionsScroll", ClipToBounds = true, Content = actions, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        removeAll.Width = 136; removeAll.VerticalAlignment = VerticalAlignment.Center;
        actionDock.Children.Add(actionScroll); Grid.SetColumn(removeAll, 2); actionDock.Children.Add(removeAll);
        // A separate opaque row also prevents native scroll surfaces from painting over actions.
        var actionBackground = new Border { Name = "ExportActionsBackground", Background = Brush.Parse("#202023"), Child = actionDock, ClipToBounds = true, ZIndex = 5 };
        Grid.SetRow(actionBackground, 2); panel.Children.Add(actionBackground);
        _adaptScreenTable = width =>
        {
            var narrow = width < 1280;
            _layerDrop.IsVisible = !narrow;
            _singleModeButton.Content = width < 1180 ? L.T("view.singleCompact") : L.T("ui139");
            ExistingAction("ExportSelectedScreen").Content = narrow ? L.T("ui236") : L.T("ui233");
            ExistingAction("ExportCanvas").Content = narrow ? "Canvas PNG…" : L.T("ui234");
        };
        return panel;
    }

    private Task<bool> ExportForViewAsync(bool canvas) => ExportSceneAsync(canvas);
    private bool MoveLayerFocus(TextBox field, bool backwards)
    {
        var rows = _document.Project.Canvas.Placements.Reverse().SelectMany(p => _layerEditors.TryGetValue(p.ScreenId, out var row) ? new[] { row.X, row.Y } : []).Where(f => f.IsEffectivelyEnabled).ToArray();
        var index = Array.IndexOf(rows, field);
        if (index < 0) return false;
        var target = index + (backwards ? -1 : 1);
        if (target < 0 || target >= rows.Length) return false;
        var targetId = _layerEditors.First(p => ReferenceEquals(p.Value.X, rows[target]) || ReferenceEquals(p.Value.Y, rows[target])).Key;
        _canvasLayers.ScrollIntoView(_document.Project.Canvas.Placements.Reverse().ToList().FindIndex(p => p.ScreenId == targetId));
        rows[target].Focus(NavigationMethod.Tab); return true;
    }
    private ScreenStyle NextColourStyle(ProjectModel project)
    {
        var profile = ColourProfiles.FirstOrDefault(p => !project.Screens.Any(s => s.Style.Pattern == FillPattern.Checkerboard && s.Style.PrimaryColor == ToRgba(Color.Parse(p.First)) && s.Style.SecondaryColor == ToRgba(Color.Parse(p.Second))));
        if (profile.Name is null) profile = ColourProfiles[project.Screens.Count % ColourProfiles.Length];
        return new(FillPattern.Checkerboard, ToRgba(Color.Parse(profile.First)), ToRgba(Color.Parse(profile.Second)), true,
            decorations: new(Diagonals: true, Raster: true, Circle: true, CornerCircles: true),
            numbering: new(enabled: true, format: NumberingFormat.A1, background: false), information: new(showName: false, showResolution: false,
                showPhysicalSize: false, showCabinetCount: false, rampOverride: false, independentParts: true));
    }
    private void ZoomStep(double factor)
    {
        if (_busy || HasPointerEdit || _pngWidth == 0) return;
        var center = ViewCenter; var scene = SceneAtView(center);
        var current = double.IsNaN(_viewFactor) ? PreviewRasterRect().Width / PreviewWidth : _viewFactor;
        SetZoom(current * factor, scene, center);
    }
    private void FitAxis(bool width)
    {
        if (_busy || HasPointerEdit || _pngWidth == 0) return;
        var factor = width ? (_previewViewport.Viewport.Width - 32) / PreviewWidth : (_previewViewport.Viewport.Height - 32) / PreviewHeight;
        SetZoom(factor, new Point(PreviewWidth / 2d, PreviewHeight / 2d), ViewCenter);
    }
    private void SyncDesign()
    {
        _syncDesign = true;
        try
        {
            foreach (var profile in _profileButtons)
                profile.Button.Classes.Set("active-profile", _pattern.SelectedIndex == 0 && _primary.Color == profile.First && _secondary.Color == profile.Second);
            _infoBackgroundOpacity.Value = _infoBackgroundColor.Color.A * 100d / 255;
            _infoOpacityValue.Text = _infoBackgroundOpacity.Value.ToString("0") + "%";
            _screenCount.Text = L.T("ui229") + " · " + _document.Project.Screens.Count;
            _adaptScreenTable?.Invoke(Bounds.Width > 0 ? Bounds.Width : Width);
            _welcome.IsEnabled = !_busy && !HasPointerEdit;
            _singleModeButton.IsEnabled = _canvasModeButton.IsEnabled = !_busy && !HasPointerEdit;
            _welcome.IsVisible = _emptyMessage.IsVisible = !HasScreens; _editor.IsVisible = HasScreens;
            foreach (var name in new[] { "Export", "ExportSelectedScreen", "ExportCanvas", "RemoveAllScreens" }) ExistingAction(name).IsEnabled = HasScreens && !_busy && !HasPointerEdit;
            this.GetLogicalDescendants().OfType<Expander>().Single(c => c.Name == "CabinetSection").IsVisible = false;
            _singleModeButton.Classes.Set("active", !IsCanvas); _canvasModeButton.Classes.Set("active", IsCanvas);
            _canvasTools.IsVisible = IsCanvas;
            _canvasTools.IsEnabled = !_busy && !HasPointerEdit;
            _edgeSnapButton.IsVisible = IsCanvas; _edgeSnapButton.IsEnabled = !_busy && !HasPointerEdit;
            _edgeSnapButton.Classes.Set("active", _document.Project.Canvas.Snap.Enabled);
            _edgeSnapButton.Background = Brush.Parse(_document.Project.Canvas.Snap.Enabled ? "#646080" : "#34343B");
            _edgeSnapButton.BorderBrush = Brush.Parse(_document.Project.Canvas.Snap.Enabled ? "#9A90B4" : "#48484F");
            _canvasAlphaButton.IsVisible = IsCanvas; _canvasAlphaButton.IsEnabled = !_busy && !HasPointerEdit;
            var alpha = _document.Project.Canvas.TransparentBackground;
            _canvasAlphaButton.Classes.Set("active", alpha);
            _canvasAlphaButton.Background = Brush.Parse(alpha ? "#646080" : "#34343B");
            _canvasAlphaButton.BorderBrush = Brush.Parse(alpha ? "#9A90B4" : "#48484F");
            UpdateMaskState(); SyncImageMaskState();
            UpdateCanvasSizeLabel();
            if (_customX.Parent?.Parent is Control textPosition) textPosition.IsVisible = _information.IsChecked != true;
            _customBackground.IsVisible = _information.IsChecked != true;
            this.GetLogicalDescendants().OfType<Expander>().Single(e => e.Name == "LogoPlacementSection").IsVisible = false;
            _showDetails.IsChecked = new[] { _infoResolution, _infoPhysical, _infoCount }.Any(c => c.IsChecked == true);
            _logoScaleSlider.Value = Math.Clamp((double)SelectedScreen.Style.Logo.ScalePercent, 70, 130);
            this.GetLogicalDescendants().OfType<TextBlock>().Single(c => c.Name == "LogoScaleValue").Text = _logoScaleSlider.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
            _logoOpacitySlider.Value = SelectedScreen.Style.Logo.OpacityPercent;
            this.GetLogicalDescendants().OfType<TextBlock>().Single(c => c.Name == "LogoOpacityValue").Text = SelectedScreen.Style.Logo.OpacityPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        }
        finally { _syncDesign = false; }
        ExistingAction("ExportResolume").IsVisible = IsCanvas;
        ExistingAction("ExportResolume").IsEnabled = CanExportResolume;
        SyncProjectionDesign();
    }
    private static Control ActionIcon(string kind)
    {
        if (kind == "Ndi") return NdiLogo.Create();
        if (kind == "Settings")
        {
            var icon = new Canvas { Width = 16, Height = 16 };
            var brush = Brush.Parse("#D4D4DB");
            icon.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 10, Height = 10, Margin = new Thickness(3), Stroke = brush, StrokeThickness = 1.4 });
            icon.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 4, Height = 4, Margin = new Thickness(6), Stroke = brush, StrokeThickness = 1.2 });
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Math.PI / 4;
                icon.Children.Add(new Avalonia.Controls.Shapes.Line { StartPoint = new Point(8 + 5 * Math.Cos(angle), 8 + 5 * Math.Sin(angle)), EndPoint = new Point(8 + 7.5 * Math.Cos(angle), 8 + 7.5 * Math.Sin(angle)), Stroke = brush, StrokeThickness = 2 });
            }
            return icon;
        }
        if (kind == "ImageMask") return new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M1,1 L15,1 L15,15 L1,15 Z M2,3 L13,14 M2,8 L8,14 M7,2 L14,9 M12,2 L14,4"), Stroke = Brush.Parse("#D4D4DB"), StrokeThickness = 1.2, Width = 16, Height = 16 };
        if (kind == "Transparency")
        {
            var grid = new Grid { Width = 16, Height = 16, ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*,*") };
            grid.Children.Add(new Border { Background = Brush.Parse("#D4D4DB") });
            var opposite = new Border { Background = Brush.Parse("#D4D4DB") }; Grid.SetRow(opposite, 1); Grid.SetColumn(opposite, 1); grid.Children.Add(opposite);
            return new Border { BorderBrush = Brush.Parse("#D4D4DB"), BorderThickness = new Thickness(1), Child = grid };
        }
        if (kind == "Magnet") return new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M2,5 L5,5 L5,9 A3,3 0 0 0 11,9 L11,5 L14,5 L14,9 A6,6 0 0 1 2,9 Z M2,1 L5,1 L5,4 L2,4 Z M11,1 L14,1 L14,4 L11,4 Z"),
            Fill = Brushes.White, Width = 16, Height = 16,
            RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(135)
        };
        var data = kind switch
        {
            "Undo" => "M6,3 L2,6 L6,9 M2,6 L9,6 Q14,6 14,11 L14,13",
            "Redo" => "M10,3 L14,6 L10,9 M14,6 L7,6 Q2,6 2,11 L2,13",
            "Visible" => "M1,8 Q8,1 15,8 Q8,15 1,8 Z M6,8 A2,2 0 1 0 10,8 A2,2 0 1 0 6,8",
            "Hidden" => "M1,8 Q8,1 15,8 Q8,15 1,8 Z M2,2 L14,14",
            "CopyScreen" or "Duplicate" => "M2,10 L2,2 L10,2 M5,5 L14,5 L14,14 L5,14 Z",
            "DuplicateScreen" => "M8,2 L8,14 M2,8 L14,8",
            "ScreenDown" => "M8,2 L8,14 M3,9 L8,14 L13,9",
            "ScreenUp" => "M8,14 L8,2 M3,7 L8,2 L13,7",
            "Profiles" => "M1,1 L6,1 L6,6 L1,6 Z M10,1 L15,1 L15,6 L10,6 Z M1,10 L6,10 L6,15 L1,15 Z M10,11 L13,14 L16,11",
            "Lock" => "M4,7 V5 A4,4 0 0 1 12,5 V7 M3,7 L13,7 L13,15 L3,15 Z M8,10 L8,12",
            "Unlock" => "M5,7 V5 A3.5,3.5 0 0 1 12,3 M3,7 L13,7 L13,15 L3,15 Z M8,10 L8,12",
            "Eraser" => "M2,10 L8,2 L14,7 L8,14 L5,14 Z M5,7 L11,12 M8,14 L15,14",
            _ => "M4,4 L12,12 M12,4 L4,12"
        };
        return new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), Stroke = Brush.Parse("#D4D4DB"), StrokeThickness = 1.3, Width = 16, Height = 16 };
    }
    private static void DescribeIcon(Button button, string description)
    { ToolTip.SetTip(button, description); AutomationProperties.SetName(button, description); }
    private Control BuildLayerEditor(CanvasLayerRow? row)
    {
        if (row is null) return new Panel();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("24,30,30,80,80,*,36,28"), ColumnSpacing = 3 };
        grid.Children.Add(new TextBlock { Name = "LayerDrag_" + row.Id.ToString("N"), Text = "⋮", Cursor = ResizeCursors.Move, VerticalAlignment = VerticalAlignment.Center, FontSize = 20 });
        var visible = new Button { Name = "LayerVisibility_" + row.Id.ToString("N"), Content = ActionIcon("Visible"), Classes = { "icon" } }; DescribeIcon(visible, L.T("ui237"));
        visible.Click += async (_, _) => { if (SelectLayerForInput(row.Id)) await CanvasAction(p => CanvasEditing.Place(p, row.Id, Placement.X, Placement.Y, Placement.IsLocked, !Placement.IsHidden)); };
        Grid.SetColumn(visible, 1); grid.Children.Add(visible);
        var duplicate = new Button { Name = "LayerDuplicate_" + row.Id.ToString("N"), Content = ActionIcon("Duplicate"), Classes = { "icon" } }; DescribeIcon(duplicate, L.T("ui238"));
        duplicate.Click += async (_, _) => { if (SelectLayerForInput(row.Id)) await CanvasAction(p => CanvasEditing.Add(p, CanvasEditing.Copy(SelectedScreen, NextScreenName()), Placement.X, Placement.Y)); };
        Grid.SetColumn(duplicate, 2); grid.Children.Add(duplicate);
        var x = LayerCoordinate(row.Id, true); var y = LayerCoordinate(row.Id, false);
        x.IsVisible=y.IsVisible=!IsProjection;
        if(IsProjection)grid.ColumnDefinitions[3].Width=grid.ColumnDefinitions[4].Width=new GridLength(0);
        Grid.SetColumn(x, 3); grid.Children.Add(x); Grid.SetColumn(y, 4); grid.Children.Add(y);
        var name = new Grid { Margin = new Thickness(8, 0) };
        var label = new TextBlock { Name = "LayerName_" + row.Id.ToString("N"), Text = row.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Children.Add(label);
        Grid.SetColumn(name, 5); grid.Children.Add(name);
        var ndi = new Button { Name = "LayerNdi_" + row.Id.ToString("N"), Content = ActionIcon("Ndi"), Width = 34, Height = 28, MinHeight = 28, Padding = new Thickness(0) };
        ndi.Click += async (_, _) => await ToggleNdiAsync(row.Id);
        Grid.SetColumn(ndi, 6); grid.Children.Add(ndi);
        var remove = new Button { Name = "LayerRemove_" + row.Id.ToString("N"), Content = ActionIcon("Remove"), Classes = { "icon" } }; DescribeIcon(remove, L.T("ui151"));
        remove.Click += async (_, _) => { if (SelectLayerForInput(row.Id)) await CanvasAction(p => CanvasEditing.Remove(p, row.Id)); };
        Grid.SetColumn(remove, 7); grid.Children.Add(remove);
        var boundary = new Border { Name = "LayerBoundary_" + row.Id.ToString("N"), BorderBrush = Brush.Parse("#42424B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 2), Margin = new Thickness(0, 2), Child = grid };
        _layerEditors[row.Id] = (x, y, visible, boundary, ndi); UpdateLayerEditor(row.Id); return boundary;
    }
    private bool SelectLayerForInput(Guid id)
    {
        if (_busy || HasPointerEdit || !_document.Project.Screens.Any(s => s.Id == id) || _screenId != id && HasDraft && !ApplyAutomatic()) return false;
        if (_screenId != id || !_objectsSelected) { _screenId = id; _objectsSelected = true; LoadControls(); if (!IsCanvas) RefreshPreview(); else { UpdatePreviewSummary(); UpdateScreenOverlay(); } }
        return true;
    }
    private TextBox LayerCoordinate(Guid id, bool horizontal)
    {
        var field = new TextBox { Name = (horizontal ? "LayerX_" : "LayerY_") + id.ToString("N"), MinWidth = 65, Margin = new Thickness(2, 0) };
        void Commit(bool normalize)
        {
            if (_syncLayers || _syncingControls || !SelectLayerForInput(id) || Placement.IsLocked) return;
            var target = horizontal ? _placementX : _placementY;
            target.Text = field.Text;
            if (normalize) CommitNumericField(target); else ApplyAutomatic(_editKey);
            field.Text = target.Text;
        }
        field.GotFocus += (_, _) => SelectLayerForInput(id);
        field.Tag = (Action<bool>)Commit;
        field.LostFocus += (_, _) => { if (_objectsSelected && _screenId == id) Commit(true); EndEditGroup(); };
        field.TextChanged += (_, _) => { if (!_syncLayers && !_syncingControls && field.IsFocused) Commit(false); };
        return field;
    }
    private void UpdateLayerEditor(Guid id)
    {
        if (!_layerEditors.TryGetValue(id, out var controls)) return;
        var placement = _document.Project.Canvas.Placements.FirstOrDefault(p => p.ScreenId == id);
        var screen = _document.Project.Screens.FirstOrDefault(s => s.Id == id);
        if (placement is null || screen is null) return;
        if (!controls.X.IsFocused) controls.X.Text = id == _screenId ? _placementX.Text : placement.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!controls.Y.IsFocused) controls.Y.Text = id == _screenId ? _placementY.Text : placement.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        controls.X.IsEnabled = controls.Y.IsEnabled = !_busy && !placement.IsLocked;
        controls.Visible.Content = ActionIcon(placement.IsHidden ? "Hidden" : "Visible");
        UpdateNdiButton(id, controls.Ndi);
        var c = screen.Style.PrimaryColor;
        // Tint the complete row while keeping white labels and coordinate fields legible.
        controls.Chip.Background = new SolidColorBrush(Color.FromRgb((byte)(24 + c.R * .32), (byte)(24 + c.G * .32), (byte)(24 + c.B * .32)));
        controls.Chip.BorderBrush = Brush.Parse(_objectsSelected && id == _screenId ? "#B5ACC9" : "#55555F");
    }
}

internal sealed class TransparencyBackground : Control
{
    public override void Render(DrawingContext context)
    {
        var first = Brush.Parse("#19191D"); var second = Brush.Parse("#1C1C20");
        const int cell = 16;
        for (var y = 0; y < Bounds.Height; y += cell)
            for (var x = 0; x < Bounds.Width; x += cell)
                context.DrawRectangle(((x / cell + y / cell) & 1) == 0 ? first : second, null, new Rect(x, y, Math.Min(cell, Bounds.Width - x), Math.Min(cell, Bounds.Height - y)));
    }
}
