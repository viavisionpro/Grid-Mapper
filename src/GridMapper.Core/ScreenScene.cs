namespace GridMapper.Core;

// Prepared geometry is clipped to the output raster; editor state is not included.
public sealed record SceneCabinet(Guid Id, int X, int Y, int Width, int Height, RgbaColor Color, bool IsMasked = false);
public sealed record SceneCabinetLabel(Guid CabinetId, string Text, PixelBounds Bounds);

public sealed record SceneScreenDetails(string Name, string PhysicalSize, int PanelWidth, int PanelHeight, int CabinetCount = 0);

public sealed class SceneLayer
{
    public SceneLayer? UnclippedSource { get; }
    public SceneScreenDetails? Details { get; }
    public PixelBounds Bounds { get; }
    public IReadOnlyList<SceneCabinet> Cabinets { get; }
    public bool ShowResolutionLabel { get; }
    public ScreenDecorations Decorations { get; }
    public CabinetNumbering Numbering { get; }
    public IReadOnlyList<SceneCabinetLabel> CabinetLabels { get; }
    public ScreenInformation Information { get; }
    public IReadOnlyList<string> InformationLines { get; }
    public ScreenText Text { get; }
    public ScreenLogo Logo { get; }

    public SceneLayer(PixelBounds bounds, IReadOnlyList<SceneCabinet> cabinets, bool showResolutionLabel,
        ScreenDecorations? decorations = null, CabinetNumbering? numbering = null, IReadOnlyList<SceneCabinetLabel>? cabinetLabels = null,
        ScreenInformation? information = null, IReadOnlyList<string>? informationLines = null, ScreenText? text = null, ScreenLogo? logo = null, SceneScreenDetails? details = null, SceneLayer? unclippedSource = null)
    {
        ModelLimits.ValidateRaster(bounds.Width, bounds.Height);
        ArgumentNullException.ThrowIfNull(cabinets);
        UnclippedSource = unclippedSource; Details = details; Bounds = bounds;
        Cabinets = Array.AsReadOnly(cabinets.ToArray());
        ShowResolutionLabel = showResolutionLabel;
        Decorations = decorations ?? new();
        Numbering = numbering ?? new();
        CabinetLabels = Array.AsReadOnly((cabinetLabels ?? []).ToArray());
        Information = information ?? new();
        InformationLines = Array.AsReadOnly((informationLines ?? []).ToArray());
        Text = text ?? new();
        Logo = logo ?? new();
        ModelLimits.Require(InformationLines.Count <= 8 && InformationLines.All(x => x is not null && x.Length <= 256 && !x.Any(char.IsControl)),
            ModelError.InvalidInformation, "Недопустимый текст инфоблока.");
        foreach (var label in CabinetLabels)
        {
            ArgumentNullException.ThrowIfNull(label);
            ModelLimits.ValidateRaster(label.Bounds.Width, label.Bounds.Height);
            ModelLimits.Require(label.Text is not null && label.Text.Length <= 64 && !label.Text.Any(char.IsControl),
                ModelError.InvalidNumbering, "Недопустимая подпись кабинета.");
        }
    }
}

public sealed class ScreenScene
{
    public ProjectionSettings? Projection { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<SceneLayer> Layers { get; }
    public bool OpaqueBackground { get; }
    public bool TransparentMaskedPanels { get; }

    public ScreenScene(int width, int height, IReadOnlyList<SceneLayer> layers, bool opaqueBackground = false, bool transparentMaskedPanels = false, bool virtualCanvas = false, ProjectionSettings? projection = null)
    {
        if (virtualCanvas) ModelLimits.ValidateCanvas(width, height);
        else ModelLimits.ValidateRaster(width, height);
        ArgumentNullException.ThrowIfNull(layers);
        ModelLimits.Require(layers.Count <= ModelLimits.MaxScreens, ModelError.TooManyItems, "Слишком много слоёв в сцене.");
        var count = 0;
        var labelCount = 0;
        foreach (var layer in layers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            count += layer.Cabinets.Count;
            labelCount += layer.CabinetLabels.Count;
            ModelLimits.Require(labelCount <= ModelLimits.MaxCabinets, ModelError.TooManyItems, "Слишком много подписей кабинетов.");
            ModelLimits.Require(count <= ModelLimits.MaxCabinets, ModelError.TooManyItems, "Слишком много кабинетов в сцене.");
            foreach (var item in layer.Cabinets)
            {
                ArgumentNullException.ThrowIfNull(item);
                if (item.X < 0 || item.Y < 0 || item.Width <= 0 || item.Height <= 0
                    || (long)item.X + item.Width > width || (long)item.Y + item.Height > height)
                    throw new ArgumentException("Кабинет выходит за границы растра.", nameof(layers));
            }
        }
        OpaqueBackground = opaqueBackground; TransparentMaskedPanels = transparentMaskedPanels;
        Projection = projection; Width = width;
        Height = height;
        Layers = Array.AsReadOnly(layers.ToArray());
    }
}

public static class SceneBuilder
{
    public static ScreenScene ForProjection(ProjectionSettings settings) => new(settings.WidthPx, settings.HeightPx, [], projection: settings);

    public static ScreenScene ForScreen(ScreenModel screen, ScreenPlacement? placement = null, bool transparentMaskedPanels = false)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var bounds = screen.Geometry.Bounds;
        var types = screen.CabinetTypes.ToDictionary(x => x.Id);
        var usedTypes = screen.Cabinets.Select(x => types[x.TypeId]).ToArray();
        var cellWidth = usedTypes.Min(x => x.WidthPx);
        var cellHeight = usedTypes.Min(x => x.HeightPx);
        var cabinets = screen.Cabinets.Select(cabinet =>
        {
            var type = types[cabinet.TypeId];
            var x = cabinet.X - bounds.X;
            var y = cabinet.Y - bounds.Y;
            var alternate = screen.Style.Pattern == FillPattern.Checkerboard
                && (x / cellWidth + y / cellHeight) % 2 == 1;
            return new SceneCabinet(cabinet.Id, x, y, type.WidthPx, type.HeightPx,
                alternate ? screen.Style.SecondaryColor : screen.Style.PrimaryColor, cabinet.IsMasked);
        }).ToArray();
        var byId = cabinets.ToDictionary(x => x.Id);
        var labels = screen.Style.Numbering.Enabled ? CabinetAddresses.Build(screen).Select(address =>
        {
            var cabinet = byId[address.CabinetId];
            return new SceneCabinetLabel(address.CabinetId, address.Label, new(cabinet.X, cabinet.Y, cabinet.Width, cabinet.Height));
        }).ToArray() : [];
        return new(bounds.Width, bounds.Height,
            [new(new(0, 0, bounds.Width, bounds.Height), cabinets, screen.Style.ShowResolutionLabel, screen.Style.Decorations, screen.Style.Numbering, labels,
                screen.Style.Information, screen.Style.Information.Lines(screen, placement), screen.Style.Text, screen.Style.Logo,
                new(screen.Name, FormattableString.Invariant($"{screen.Geometry.WidthMm:0.###} × {screen.Geometry.HeightMm:0.###} mm"), cellWidth, cellHeight, screen.Geometry.CabinetCount))], transparentMaskedPanels: transparentMaskedPanels);
    }

    public static ScreenScene ForCanvas(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var screens = project.Screens.ToDictionary(x => x.Id);
        var canvas = project.Canvas;
        var layers = new List<SceneLayer>();
        foreach (var placement in canvas.Placements.Where(x => !x.IsHidden))
        {
            var screen = screens[placement.ScreenId];
            var bounds = new PixelBounds(placement.X, placement.Y, screen.Geometry.Bounds.Width, screen.Geometry.Bounds.Height);
            if (bounds.Right <= 0 || bounds.Bottom <= 0 || bounds.X >= canvas.WidthPx || bounds.Y >= canvas.HeightPx)
                continue;
            var cabinets = new List<SceneCabinet>();
            var source = ForScreen(screen, placement).Layers[0];
            foreach (var cabinet in source.Cabinets)
            {
                var x = (long)placement.X + cabinet.X;
                var y = (long)placement.Y + cabinet.Y;
                var left = Math.Max(0, x);
                var top = Math.Max(0, y);
                var right = Math.Min(canvas.WidthPx, x + cabinet.Width);
                var bottom = Math.Min(canvas.HeightPx, y + cabinet.Height);
                if (left < right && top < bottom)
                    cabinets.Add(new(cabinet.Id, (int)left, (int)top, (int)(right - left), (int)(bottom - top), cabinet.Color, cabinet.IsMasked));
            }
            var labels = source.CabinetLabels.Select(label => new SceneCabinetLabel(label.CabinetId, label.Text,
                new(placement.X + label.Bounds.X, placement.Y + label.Bounds.Y, label.Bounds.Width, label.Bounds.Height)))
                .Where(label => label.Bounds.Right > 0 && label.Bounds.Bottom > 0 && label.Bounds.X < canvas.WidthPx && label.Bounds.Y < canvas.HeightPx).ToArray();
            layers.Add(new(bounds, cabinets, screen.Style.ShowResolutionLabel, screen.Style.Decorations, screen.Style.Numbering, labels,
                source.Information, screen.Style.Information.Lines(screen, placement), screen.Style.Text, screen.Style.Logo, source.Details, source));
        }
        return new(canvas.WidthPx, canvas.HeightPx, layers, opaqueBackground: !canvas.TransparentBackground, transparentMaskedPanels: canvas.TransparentBackground, virtualCanvas: true);
    }
}
