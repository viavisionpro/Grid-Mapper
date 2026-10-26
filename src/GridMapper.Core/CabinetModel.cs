using System.Text.Json.Serialization;

namespace GridMapper.Core;

public sealed class CabinetType
{
    public Guid Id { get; }
    public string Name { get; }
    public decimal WidthMm { get; }
    public decimal HeightMm { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }
    [JsonIgnore] public decimal DensityX => WidthPx / WidthMm;
    [JsonIgnore] public decimal DensityY => HeightPx / HeightMm;
    [JsonIgnore] public decimal PitchX => WidthMm / WidthPx;
    [JsonIgnore] public decimal PitchY => HeightMm / HeightPx;

    public CabinetType(Guid id, string name, decimal widthMm, decimal heightMm, int widthPx, int heightPx)
    {
        ModelLimits.ValidateId(id);
        ModelLimits.ValidateName(name);
        ModelLimits.ValidateRaster(widthPx, heightPx);
        ModelLimits.Require(widthMm is >= ModelLimits.MinMillimeters and <= ModelLimits.MaxMillimeters
            && heightMm is >= ModelLimits.MinMillimeters and <= ModelLimits.MaxMillimeters,
            ModelError.InvalidPhysicalSize, "Размеры корпуса должны быть в диапазоне 0.001–1 000 000 мм.");
        Id = id;
        Name = name;
        WidthMm = widthMm;
        HeightMm = heightMm;
        WidthPx = widthPx;
        HeightPx = heightPx;
    }
}

public sealed class CabinetInstance
{
    public Guid Id { get; }
    public Guid TypeId { get; }
    public int X { get; }
    public int Y { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsMasked { get; }

    public CabinetInstance(Guid id, Guid typeId, int x, int y, bool isMasked = false)
    {
        ModelLimits.ValidateId(id);
        ModelLimits.ValidateId(typeId);
        ModelLimits.Require(x >= 0 && y >= 0, ModelError.NegativeCabinetPosition,
            "Локальные координаты кабинета должны быть неотрицательными целыми пикселями.");
        Id = id;
        TypeId = typeId;
        X = x;
        Y = y;
        IsMasked = isMasked;
    }
}
