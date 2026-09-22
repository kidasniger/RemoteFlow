using System.Runtime.InteropServices;

namespace RemoteFlow.Windows.Diagnostics;

public sealed record WindowsSystemMetrics(
    double? CpuUsagePercent,
    ulong TotalMemoryBytes,
    ulong UsedMemoryBytes,
    ulong AvailableMemoryBytes,
    double MemoryUsagePercent,
    TimeSpan Uptime,
    DateTimeOffset SampledAtUtc);

public static class WindowsSystemMetricsReader
{
    private static readonly object Gate = new();
    private static ulong _previousIdle;
    private static ulong _previousKernel;
    private static ulong _previousUser;
    private static bool _hasCpuSample;

    public static WindowsSystemMetrics Sample()
    {
        GetMemory(out var totalMemory, out var availableMemory, out var usedMemory, out var memoryUsagePercent);
        var cpuUsage = GetCpuUsagePercent();
        var uptime = TimeSpan.FromMilliseconds(GetTickCount64());

        return new WindowsSystemMetrics(
            cpuUsage,
            totalMemory,
            usedMemory,
            availableMemory,
            memoryUsagePercent,
            uptime,
            DateTimeOffset.UtcNow);
    }

    private static double? GetCpuUsagePercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return null;

        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);

        lock (Gate)
        {
            if (!_hasCpuSample)
            {
                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;
                _hasCpuSample = true;
                return null;
            }

            var idleDelta = idle - _previousIdle;
            var kernelDelta = kernel - _previousKernel;
            var userDelta = user - _previousUser;

            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;

            var totalDelta = kernelDelta + userDelta;
            if (totalDelta == 0)
                return 0;

            var busyDelta = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
            return Math.Clamp(busyDelta * 100d / totalDelta, 0d, 100d);
        }
    }

    private static void GetMemory(
        out ulong totalMemory,
        out ulong availableMemory,
        out ulong usedMemory,
        out double usagePercent)
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            totalMemory = 0;
            availableMemory = 0;
            usedMemory = 0;
            usagePercent = 0;
            return;
        }

        totalMemory = status.TotalPhysicalMemory;
        availableMemory = status.AvailablePhysicalMemory;
        usedMemory = totalMemory >= availableMemory
            ? totalMemory - availableMemory
            : 0;

        usagePercent = totalMemory == 0
            ? 0
            : Math.Clamp(usedMemory * 100d / totalMemory, 0d, 100d);
    }

    private static ulong ToUInt64(SystemTime time) =>
        ((ulong)time.HighDateTime << 32) | time.LowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out SystemTime idleTime,
        out SystemTime kernelTime,
        out SystemTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
