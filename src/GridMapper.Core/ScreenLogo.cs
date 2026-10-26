using System.Text.Json.Serialization;

namespace GridMapper.Core;

public enum LogoAnchor { TopLeft, TopRight, BottomLeft, BottomRight, Center }

public sealed record ScreenLogo
{
    public bool Enabled { get; }
    public string SourcePath { get; }
    public LogoAnchor Anchor { get; }
    public int OffsetX { get; }
    public int OffsetY { get; }
    public decimal ScalePercent { get; }
    public int OpacityPercent { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public EmbeddedLogo? EmbeddedImage { get; }
    [JsonPropertyName("background"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? BackgroundOverride { get; }
    [JsonIgnore] public bool Background => BackgroundOverride != false;

    public ScreenLogo(bool enabled = false, string sourcePath = "", LogoAnchor anchor = LogoAnchor.TopLeft,
        int offsetX = 16, int offsetY = 16, decimal scalePercent = 100, int opacityPercent = 100, EmbeddedLogo? embeddedImage = null, bool? backgroundOverride = null)
    {
        ModelLimits.Require(sourcePath is not null && sourcePath.Length <= 4096 && !sourcePath.Any(char.IsControl),
            ModelError.InvalidLogo, "Недопустимая ссылка на файл логотипа.");
        ModelLimits.Require(Enum.IsDefined(anchor) && offsetX is >= -ModelLimits.MaxDimension and <= ModelLimits.MaxDimension
            && offsetY is >= -ModelLimits.MaxDimension and <= ModelLimits.MaxDimension
            && scalePercent is >= 0.1m and <= 1000m && opacityPercent is >= 0 and <= 100,
            ModelError.InvalidLogo, "Логотип: отступы ±16 384 px, масштаб 0.1–1000%, непрозрачность 0–100%.");
        EmbeddedImage = embeddedImage; BackgroundOverride = backgroundOverride == false ? false : null;
        Enabled = enabled; SourcePath = embeddedImage?.Key ?? sourcePath!; Anchor = anchor; OffsetX = offsetX; OffsetY = offsetY;
        ScalePercent = scalePercent; OpacityPercent = opacityPercent;
    }
}
