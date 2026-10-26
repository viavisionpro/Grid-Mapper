namespace GridMapper.Core;

public static class CabinetEditing
{
    public static ScreenModel Move(ScreenModel screen, Guid cabinetId, int x, int y)
    {
        var cabinet = Find(screen, cabinetId);
        if (cabinet.X == x && cabinet.Y == y) return screen;
        return Rebuild(screen, screen.CabinetTypes, screen.Cabinets.Select(c => c.Id == cabinetId ? new(c.Id, c.TypeId, x, y, c.IsMasked) : c).ToArray());
    }
    public static ScreenModel AssignType(ScreenModel screen, Guid cabinetId, Guid typeId)
    {
        var cabinet = Find(screen, cabinetId);
        if (cabinet.TypeId == typeId) return screen;
        return Rebuild(screen, screen.CabinetTypes, screen.Cabinets.Select(c => c.Id == cabinetId ? new(c.Id, typeId, c.X, c.Y, c.IsMasked) : c).ToArray());
    }
    public static ScreenModel SetMask(ScreenModel screen, IReadOnlySet<Guid> cabinetIds, bool masked) =>
        Rebuild(screen, screen.CabinetTypes, screen.Cabinets.Select(c => cabinetIds.Contains(c.Id)
            ? new CabinetInstance(c.Id, c.TypeId, c.X, c.Y, masked) : c).ToArray());
    public static ScreenModel Add(ScreenModel screen, Guid cabinetId, Guid typeId, int x, int y) =>
        Rebuild(screen, screen.CabinetTypes, screen.Cabinets.Append(new(cabinetId, typeId, x, y)).ToArray());
    public static ScreenModel Remove(ScreenModel screen, Guid cabinetId)
    {
        Find(screen, cabinetId);
        return Rebuild(screen, screen.CabinetTypes, screen.Cabinets.Where(c => c.Id != cabinetId).ToArray());
    }
    public static ScreenModel AddType(ScreenModel screen, CabinetType type) => Rebuild(screen, screen.CabinetTypes.Append(type).ToArray(), screen.Cabinets);
    public static ScreenModel EditType(ScreenModel screen, Guid typeId, string name, int widthPx, int heightPx, decimal? widthMm = null, decimal? heightMm = null)
    {
        var type = screen.CabinetTypes.FirstOrDefault(t => t.Id == typeId) ?? throw new ArgumentException("Тип кабинета отсутствует.");
        if (type.Name == name && type.WidthPx == widthPx && type.HeightPx == heightPx && (widthMm is null || type.WidthMm == widthMm) && (heightMm is null || type.HeightMm == heightMm)) return screen;
        // Explicit mm values are validated against the catalog. Omitting them preserves pitch for programmatic edits.
        var next = new CabinetType(type.Id, name, widthMm ?? type.PitchX * widthPx, heightMm ?? type.PitchY * heightPx, widthPx, heightPx);
        return Rebuild(screen, screen.CabinetTypes.Select(t => t.Id == typeId ? next : t).ToArray(), screen.Cabinets);
    }
    public static Guid? HitTest(ScreenModel screen, double localX, double localY)
    {
        var x = localX + screen.Geometry.Bounds.X; var y = localY + screen.Geometry.Bounds.Y;
        var types = screen.CabinetTypes.ToDictionary(t => t.Id);
        return screen.Cabinets.LastOrDefault(c => x >= c.X && y >= c.Y && x < (long)c.X + types[c.TypeId].WidthPx
            && y < (long)c.Y + types[c.TypeId].HeightPx)?.Id;
    }
    public static (int X, int Y) NextPosition(ScreenModel screen, Guid typeId)
    {
        var type = screen.CabinetTypes.First(t => t.Id == typeId); var b = screen.Geometry.Bounds;
        foreach (var (x, y) in new[] { (b.Right, (long)b.Y), ((long)b.X, b.Bottom) })
        {
            if (x > int.MaxValue || y > int.MaxValue) continue;
            try { Add(screen, Guid.NewGuid(), type.Id, (int)x, (int)y); return ((int)x, (int)y); }
            catch (ModelValidationException) { }
        }
        throw new ArgumentException("Нет места справа или снизу в пределах допустимого растра. Уменьшите сборку или задайте свободное место вручную.");
    }
    public static (int X, int Y) Snap(ScreenModel screen, Guid cabinetId, int x, int y, double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var moving = Find(screen, cabinetId); var types = screen.CabinetTypes.ToDictionary(t => t.Id); var size = types[moving.TypeId];
        var xs = new List<long> { 0 }; var ys = new List<long> { 0 };
        foreach (var other in screen.Cabinets.Where(c => c.Id != cabinetId))
        {
            var t = types[other.TypeId];
            xs.AddRange([other.X, (long)other.X + t.WidthPx, (long)other.X - size.WidthPx, (long)other.X + t.WidthPx - size.WidthPx]);
            ys.AddRange([other.Y, (long)other.Y + t.HeightPx, (long)other.Y - size.HeightPx, (long)other.Y + t.HeightPx - size.HeightPx]);
        }
        int Nearest(IEnumerable<long> values, int original) => (int)values.Where(v => v >= 0 && v <= int.MaxValue && Math.Abs(v - original) <= tolerance)
            .OrderBy(v => Math.Abs(v - original)).ThenBy(v => v).DefaultIfEmpty(original).First();
        var sx = Nearest(xs, x); var sy = Nearest(ys, y);
        foreach (var candidate in new[] { (sx, sy), (sx, y), (x, sy), (x, y) })
        {
            try { Move(screen, cabinetId, candidate.Item1, candidate.Item2); return candidate; }
            catch (ModelValidationException) { }
        }
        return (x, y); // Caller reports an invalid final position without committing it.
    }
    private static CabinetInstance Find(ScreenModel screen, Guid id) => screen.Cabinets.FirstOrDefault(c => c.Id == id)
        ?? throw new ArgumentException("Выбранный кабинет отсутствует.");
    private static ScreenModel Rebuild(ScreenModel screen, IReadOnlyList<CabinetType> types, IReadOnlyList<CabinetInstance> cabinets) =>
        new(screen.Id, screen.Name, types, cabinets, screen.Style, screen.Projection);
}

public sealed record ReplaceCabinetScreenCommand(ScreenModel Screen) : IProjectCommand
{
    public string Description => "Изменить кабинеты";
    public ProjectModel Apply(ProjectModel project)
    {
        var previous = project.Screens.FirstOrDefault(s => s.Id == Screen.Id) ?? throw new ArgumentException("Экран отсутствует.");
        return ReferenceEquals(previous, Screen) ? project : new(project.FormatVersion,
            project.Screens.Select(s => s.Id == Screen.Id ? Screen : s).ToArray(), project.Canvas, project.Mode, project.InactiveLayout);
    }
}
