using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GridMapper.Rendering;

public sealed class RenderLimitException(string message) : InvalidOperationException(message);

public static class RenderSafety
{
    public const long MaxPreparedBytes = 256L * 1024 * 1024;

    public static void CheckMemory(long estimatedBytes)
    {
        var memory = GC.GetGCMemoryInfo();
        var total = memory.TotalAvailableMemoryBytes;
        // macOS's GC memory-load statistic includes reclaimable cache and can read
        // 100% on a healthy machine. Bound this process independently; on Windows
        // also check real available physical memory before allocating native rasters.
        var processLimit = Math.Min(2L * 1024 * 1024 * 1024, total > 0 ? total / 3 : long.MaxValue);
        var free = processLimit - Environment.WorkingSet;
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref status)) free = Math.Min(free, (long)status.AvailablePhysical);
        }
        if (estimatedBytes == 0) return;
        if (estimatedBytes > MaxPreparedBytes * 4 || free < estimatedBytes + 128L * 1024 * 1024)
            throw new RenderLimitException("Недостаточно свободной памяти для операции. Действие отменено; уменьшите число или разрешение экранов.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public static void CheckPreparedBytes(long bytes)
    {
        if (bytes > MaxPreparedBytes)
            throw new RenderLimitException("Предпросмотр превышает бюджет 256 MiB. Действие отменено; уменьшите число или разрешение экранов. Размер Canvas менять не требуется.");
        // Current frames can coexist with candidate frames and their encoding buffers.
        CheckMemory(checked(bytes * 3));
    }
}

internal sealed class RenderWork(CancellationToken token, TimeSpan timeout)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    public void Check()
    {
        token.ThrowIfCancellationRequested();
        if (_clock.Elapsed > timeout)
            throw new RenderLimitException("Операция превысила допустимое время и отменена. Упростите проект и повторите попытку.");
    }
}
