using System.Diagnostics;
using GridMapper.Core;
using GridMapper.Rendering;
using SkiaSharp;

namespace GridMapper.Desktop;

internal sealed record NdiSnapshot(ScreenModel Screen, LogoImage? Logo, ProjectionSettings? Projection = null)
{
    internal static string SourceName(ScreenModel screen)
    {
        var name = new string(screen.Name.Select(c => char.IsControl(c) || "\\/:*?\"<>|".Contains(c) ? ' ' : c).ToArray()).Trim();
        // Stable suffix disambiguates duplicate names; UTF-8 is passed to the native API.
        return $"Grid Mapper - {name[..Math.Min(name.Length, 140)]} [{screen.Id.ToString("N")[..8]}]";
    }
    internal int Width => Projection?.WidthPx ?? Screen.Geometry.Bounds.Width;
    internal int Height => Projection?.HeightPx ?? Screen.Geometry.Bounds.Height;
    internal long Bytes => (long)Width * Height * 4;
    internal SKBitmap Render()
    {
        var screen = MainWindow.PresentationScreen(Screen); var bounds = screen.Geometry.Bounds;
        var images = Logo is null ? new Dictionary<string, LogoImage>() : new Dictionary<string, LogoImage> { [screen.Style.Logo.SourcePath] = Logo };
        using var picture = SceneRenderer.Record(Projection is { } p ? SceneBuilder.ForProjection(p) : SceneBuilder.ForScreen(screen), images);
        using var raster = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (raster.GetPixels() == 0) throw new OutOfMemoryException(L.T("ui289"));
        using (var canvas = new SKCanvas(raster)) { canvas.Clear(SKColors.Transparent); canvas.DrawPicture(picture); canvas.Flush(); }
        var result = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        try
        {
            if (result.GetPixels() == 0) throw new OutOfMemoryException(L.T("ui289"));
            using var pixels = raster.PeekPixels();
            if (!pixels.ReadPixels(result.Info, result.GetPixels(), result.RowBytes, 0, 0)) throw new InvalidOperationException(L.T("ui290"));
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}

// One dedicated, clocked sender per screen. Immutable snapshots coalesce edits;
// at most one current raster is kept, with no PNG encoding in the frame loop.
internal sealed class NdiOutput
{
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NdiSnapshot _latest;
    internal Task Started => _started.Task;
    internal Task Completion { get; }
    private string _name;
    internal string Name => Volatile.Read(ref _name);
    private static long _liveBytes;
    internal static long LiveFrameBytes => Interlocked.Read(ref _liveBytes);
    private long _sent, _renders;
    private int _frameRate;
    internal int FrameRate { get => Volatile.Read(ref _frameRate); set => Volatile.Write(ref _frameRate, value is 50 or 60 ? value : throw new ArgumentOutOfRangeException(nameof(value))); }
    internal long FramesSent => Interlocked.Read(ref _sent);
    internal long RenderCount => Interlocked.Read(ref _renders);
    internal NdiSnapshot Latest => Volatile.Read(ref _latest);
    internal NdiOutput(NdiSnapshot initial, Func<string, INdiSender> factory, int frameRate = 60)
    {
        FrameRate = frameRate;
        _latest = initial; _name = NdiSnapshot.SourceName(initial.Screen);
        Completion = Task.Factory.StartNew(() => Run(factory), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    internal void Update(NdiSnapshot snapshot) => Volatile.Write(ref _latest, snapshot);
    internal void RequestStop() { try { _stop.Cancel(); } catch (ObjectDisposedException) { } }
    internal async Task StopAsync() { RequestStop(); await Completion.ConfigureAwait(false); }
    private void Run(Func<string, INdiSender> factory)
    {
        SKBitmap? bitmap = null; INdiSender? sender = null;
        try
        {
            if (_stop.IsCancellationRequested) { _started.TrySetCanceled(); return; }
            sender = factory(Name);
            NdiSnapshot? rendered = null;
            var clock = Stopwatch.StartNew(); long count = 0; var previousRate = FrameRate;
            while (!_stop.IsCancellationRequested)
            {
                var snapshot = Latest;
                if (!ReferenceEquals(snapshot, rendered))
                {
                    var name = NdiSnapshot.SourceName(snapshot.Screen);
                    if (name != Name) { sender.Dispose(); sender = null; sender = factory(name); Volatile.Write(ref _name, name); }
                    var next = snapshot.Render(); Interlocked.Add(ref _liveBytes, next.ByteCount);
                    if (bitmap is not null) { Interlocked.Add(ref _liveBytes, -bitmap.ByteCount); bitmap.Dispose(); } bitmap = next; rendered = snapshot;
                    Interlocked.Increment(ref _renders);
                }
                if (_stop.IsCancellationRequested) break;
                var rate = FrameRate;
                if (rate != previousRate) { clock.Restart(); count = 0; previousRate = rate; }
                var frame = new NdiVideoFrame { Width = bitmap!.Width, Height = bitmap.Height,
                    FourCC = 0x41524742, FrameRateN = rate, FrameRateD = 1, FrameFormat = 1,
                    AspectRatio = (float)bitmap.Width / bitmap.Height, Timecode = long.MaxValue,
                    Data = bitmap.GetPixels(), Stride = bitmap.RowBytes };
                sender.Send(in frame); Interlocked.Increment(ref _sent); _started.TrySetResult();
                // The SDK clocks connected video. A deadline also bounds idle/fake
                // runtimes which can return immediately when nobody is receiving.
                var due = ++count * (double)Stopwatch.Frequency / rate;
                var wait = (due - clock.ElapsedTicks) / Stopwatch.Frequency;
                if (wait > 0) _stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(wait));
                else if (wait < -.1) { clock.Restart(); count = 0; }
            }
            _started.TrySetCanceled();
        }
        catch (Exception error) { _started.TrySetException(error); throw; }
        finally
        {
            try { sender?.Dispose(); }
            finally { if (bitmap is not null) { Interlocked.Add(ref _liveBytes, -bitmap.ByteCount); bitmap.Dispose(); } _stop.Dispose(); }
        }
    }
}
