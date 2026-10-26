using System.Text.Json;
using System.Text.Json.Serialization;
using GridMapper.Core;

namespace GridMapper.Storage;

public sealed class ProjectFileException(string message, Exception? inner = null) : IOException(message, inner);

public static class ProjectFiles
{
    public const int MaxFileBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false
    };

    public static byte[] Serialize(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, Options);
        if (bytes.Length > MaxFileBytes) throw new ProjectFileException("Проект превышает допустимый размер файла (16 MiB).");
        return bytes;
    }

    public static ProjectModel Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxFileBytes) throw new ProjectFileException("Файл проекта превышает допустимый размер (16 MiB).");
        try
        {
            var project = JsonSerializer.Deserialize<ProjectModel>(bytes, Options)
                ?? throw new ProjectFileException("Файл не содержит проект Grid Mapper.");
            return HasLayoutMarker(bytes) ? project : MigrateSharedLayout(project);
        }
        catch (ModelValidationException error) { throw new ProjectFileException(error.Message, error); }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        { throw new ProjectFileException("Файл не содержит корректный проект Grid Mapper.", error); }
    }

    private static bool HasLayoutMarker(ReadOnlySpan<byte> bytes)
    {
        // Scan tokens rather than building another JSON tree with copies of embedded image/font data.
        var reader = new Utf8JsonReader(bytes);
        while (reader.Read())
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
            {
                if (reader.ValueTextEquals("inactiveLayout")) return true;
                reader.Read();
                reader.Skip();
            }
        return false;
    }

    private static ProjectModel MigrateSharedLayout(ProjectModel project)
    {
        var led = new ModeLayout(project.Screens, project.Canvas);
        var variants = new Dictionary<Guid, ScreenModel>();
        foreach (var screen in project.Screens)
            if (screen.Projection is not null || project.Mode == ProjectMode.Projection)
                // Projection never renders the LED logo. Keep its full source in LED,
                // without serializing the same multi-MiB asset twice during migration.
                variants.Add(screen.Id, CanvasEditing.Copy(screen, screen.Name)
                    .WithStyle(screen.Style.WithLogo(new ScreenLogo()))
                    .WithProjection(screen.Projection ?? ProjectionSettings.Default.With(checkerboard: true)));
        var placements = project.Canvas.Placements.Where(p => variants.ContainsKey(p.ScreenId))
            .Select(p => new ScreenPlacement(Guid.NewGuid(), variants[p.ScreenId].Id, p.X, p.Y, p.IsLocked, p.IsHidden)).ToArray();
        var projection = new ModeLayout(variants.Values.ToArray(), new(project.Canvas.WidthPx, project.Canvas.HeightPx,
            placements, project.Canvas.Snap, project.Canvas.TransparentBackground));
        return project.Mode == ProjectMode.Led
            ? new(project.FormatVersion, led.Screens, led.Canvas, ProjectMode.Led, projection)
            : new(project.FormatVersion, projection.Screens, projection.Canvas, ProjectMode.Projection, led);
    }

    // Asynchronous reads do not make the initial OS open asynchronous. Permission
    // prompts and slow filesystems can block that call, so keep the whole file
    // operation off the caller's UI thread, including parsing and validation.
    public static Task<ProjectModel> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadCoreAsync(path, cancellationToken), cancellationToken);

    private static async Task<ProjectModel> LoadCoreAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (!input.CanSeek) throw new ProjectFileException("Выберите обычный файл проекта.");
        if (input.Length > MaxFileBytes) throw new ProjectFileException("Файл проекта превышает допустимый размер (16 MiB).");
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var count = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (buffer.Length + count > MaxFileBytes) throw new ProjectFileException("Файл проекта превышает допустимый размер (16 MiB).");
            buffer.Write(chunk, 0, count);
        }
        return Deserialize(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static Task SaveAsync(string path, ProjectModel project, CancellationToken cancellationToken = default) =>
        AtomicFiles.WriteAsync(path, Serialize(project), cancellationToken);
}

public static class AtomicFiles
{
    public static Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) =>
        Task.Run(() => WriteCoreAsync(path, bytes, cancellationToken), cancellationToken);

    public static Task WriteStreamAsync(string path, Action<Stream, CancellationToken> write, CancellationToken cancellationToken = default) =>
        Task.Run(() => WriteStreamCore(path, write, cancellationToken), cancellationToken);

    private static void WriteStreamCore(string path, Action<Stream, CancellationToken> write, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("Папка сохранения не найдена.");
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
            {
                write(output, token); token.ThrowIfCancellationRequested(); output.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task WriteCoreAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("Папка сохранения не найдена.");
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
