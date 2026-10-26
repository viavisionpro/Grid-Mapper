namespace GridMapper.Core;

public sealed record ScreenText
{
    public const int MaxCharacters = 2048;
    public const int MaxLines = 20;
    public bool Enabled { get; }
    public string Content { get; }
    public int X { get; }
    public int Y { get; }
    public int FontSize { get; }
    public bool Background { get; }
    public EmbeddedFont Font { get; }

    public ScreenText(bool enabled = false, string content = "", int x = 16, int y = 48,
        int fontSize = 24, bool background = true, EmbeddedFont font = EmbeddedFont.NotoSans)
    {
        ValidateContent(content);
        EmbeddedFonts.Validate(font);
        ModelLimits.Require(x is >= -ModelLimits.MaxDimension and <= ModelLimits.MaxDimension
            && y is >= -ModelLimits.MaxDimension and <= ModelLimits.MaxDimension && fontSize is >= 6 and <= 256,
            ModelError.InvalidText, "Текст: X/Y от −16 384 до 16 384 px, размер от 6 до 256 px.");
        Enabled = enabled; Content = Normalize(content); X = x; Y = y;
        FontSize = fontSize; Background = background;
        Font = font;
    }

    public static void ValidateContent(string? content) => ModelLimits.Require(content is not null
        && content.Length <= MaxCharacters && !content.Any(x => char.IsControl(x) && x is not '\r' and not '\n')
        && Normalize(content).Count(x => x == '\n') < MaxLines, ModelError.InvalidText,
        "Подпись: до 2048 символов и 20 строк, без управляющих символов кроме переноса строки.");

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    public IReadOnlyList<string> Lines() => Array.AsReadOnly(Enabled && !string.IsNullOrWhiteSpace(Content) ? Content.Split('\n') : []);
}
