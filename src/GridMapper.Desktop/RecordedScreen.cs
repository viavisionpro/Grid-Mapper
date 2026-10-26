using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using GridMapper.Rendering;

namespace GridMapper.Desktop;

// The UI frame and render-thread operations own separate leases. Replacing a
// frame cannot free a picture while an earlier scene-graph node is drawing it.
internal sealed class RecordedScreen : IDisposable
{
    private readonly SKPicture _picture;
    private static int _live;
    internal static int LiveCount => Volatile.Read(ref _live);
    public RecordedScreen(SKPicture picture) { _picture = picture; Interlocked.Increment(ref _live); }
    ~RecordedScreen() => Dispose();
    private readonly object _gate = new();
    private int _references = 1;
    private bool _ownerReleased;
    internal bool OwnerReleased { get { lock (_gate) return _ownerReleased; } }
    public Operation Draw(Rect target, Size source) => new(this, target, source);
    internal byte[] RenderPng(int width, int height)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_ownerReleased, this); return SceneRenderer.RenderPng(_picture, width, height); }
    }
    public void Dispose()
    {
        lock (_gate) { if (_ownerReleased) return; _ownerReleased = true; Release(); }
        GC.SuppressFinalize(this);
    }
    private void Release() { if (--_references == 0) { _picture.Dispose(); Interlocked.Decrement(ref _live); } }

    internal sealed class Operation : ICustomDrawOperation
    {
        private RecordedScreen? _owner;
        private readonly Size _source;
        public Rect Bounds { get; }
        public Operation(RecordedScreen owner, Rect target, Size source)
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(owner._ownerReleased, owner);
                owner._references++;
            }
            _owner = owner; Bounds = target; _source = source;
        }
        // RenderTargetBitmap's immediate context can render without retaining/
        // disposing the operation. A finalizer also releases that short-lived lease.
        ~Operation() => Dispose();
        public bool HitTest(Point p) => Bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => other is Operation operation && ReferenceEquals(_owner, operation._owner)
            && Bounds == operation.Bounds && _source == operation._source;
        public void Render(ImmediateDrawingContext context)
        {
            var owner = _owner; if (owner is null) return;
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease(); var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.Translate((float)Bounds.X, (float)Bounds.Y);
            canvas.Scale((float)(Bounds.Width / _source.Width), (float)(Bounds.Height / _source.Height));
            canvas.DrawPicture(owner._picture);
            canvas.Restore();
        }
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null) lock (owner._gate) owner.Release();
            GC.SuppressFinalize(this);
        }
    }
}
