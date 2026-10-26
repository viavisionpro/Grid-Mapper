using System.Text.Json.Serialization;

namespace GridMapper.Core;

public enum InformationCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public sealed record ScreenInformation
{
    [JsonIgnore] public bool Ramp => RampOverride ?? !IndependentParts;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IndependentParts { get; }
    [JsonIgnore] public bool HasParts => IndependentParts ? ShowName || ShowResolution || ShowPhysicalSize || ShowCabinetCount || Ramp : Enabled;
    [JsonPropertyName("ramp"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RampOverride { get; }
    public bool Enabled { get; }
    public bool ShowName { get; }
    public bool ShowResolution { get; }
    public bool ShowPhysicalSize { get; }
    public bool ShowCabinetCount { get; }
    public int FontSize { get; }
    public bool Background { get; }
    public InformationCorner Corner { get; }
    public bool ShowCoordinates { get; }
    public EmbeddedFont Font { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RgbaColor? BackgroundColor { get; }

    public ScreenInformation(bool enabled = false, bool showName = true, bool showResolution = true,
        bool showPhysicalSize = true, bool showCabinetCount = true, int fontSize = 16,
        bool background = true, InformationCorner corner = InformationCorner.TopLeft, bool showCoordinates = false, EmbeddedFont font = EmbeddedFont.NotoSans, RgbaColor? backgroundColor = null, bool? rampOverride = null, bool independentParts = false)
    {
        ModelLimits.Require(fontSize is >= 6 and <= 256 && Enum.IsDefined(corner),
            ModelError.InvalidInformation, "Инфоблок: размер текста от 6 до 256 px, корректный угол.");
        EmbeddedFonts.Validate(font);
        IndependentParts = independentParts;
        RampOverride = independentParts ? rampOverride : rampOverride == false ? false : null; Enabled = enabled; ShowName = showName; ShowResolution = showResolution;
        ShowPhysicalSize = showPhysicalSize; ShowCabinetCount = showCabinetCount;
        FontSize = fontSize; Background = background; Corner = corner;
        ShowCoordinates = showCoordinates;
        Font = font;
        BackgroundColor = backgroundColor == new RgbaColor(0, 0, 0, 210)
            || independentParts && backgroundColor == new RgbaColor(24, 24, 24) ? null : backgroundColor;
    }

    public ScreenInformation Independent() => IndependentParts ? this : new(Enabled, Enabled && ShowName,
        Enabled && ShowResolution, Enabled && ShowPhysicalSize, Enabled && ShowCabinetCount, FontSize, Background, Corner,
        Enabled && ShowCoordinates, Font, BackgroundColor, Enabled && Ramp, true);

    public IReadOnlyList<string> Lines(ScreenModel screen, ScreenPlacement? placement = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ModelLimits.Require(placement is null || placement.ScreenId == screen.Id, ModelError.MissingScreen,
            "Размещение относится к другому экрану.");
        var lines = new List<string>();
        if (!HasParts) return lines.AsReadOnly();
        var geometry = screen.Geometry;
        if (ShowName) lines.Add(screen.Name);
        if (ShowResolution) lines.Add(FormattableString.Invariant($"{geometry.Bounds.Width} × {geometry.Bounds.Height} px"));
        if (ShowPhysicalSize)
        {
            lines.Add(FormattableString.Invariant($"{geometry.WidthMm:0.###} × {geometry.HeightMm:0.###} мм"));
            lines.Add(FormattableString.Invariant($"{geometry.WidthMeters:0.######} × {geometry.HeightMeters:0.######} м"));
        }
        if (ShowCabinetCount) lines.Add(FormattableString.Invariant($"Кабинетов: {geometry.CabinetCount}"));
        if (ShowCoordinates)
        {
            lines.Add("X/Y растра экрана: 0 / 0 px");
            if (placement is not null) lines.Add(FormattableString.Invariant($"X/Y на канвасе: {placement.X} / {placement.Y} px"));
            lines.Add(FormattableString.Invariant($"Offset кабинетов: {geometry.Bounds.X} / {geometry.Bounds.Y} px"));
        }
        return lines.AsReadOnly();
    }
}
