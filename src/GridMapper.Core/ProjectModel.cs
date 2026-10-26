using System.Text.Json.Serialization;

namespace GridMapper.Core;

public sealed class ScreenPlacement
{
    public Guid Id { get; }
    public Guid ScreenId { get; }
    public int X { get; }
    public int Y { get; }
    public bool IsLocked { get; }
    public bool IsHidden { get; }

    public ScreenPlacement(Guid id, Guid screenId, int x, int y, bool isLocked, bool isHidden)
    {
        ModelLimits.ValidateId(id);
        ModelLimits.ValidateId(screenId);
        Id = id;
        ScreenId = screenId;
        X = x;
        Y = y;
        IsLocked = isLocked;
        IsHidden = isHidden;
    }
}

public sealed class CanvasModel
{
    public int WidthPx { get; }
    public int HeightPx { get; }
    public CanvasSnap Snap { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CanvasImageMask? ImageMask { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TransparentBackground { get; }
    // Array order is paint order: later placements appear on top.
    public IReadOnlyList<ScreenPlacement> Placements { get; }

    public CanvasModel(int widthPx, int heightPx, IReadOnlyList<ScreenPlacement> placements, CanvasSnap? snap = null, bool transparentBackground = false, CanvasImageMask? imageMask = null)
    {
        ModelLimits.ValidateCanvas(widthPx, heightPx);
        ArgumentNullException.ThrowIfNull(placements);
        ModelLimits.Require(placements.Count <= ModelLimits.MaxScreens, ModelError.TooManyItems, "Слишком много размещений экранов.");
        var ids = new HashSet<Guid>();
        foreach (var placement in placements)
        {
            ArgumentNullException.ThrowIfNull(placement);
            ModelLimits.Require(ids.Add(placement.Id), ModelError.DuplicateId, "ID размещений должны быть уникальными.");
        }
        Snap = snap ?? new();
        ImageMask = imageMask;
        TransparentBackground = transparentBackground;
        WidthPx = widthPx;
        HeightPx = heightPx;
        Placements = Array.AsReadOnly(placements.ToArray());
    }
}

public enum LayoutWarningKind { OutsideCanvas, ScreenOverlap }
public sealed record LayoutWarning(LayoutWarningKind Kind, Guid PlacementId, Guid? OtherPlacementId = null);

public sealed class ModeLayout
{
    public IReadOnlyList<ScreenModel> Screens { get; }
    public CanvasModel Canvas { get; }

    public ModeLayout(IReadOnlyList<ScreenModel> screens, CanvasModel canvas)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(canvas);
        ModelLimits.Require(screens.Count <= ModelLimits.MaxScreens,
            ModelError.TooManyItems, "Проект должен содержать до 1 000 экранов.");
        var byId = new Dictionary<Guid, ScreenModel>();
        var cabinetIds = new HashSet<Guid>();
        var typeCount = 0;
        foreach (var screen in screens)
        {
            ArgumentNullException.ThrowIfNull(screen);
            typeCount += screen.CabinetTypes.Count;
            ModelLimits.Require(typeCount <= ModelLimits.MaxCabinets, ModelError.TooManyItems, "В проекте допускается до 10 000 типов кабинетов.");
            ModelLimits.Require(byId.TryAdd(screen.Id, screen), ModelError.DuplicateId, "ID экранов должны быть уникальными.");
            foreach (var cabinet in screen.Cabinets)
                ModelLimits.Require(cabinetIds.Add(cabinet.Id), ModelError.DuplicateId, "ID кабинетов должны быть уникальными во всём проекте.");
        }
        ModelLimits.Require(cabinetIds.Count <= ModelLimits.MaxCabinets, ModelError.TooManyItems, "В проекте допускается до 10 000 кабинетов.");
        var placed = new HashSet<Guid>();
        foreach (var placement in canvas.Placements)
        {
            ModelLimits.Require(byId.ContainsKey(placement.ScreenId), ModelError.MissingScreen, "Размещение ссылается на отсутствующий экран.");
            ModelLimits.Require(placed.Add(placement.ScreenId), ModelError.DuplicatePlacement, "Экран должен иметь ровно одно размещение на canvas.");
        }
        ModelLimits.Require(placed.Count == screens.Count, ModelError.MissingPlacement, "Для каждого экрана требуется размещение на canvas.");
        Screens = Array.AsReadOnly(screens.ToArray());
        Canvas = canvas;
    }
}

public sealed class ProjectModel
{
    public const int CurrentFormatVersion = 1;
    public int FormatVersion { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public ProjectMode Mode { get; }
    public IReadOnlyList<ScreenModel> Screens { get; }
    public CanvasModel Canvas { get; }
    [JsonIgnore] public IReadOnlyList<LayoutWarning> LayoutWarnings { get; }

    // The explicit null marker distinguishes newly saved one-bank projects from legacy shared layouts.
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public ModeLayout? InactiveLayout { get; }
    [JsonIgnore] public IEnumerable<ScreenModel> AllScreens => Screens.Concat(InactiveLayout?.Screens ?? []);

    public ProjectModel(int formatVersion, IReadOnlyList<ScreenModel> screens, CanvasModel canvas,
        ProjectMode mode = ProjectMode.Led, ModeLayout? inactiveLayout = null)
    {
        ModelLimits.Require(formatVersion == CurrentFormatVersion, ModelError.UnsupportedProjectVersion, "Неподдерживаемая версия формата проекта.");
        ModelLimits.Require(Enum.IsDefined(mode), ModelError.InvalidPattern, "Invalid project mode.");
        var active = new ModeLayout(screens, canvas);
        var allScreens = active.Screens.Concat(inactiveLayout?.Screens ?? []).ToArray();
        ModelLimits.Require(allScreens.Select(s => s.Id).Distinct().Count() == allScreens.Length
            && allScreens.SelectMany(s => s.Cabinets).Select(c => c.Id).Distinct().Count() == allScreens.Sum(s => s.Cabinets.Count),
            ModelError.DuplicateId, "ID экранов и кабинетов должны быть уникальными во всём проекте.");
        ModelLimits.Require(!active.Screens.SelectMany(s => s.CabinetTypes).Select(t => t.Id)
                .Intersect((inactiveLayout?.Screens ?? []).SelectMany(s => s.CabinetTypes).Select(t => t.Id)).Any(),
            ModelError.DuplicateId, "ID типов кабинетов должны быть независимыми между режимами.");
        var placements = canvas.Placements.Concat(inactiveLayout?.Canvas.Placements ?? []).ToArray();
        ModelLimits.Require(placements.Select(p => p.Id).Distinct().Count() == placements.Length,
            ModelError.DuplicateId, "ID размещений должны быть уникальными во всём проекте.");
        Mode = mode; FormatVersion = formatVersion;
        Screens = active.Screens;
        Canvas = canvas;
        InactiveLayout = inactiveLayout;
        LayoutWarnings = GetWarnings(Screens.ToDictionary(s => s.Id), canvas);
    }

    public ProjectModel With(IReadOnlyList<ScreenModel>? screens = null, CanvasModel? canvas = null, ProjectMode? mode = null)
    {
        if (mode is { } nextMode && nextMode != Mode && screens is null && canvas is null)
        {
            var next = InactiveLayout ?? new ModeLayout([], new(3840, 2160, []));
            return new(FormatVersion, next.Screens, next.Canvas, nextMode, new(Screens, Canvas));
        }
        return new(FormatVersion, screens ?? Screens, canvas ?? Canvas, mode ?? Mode, InactiveLayout);
    }

    private static IReadOnlyList<LayoutWarning> GetWarnings(Dictionary<Guid, ScreenModel> screens, CanvasModel canvas)
    {
        var warnings = new List<LayoutWarning>();
        var visible = canvas.Placements.Where(x => !x.IsHidden).ToArray();
        for (var i = 0; i < visible.Length; i++)
        {
            var a = visible[i];
            var aSize = screens[a.ScreenId].Geometry.Bounds;
            var right = (long)a.X + aSize.Width;
            var bottom = (long)a.Y + aSize.Height;
            if (a.X < 0 || a.Y < 0 || right > canvas.WidthPx || bottom > canvas.HeightPx)
                warnings.Add(new(LayoutWarningKind.OutsideCanvas, a.Id));
            for (var j = i + 1; j < visible.Length; j++)
            {
                var b = visible[j];
                var bSize = screens[b.ScreenId].Geometry.Bounds;
                if (a.X < (long)b.X + bSize.Width && b.X < right
                    && a.Y < (long)b.Y + bSize.Height && b.Y < bottom)
                    warnings.Add(new(LayoutWarningKind.ScreenOverlap, a.Id, b.Id));
            }
        }
        return warnings.AsReadOnly();
    }
}
