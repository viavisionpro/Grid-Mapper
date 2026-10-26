using System.Globalization;

namespace GridMapper.Core;

public enum NumberingFormat { A1, ColumnRow, Sequential }
public enum NumberingTraversal { Rows, Columns }
public enum NumberingCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public sealed record CabinetNumbering
{
    public const int MaxStart = 1_000_000;
    public const int MinFontSize = 6;
    public const int MaxFontSize = 256;
    public bool Enabled { get; }
    public NumberingFormat Format { get; }
    public NumberingTraversal Traversal { get; }
    public NumberingCorner Corner { get; }
    public string Prefix { get; }
    public int Start { get; }
    public int FontSize { get; }
    public bool Background { get; }
    public EmbeddedFont Font { get; }

    public CabinetNumbering(bool enabled = false, NumberingFormat format = NumberingFormat.A1,
        NumberingTraversal traversal = NumberingTraversal.Rows, NumberingCorner corner = NumberingCorner.TopLeft,
        string prefix = "", int start = 1, int fontSize = 24, bool background = true, EmbeddedFont font = EmbeddedFont.NotoSans)
    {
        ModelLimits.Require(Enum.IsDefined(format) && Enum.IsDefined(traversal) && Enum.IsDefined(corner),
            ModelError.InvalidNumbering, "Неизвестный формат или направление нумерации.");
        ValidatePrefix(prefix);
        EmbeddedFonts.Validate(font);
        ModelLimits.Require(start >= 0 && start <= MaxStart, ModelError.InvalidNumbering, "Начало счёта: от 0 до 1 000 000.");
        ModelLimits.Require(fontSize >= MinFontSize && fontSize <= MaxFontSize,
            ModelError.InvalidNumbering, "Размер подписи: от 6 до 256 px.");
        Enabled = enabled; Format = format; Traversal = traversal; Corner = corner;
        Prefix = prefix; Start = start; FontSize = fontSize; Background = background;
        Font = font;
    }

    public static void ValidatePrefix(string? prefix) => ModelLimits.Require(prefix is not null && prefix.Length <= 32
        && !prefix.Any(char.IsControl) && (prefix.Length == 0 || !string.IsNullOrWhiteSpace(prefix)),
        ModelError.InvalidNumbering, "Префикс: до 32 символов, без управляющих символов.");
}

public sealed record CabinetAddress(Guid CabinetId, int Column, int Row, int Sequence, string Label);

public static class CabinetAddresses
{
    public static IReadOnlyList<CabinetAddress> Build(ScreenModel screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var settings = screen.Style.Numbering;
        var types = screen.CabinetTypes.ToDictionary(x => x.Id);
        var fromRight = settings.Corner is NumberingCorner.TopRight or NumberingCorner.BottomRight;
        var fromBottom = settings.Corner is NumberingCorner.BottomLeft or NumberingCorner.BottomRight;
        long X(CabinetInstance cabinet) => fromRight ? (long)cabinet.X + types[cabinet.TypeId].WidthPx : cabinet.X;
        long Y(CabinetInstance cabinet) => fromBottom ? (long)cabinet.Y + types[cabinet.TypeId].HeightPx : cabinet.Y;
        var xs = screen.Cabinets.Select(X).Distinct().Order().ToArray();
        var ys = screen.Cabinets.Select(Y).Distinct().Order().ToArray();
        if (fromRight) Array.Reverse(xs);
        if (fromBottom) Array.Reverse(ys);
        var columns = xs.Select((x, i) => (x, i)).ToDictionary(x => x.x, x => x.i);
        var rows = ys.Select((y, i) => (y, i)).ToDictionary(x => x.y, x => x.i);
        var positions = screen.Cabinets.Select(x => (Cabinet: x, Column: columns[X(x)], Row: rows[Y(x)]));
        var ordered = settings.Traversal == NumberingTraversal.Rows
            ? positions.OrderBy(x => x.Row).ThenBy(x => x.Column)
            : positions.OrderBy(x => x.Column).ThenBy(x => x.Row);
        return Array.AsReadOnly(ordered.Select((item, i) =>
        {
            var label = settings.Format switch
            {
                NumberingFormat.A1 => Letters(item.Row) + (settings.Start + item.Column).ToString(CultureInfo.InvariantCulture),
                NumberingFormat.ColumnRow => FormattableString.Invariant($"{settings.Start + item.Column}-{settings.Start + item.Row}"),
                _ => (settings.Start + i).ToString(CultureInfo.InvariantCulture)
            };
            if (settings.Prefix.Length > 0) label = settings.Prefix + "-" + label;
            return new CabinetAddress(item.Cabinet.Id, item.Column, item.Row, settings.Start + i, label);
        }).ToArray());
    }

    private static string Letters(int column)
    {
        var value = column + 1;
        var text = "";
        while (value > 0) { value--; text = (char)('A' + value % 26) + text; value /= 26; }
        return text;
    }
}
