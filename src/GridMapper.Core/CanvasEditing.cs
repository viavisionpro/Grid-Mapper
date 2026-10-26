namespace GridMapper.Core;

public static class CanvasEditing
{
    public sealed record SnapGuide(bool Vertical, double Position, double From, double To);
    public sealed record SnapResult(int X, int Y, IReadOnlyList<SnapGuide> Guides);
    private sealed record AnchorMatch(double Anchor, double CrossStart, double CrossSize);

    public static (int X, int Y) SnapEdges(ProjectModel project, Guid screenId, int x, int y, double tolerance)
    {
        var result = SnapWithGuides(project, screenId, x, y, tolerance);
        return (result.X, result.Y);
    }
    // Each axis independently matches edges and midpoints. Integer coordinates remain exact.
    // The editor converts a fixed cursor distance to scene pixels through the current zoom.
    public static SnapResult SnapWithGuides(ProjectModel project, Guid screenId, int x, int y, double tolerance)
    {
        if (!project.Canvas.Snap.Enabled || !double.IsFinite(tolerance) || tolerance <= 0) return new(x, y, []);
        var screens = project.Screens.ToDictionary(s => s.Id);
        var moving = screens[screenId].Geometry.Bounds;
        var bestX = tolerance + 1; var bestY = tolerance + 1;
        var snappedX = x; var snappedY = y;
        AnchorMatch? matchX = null, matchY = null;
        void Match(double start, double size, double crossStart, double crossSize, int proposed, int movingSize,
            ref double best, ref int snapped, ref AnchorMatch? match)
        {
            // Edges win exact ties; midpoint alignment is available in addition to them.
            foreach (var anchor in new[] { start, start + size, start + size / 2 })
                foreach (var offset in new[] { 0d, (double)movingSize, movingSize / 2d })
                {
                    var candidate = Math.Round(anchor - offset, MidpointRounding.AwayFromZero);
                    if (candidate < int.MinValue || candidate > int.MaxValue) continue;
                    var distance = Math.Abs(candidate - proposed);
                    if (distance <= tolerance && distance < best)
                    { best = distance; snapped = (int)candidate; match = new(anchor, crossStart, crossSize); }
                }
        }
        void Target(double left, double top, double width, double height)
        {
            Match(left, width, top, height, x, moving.Width, ref bestX, ref snappedX, ref matchX);
            Match(top, height, left, width, y, moving.Height, ref bestY, ref snappedY, ref matchY);
        }
        foreach (var placement in project.Canvas.Placements.Where(p => p.ScreenId != screenId && !p.IsHidden))
        {
            var bounds = screens[placement.ScreenId].Geometry.Bounds;
            Target(placement.X, placement.Y, bounds.Width, bounds.Height);
        }
        Target(0, 0, project.Canvas.WidthPx, project.Canvas.HeightPx);
        var guides = new List<SnapGuide>(2);
        if (matchX is { } vertical) guides.Add(new(true, vertical.Anchor, Math.Min(snappedY, vertical.CrossStart),
            Math.Max((double)snappedY + moving.Height, vertical.CrossStart + vertical.CrossSize)));
        if (matchY is { } horizontal) guides.Add(new(false, horizontal.Anchor, Math.Min(snappedX, horizontal.CrossStart),
            Math.Max((double)snappedX + moving.Width, horizontal.CrossStart + horizontal.CrossSize)));
        return new(snappedX, snappedY, guides.AsReadOnly());
    }
    public static ProjectModel SetSnap(ProjectModel project, CanvasSnap snap) => project.Canvas.Snap == snap ? project
        : new(project.FormatVersion, project.Screens, new(project.Canvas.WidthPx, project.Canvas.HeightPx, project.Canvas.Placements, snap, project.Canvas.TransparentBackground, project.Canvas.ImageMask), project.Mode, project.InactiveLayout);
    public static ProjectModel SetTransparency(ProjectModel project, bool transparent) => project.Canvas.TransparentBackground == transparent ? project
        : new(project.FormatVersion, project.Screens, new(project.Canvas.WidthPx, project.Canvas.HeightPx,
            project.Canvas.Placements, project.Canvas.Snap, transparent, project.Canvas.ImageMask), project.Mode, project.InactiveLayout);
    public static ProjectModel SetImageMask(ProjectModel project, CanvasImageMask? mask) => project.Canvas.ImageMask == mask ? project
        : new(project.FormatVersion, project.Screens, new(project.Canvas.WidthPx, project.Canvas.HeightPx,
            project.Canvas.Placements, project.Canvas.Snap, project.Canvas.TransparentBackground, mask), project.Mode, project.InactiveLayout);
    public static Guid? HitTest(ProjectModel project, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= project.Canvas.WidthPx || y >= project.Canvas.HeightPx) return null;
        return HitTestWorkspace(project, x, y);
    }
    public static Guid? HitTestWorkspace(ProjectModel project, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
        var screens = project.Screens.ToDictionary(s => s.Id);
        return project.Canvas.Placements.LastOrDefault(p => !p.IsHidden && x >= p.X && y >= p.Y
            && x < (long)p.X + screens[p.ScreenId].Geometry.Bounds.Width && y < (long)p.Y + screens[p.ScreenId].Geometry.Bounds.Height)?.ScreenId;
    }
    public static ProjectModel Resize(ProjectModel project, int width, int height) => width == project.Canvas.WidthPx && height == project.Canvas.HeightPx
        ? project : new(project.FormatVersion, project.Screens, new(width, height, project.Canvas.Placements, project.Canvas.Snap, project.Canvas.TransparentBackground, project.Canvas.ImageMask), project.Mode, project.InactiveLayout);
    public static ProjectModel Place(ProjectModel project, Guid screenId, int x, int y, bool locked, bool hidden)
    {
        var old = project.Canvas.Placements.FirstOrDefault(p => p.ScreenId == screenId) ?? throw new ArgumentException("Экран отсутствует.");
        if (old.IsLocked && (old.X != x || old.Y != y)) throw new ArgumentException("Положение экрана заблокировано. Сначала снимите блокировку.");
        if (old.X == x && old.Y == y && old.IsLocked == locked && old.IsHidden == hidden) return project;
        var next = new ScreenPlacement(old.Id, screenId, x, y, locked, hidden);
        return new(project.FormatVersion, project.Screens, new(project.Canvas.WidthPx, project.Canvas.HeightPx,
            project.Canvas.Placements.Select(p => p.ScreenId == screenId ? next : p).ToArray(), project.Canvas.Snap, project.Canvas.TransparentBackground, project.Canvas.ImageMask), project.Mode, project.InactiveLayout);
    }
    public static ProjectModel Add(ProjectModel project, ScreenModel screen, int x = 0, int y = 0) => new(project.FormatVersion,
        project.Screens.Append(screen).ToArray(), new(project.Canvas.WidthPx, project.Canvas.HeightPx,
            project.Canvas.Placements.Append(new(Guid.NewGuid(), screen.Id, x, y, false, false)).ToArray(), project.Canvas.Snap, project.Canvas.TransparentBackground, project.Canvas.ImageMask), project.Mode, project.InactiveLayout);
    public static ProjectModel Remove(ProjectModel project, Guid screenId)
    {
        if (!project.Screens.Any(s => s.Id == screenId)) throw new ArgumentException("Экран отсутствует.");
        return new(project.FormatVersion, project.Screens.Where(s => s.Id != screenId).ToArray(), new(project.Canvas.WidthPx, project.Canvas.HeightPx,
            project.Canvas.Placements.Where(p => p.ScreenId != screenId).ToArray(), project.Canvas.Snap, project.Canvas.TransparentBackground, project.Canvas.ImageMask), project.Mode, project.InactiveLayout);
    }
    public static ScreenModel Copy(ScreenModel screen, string name)
    {
        var types = screen.CabinetTypes.Select(t => new CabinetType(Guid.NewGuid(), t.Name, t.WidthMm, t.HeightMm, t.WidthPx, t.HeightPx)).ToArray();
        var ids = screen.CabinetTypes.Zip(types).ToDictionary(p => p.First.Id, p => p.Second.Id);
        return new(Guid.NewGuid(), name, types, screen.Cabinets.Select(c => new CabinetInstance(Guid.NewGuid(), ids[c.TypeId], c.X, c.Y, c.IsMasked)).ToArray(), screen.Style, screen.Projection);
    }
    public static ProjectModel Reorder(ProjectModel project, Guid screenId, int direction)
    {
        if (direction is not -1 and not 1) throw new ArgumentOutOfRangeException(nameof(direction));
        var list = project.Canvas.Placements.ToList(); var index = list.FindIndex(p => p.ScreenId == screenId);
        if (index < 0) throw new ArgumentException("Экран отсутствует.");
        var target = index + direction; if (target < 0 || target >= list.Count) return project;
        return ReorderTo(project, screenId, target);
    }
    public static ProjectModel ReorderTo(ProjectModel project, Guid screenId, int target)
    {
        var list = project.Canvas.Placements.ToList(); var index = list.FindIndex(p => p.ScreenId == screenId);
        if (index < 0) throw new ArgumentException("Экран отсутствует.");
        if (target < 0 || target >= list.Count) throw new ArgumentOutOfRangeException(nameof(target));
        if (target == index) return project;
        var placement = list[index]; list.RemoveAt(index); list.Insert(target, placement);
        return new(project.FormatVersion, project.Screens, new(project.Canvas.WidthPx, project.Canvas.HeightPx, list, project.Canvas.Snap, project.Canvas.TransparentBackground, project.Canvas.ImageMask), project.Mode, project.InactiveLayout);
    }
}
public sealed record ReplaceCanvasProjectCommand(ProjectModel Next) : IProjectCommand
{
    public string Description => "Изменить Canvas";
    public ProjectModel Apply(ProjectModel project) => Next;
}
