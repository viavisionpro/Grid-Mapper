using System.Globalization;
using System.Text.Json.Serialization;

namespace GridMapper.Core;

public enum ProjectMode { Led, Projection }

// Metres describe the projected surface; the raster defines its aspect ratio.
public sealed record ProjectionSettings
{
    public int WidthPx { get; }
    public int HeightPx { get; }
    public decimal WidthMeters { get; }
    public decimal GridMeters { get; }
    public RgbaColor GridColor { get; }
    public RgbaColor CircleColor { get; }
    public bool ShowIds { get; }
    public bool Checkerboard { get; }
    public bool Circles { get; }
    public int CircleRows { get; }
    public bool ShowInformation { get; }
    public bool GridEnabled { get; }
    public bool FrameEnabled { get; }
    [JsonInclude] public RgbaColor FrameColor { get; private init; } = new(255,190,215);
    [JsonIgnore] public decimal HeightMeters => WidthMeters * HeightPx / WidthPx;
    [JsonIgnore] public decimal CellSizePx => GridMeters * WidthPx / WidthMeters;
    [JsonIgnore] public decimal CircleDiameterMeters => HeightMeters / CircleRows;
    public static ProjectionSettings Default { get; } = new();
    public ProjectionSettings() : this(1920,1080,16,1,new(255,255,255),new(255,190,215),true,false,true,1,true,gridEnabled:true,frameEnabled:true) { }
    // Missing frameEnabled means an older project: keep its existing pattern.
    // New screens use the explicit enabled default above.
    [JsonConstructor]
    public ProjectionSettings(int widthPx, int heightPx, decimal widthMeters, decimal gridMeters,
        RgbaColor gridColor, RgbaColor circleColor, bool showIds, bool checkerboard,
        bool circles, int circleRows, bool showInformation, bool gridEnabled = true, bool frameEnabled = false)
    {
        ModelLimits.ValidateRaster(widthPx, heightPx);
        ModelLimits.Require(widthMeters is >= .001m and <= 1_000_000m && gridMeters is >= .001m and <= 1_000_000m,
            ModelError.InvalidPhysicalSize, "projection.dimensions");
        ModelLimits.Require(circleRows is >= 1 and <= 10, ModelError.InvalidPattern, "projection.circleLimit");
        var cell = gridMeters * widthPx / widthMeters;
        var columns = 2 * decimal.Ceiling(widthPx / (2 * cell));
        var rows = 2 * decimal.Ceiling(heightPx / (2 * cell));
        var diameter = (decimal)heightPx / circleRows;
        var circleColumns = 2 * decimal.Ceiling((widthPx / 2m + diameter / 2) / diameter) + 1;
        var circleCount = circles ? circleRows * circleColumns : 0;
        var gridCount = gridEnabled ? columns * rows : 0;
        // Two opposite edges per axis, with 0.1m ticks aligned to the centre.
        var tickCount = frameEnabled ? 4 * (decimal.Floor(widthMeters * 5) + decimal.Floor(widthMeters * heightPx / widthPx * 5)) + 4 : 0;
        ModelLimits.Require(gridCount + circleCount + tickCount <= ModelLimits.MaxCabinets, ModelError.TooManyItems, "projection.density");
        WidthPx = widthPx; HeightPx = heightPx; WidthMeters = widthMeters; GridMeters = gridMeters;
        GridColor = gridColor; CircleColor = circleColor;
        ShowIds = showIds; Checkerboard = checkerboard; Circles = circles; CircleRows = circleRows; ShowInformation = showInformation;
        GridEnabled = gridEnabled; FrameEnabled = frameEnabled;
    }
    public ProjectionSettings With(int? widthPx = null, int? heightPx = null, decimal? widthMeters = null, decimal? gridMeters = null,
        RgbaColor? gridColor = null, RgbaColor? circleColor = null, bool? showIds = null, bool? checkerboard = null,
        bool? circles = null, int? circleRows = null, bool? showInformation = null,
        bool? gridEnabled = null, bool? frameEnabled = null, RgbaColor? frameColor = null) =>
        new(widthPx ?? WidthPx, heightPx ?? HeightPx, widthMeters ?? WidthMeters, gridMeters ?? GridMeters,
            gridColor ?? GridColor, circleColor ?? CircleColor, showIds ?? ShowIds, checkerboard ?? Checkerboard,
            circles ?? Circles, circleRows ?? CircleRows, showInformation ?? ShowInformation,
            gridEnabled ?? GridEnabled, frameEnabled ?? FrameEnabled) { FrameColor = frameColor ?? FrameColor };
    public static string Meters(decimal value) => value == decimal.Truncate(value)
        ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.00", CultureInfo.InvariantCulture);
}
