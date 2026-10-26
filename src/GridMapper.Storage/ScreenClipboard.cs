using System.Text;
using GridMapper.Core;

namespace GridMapper.Storage;

public sealed record CopiedScreen(ScreenModel Screen, int X, int Y);

public static class ScreenClipboard
{
    public const string Header = "GridMapper.Screen/1\n";
    public static string Serialize(ScreenModel screen, ScreenPlacement placement)
    {
        if (screen.Id != placement.ScreenId) throw new ArgumentException("Размещение относится к другому экрану.");
        var bounds = screen.Geometry.Bounds;
        var project = new ProjectModel(1, [screen], new(bounds.Width, bounds.Height, [placement]));
        return Header + Encoding.UTF8.GetString(ProjectFiles.Serialize(project));
    }
    public static CopiedScreen Deserialize(string? text)
    {
        if (text is null || !text.StartsWith(Header, StringComparison.Ordinal)) throw new ProjectFileException("В буфере нет экрана Grid Mapper.");
        var json = text.AsSpan(Header.Length);
        if (json.Length > ProjectFiles.MaxFileBytes || Encoding.UTF8.GetByteCount(json) > ProjectFiles.MaxFileBytes)
            throw new ProjectFileException("Экран в буфере превышает 16 MiB.");
        var project = ProjectFiles.Deserialize(Encoding.UTF8.GetBytes(json.ToString()));
        if (project.Screens.Count != 1) throw new ProjectFileException("Буфер должен содержать один экран.");
        var placement = project.Canvas.Placements[0];
        return new(project.Screens[0], placement.X, placement.Y);
    }
}
