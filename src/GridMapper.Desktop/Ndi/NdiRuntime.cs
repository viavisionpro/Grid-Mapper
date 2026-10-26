using System.Runtime.InteropServices;

namespace GridMapper.Desktop;

// Optional, installed NDI runtime. No vendor binaries are redistributed.
// ABI: NDIlib_send_create_t and NDIlib_video_frame_v2_t (SDK C API).
internal sealed class NdiRuntime
{
    private static readonly object Gate = new();
    private static NdiRuntime? _shared;
    private readonly nint _library; // Process lifetime: delegates and in-flight senders must remain valid.
    internal string Version { get; }
    internal string LibraryPath { get; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint VersionFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint CreateFn(in SendSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyFn(nint sender);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SendFn(nint sender, in NdiVideoFrame frame);
    private readonly CreateFn _create;
    private readonly DestroyFn _destroy;
    private readonly SendFn _send;
    [StructLayout(LayoutKind.Sequential)] private struct SendSettings
    {
        public nint Name, Groups;
        public byte ClockVideo, ClockAudio;
    }
    private NdiRuntime(nint library, string path)
    {
        _library = library; LibraryPath = path;
        _create = Function<CreateFn>("NDIlib_send_create"); _destroy = Function<DestroyFn>("NDIlib_send_destroy");
        _send = Function<SendFn>("NDIlib_send_send_video_v2");
        if (Function<Initialize>("NDIlib_initialize")() == 0) throw new InvalidOperationException(L.T("ui286"));
        Version = Marshal.PtrToStringUTF8(Function<VersionFn>("NDIlib_version")()) ?? "NDI";
    }
    internal T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    internal static NdiRuntime Get()
    {
        lock (Gate)
        {
            if (_shared is not null) return _shared;
            foreach (var path in LibraryCandidates())
            {
                nint library = 0;
                try
                {
                    if (!NativeLibrary.TryLoad(path, out library)) continue;
                    return _shared = new NdiRuntime(library, path);
                }
                catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                { if (library != 0) NativeLibrary.Free(library); }
                catch { if (library != 0) NativeLibrary.Free(library); throw; }
            }
            throw new InvalidOperationException(L.T("ui287"));
        }
    }
    internal static IEnumerable<string> LibraryCandidates()
    {
        var file = OperatingSystem.IsWindows() ? "Processing.NDI.Lib.x64.dll" : "libndi.dylib";
        // An explicit per-application override wins over installed system runtimes.
        yield return Path.Combine(ApplicationPaths.CustomizationDirectory, "NDI", file);
        yield return Path.Combine(ApplicationPaths.InstallationDirectory, file);
        foreach (var variable in new[] { "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } directory) yield return Path.Combine(directory, file);
        if (OperatingSystem.IsMacOS()) yield return "/usr/local/lib/libndi.dylib";
        if (OperatingSystem.IsWindows())
            foreach (var version in new[] { 6, 5 })
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", $"NDI {version} Runtime", $"v{version}", file);
        yield return file;
    }
    internal INdiSender CreateSender(string name)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            var settings = new SendSettings { Name = utf8, ClockVideo = 1 };
            var handle = _create(in settings);
            if (handle == 0) throw new InvalidOperationException(L.T("ui288"));
            return new Sender(this, handle);
        }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }
    private sealed class Sender(NdiRuntime runtime, nint handle) : INdiSender
    {
        public void Send(in NdiVideoFrame frame) => runtime._send(handle, in frame);
        public void Dispose() { if (handle == 0) return; runtime._destroy(handle); handle = 0; }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NdiVideoFrame
{
    public int Width, Height, FourCC, FrameRateN, FrameRateD;
    public float AspectRatio;
    public int FrameFormat;
    public long Timecode;
    public nint Data;
    public int Stride;
    public nint Metadata;
    public long Timestamp;
}
internal interface INdiSender : IDisposable
{
    // Synchronous handoff: the caller may replace/free the pixels after this returns.
    void Send(in NdiVideoFrame frame);
}
