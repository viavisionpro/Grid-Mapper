using System.Text.Json.Serialization;

namespace GridMapper.Core;

public readonly record struct PixelBounds(int X, int Y, int Width, int Height)
{
    public long Right => (long)X + Width;
    public long Bottom => (long)Y + Height;
}

public sealed record ScreenGeometry(PixelBounds Bounds, decimal WidthMm, decimal HeightMm,
    decimal PitchX, decimal PitchY, int CabinetCount, int AspectWidth, int AspectHeight)
{
    public decimal WidthMeters => WidthMm / 1000;
    public decimal HeightMeters => HeightMm / 1000;
    public decimal AspectRatio => (decimal)Bounds.Width / Bounds.Height;
}

public sealed class ScreenModel
{
    public Guid Id { get; }
    public string Name { get; }
    public IReadOnlyList<CabinetType> CabinetTypes { get; }
    public IReadOnlyList<CabinetInstance> Cabinets { get; }
    public ScreenStyle Style { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProjectionSettings? Projection { get; }
    [JsonIgnore] public ScreenGeometry Geometry { get; }

    public ScreenModel(Guid id, string name, IReadOnlyList<CabinetType> cabinetTypes,
        IReadOnlyList<CabinetInstance> cabinets, ScreenStyle style, ProjectionSettings? projection = null)
    {
        ModelLimits.ValidateId(id);
        ModelLimits.ValidateName(name);
        ArgumentNullException.ThrowIfNull(cabinetTypes);
        ArgumentNullException.ThrowIfNull(cabinets);
        ArgumentNullException.ThrowIfNull(style);
        ModelLimits.Require(cabinets.Count > 0, ModelError.EmptyScreen, "Экран должен содержать хотя бы один кабинет.");
        ModelLimits.Require(cabinets.Count <= ModelLimits.MaxCabinets && cabinetTypes.Count <= ModelLimits.MaxCabinets,
            ModelError.TooManyItems, "Превышено допустимое количество кабинетов или типов.");

        var types = new Dictionary<Guid, CabinetType>();
        foreach (var type in cabinetTypes)
        {
            ArgumentNullException.ThrowIfNull(type);
            ModelLimits.Require(types.TryAdd(type.Id, type), ModelError.DuplicateId, "ID типов кабинетов должны быть уникальными внутри экрана.");
        }
        var ids = new HashSet<Guid>();
        var resolved = new List<(CabinetInstance Instance, CabinetType Type)>();
        foreach (var cabinet in cabinets)
        {
            ArgumentNullException.ThrowIfNull(cabinet);
            ModelLimits.Require(ids.Add(cabinet.Id), ModelError.DuplicateId, "ID экземпляров кабинетов должны быть уникальными.");
            if (!types.TryGetValue(cabinet.TypeId, out var type))
                throw new ModelValidationException(ModelError.MissingCabinetType, "Кабинет ссылается на отсутствующий тип.");
            resolved.Add((cabinet, type));
        }

        // Choose a nominal pitch independent of cabinet/catalog ordering and GUID generation.
        var reference = resolved.Select(x => x.Type).OrderBy(x => x.WidthPx).ThenBy(x => x.HeightPx)
            .ThenBy(x => x.WidthMm).ThenBy(x => x.HeightMm).First();
        foreach (var type in types.Values)
            ModelLimits.Require(ModelLimits.SameDensity(reference.DensityX, type.DensityX)
                && ModelLimits.SameDensity(reference.DensityY, type.DensityY), ModelError.DensityMismatch,
                $"Тип «{type.Name}» должен иметь такую же плотность пикселей, как «{reference.Name}», по обеим осям.");

        var minX = cabinets.Min(x => x.X);
        var minY = cabinets.Min(x => x.Y);
        var maxX = resolved.Max(x => (long)x.Instance.X + x.Type.WidthPx);
        var maxY = resolved.Max(x => (long)x.Instance.Y + x.Type.HeightPx);
        ModelLimits.ValidateRaster(maxX - minX, maxY - minY);
        ValidateNoOverlap(resolved);

        var width = (int)(maxX - minX);
        var height = (int)(maxY - minY);
        // Positions use nominal pitch; each cabinet contributes its own physical extent.
        var widthMm = resolved.Max(x => (x.Instance.X - (decimal)minX) * reference.PitchX + x.Type.WidthMm);
        var heightMm = resolved.Max(x => (x.Instance.Y - (decimal)minY) * reference.PitchY + x.Type.HeightMm);
        var divisor = GreatestCommonDivisor(width, height);
        Geometry = new(new(minX, minY, width, height), widthMm, heightMm,
            reference.PitchX, reference.PitchY, cabinets.Count(c => !c.IsMasked), width / divisor, height / divisor);
        Id = id;
        Name = name;
        CabinetTypes = Array.AsReadOnly(cabinetTypes.ToArray());
        Cabinets = Array.AsReadOnly(cabinets.ToArray());
        Style = style; Projection = projection;
    }

    public ScreenModel WithStyle(ScreenStyle style) => new(Id, Name, CabinetTypes, Cabinets, style, Projection);
    public ScreenModel WithName(string name) => new(Id, name, CabinetTypes, Cabinets, Style, Projection);

    public ScreenModel WithProjection(ProjectionSettings settings) => new(Id, Name, CabinetTypes, Cabinets, Style, settings);

    public static ScreenModel Regular(string name, CabinetType type, int columns, int rows, ScreenStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ModelLimits.Require(columns > 0 && rows > 0 && (long)columns * rows <= ModelLimits.MaxCabinets,
            ModelError.TooManyItems, "Нужно положительное число рядов и колонок, не более 10 000 кабинетов.");
        ModelLimits.ValidateRaster((long)type.WidthPx * columns, (long)type.HeightPx * rows);
        var cabinets = new List<CabinetInstance>();
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
                cabinets.Add(new(Guid.NewGuid(), type.Id, column * type.WidthPx, row * type.HeightPx));
        return new(Guid.NewGuid(), name, [type], cabinets, style ?? ScreenStyle.Default);
    }

    private static void ValidateNoOverlap(List<(CabinetInstance Instance, CabinetType Type)> cabinets)
    {
        // Sweep along the less crowded axis. A tall column must not compare every
        // cabinet against the whole column just because all X coordinates match.
        var spanX = cabinets.Max(c => (long)c.Instance.X + c.Type.WidthPx) - cabinets.Min(c => c.Instance.X);
        var spanY = cabinets.Max(c => (long)c.Instance.Y + c.Type.HeightPx) - cabinets.Min(c => c.Instance.Y);
        var sweepX = cabinets.Sum(c => (long)c.Type.WidthPx) * spanY <= cabinets.Sum(c => (long)c.Type.HeightPx) * spanX;
        long Start((CabinetInstance Instance, CabinetType Type) c) => sweepX ? c.Instance.X : c.Instance.Y;
        long End((CabinetInstance Instance, CabinetType Type) c) => Start(c) + (sweepX ? c.Type.WidthPx : c.Type.HeightPx);
        long CrossStart((CabinetInstance Instance, CabinetType Type) c) => sweepX ? c.Instance.Y : c.Instance.X;
        long CrossEnd((CabinetInstance Instance, CabinetType Type) c) => CrossStart(c) + (sweepX ? c.Type.HeightPx : c.Type.WidthPx);
        var ordered = cabinets.OrderBy(Start).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var a = ordered[i]; var end = End(a);
            for (var j = i + 1; j < ordered.Length && Start(ordered[j]) < end; j++)
            {
                var b = ordered[j];
                if (CrossStart(a) < CrossEnd(b) && CrossStart(b) < CrossEnd(a))
                    throw new ModelValidationException(ModelError.CabinetOverlap,
                        $"Кабинеты в ({a.Instance.X}, {a.Instance.Y}) и ({b.Instance.X}, {b.Instance.Y}) пересекаются.");
            }
        }
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
