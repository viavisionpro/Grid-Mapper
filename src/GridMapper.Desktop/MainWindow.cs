using Avalonia.LogicalTree;
using System.Globalization;
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

public sealed partial class MainWindow : Window
{
    internal static string DisplayVersion
    {
        get
        {
            var version = typeof(MainWindow).Assembly.GetName().Version;
            return version is null ? "0.1" : version.Build == 0 ? version.ToString(2) : version.ToString(3);
        }
    }
    internal const double MinimumEditorWidth = 1000;
    internal const double MinimumEditorHeight = 700;
    private readonly IEditorDialogs _dialogs;
    private readonly EditorSession _document = new(ProjectDefaults.Create());
    private readonly Image _preview = new() { Name = "Preview", Stretch = Stretch.Uniform };
    private readonly WorkspacePreview _workspaceRasters = new() { Name = "WorkspaceRasters", IsHitTestVisible = false };
    private readonly ScrollViewer _previewViewport = new() { Name = "PreviewViewport" };
    private readonly Border _previewWorkspace = new() { Name = "PreviewWorkspace", Focusable = true,
        Background = new SolidColorBrush(Color.Parse("#111318")), Padding = new Thickness(20) };
    private readonly TextBlock _status = new() { Name = "Status", TextWrapping = TextWrapping.Wrap };
    private readonly SelectableTextBlock _summary = new() { Name = "GeometrySummary", TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _screens = new() { Name = "Screens" };
    private readonly ComboBox _zoom = new() { Name = "Zoom", ItemsSource = new[] { "Fit", "100%", "200%" }, SelectedIndex = 0, Width = 92, MinWidth = 92, MaxWidth = 92 };
    private readonly ComboBox _pattern = new() { Name = "Pattern", ItemsSource = new[] { L.T("ui044"), L.T("ui045") } };
    private readonly CheckBox _label = new() { Name = "ResolutionLabel", Content = L.T("ui046") };
    private readonly CheckBox _border = new() { Name = "Border", Content = L.T("ui047") };
    private readonly CheckBox _corners = new() { Name = "CornerMarkers", Content = L.T("ui048") };
    private readonly CheckBox _diagonals = new() { Name = "Diagonals", Content = L.T("ui049") };
    private readonly CheckBox _centerPlus = new() { Name = "CenterPlus", Content = L.T("ui050") };
    private readonly CheckBox _crosspoint = new() { Name = "Crosspoint", Content = L.T("ui051") };
    private readonly CheckBox _numbering = new() { Name = "Numbering", Content = L.T("ui052") };
    private readonly ComboBox _numberFormat = new() { Name = "NumberingFormat", ItemsSource = new[] { "A1", "1-1", L.T("ui053") } };
    private readonly ComboBox _numberTraversal = new() { Name = "NumberingTraversal", ItemsSource = new[] { L.T("ui054"), L.T("ui055") } };
    private readonly ComboBox _numberCorner = new() { Name = "NumberingCorner", ItemsSource = new[] { L.T("ui056"), L.T("ui057"), L.T("ui058"), L.T("ui059") } };
    private readonly TextBox _numberPrefix = Field("NumberingPrefix");
    private readonly TextBox _numberStart = Field("NumberingStart");
    private readonly TextBox _numberFontSize = Field("NumberingFontSize");
    private readonly ComboBox _numberFont = FontChoice("NumberingFont");
    private readonly CheckBox _numberBackground = new() { Name = "NumberingBackground", Content = L.T("ui060") };
    private readonly CheckBox _information = new() { Name = "Information", Content = L.T("ui061") };
    private readonly CheckBox _infoName = new() { Name = "InformationName", Content = L.T("ui062") };
    private readonly CheckBox _infoResolution = new() { Name = "InformationResolution", Content = L.T("ui063") };
    private readonly CheckBox _infoPhysical = new() { Name = "InformationPhysicalSize", Content = L.T("ui064") };
    private readonly CheckBox _infoCount = new() { Name = "InformationCabinetCount", Content = L.T("ui065") };
    private readonly CheckBox _infoCoordinates = new() { Name = "InformationCoordinates", Content = L.T("ui066") };
    private readonly CheckBox _infoBackground = new() { Name = "InformationBackground", Content = L.T("ui060") };
    private readonly ComboBox _infoCorner = new() { Name = "InformationCorner", ItemsSource = new[] { L.T("ui056"), L.T("ui057"), L.T("ui058"), L.T("ui059") } };
    private readonly TextBox _infoFontSize = Field("InformationFontSize");
    private readonly ComboBox _infoFont = FontChoice("InformationFont");
    private readonly CheckBox _customText = new() { Name = "CustomText", Content = L.T("ui067") };
    private readonly TextBox _customContent = new() { Name = "CustomTextContent", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 };
    private readonly TextBox _customX = Field("CustomTextX");
    private readonly TextBox _customY = Field("CustomTextY");
    private readonly TextBox _customFont = Field("CustomTextFontSize");
    private readonly ComboBox _customFontFamily = FontChoice("CustomTextFont");
    private readonly CheckBox _customBackground = new() { Name = "CustomTextBackground", Content = L.T("ui060") };
    private readonly CheckBox _logo = new() { Name = "Logo", Content = L.T("ui068") };
    private readonly TextBlock _logoInfo = new() { Name = "LogoInfo", TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _logoAnchor = new() { Name = "LogoAnchor", ItemsSource = new[] { L.T("ui056"), L.T("ui057"), L.T("ui058"), L.T("ui059"), L.T("ui069") } };
    private readonly TextBox _logoX = Field("LogoOffsetX");
    private readonly TextBox _logoY = Field("LogoOffsetY");
    private readonly TextBox _logoScale = Field("LogoScale");
    private readonly TextBox _logoOpacity = Field("LogoOpacity");
    private Dictionary<string, LogoImage> _logoImages = [];
    private Dictionary<string, string> _logoProblems = [];
    private readonly TextBox _name = Field("ScreenName");
    private readonly TextBox[] _gridFields = [Field("WidthMm"), Field("HeightMm"), Field("WidthPx"), Field("HeightPx"), Field("Columns"), Field("Rows")];
    private readonly ColorPicker _primary = Picker("PrimaryColor");
    private readonly ColorPicker _secondary = Picker("SecondaryColor");
    private readonly StackPanel _gridPanel = new() { Name = "GridFields", Spacing = 8 };
    private readonly StackPanel _editor = new() { Spacing = 12, Margin = new Thickness(20) };
    private readonly List<Button> _fileButtons = [];
    private readonly Button _undo = new() { Name = "Undo", Content = L.T("ui070") };
    private readonly Button _redo = new() { Name = "Redo", Content = L.T("ui071") };
    private Guid _screenId;
    private bool _objectsSelected = true;
    private Control[] _objectInputs = [];
    private string? _filePath;
    private string[] _loadedText = [];
    private ScreenStyle _loadedStyle = ScreenStyle.Default;
    private bool _syncingControls;
    private bool _busy;
    private bool _allowClose;
    private Bitmap? _bitmap;
    private WorkspacePreview.Frame? _singleFrame;
    private byte[] _png = [];
    private int _pngWidth;
    private int _pngHeight;
    private Control? _editSource;
    private object? _editKey;
    private ProjectModel? _editBaseline;
    private readonly Dictionary<(int Column, int Row), Guid> _editCabinetIds = [];
    private readonly HashSet<Guid> _editMaskedIds = [];

    internal EditorSession Document => _document;
    internal string? FilePath => _filePath;
    internal bool HasDraft => IsProjection ? HasProjectionDraft : _loadedText.Length > 0 && (!ReadText().SequenceEqual(_loadedText)
        || !StyleEquals(ReadStyle(), _loadedStyle)
        || (_placementLocked.IsChecked == true) != Placement.IsLocked
        || (_placementHidden.IsChecked == true) != Placement.IsHidden
        || (_screenSnap.IsChecked == true) != _document.Project.Canvas.Snap.Enabled);

    public MainWindow() : this(new NativeEditorDialogs(), startEmpty: true) { }

    public MainWindow(IEditorDialogs dialogs, bool startEmpty = false)
    {
        _dialogs = dialogs;
        if (startEmpty) { _document.Reset(ProjectDefaults.Empty()); _objectsSelected = false; }
        else _document.Reset(PresentationProject(_document.Project));
        using var icon = Avalonia.Platform.AssetLoader.Open(new Uri("avares://GridMapper.Desktop/Assets/GridMapper.ico"));
        Icon = new WindowIcon(icon);
        Width = 1100; Height = 780;
        MinWidth = MinimumEditorWidth; MinHeight = MinimumEditorHeight;
        FontFamily = new FontFamily("avares://GridMapper.Desktop/Assets/Fonts#Noto Sans");
        RenderOptions.SetBitmapInterpolationMode(_preview, BitmapInterpolationMode.HighQuality);
        RenderOptions.SetBitmapInterpolationMode(_workspaceRasters, BitmapInterpolationMode.HighQuality);
        _screenId = _document.Project.Screens.FirstOrDefault()?.Id ?? Guid.Empty;

        BuildOpenLines();

        foreach (var field in TextFields())
        {
            field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) DraftChanged(field); };
            field.TextChanged += (_, _) => { if (!_syncingControls && !_busy && IsPendingText(field)) DraftChanged(field); };
            field.LostFocus += (_, _) =>
            {
                if (!_syncingControls && !_busy && IsCanvasDimension(field)) ApplyCanvasDimension(field, true);
                else if (!_syncingControls && !_busy && HasDraft) ApplyAutomatic(_editKey);
                if (ReferenceEquals(_editSource, field)) EndEditGroup();
            };
        }
        _primary.ColorChanged += (_, _) => DraftChanged(_primary); _secondary.ColorChanged += (_, _) => DraftChanged(_secondary);
        _label.IsCheckedChanged += (_, _) => DraftChanged(_label); _pattern.SelectionChanged += (_, _) => DraftChanged(_pattern);
        foreach (var toggle in new[] { _border, _corners, _diagonals, _centerPlus, _crosspoint, _raster, _circle, _cornerCircles }) toggle.IsCheckedChanged += (_, _) => DraftChanged(toggle);
        _numbering.IsCheckedChanged += (_, _) => DraftChanged(_numbering);
        _numberBackground.IsCheckedChanged += (_, _) => DraftChanged(_numberBackground);
        foreach (var choice in new[] { _numberFormat, _numberTraversal, _numberCorner }) choice.SelectionChanged += (_, _) => DraftChanged(choice);
        foreach (var toggle in new[] { _information, _infoName, _infoResolution, _infoPhysical, _infoCount, _infoCoordinates, _infoBackground, _ramp }) toggle.IsCheckedChanged += (_, _) => DraftChanged(toggle);
        _infoCorner.SelectionChanged += (_, _) => DraftChanged(_infoCorner);
        _customText.IsCheckedChanged += (_, _) => DraftChanged(_customText);
        _customBackground.IsCheckedChanged += (_, _) => DraftChanged(_customBackground);
        foreach (var choice in new[] { _numberFont, _infoFont, _customFontFamily }) choice.SelectionChanged += (_, _) => DraftChanged(choice);
        _logo.IsCheckedChanged += (_, _) => DraftChanged(_logo);
        _logoAnchor.SelectionChanged += (_, _) => DraftChanged(_logoAnchor);
        _screens.SelectionChanged += (_, _) => SelectScreen();
        _zoom.SelectionChanged += (_, _) => SelectZoom();
        AddHandler(KeyDownEvent, HandleKeys, RoutingStrategies.Tunnel);
        Opened += (_, _) => { SyncDesign(); ScheduleExternalOpen(); };
        Closing += HandleClosing; Closed += (_, _) => { CancelExternalOpens(); _ndiClosed = true; _ = StopAllNdiAsync(); ReleasePreviewResources(); };
        LoadControls(); RefreshPreview(); SetStatus(L.T("ui072"));
        L.Track(this);
    }

    private static TextBox Field(string name) => new() { Name = name, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static ComboBox FontChoice(string name) => new() { Name = name, ItemsSource = new[] { "Noto Sans", "Noto Sans Mono" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static ColorPicker Picker(string name) => new LiveColorPicker { Name = name, IsAlphaEnabled = true, IsAlphaVisible = true, IsColorPaletteVisible = false };
    private static void AddField(StackPanel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label }); panel.Children.Add(control);
    }
    private static void AddPair(StackPanel panel, string leftLabel, Control left, string rightLabel, Control right)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var a = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 8, 0) }; AddField(a, leftLabel, left);
        var b = new StackPanel { Spacing = 4 }; AddField(b, rightLabel, right);
        row.Children.Add(a); Grid.SetColumn(b, 1); row.Children.Add(b); panel.Children.Add(row);
    }
    private void AddAction(Panel toolbar, string name, string text, Func<Task<bool>> action)
    {
        var button = new Button { Name = name, Content = text, Margin = new Thickness(0, 0, 8, 6) };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception error) when (error is RenderLimitException or OutOfMemoryException) { ResourceFailure(error); }
        };
        _fileButtons.Add(button); toolbar.Children.Add(button);
    }

    private static readonly ScreenModel EmptyEditorScreen = ProjectDefaults.Create().Screens[0];
    private static readonly ScreenPlacement EmptyEditorPlacement = new(Guid.NewGuid(), EmptyEditorScreen.Id, 0, 0, false, false);
    private bool HasScreens => _document.Project.Screens.Count > 0;
    private ScreenModel SelectedScreen => _document.Project.Screens.FirstOrDefault(x => x.Id == _screenId) ?? EmptyEditorScreen;
    private TextBox[] TextFields() => _gridFields.Prepend(_name).Concat(new[] { _numberPrefix, _numberStart, _numberFontSize, _infoFontSize,
        _customContent, _customX, _customY, _customFont, _logoX, _logoY, _logoScale, _logoOpacity }).Concat(CabinetFields()).Concat(CanvasFields()).ToArray();
    private bool IsPendingText(TextBox field)
    {
        var index = Array.IndexOf(TextFields(), field);
        return index >= 0 && index < _loadedText.Length && (field.Text ?? "") != _loadedText[index];
    }
    private string[] ReadText() => TextFields().Select(x => x.Text ?? "").ToArray();
    private CabinetNumbering NumberingOptions(string prefix, int start, int fontSize) => new(_numbering.IsChecked == true,
        NumberingFormat.A1, NumberingTraversal.Rows, NumberingCorner.TopLeft, "", 1, fontSize, false, (EmbeddedFont)Math.Max(0, _numberFont.SelectedIndex));
    private ScreenInformation InformationOptions(int fontSize)
    {
        var next = new ScreenInformation(_information.IsChecked == true, _infoName.IsChecked == true,
            _infoResolution.IsChecked == true, _infoPhysical.IsChecked == true, _infoCount.IsChecked == true,
            fontSize, _infoBackground.IsChecked == true, (InformationCorner)Math.Max(0, _infoCorner.SelectedIndex), _infoCoordinates.IsChecked == true,
            (EmbeddedFont)Math.Max(0, _infoFont.SelectedIndex), ToRgba(_infoBackgroundColor.Color), _ramp.IsChecked == true, true);
        var current = SelectedScreen.Style.Information;
        // Merely moving a legacy screen must not rewrite its style or raster.
        return current.Independent() == next ? current : next;
    }
    private ScreenText TextOptions(string content, int x, int y, int fontSize) => new(_customText.IsChecked == true, content, x, y, fontSize, _customBackground.IsChecked == true, (EmbeddedFont)Math.Max(0, _customFontFamily.SelectedIndex));
    private ScreenLogo LogoOptions(int x, int y, decimal scale, int opacity) => new(_logo.IsChecked == true, SelectedScreen.Style.Logo.SourcePath,
        (LogoAnchor)Math.Max(0, _logoAnchor.SelectedIndex), x, y, scale, opacity, SelectedScreen.Style.Logo.EmbeddedImage, _logoBackground.IsChecked == true);
    private ScreenStyle ReadStyle(CabinetNumbering? numbering = null, ScreenInformation? information = null, ScreenText? text = null, ScreenLogo? logo = null) => new(_pattern.SelectedIndex == 1 ? FillPattern.Solid : FillPattern.Checkerboard,
        ToRgba(_primary.Color), ToRgba(_secondary.Color), true,
        new(_border.IsChecked == true, _corners.IsChecked == true, _diagonals.IsChecked == true, _centerPlus.IsChecked == true, _crosspoint.IsChecked == true, _raster.IsChecked == true, _circle.IsChecked == true, _cornerCircles.IsChecked == true),
        numbering ?? NumberingOptions(SelectedScreen.Style.Numbering.Prefix, SelectedScreen.Style.Numbering.Start, SelectedScreen.Style.Numbering.FontSize),
        information ?? InformationOptions(SelectedScreen.Style.Information.FontSize),
        text ?? TextOptions(SelectedScreen.Style.Text.Content, SelectedScreen.Style.Text.X, SelectedScreen.Style.Text.Y, SelectedScreen.Style.Text.FontSize),
        logo ?? LogoOptions(SelectedScreen.Style.Logo.OffsetX, SelectedScreen.Style.Logo.OffsetY, SelectedScreen.Style.Logo.ScalePercent, SelectedScreen.Style.Logo.OpacityPercent));
    private static RgbaColor ToRgba(Color color) => new(color.R, color.G, color.B, color.A);
    private static Color ToColor(RgbaColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);
    private static bool StyleEquals(ScreenStyle a, ScreenStyle b) => a.Pattern == b.Pattern && a.PrimaryColor == b.PrimaryColor
        && a.SecondaryColor == b.SecondaryColor && a.ShowResolutionLabel == b.ShowResolutionLabel && a.Decorations == b.Decorations && a.Numbering == b.Numbering && a.Information == b.Information && a.Text == b.Text && a.Logo == b.Logo;

    private void LoadControls()
    {
        EndEditGroup();
        _syncingControls = true;
        try
        {
            var screen = SelectedScreen;
            _screens.ItemsSource = _document.Project.Screens.Select(x => x.Name).ToArray();
            _screens.SelectedIndex = _objectsSelected ? _document.Project.Screens.ToList().FindIndex(x => x.Id == _screenId) : -1;
            _screens.IsVisible = _document.Project.Screens.Count > 1;
            _name.Text = screen.Name;
            var regular = RegularGrid.Detect(screen);
            _gridPanel.IsVisible = regular is not null && _cabinetMode.IsChecked != true;
            _editor.GetLogicalDescendants().OfType<TextBlock>().Single(x => x.Name == "MixedNote").IsVisible = regular is null;
            var values = regular is null ? new string[6] : new[] {
                regular.Type.WidthMm.ToString(CultureInfo.InvariantCulture), regular.Type.HeightMm.ToString(CultureInfo.InvariantCulture),
                regular.Type.WidthPx.ToString(CultureInfo.InvariantCulture), regular.Type.HeightPx.ToString(CultureInfo.InvariantCulture),
                regular.Columns.ToString(CultureInfo.InvariantCulture), regular.Rows.ToString(CultureInfo.InvariantCulture) };
            for (var i = 0; i < 6; i++) _gridFields[i].Text = values[i] ?? "";
            _pattern.SelectedIndex = screen.Style.Pattern == FillPattern.Solid ? 1 : 0;
            _primary.Color = ToColor(screen.Style.PrimaryColor); _secondary.Color = ToColor(screen.Style.SecondaryColor);
            _label.IsChecked = screen.Style.ShowResolutionLabel;
            _border.IsChecked = screen.Style.Decorations.Border; _corners.IsChecked = screen.Style.Decorations.CornerMarkers;
            _diagonals.IsChecked = screen.Style.Decorations.Diagonals; _centerPlus.IsChecked = screen.Style.Decorations.CenterPlus;
            _crosspoint.IsChecked = screen.Style.Decorations.Crosspoint;
            var numbering = screen.Style.Numbering;
            _numbering.IsChecked = numbering.Enabled; _numberFormat.SelectedIndex = (int)numbering.Format;
            _numberTraversal.SelectedIndex = (int)numbering.Traversal; _numberCorner.SelectedIndex = (int)numbering.Corner;
            _numberPrefix.Text = numbering.Prefix; _numberStart.Text = numbering.Start.ToString(CultureInfo.InvariantCulture);
            _numberFontSize.Text = numbering.FontSize.ToString(CultureInfo.InvariantCulture); _numberBackground.IsChecked = numbering.Background;
            _numberFont.SelectedIndex = (int)numbering.Font;
            var information = screen.Style.Information.Independent();
            _information.IsChecked = information.Enabled; _infoName.IsChecked = information.ShowName;
            _infoResolution.IsChecked = information.ShowResolution; _infoPhysical.IsChecked = information.ShowPhysicalSize;
            _infoCount.IsChecked = information.ShowCabinetCount; _infoBackground.IsChecked = information.Background;
            _infoCoordinates.IsChecked = information.ShowCoordinates;
            _infoCorner.SelectedIndex = (int)information.Corner; _infoFontSize.Text = information.FontSize.ToString(CultureInfo.InvariantCulture);
            _infoFont.SelectedIndex = (int)information.Font;
            var text = screen.Style.Text;
            _customText.IsChecked = text.Enabled; _customContent.Text = text.Content;
            _customX.Text = text.X.ToString(CultureInfo.InvariantCulture); _customY.Text = text.Y.ToString(CultureInfo.InvariantCulture);
            _customFont.Text = text.FontSize.ToString(CultureInfo.InvariantCulture); _customBackground.IsChecked = text.Background;
            _customFontFamily.SelectedIndex = (int)text.Font;
            var logo = screen.Style.Logo; _logo.IsChecked = logo.Enabled; _logoAnchor.SelectedIndex = (int)logo.Anchor; _logoBackground.IsChecked = logo.Background;
            _raster.IsChecked = screen.Style.Decorations.Raster;
            _circle.IsChecked = screen.Style.Decorations.Circle; _cornerCircles.IsChecked = screen.Style.Decorations.CornerCircles;
            _ramp.IsChecked = information.Ramp;
            _infoBackgroundColor.Color = ToColor(information.BackgroundColor ?? new RgbaColor(24, 24, 24));
            _logoX.Text = logo.OffsetX.ToString(CultureInfo.InvariantCulture); _logoY.Text = logo.OffsetY.ToString(CultureInfo.InvariantCulture);
            _logoScale.Text = logo.ScalePercent.ToString(CultureInfo.InvariantCulture); _logoOpacity.Text = logo.OpacityPercent.ToString(CultureInfo.InvariantCulture);
            LoadCabinetControls(); LoadCanvasControls();
            _loadedText = ReadText(); _loadedStyle = ReadStyle(); LoadProjectionControls();
        }
        finally { _syncingControls = false; }
        UpdateActions();
    }

    private bool ApplyAutomatic(object? mergeKey = null)
    {
        if (IsProjection) return ApplyProjectionAutomatic(mergeKey);
        if (!HasScreens) return ApplyEmptyCanvasDraft(mergeKey);
        if (!HasDraft) { UpdateActions(); SetStatus(L.T("ui072")); return true; }
        try
        {
            var problems = new List<string>();
            var inputName = _name.Text ?? "";
            var name = SelectedScreen.Name;
            var validName = false;
            try { ModelLimits.ValidateName(inputName); name = inputName; validName = true; }
            catch (ModelValidationException error) { problems.Add(error.Message); }
            var previousNumbering = SelectedScreen.Style.Numbering;
            var prefix = _numberPrefix.Text ?? ""; var validPrefix = false;
            try { CabinetNumbering.ValidatePrefix(prefix); validPrefix = true; }
            catch (ModelValidationException error) { problems.Add(error.Message); prefix = previousNumbering.Prefix; }
            var start = ReadBoundedInt(_numberStart, L.T("ui073"), 0, CabinetNumbering.MaxStart, previousNumbering.Start, problems, out var validStart);
            var fontSize = ReadBoundedInt(_numberFontSize, L.T("ui074"), CabinetNumbering.MinFontSize, CabinetNumbering.MaxFontSize,
                previousNumbering.FontSize, problems, out var validFontSize);
            var infoFontSize = ReadBoundedInt(_infoFontSize, L.T("ui075"), 6, 256,
                SelectedScreen.Style.Information.FontSize, problems, out var validInfoFontSize);
            var previousText = SelectedScreen.Style.Text;
            var content = _customContent.Text ?? ""; var validContent = false;
            try { ScreenText.ValidateContent(content); validContent = true; }
            catch (ModelValidationException error) { problems.Add(error.Message); content = previousText.Content; }
            var textX = ReadBoundedInt(_customX, L.T("ui076"), -ModelLimits.MaxDimension, ModelLimits.MaxDimension, previousText.X, problems, out var validTextX);
            var textY = ReadBoundedInt(_customY, L.T("ui077"), -ModelLimits.MaxDimension, ModelLimits.MaxDimension, previousText.Y, problems, out var validTextY);
            var textFont = ReadBoundedInt(_customFont, L.T("ui074"), 6, 256, previousText.FontSize, problems, out var validTextFont);
            var oldLogo = SelectedScreen.Style.Logo;
            var logoX = ReadBoundedInt(_logoX, L.T("ui078"), -ModelLimits.MaxDimension, ModelLimits.MaxDimension, oldLogo.OffsetX, problems, out var validLogoX);
            var logoY = ReadBoundedInt(_logoY, L.T("ui079"), -ModelLimits.MaxDimension, ModelLimits.MaxDimension, oldLogo.OffsetY, problems, out var validLogoY);
            var logoScale = ReadBoundedDecimal(_logoScale, L.T("ui080"), 0.1m, 1000m, oldLogo.ScalePercent, problems, out var validLogoScale);
            var logoOpacity = ReadBoundedInt(_logoOpacity, L.T("ui081"), 0, 100, oldLogo.OpacityPercent, problems, out var validLogoOpacity);
            var style = ReadStyle(NumberingOptions(prefix, start, fontSize), InformationOptions(infoFontSize), TextOptions(content, textX, textY, textFont), LogoOptions(logoX, logoY, logoScale, logoOpacity));
            GridSettings? settings = null;
            var acceptedFields = new bool[_gridFields.Length];
            ProjectModel next;
            try
            {
                if (_cabinetMode.IsChecked != true && RegularGrid.Detect(SelectedScreen) is { } regular)
                    settings = ReadAutomaticGrid(regular.Settings, problems, acceptedFields);
                next = new EditScreenCommand(_screenId, name, style, settings, _editCabinetIds, _editMaskedIds).Apply(_document.Project);
            }
            catch (Exception error) when (error is ArgumentException or NumericExpressionException)
            {
                problems.Add(error.Message);
                var requested = settings;
                var previouslyAccepted = acceptedFields.ToArray();
                settings = null;
                Array.Clear(acceptedFields);
                next = new EditScreenCommand(_screenId, name, style, null).Apply(_document.Project);
                if (requested is not null && RegularGrid.Detect(SelectedScreen) is { } previous)
                {
                    var effective = previous.Settings;
                    foreach (var group in new[] { new[] { 0, 2 }, new[] { 1, 3 }, new[] { 4 }, new[] { 5 } })
                    {
                        var candidate = group[0] switch
                        {
                            0 => effective with { WidthMm = requested.WidthMm, WidthPx = requested.WidthPx },
                            1 => effective with { HeightMm = requested.HeightMm, HeightPx = requested.HeightPx },
                            4 => effective with { Columns = requested.Columns },
                            _ => effective with { Rows = requested.Rows }
                        };
                        try
                        {
                            next = new EditScreenCommand(_screenId, name, style, candidate, _editCabinetIds, _editMaskedIds).Apply(next);
                            effective = candidate;
                            foreach (var index in group) acceptedFields[index] = previouslyAccepted[index];
                        }
                        catch (ArgumentException) { }
                    }
                    settings = effective;
                }
            }
            var cabinetAccepted = new bool[7];
            next = ApplyCabinetDraft(next, problems, cabinetAccepted);
            var canvasAccepted = new bool[5];
            next = ApplyCanvasDraft(next, problems, canvasAccepted);
            var effectiveGrid = settings ?? RegularGrid.Detect(SelectedScreen)?.Settings;
            if (_editBaseline is not null && SameCanvas(next.Canvas, _editBaseline.Canvas))
            {
                var baseline = _editBaseline.Screens.First(x => x.Id == _screenId);
                if (baseline.Name == name && StyleEquals(baseline.Style, style)
                    && RegularGrid.Detect(baseline)?.Settings == effectiveGrid && SameCabinetGeometry(baseline, next.Screens.First(x => x.Id == _screenId))) next = _editBaseline;
            }
            if (!ReferenceEquals(next, _document.Project))
            {
                var rendered = Render(next, next.Screens.First(x => x.Id == _screenId));
                _document.Execute(new PreparedEdit(next), mergeKey);
                UsePreview(rendered);
            }
            RememberCabinetIds();
            if (validName) _loadedText[0] = inputName;
            for (var i = 0; i < _gridFields.Length; i++)
                if (acceptedFields[i]) _loadedText[i + 1] = _gridFields[i].Text ?? "";
            _loadedStyle = style;
            if (validPrefix) _loadedText[7] = _numberPrefix.Text ?? "";
            if (validStart) _loadedText[8] = _numberStart.Text ?? "";
            if (validFontSize) _loadedText[9] = _numberFontSize.Text ?? "";
            if (validInfoFontSize) _loadedText[10] = _infoFontSize.Text ?? "";
            if (validContent) _loadedText[11] = _customContent.Text ?? "";
            if (validTextX) _loadedText[12] = _customX.Text ?? "";
            if (validTextY) _loadedText[13] = _customY.Text ?? "";
            if (validTextFont) _loadedText[14] = _customFont.Text ?? "";
            if (validLogoX) _loadedText[15] = _logoX.Text ?? "";
            if (validLogoY) _loadedText[16] = _logoY.Text ?? "";
            if (validLogoScale) _loadedText[17] = _logoScale.Text ?? "";
            if (validLogoOpacity) _loadedText[18] = _logoOpacity.Text ?? "";
            for (var i = 0; i < 7; i++) if (cabinetAccepted[i]) _loadedText[19 + i] = CabinetFields()[i].Text ?? "";
            for (var i = 0; i < 5; i++) if (canvasAccepted[i]) _loadedText[26 + i] = CanvasFields()[i].Text ?? "";
            UpdateCanvasControls();
            UpdateCabinetDescription();
            if (_cabinetMode.IsChecked != true) SyncCabinetControls();
            UpdateScreenNames(); UpdateActions();
            SetStatus(problems.Count == 0 ? L.T("ui072") : problems[0]);
            return problems.Count == 0;
        }
        catch (Exception error) when (error is RenderLimitException or OutOfMemoryException)
        { ResourceFailure(error); return false; }
        catch (Exception error) when (error is ArgumentException or NumericExpressionException or InvalidOperationException or NotSupportedException)
        { SetStatus(error.Message); UpdateActions(); return false; }
    }

    private static decimal ParseMm(TextBox field, string label)
    {
        try { return NumericExpression.Evaluate((field.Text ?? "").Replace(',', '.')); }
        catch (NumericExpressionException error) { throw new NumericExpressionException($"{label}: {error.Message}"); }
    }
    private static int ParseInt(TextBox field, string label)
    {
        try { return NumericExpression.EvaluateInt32(field.Text); }
        catch (NumericExpressionException error) { throw new NumericExpressionException($"{label}: {error.Message}"); }
    }
    private GridSettings ReadAutomaticGrid(GridSettings previous, List<string> problems, bool[] accepted)
    {
        decimal Millimeters(int index, string label, decimal oldValue)
        {
            try
            {
                var value = ParseMm(_gridFields[index], label);
                if (value < ModelLimits.MinMillimeters || value > ModelLimits.MaxMillimeters)
                    throw new NumericExpressionException(L.F("ui082", label));
                accepted[index] = true; return value;
            }
            catch (NumericExpressionException error) { problems.Add(error.Message); return oldValue; }
        }
        int Integer(int index, string label, int oldValue, int max)
        {
            try
            {
                var value = ParseInt(_gridFields[index], label);
                if (value < 1 || value > max) throw new NumericExpressionException(L.F("ui083", label, max));
                accepted[index] = true; return value;
            }
            catch (NumericExpressionException error) { problems.Add(error.Message); return oldValue; }
        }
        return new(Millimeters(0, L.T("ui084"), previous.WidthMm), Millimeters(1, L.T("ui085"), previous.HeightMm),
            Integer(2, L.T("ui086"), previous.WidthPx, ModelLimits.MaxDimension), Integer(3, L.T("ui087"), previous.HeightPx, ModelLimits.MaxDimension),
            Integer(4, L.T("ui088"), previous.Columns, ModelLimits.MaxCabinets), Integer(5, L.T("ui089"), previous.Rows, ModelLimits.MaxCabinets));
    }

    private static string[] GridValues(RegularGrid grid) => [
        grid.Type.WidthMm.ToString(CultureInfo.InvariantCulture), grid.Type.HeightMm.ToString(CultureInfo.InvariantCulture),
        grid.Type.WidthPx.ToString(CultureInfo.InvariantCulture), grid.Type.HeightPx.ToString(CultureInfo.InvariantCulture),
        grid.Columns.ToString(CultureInfo.InvariantCulture), grid.Rows.ToString(CultureInfo.InvariantCulture) ];

    private void CommitNumericField(TextBox? field)
    {
        if (field is not null && IsCanvasDimension(field)) { ApplyCanvasDimension(field, true); EndEditGroup(); return; }
        if (field?.Tag is Action<bool> layerCommit) { layerCommit(true); return; }
        ApplyAutomatic(_editKey);
        var index = Array.IndexOf(TextFields(), field);
        var grid = RegularGrid.Detect(SelectedScreen);
        var value = index switch
        {
            >= 1 and <= 6 when grid is not null => GridValues(grid)[index - 1],
            8 => SelectedScreen.Style.Numbering.Start.ToString(CultureInfo.InvariantCulture),
            9 => SelectedScreen.Style.Numbering.FontSize.ToString(CultureInfo.InvariantCulture),
            10 => SelectedScreen.Style.Information.FontSize.ToString(CultureInfo.InvariantCulture),
            12 => SelectedScreen.Style.Text.X.ToString(CultureInfo.InvariantCulture),
            13 => SelectedScreen.Style.Text.Y.ToString(CultureInfo.InvariantCulture),
            14 => SelectedScreen.Style.Text.FontSize.ToString(CultureInfo.InvariantCulture),
            15 => SelectedScreen.Style.Logo.OffsetX.ToString(CultureInfo.InvariantCulture),
            16 => SelectedScreen.Style.Logo.OffsetY.ToString(CultureInfo.InvariantCulture),
            17 => SelectedScreen.Style.Logo.ScalePercent.ToString(CultureInfo.InvariantCulture),
            18 => SelectedScreen.Style.Logo.OpacityPercent.ToString(CultureInfo.InvariantCulture),
            >= 19 and <= 25 => CabinetValue(index - 19),
            >= 26 and <= 30 => CanvasValue(index - 26),
            _ => null
        };
        if (value is not null && field!.Text == _loadedText[index])
        {
            _syncingControls = true;
            try { field.Text = value; field.CaretIndex = value.Length; _loadedText[index] = value; }
            finally { _syncingControls = false; }
            UpdateActions();
        }
        EndEditGroup();
    }
    private static int ReadBoundedInt(TextBox field, string label, int min, int max, int previous, List<string> problems, out bool valid)
    {
        valid = false;
        try
        {
            var value = ParseInt(field, label);
            if (value < min || value > max) throw new NumericExpressionException(L.F("ui090", label, min, max));
            valid = true; return value;
        }
        catch (NumericExpressionException error) { problems.Add(error.Message); return previous; }
    }
    private static decimal ReadBoundedDecimal(TextBox field, string label, decimal min, decimal max, decimal previous, List<string> problems, out bool valid)
    {
        valid = false;
        try
        {
            var value = ParseMm(field, label);
            if (value < min || value > max) throw new NumericExpressionException(L.F("ui090", label, min, max));
            valid = true; return value;
        }
        catch (NumericExpressionException error) { problems.Add(error.Message); return previous; }
    }
    private sealed record PreparedEdit(ProjectModel Next) : IProjectCommand
    { public string Description => L.T("ui091"); public ProjectModel Apply(ProjectModel project) => Next; }

    private sealed record RenderedPreview(byte[] Png, Bitmap? Bitmap, IImage Source, int Width, int Height, Rect SceneBounds, IReadOnlyList<WorkspacePreview.Frame>? Frames, WorkspacePreview.Frame? SingleFrame);
    private double PreviewWidth => _workspaceRasters.Frames is null ? _pngWidth : _workspaceRasters.SceneBounds.Width;
    private double PreviewHeight => _workspaceRasters.Frames is null ? _pngHeight : _workspaceRasters.SceneBounds.Height;
    private Point PreviewOrigin => _workspaceRasters.Frames is null ? default : _workspaceRasters.SceneBounds.TopLeft;
    internal static ScreenModel PresentationScreen(ScreenModel screen)
    {
        var s = screen.Style;
        var usedTypes = screen.Cabinets.Select(c => c.TypeId).ToHashSet();
        var idSize = Math.Clamp(screen.CabinetTypes.Where(t => usedTypes.Contains(t.Id)).Min(t => Math.Min(t.WidthPx, t.HeightPx)) / 4, 6, 256);
        var numbering = new CabinetNumbering(s.Numbering.Enabled, NumberingFormat.A1, NumberingTraversal.Rows, NumberingCorner.TopLeft, "", 1, idSize, false, s.Numbering.Font);
        var decorations = new ScreenDecorations(Diagonals: s.Decorations.Diagonals, Raster: s.Decorations.Raster, Circle: s.Decorations.Circle, CornerCircles: s.Decorations.CornerCircles);
        return screen.WithStyle(new(s.Pattern, s.PrimaryColor, s.SecondaryColor, true, decorations, numbering, s.Information.Independent(), s.Text, s.Logo));
    }
    internal static ProjectModel PresentationProject(ProjectModel project) => new(project.FormatVersion, project.Screens.Select(PresentationScreen).ToArray(), project.Canvas, project.Mode);
    private static (WorkspacePreview.Frame Frame, byte[] Png) PrepareFrame(ScreenModel screen, ScreenPlacement position, IReadOnlyDictionary<string, LogoImage> images, bool transparentMasks = false)
    {
        var scene = SceneBuilder.ForScreen(PresentationScreen(screen), position, transparentMasks);
        var vector = new RecordedScreen(SceneRenderer.Record(scene, images, allowMissingLogos: true));
        try
        {
            var png = vector.RenderPng(scene.Width, scene.Height);
            using var stream = new MemoryStream(png); var bitmap = new Bitmap(stream);
            images.TryGetValue(screen.Style.Logo.SourcePath, out var logo);
            return (new(bitmap, position.X, position.Y, screen, logo, vector, transparentMasks), png);
        }
        catch { vector.Dispose(); throw; }
    }
    private RenderedPreview Render(ProjectModel project, ScreenModel screen, IReadOnlyDictionary<string, LogoImage>? logos = null)
    {
        if (project.Mode==ProjectMode.Projection && project.Screens.Count>0) return RenderProjection(screen);
        var canvasView = project.Mode == ProjectMode.Led && _viewMode.SelectedIndex == 1;
        ValidatePreviewBudget(project, screen);
        var images = logos ?? _logoImages;
        var transparentMasks = canvasView && project.Canvas.TransparentBackground;
        var current = (_workspaceRasters.Frames ?? []).Concat(_singleFrame is null ? [] : new[] { _singleFrame }).ToDictionary(f => f.Source.Id);
        var sources = project.Screens.ToDictionary(s => s.Id);
        WorkspacePreview.Frame? Cached(ScreenModel original, ScreenPlacement placement)
        {
            images.TryGetValue(original.Style.Logo.SourcePath, out var logo);
            return current.TryGetValue(original.Id, out var frame) && !frame.IsProjection && ReferenceEquals(frame.Source, original) && ReferenceEquals(frame.Logo, logo) && (frame.TransparentMasks == transparentMasks || original.Cabinets.All(c => !c.IsMasked))
                && (!(original.Style.Information.Enabled && original.Style.Information.ShowCoordinates) || frame.X == placement.X && frame.Y == placement.Y) ? frame : null;
        }
        if (canvasView || project.Screens.Count == 0)
        {
            var extent = new Rect(0, 0, project.Canvas.WidthPx, project.Canvas.HeightPx);
            if (canvasView && project.Canvas.ImageMask is { Enabled: true, Image: not null } guide)
                extent = extent.Union(new Rect(guide.X, guide.Y, guide.Width, guide.Height));
            var prepared = new List<WorkspacePreview.Frame>(); var created = new List<WorkspacePreview.Frame>();
            try
            {
                if (canvasView) foreach (var placement in project.Canvas.Placements.Where(p => !p.IsHidden))
                {
                    var original = sources[placement.ScreenId];
                    var frame = Cached(original, placement);
                    if (frame is null)
                    {
                        frame = PrepareFrame(original, placement, images, transparentMasks).Frame; created.Add(frame);
                    }
                    prepared.Add(frame.X == placement.X && frame.Y == placement.Y ? frame : frame with { X = placement.X, Y = placement.Y });
                    extent = extent.Union(new Rect(placement.X, placement.Y, original.Geometry.Bounds.Width, original.Geometry.Bounds.Height));
                }
                return new([], null, new PreviewExtent(new(project.Canvas.WidthPx, project.Canvas.HeightPx)),
                    project.Canvas.WidthPx, project.Canvas.HeightPx, extent, canvasView ? prepared : null, null);
            }
            catch { foreach (var frame in created) { frame.Bitmap.Dispose(); frame.Vector.Dispose(); } throw; }
        }
        var position = project.Canvas.Placements.Single(p => p.ScreenId == screen.Id);
        var cached = Cached(screen, position);
        var preparedSingle = cached is null ? PrepareFrame(screen, position, images)
            : (cached, ReferenceEquals(cached, _singleFrame) && _png.Length > 0 ? _png
                : cached.Vector.RenderPng(screen.Geometry.Bounds.Width, screen.Geometry.Bounds.Height));
        return new(preparedSingle.Item2, preparedSingle.Item1.Bitmap, preparedSingle.Item1.Bitmap,
            screen.Geometry.Bounds.Width, screen.Geometry.Bounds.Height, new(0, 0, screen.Geometry.Bounds.Width, screen.Geometry.Bounds.Height), null, preparedSingle.Item1);
    }
    private void ReleasePreviewResources()
    {
        _imageMaskClosed = true; DisposeImageMask();
        _preview.Source = null; _bitmap?.Dispose(); _bitmap = null; _singleFrame?.Vector.Dispose(); _singleFrame = null; _workspaceRasters.SingleFrame = null;
        _workspaceRasters.DisposeFrames(); _png = [];
        _logoThumbnail.Source = null; _logoThumbnailBitmap?.Dispose(); _logoThumbnailBitmap = null; _thumbnailSource = null;
        _logoImages.Clear(); _logoProblems.Clear();
        _maskStroke = null; _maskPointer = null; _maskOutlineProject = null;
        _maskedPanels = []; _maskEdges = []; _maskOverlay.Outlines = []; _maskOverlay.Edges = []; _maskOutlineTransform = null;
    }
    private void RefreshPreview()
    {
        try { UsePreview(Render(_document.Project, SelectedScreen)); }
        catch (Exception error) when (error is RenderLimitException or OutOfMemoryException) { ResourceFailure(error); }
    }
    private void UsePreview(RenderedPreview rendered)
    {
        var oldOrigin = PreviewOrigin;
        var hadWorkspace = _workspaceRasters.Frames is not null;
        var oldCenter = SceneAtView(ViewCenter);
        var oldAnchor = _viewAnchor;
        var previous = _bitmap; var previousSingle = _singleFrame; _bitmap = rendered.Bitmap;
        _workspaceRasters.ReplaceFrames(rendered.Frames, rendered.Bitmap); _singleFrame = rendered.SingleFrame; _workspaceRasters.SceneBounds = rendered.SceneBounds;
        _workspaceRasters.SingleFrame = rendered.SingleFrame;
        _workspaceRasters.CanvasSize = new(rendered.Width, rendered.Height);
        _workspaceRasters.TransparentCanvas = _document.Project.Canvas.TransparentBackground;
        if (IsCanvas && hadWorkspace && !double.IsNaN(_viewFactor))
        {
            var shift = oldOrigin - PreviewOrigin;
            _viewAnchor = oldAnchor is { } anchor ? new(anchor.Scene + shift, anchor.View) : new(oldCenter + shift, ViewCenter);
        }
        _png = rendered.Png;
        _preview.Stretch = rendered.Frames is null ? Stretch.Uniform : Stretch.Fill;
        _preview.Opacity = 0; _workspaceRasters.InvalidateVisual();
        _pngWidth = rendered.Width; _pngHeight = rendered.Height; _preview.Source = rendered.Source;
        if (previous is not null && !ReferenceEquals(previous, rendered.Bitmap) && rendered.Frames?.Any(f => ReferenceEquals(f.Bitmap, previous)) != true)
        { previous.Dispose(); previousSingle?.Vector.Dispose(); }
        SyncNdiOutputs(_maskStroke?.Candidate ?? _document.Project);
        UpdatePreviewSummary();
        ApplyZoom(); UpdateCabinetOverlay(); UpdateScreenOverlay();
    }
    private void UpdatePreviewSummary()
    {
        var screen = (_screenResize?.Candidate ?? _maskStroke?.Candidate)?.Screens.First(s => s.Id == _screenId) ?? SelectedScreen;
        if(IsProjection) { var p=screen.Projection??ProjectionSettings.Default;_summary.Text=$"{p.WidthPx} × {p.HeightPx} px\n{ProjectionSettings.Meters(p.WidthMeters)} × {ProjectionSettings.Meters(p.HeightMeters)} m";return; }
        var g = screen.Geometry;
        var grid = RegularGrid.Detect(screen);
        _compactGeometry.Text = FormattableString.Invariant($"{g.Bounds.Width} × {g.Bounds.Height} px · ") + (grid is null ? L.F("ui092", g.CabinetCount) : L.F("ui093", grid.Columns, grid.Rows));
        _summary.Text = L.F("ui094", g.Bounds.Width, g.Bounds.Height, g.CabinetCount, g.WidthMm, g.HeightMm, g.WidthMeters, g.HeightMeters, g.AspectWidth, g.AspectHeight, decimal.Truncate(g.PitchX * 100) / 100, decimal.Truncate(g.PitchY * 100) / 100);
    }
    private void DraftChanged(Control source)
    {
        if (_syncingControls || _busy || HasPointerEdit) return;
        if (source is TextBox or ColorPicker)
        {
            if (!ReferenceEquals(_editSource, source))
            {
                EndEditGroup(); _editSource = source; _editKey = new object(); _editBaseline = _document.Project;
                RememberCabinetIds();
            }
        }
        else EndEditGroup();
        if (source is TextBox dimension && IsCanvasDimension(dimension)) ApplyCanvasDimension(dimension, false);
        else ApplyAutomatic(_editKey);
    }
    private void EndEditGroup()
    {
        _editSource = null; _editKey = null; _editBaseline = null; _editCabinetIds.Clear(); _editMaskedIds.Clear();
    }
    private void RememberCabinetIds()
    {
        if (_editKey is null) return;
        var grid = RegularGrid.Detect(SelectedScreen);
        if (grid is null) return;
        foreach (var cabinet in SelectedScreen.Cabinets)
        {
            _editCabinetIds.TryAdd(((cabinet.X - grid.OriginX) / grid.Type.WidthPx, (cabinet.Y - grid.OriginY) / grid.Type.HeightPx), cabinet.Id);
            if (cabinet.IsMasked) _editMaskedIds.Add(cabinet.Id);
        }
    }
    private void UpdateScreenNames()
    {
        var names = _document.Project.Screens.Select(x => x.Name).ToArray();
        if (_screens.ItemsSource is string[] current && current.SequenceEqual(names)) return;
        _syncingControls = true;
        _screens.ItemsSource = names;
        _screens.SelectedIndex = _objectsSelected ? _document.Project.Screens.ToList().FindIndex(x => x.Id == _screenId) : -1;
        _syncingControls = false;
    }
    private void UpdateActions()
    {
        foreach (var control in _objectInputs) control.IsEnabled = true;
        var version = DisplayVersion;
        Title = $"Grid Mapper {version} — {(_filePath is null ? L.T("ui095") : Path.GetFileName(_filePath))}{(_document.IsDirty || HasDraft ? " *" : "")}";
        _undo.IsEnabled = !_busy && !HasPointerEdit && (_document.CanUndo || HasDraft);
        _redo.IsEnabled = !_busy && !HasPointerEdit && _document.CanRedo && !HasDraft;
        _zoom.IsEnabled = !_busy && !HasPointerEdit;
        _secondary.IsEnabled = _pattern.SelectedIndex != 1;
        _numberTraversal.IsEnabled = _numberFormat.SelectedIndex == (int)NumberingFormat.Sequential;
        _label.IsEnabled = !_information.IsChecked.GetValueOrDefault();
        foreach (var button in _fileButtons) button.IsEnabled = !_busy && !HasPointerEdit;
        _editor.IsEnabled = !_busy && !HasPointerEdit;
        UpdateLogoInfo(); UpdateLogoThumbnail(); UpdateCanvasControls();
        if (!_objectsSelected)
        {
            foreach (var control in _objectInputs) control.IsEnabled = false;
            _placementX.IsEnabled = _placementY.IsEnabled = _placementLocked.IsEnabled = _placementHidden.IsEnabled = false;
            foreach (var button in _fileButtons.Where(b => b.Name is "CopyScreen" or "DuplicateScreen" or "DeleteScreen" or "ScreenUp" or "ScreenDown")) button.IsEnabled = false;
        }
        else _placementLocked.IsEnabled = _placementHidden.IsEnabled = true;
        UpdateMenuActions();
        SyncDesign();
        ScheduleExternalOpen();
    }
    private void UpdateLogoInfo()
    {
        var source = SelectedScreen.Style.Logo.SourcePath;
        _logoInfo.Text = source.Length == 0 ? L.T("ui096") : SelectedScreen.Style.Logo.EmbeddedImage is { } embedded && _logoImages.TryGetValue(source, out var stored)
            ? L.F("ui097", embedded.FileName, stored.Width, stored.Height) : _logoImages.TryGetValue(source, out var image)
            ? $"{Path.GetFileName(source)} · {image.Width} × {image.Height} px"
            : L.F("ui098", Path.GetFileName(source), (_logoProblems.TryGetValue(source, out var reason) ? reason : ""));
    }
    private void SetStatus(string text) => _status.Text = L.Error(text);
    private void SetBusy(bool value) { EndEditGroup(); _busy = value; UpdateActions(); }
    private void Undo()
    {
        if (_busy) return;
        EndEditGroup();
        if (HasDraft) { LoadControls(); SetStatus(L.T("ui099")); return; }
        ChangeHistory(undo: true);
    }
    private void Redo()
    {
        if (_busy || HasDraft) return;
        EndEditGroup();
        ChangeHistory(undo: false);
    }
    private void EnsureScreenSelection()
    {
        var wasEmpty = _screenId == Guid.Empty;
        if (!_document.Project.Screens.Any(s => s.Id == _screenId)) _screenId = (_document.Project.Screens.FirstOrDefault(s => s.Id == _modeSelections.GetValueOrDefault(_document.Project.Mode)) ?? _document.Project.Screens.FirstOrDefault())?.Id ?? Guid.Empty;
        if (!HasScreens) _objectsSelected = false; else if (wasEmpty) _objectsSelected = true;
    }
    private void SelectScreen()
    {
        if (_syncingControls || _busy || _screens.SelectedIndex < 0) return;
        var next = _document.Project.Screens[_screens.SelectedIndex].Id;
        if (next == _screenId && _objectsSelected) return;
        if (HasDraft && !ApplyAutomatic())
        {
            _syncingControls = true;
            _screens.SelectedIndex = _objectsSelected ? _document.Project.Screens.ToList().FindIndex(x => x.Id == _screenId) : -1;
            _syncingControls = false; return;
        }
        _objectsSelected = true; _screenId = next; LoadControls();
        if (IsCanvas) { UpdatePreviewSummary(); UpdateScreenOverlay(); } else RefreshPreview();
        SetStatus(L.T("ui008"));
    }

    internal async Task<bool> SaveProjectAsync(bool saveAs)
    {
        if (_busy || HasPointerEdit) return false;
        SetBusy(true);
        try { return await SaveCoreAsync(saveAs); }
        catch (Exception error) when (error is RenderLimitException or OutOfMemoryException) { ResourceFailure(error); return false; }
        catch (Exception error) when (IsFileError(error)) { SetStatus(L.F("ui102", error.Message)); return false; }
        finally { SetBusy(false); }
    }
    internal async Task<bool> ImportLogoAsync()
    {
        if (_busy || HasPointerEdit) return false;
        SetBusy(true);
        try
        {
            ApplyAutomatic();
            var path = await _dialogs.PickLogoAsync(this); if (path is null) return false;
            path = Path.GetFullPath(path);
            var (image, asset) = await LogoImages.ImportAsync(path);
            if (SelectedScreen.Style.Logo.EmbeddedImage is { } existing && existing.Key == asset.Key) asset = existing;
            if (_logoImages.TryGetValue(asset.Key, out var cached)) image = cached;
            var total = _logoImages.Where(x => x.Key != asset.Key).Sum(x => (long)x.Value.ByteCount) + image.ByteCount;
            if (total > LogoImages.MaxCacheBytes) throw new IOException(L.T("ui103"));
            var images = new Dictionary<string, LogoImage>(_logoImages) { [asset.Key] = image };
            var previous = SelectedScreen.Style.Logo;
            var logo = new ScreenLogo(true, asset.Key, previous.Anchor, previous.OffsetX, previous.OffsetY, Math.Clamp(previous.ScalePercent, 70, 130), previous.OpacityPercent, asset, previous.Background);
            ApplyLogoReference(logo, images); _logoProblems.Remove(asset.Key);
            SetStatus(L.F("ui104", Path.GetFileName(path))); return true;
        }
        catch (Exception error) when (error is RenderLimitException or OutOfMemoryException) { ResourceFailure(error); return false; }
        catch (Exception error) when (IsFileError(error)) { SetStatus(L.F("ui105", error.Message)); ShowNotice(L.F("ui105", error.Message)); return false; }
        finally { SetBusy(false); }
    }
    internal Task<bool> RemoveLogoAsync()
    {
        if (_busy || HasPointerEdit) return Task.FromResult(false);
        EndEditGroup(); ApplyAutomatic();
        var previous = SelectedScreen.Style.Logo;
        ApplyLogoReference(new(false, "", previous.Anchor, previous.OffsetX, previous.OffsetY, previous.ScalePercent, previous.OpacityPercent, backgroundOverride: previous.Background), _logoImages);
        SetStatus(L.T("ui106")); return Task.FromResult(true);
    }
    private void ApplyLogoReference(ScreenLogo logo, Dictionary<string, LogoImage> images)
    {
        var next = new EditScreenCommand(_screenId, SelectedScreen.Name, SelectedScreen.Style.WithLogo(logo), null).Apply(_document.Project);
        var rendered = Render(next, next.Screens.First(x => x.Id == _screenId), images);
        if (!ReferenceEquals(next, _document.Project)) _document.Execute(new PreparedEdit(next));
        _logoImages = images; UsePreview(rendered);
        _syncingControls = true;
        try { _logo.IsChecked = logo.Enabled; _loadedStyle = ReadStyle(); }
        finally { _syncingControls = false; }
        UpdateActions();
    }
    private async Task<bool> SaveCoreAsync(bool saveAs)
    {
        if (!ApplyAutomatic()) return false;
        var path = !saveAs && _filePath is not null ? _filePath
            : await _dialogs.PickSaveAsync(this, EditorFileKind.Project, ExportFileNames.Project(_filePath));
        if (path is null) return false;
        var checkpoint = _document.Capture();
        var currentPath = _filePath;
        // Filesystem capability queries can block like file-open; preserve the
        // responsive UI and prepare the immutable snapshot on the worker too.
        var portable = await LogoImages.EmbedProjectAsync(checkpoint.Project, _logoImages, currentPath);
        var stored = await Task.Run(() => LogoImages.PrepareProjectForSave(portable, path, currentPath));
        await ProjectFiles.SaveAsync(path, stored);
        _filePath = path; _document.MarkSaved(checkpoint); UpdateActions();
        var unresolved = stored.Screens.Any(s => s.Style.Logo.SourcePath.Length > 0 && s.Style.Logo.EmbeddedImage is null);
        SetStatus(L.F("ui107", Path.GetFileName(path)) + (unresolved ? L.T("ui108") : "")); return true;
    }
    internal Task<bool> OpenProjectAsync() => OpenProjectAsync(null);

    internal async Task<bool> OpenProjectAsync(string? suppliedPath)
    {
        if (_busy || HasPointerEdit) return false;
        SetBusy(true);
        try
        {
            var path = suppliedPath ?? await _dialogs.PickOpenAsync(this); if (path is null) return false;
            path = Path.GetFullPath(path);
            var project = await ProjectFiles.LoadAsync(path);
            if (!await CanReplaceAsync()) return false;
            // Saving the current document may have overwritten the selected path.
            project = await ProjectFiles.LoadAsync(path);
            project = LogoImages.ResolveProjectPaths(project, path);
            var logos = await LogoImages.LoadProjectAsync(project, path);
            var maskPreview = await ReadImageMask(project.Canvas.ImageMask);
            await StopAllNdiAsync();
            ReplaceDocument(project, path, logos, maskPreview);
            SetStatus(logos.Problems.Count == 0 ? L.F("ui109", Path.GetFileName(path)) : L.F("ui110", logos.Problems.Count)); return true;
        }
        catch (Exception error) when (error is RenderLimitException or OutOfMemoryException) { ResourceFailure(error); return false; }
        catch (Exception error) when (IsFileError(error)) { SetStatus(L.F("ui021", error.Message)); return false; }
        finally { SetBusy(false); }
    }
    internal async Task<bool> NewProjectAsync()
    {
        if (_busy || HasPointerEdit) return false;
        SetBusy(true);
        try
        {
            if (!await CanReplaceAsync()) return false;
            await StopAllNdiAsync();
            ReplaceDocument(ProjectDefaults.Empty(), null); SetStatus(L.T("ui111")); return true;
        }
        catch (Exception error) when (error is RenderLimitException or OutOfMemoryException) { ResourceFailure(error); return false; }
        catch (Exception error) when (IsFileError(error)) { SetStatus(L.F("ui112", error.Message)); return false; }
        finally { SetBusy(false); }
    }
    private async Task<bool> CanReplaceAsync()
    {
        if (!_document.IsDirty && !HasDraft) return true;
        return await _dialogs.ConfirmUnsavedAsync(this) switch
        {
            UnsavedDecision.Discard => true,
            UnsavedDecision.Save => await SaveCoreAsync(false),
            _ => false
        };
    }
    private void ReplaceDocument(ProjectModel project, string? path, LoadedLogoImages? logos = null, CanvasMaskPreview? maskPreview = null)
    {
        var images = logos?.Images ?? new Dictionary<string, LogoImage>();
        var rendered = Render(project, project.Screens.FirstOrDefault() ?? EmptyEditorScreen, images);
        InstallImageMask(maskPreview, project.Canvas.ImageMask); _imageMaskSelected = false;
        _modeSelections.Clear();
        _document.Reset(project); _objectsSelected = project.Screens.Count > 0; _screenId = project.Screens.FirstOrDefault()?.Id ?? Guid.Empty; _filePath = path;
        _logoImages = new(images); _logoProblems = new(logos?.Problems ?? new Dictionary<string, string>());
        _canvasViewInitialized = false; _firstScreenCreated = project.Screens.Count > 0;
        LoadControls(); UsePreview(rendered);
    }
    internal Task<bool> ExportAsync() => ExportSceneAsync(IsCanvas);

    internal async Task<bool> ExportSceneAsync(bool canvas)
    {
        if (!HasScreens || _busy || HasPointerEdit || IsProjection && canvas) return false;
        SetBusy(true);
        try
        {
            if (!ApplyAutomatic()) return false;
            var exportScreens = canvas ? _document.Project.Screens.Where(s => !_document.Project.Canvas.Placements.Single(p => p.ScreenId == s.Id).IsHidden) : new[] { SelectedScreen };
            if (!IsProjection && exportScreens.Any(s => s.Style.Logo.Enabled && s.Style.Logo.SourcePath.Length > 0 && !_logoImages.ContainsKey(s.Style.Logo.SourcePath)))
                throw new IOException(L.T("ui113"));
            var project = _document.Project; var screen = SelectedScreen;
            var width = IsProjection ? Projection.WidthPx : canvas ? project.Canvas.WidthPx : screen.Geometry.Bounds.Width;
            var height = IsProjection ? Projection.HeightPx : canvas ? project.Canvas.HeightPx : screen.Geometry.Bounds.Height;
            var name = canvas ? Path.GetFileNameWithoutExtension(_filePath ?? "project.gpm") : screen.Name;
            var path = await _dialogs.PickSaveAsync(this, EditorFileKind.Png, ExportFileNames.Png(canvas, name, width, height));
            if (path is null) return false;
            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var progressWindow = new ExportProgress(this, cancel);
            try
            {
                var logos = new Dictionary<string, LogoImage>(_logoImages);
                if ((long)width * height > ModelLimits.MaxPixelCount)
                {
                    var scene = canvas ? SceneBuilder.ForCanvas(PresentationProject(project))
                        : SceneBuilder.ForScreen(PresentationScreen(screen), project.Canvas.Placements.Single(p => p.ScreenId == screen.Id));
                    await AtomicFiles.WriteStreamAsync(path, (output, token) => SceneRenderer.WritePng(output, scene, logos, token, progressWindow.Progress), cancel.Token);
                }
                else
                {
                    var png = _png;
                    if (canvas || canvas != IsCanvas || png.Length == 0)
                        png = await Task.Run(() => RenderExport(project, screen, canvas, logos, cancel.Token), cancel.Token);
                    await AtomicFiles.WriteAsync(path, png, cancel.Token);
                }
            }
            finally { progressWindow.Finish(); }
            SetStatus(L.F("ui114", Path.GetFileName(path), width, height)); return true;
        }
        catch (OperationCanceledException) { SetStatus(L.T("export.cancelled")); return false; }
        catch (Exception error) when (error is RenderLimitException or OutOfMemoryException)
        { ResourceFailure(error); return false; }
        catch (Exception error) when (IsFileError(error)) { SetStatus(L.F("ui115", error.Message)); ShowNotice(L.F("ui115", error.Message)); return false; }
        finally { SetBusy(false); }
    }
    internal static byte[] RenderExport(ProjectModel project, ScreenModel screen, bool canvas, IReadOnlyDictionary<string, LogoImage> logos, CancellationToken cancellationToken = default)
    {
        var scene = project.Mode==ProjectMode.Projection ? SceneBuilder.ForProjection(screen.Projection??ProjectionSettings.Default) : canvas ? SceneBuilder.ForCanvas(PresentationProject(project))
            : SceneBuilder.ForScreen(PresentationScreen(screen), project.Canvas.Placements.Single(p => p.ScreenId == screen.Id));
        return SceneRenderer.RenderPng(scene, logos, cancellationToken: cancellationToken);
    }
    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    private async void HandleClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        if (_busy || HasPointerEdit) { e.Cancel = true; return; }
        if (!_document.IsDirty && !HasDraft && _ndiOutputs.Count == 0 && _ndiRetiring.All(t => t.IsCompleted)) return;
        e.Cancel = true; SetBusy(true);
        try
        {
            if (await CanReplaceAsync()) { await StopAllNdiAsync(); _allowClose = true; Close(); }
        }
        catch (Exception error) when (IsFileError(error)) { SetStatus(L.F("ui102", error.Message)); }
        finally { SetBusy(false); }
    }
    private async void HandleKeys(object? sender, KeyEventArgs e)
    {
        if (_busy) return;
        if (HandleLayerKey(e) || HandleNavigationKey(e) || HandleScreenKey(e) || HandleCabinetKey(e)) return;
        var command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var textBox = e.Source as TextBox ?? (e.Source as Visual)?.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
        var textSource = textBox is not null || e.Source is SelectableTextBlock
            || (e.Source as Visual)?.GetVisualAncestors().OfType<SelectableTextBlock>().Any() == true;
        if (command && !textSource && (IsPreviewInput(e.Source) || IsLayerInput(e.Source)) && e.Key is Key.C or Key.V)
        {
            e.Handled = true;
            if (e.Key == Key.C) await CopyScreenAsync(); else await PasteScreenAsync();
            return;
        }
        if (!command)
        {
            if (e.Key == Key.Tab && textSource && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
            {
                CommitNumericField(textBox);
                if (textBox is not null && MoveLayerFocus(textBox, e.KeyModifiers == KeyModifiers.Shift)) e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && textSource && textBox != _customContent && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                CommitNumericField(textBox);
            }
            return;
        }
        if (e.Key is Key.Z or Key.Y && textSource) return;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.S: await SaveProjectAsync((e.KeyModifiers & KeyModifiers.Shift) != 0); break;
            case Key.O: await OpenProjectAsync(); break;
            case Key.N: await NewProjectAsync(); break;
            case Key.E: await ExportAsync(); break;
            case Key.W: Close(); break;
            case Key.Z: if ((e.KeyModifiers & KeyModifiers.Shift) != 0) Redo(); else Undo(); break;
            case Key.Y: Redo(); break;
            default: e.Handled = false; break;
        }
    }
}
