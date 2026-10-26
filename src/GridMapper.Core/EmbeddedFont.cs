namespace GridMapper.Core;

public enum EmbeddedFont { NotoSans, NotoSansMono }

public static class EmbeddedFonts
{
    public static void Validate(EmbeddedFont font) => ModelLimits.Require(Enum.IsDefined(font),
        ModelError.InvalidFont, "Неизвестный встроенный шрифт.");
}
