namespace GridMapper.Core;

/// <summary>Resizes a regular screen in whole cabinets, keeping the opposite edges fixed.</summary>
public sealed class ScreenGridResize
{
    private readonly ProjectModel _project;
    private readonly ScreenModel _screen;
    private readonly RegularGrid _grid;
    private readonly ScreenPlacement _placement;
    private readonly Dictionary<(int Column, int Row), Guid> _ids;
    private readonly HashSet<Guid> _masked;

    public ScreenGridResize(ProjectModel project, Guid screenId)
    {
        _project = project;
        _screen = project.Screens.Single(s => s.Id == screenId);
        _masked = _screen.Cabinets.Where(c => c.IsMasked).Select(c => c.Id).ToHashSet();
        _grid = RegularGrid.Detect(_screen) ?? throw new ArgumentException("Маркеры размера доступны для прямоугольной сетки одного типа кабинета.");
        _placement = project.Canvas.Placements.Single(p => p.ScreenId == screenId);
        _ids = _screen.Cabinets.ToDictionary(c => ((c.X - _grid.OriginX) / _grid.Type.WidthPx,
            (c.Y - _grid.OriginY) / _grid.Type.HeightPx), c => c.Id);
    }

    public ProjectModel Resize(int columns, int rows, bool fromLeft, bool fromTop)
    {
        if (columns == _grid.Columns && rows == _grid.Rows) return _project;
        ModelLimits.Require(columns > 0 && rows > 0 && (long)columns * rows <= ModelLimits.MaxCabinets,
            ModelError.TooManyItems, "Не менее одного ряда и колонки, не более 10 000 панелей.");
        ModelLimits.ValidateRaster((long)columns * _grid.Type.WidthPx, (long)rows * _grid.Type.HeightPx);
        var shiftColumns = fromLeft ? _grid.Columns - columns : 0;
        var shiftRows = fromTop ? _grid.Rows - rows : 0;
        var x = (long)_placement.X + (long)shiftColumns * _grid.Type.WidthPx;
        var y = (long)_placement.Y + (long)shiftRows * _grid.Type.HeightPx;
        ModelLimits.Require(x >= int.MinValue && x <= int.MaxValue && y >= int.MinValue && y <= int.MaxValue,
            ModelError.InvalidRasterSize, "Положение экрана выходит за допустимый диапазон.");
        if (_placement.IsLocked && (x != _placement.X || y != _placement.Y))
            throw new ArgumentException("Положение экрана заблокировано. Используйте правый или нижний маркер.");
        ModelLimits.Require((long)_grid.OriginX + (columns - 1L) * _grid.Type.WidthPx <= int.MaxValue
            && (long)_grid.OriginY + (rows - 1L) * _grid.Type.HeightPx <= int.MaxValue,
            ModelError.InvalidRasterSize, "Координаты сетки выходят за допустимый диапазон.");
        var cabinets = new List<CabinetInstance>();
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var key = (column + shiftColumns, row + shiftRows);
                if (!_ids.TryGetValue(key, out var id)) _ids[key] = id = Guid.NewGuid();
                cabinets.Add(new(id, _grid.Type.Id, _grid.OriginX + column * _grid.Type.WidthPx,
                    _grid.OriginY + row * _grid.Type.HeightPx, _masked.Contains(id)));
            }
        var screen = new ScreenModel(_screen.Id, _screen.Name, _screen.CabinetTypes, cabinets, _screen.Style, _screen.Projection);
        var next = new ProjectModel(_project.FormatVersion, _project.Screens.Select(s => s.Id == screen.Id ? screen : s).ToArray(), _project.Canvas, _project.Mode, _project.InactiveLayout);
        return CanvasEditing.Place(next, screen.Id, (int)x, (int)y, _placement.IsLocked, _placement.IsHidden);
    }
}
