namespace GridMapper.Core;

public sealed record GridSettings(decimal WidthMm, decimal HeightMm, int WidthPx, int HeightPx, int Columns, int Rows);
public sealed record RegularGrid(CabinetType Type, int Columns, int Rows, int OriginX, int OriginY)
{
    public GridSettings Settings => new(Type.WidthMm, Type.HeightMm, Type.WidthPx, Type.HeightPx, Columns, Rows);

    public static RegularGrid? Detect(ScreenModel screen)
    {
        var types = screen.CabinetTypes.ToDictionary(x => x.Id);
        var used = screen.Cabinets.Select(x => x.TypeId).Distinct().ToArray();
        if (used.Length != 1) return null;
        var type = types[used[0]];
        var bounds = screen.Geometry.Bounds;
        if (bounds.Width % type.WidthPx != 0 || bounds.Height % type.HeightPx != 0) return null;
        var columns = bounds.Width / type.WidthPx;
        var rows = bounds.Height / type.HeightPx;
        if ((long)columns * rows != screen.Cabinets.Count) return null;
        if (screen.Cabinets.Any(x => (x.X - bounds.X) % type.WidthPx != 0 || (x.Y - bounds.Y) % type.HeightPx != 0)) return null;
        return new(type, columns, rows, bounds.X, bounds.Y);
    }
}

public interface IProjectCommand
{
    string Description { get; }
    ProjectModel Apply(ProjectModel project);
}

public sealed class EditScreenCommand(Guid screenId, string name, ScreenStyle style, GridSettings? grid,
    IReadOnlyDictionary<(int Column, int Row), Guid>? retainedCabinetIds = null, IReadOnlySet<Guid>? retainedMaskedIds = null) : IProjectCommand
{
    public string Description => "Изменить экран";

    public ProjectModel Apply(ProjectModel project)
    {
        var screen = project.Screens.FirstOrDefault(x => x.Id == screenId)
            ?? throw new ModelValidationException(ModelError.MissingScreen, "Выбранный экран отсутствует.");
        var previousGrid = RegularGrid.Detect(screen);
        if (screen.Name == name && SameStyle(screen.Style, style) && (grid is null || grid == previousGrid?.Settings))
            return project;

        ScreenModel next;
        if (grid is null || grid == previousGrid?.Settings)
            next = new(screen.Id, name, screen.CabinetTypes, screen.Cabinets, style, screen.Projection);
        else
        {
            if (previousGrid is null)
                throw new ModelValidationException(ModelError.MissingCabinetType, "Эта раскладка не является регулярной сеткой.");
            var type = new CabinetType(previousGrid.Type.Id, previousGrid.Type.Name,
                grid.WidthMm, grid.HeightMm, grid.WidthPx, grid.HeightPx);
            ModelLimits.Require(grid.Columns > 0 && grid.Rows > 0 && (long)grid.Columns * grid.Rows <= ModelLimits.MaxCabinets,
                ModelError.TooManyItems, "Нужно положительное число рядов и колонок, не более 10 000 кабинетов.");
            ModelLimits.ValidateRaster((long)type.WidthPx * grid.Columns, (long)type.HeightPx * grid.Rows);
            ModelLimits.Require((long)previousGrid.OriginX + (grid.Columns - 1L) * type.WidthPx <= int.MaxValue
                && (long)previousGrid.OriginY + (grid.Rows - 1L) * type.HeightPx <= int.MaxValue,
                ModelError.InvalidRasterSize, "Координаты изменённой сетки выходят за допустимый диапазон.");
            var ids = screen.Cabinets.ToDictionary(
                x => ((x.X - previousGrid.OriginX) / previousGrid.Type.WidthPx, (x.Y - previousGrid.OriginY) / previousGrid.Type.HeightPx),
                x => x.Id);
            if (retainedCabinetIds is not null)
                foreach (var item in retainedCabinetIds) ids[item.Key] = item.Value;
            var cabinets = new List<CabinetInstance>();
            var masked = screen.Cabinets.Where(c => c.IsMasked).Select(c => c.Id).ToHashSet();
            if (retainedMaskedIds is not null) masked.UnionWith(retainedMaskedIds);
            for (var row = 0; row < grid.Rows; row++)
                for (var column = 0; column < grid.Columns; column++)
                    cabinets.Add(new(ids.TryGetValue((column, row), out var id) ? id : Guid.NewGuid(), type.Id,
                        previousGrid.OriginX + column * type.WidthPx, previousGrid.OriginY + row * type.HeightPx, masked.Contains(id)));
            next = new(screen.Id, name, screen.CabinetTypes.Select(t => t.Id == type.Id ? type : t).ToArray(), cabinets, style, screen.Projection);
        }
        return new(project.FormatVersion, project.Screens.Select(x => x.Id == screenId ? next : x).ToArray(), project.Canvas, project.Mode, project.InactiveLayout);
    }

    private static bool SameStyle(ScreenStyle a, ScreenStyle b) => a.Pattern == b.Pattern
        && a.PrimaryColor == b.PrimaryColor && a.SecondaryColor == b.SecondaryColor
        && a.ShowResolutionLabel == b.ShowResolutionLabel && a.Decorations == b.Decorations && a.Numbering == b.Numbering && a.Information == b.Information && a.Text == b.Text && a.Logo == b.Logo;
}

public static class ProjectDefaults
{
    public static ProjectModel Empty() => new(ProjectModel.CurrentFormatVersion, [], new(3840, 2160, [], new(true)));
    public static ProjectModel Create()
    {
        var screen = ScreenModel.Regular("Экран 1", new(Guid.NewGuid(), "Кабинет", 500, 500, 128, 128), 2, 2);
        return new(ProjectModel.CurrentFormatVersion, [screen], new(3840, 2160,
            [new(Guid.NewGuid(), screen.Id, 0, 0, false, false)], new(true)));
    }
}
