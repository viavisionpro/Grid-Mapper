using System.Text;

namespace GridMapper.Desktop;

internal static class ExportFileNames
{
    internal static string Project(string? path) => Path.ChangeExtension(Path.GetFileName(path ?? "project"), ".gpm");

    internal static string Png(bool canvas, string name, int width, int height) =>
        FormattableString.Invariant($"{(canvas ? "Canvas" : "Screen")}_{SafePart(name)}_{width}x{height}.png");

    internal static string Resolume(string name, int width, int height) =>
        FormattableString.Invariant($"Resolume_{SafePart(name)}_{width}x{height}.xml");

    private static string SafePart(string name)
    {
        // Use the same portable suggestions on macOS and Windows. Keep Unicode
        // and spaces; replace characters that cannot appear in Windows names.
        var cleaned = new string(name.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (cleaned.Length == 0) return "project";
        var result = new StringBuilder(); var bytes = 0;
        foreach (var rune in cleaned.EnumerateRunes())
        {
            // Leave room for the prefix, resolution and extension below the
            // common 255-byte component limit, without splitting Unicode.
            if (bytes + rune.Utf8SequenceLength > 200) break;
            result.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        return result.ToString().TrimEnd(' ', '.');
    }
}
