using System.Text.Json.Serialization;

namespace GridMapper.Core;

public enum FillPattern { Checkerboard, Solid }

[method: JsonConstructor]
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255);

[method: JsonConstructor]
public sealed record ScreenDecorations(bool Border = false, bool CornerMarkers = false, bool Diagonals = false, bool CenterPlus = false, bool Crosspoint = false, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Raster = false, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Circle = false, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CornerCircles = false);

public sealed class ScreenStyle
{
    public FillPattern Pattern { get; }
    public RgbaColor PrimaryColor { get; }
    public RgbaColor SecondaryColor { get; }
    public bool ShowResolutionLabel { get; }
    public ScreenDecorations Decorations { get; }
    public CabinetNumbering Numbering { get; }
    public ScreenInformation Information { get; }
    public ScreenText Text { get; }
    public ScreenLogo Logo { get; }

    public ScreenStyle(FillPattern pattern, RgbaColor primaryColor, RgbaColor secondaryColor, bool showResolutionLabel,
        ScreenDecorations? decorations = null, CabinetNumbering? numbering = null, ScreenInformation? information = null, ScreenText? text = null, ScreenLogo? logo = null)
    {
        ModelLimits.Require(Enum.IsDefined(pattern), ModelError.InvalidPattern, "Неизвестный вид заливки.");
        Pattern = pattern;
        PrimaryColor = primaryColor;
        SecondaryColor = secondaryColor;
        ShowResolutionLabel = showResolutionLabel;
        Decorations = decorations ?? new();
        Numbering = numbering ?? new();
        Information = information ?? new();
        Text = text ?? new();
        Logo = logo ?? new();
    }

    public static ScreenStyle Default => new(FillPattern.Checkerboard,
        new(240, 240, 240), new(32, 32, 32), true);

    public ScreenStyle WithResolutionLabel(bool show) => new(Pattern, PrimaryColor, SecondaryColor, show, Decorations, Numbering, Information, Text, Logo);
    public ScreenStyle WithLogo(ScreenLogo logo) => new(Pattern, PrimaryColor, SecondaryColor, ShowResolutionLabel, Decorations, Numbering, Information, Text, logo);
}
