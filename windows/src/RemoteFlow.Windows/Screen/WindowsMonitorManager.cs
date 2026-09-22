using System.Runtime.InteropServices;

namespace RemoteFlow.Windows.Screen;

public sealed record WindowsMonitorInfo(
    int Index,
    string DeviceName,
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary,
    int WorkX,
    int WorkY,
    int WorkWidth,
    int WorkHeight)
{
    public int DisplayIndex => Index + 1;
    public string Resolution => $"{Width} × {Height}";
    public string Position => $"{(X >= 0 ? "+" : "")}{X}, {(Y >= 0 ? "+" : "")}{Y}";
    public string PrimaryLabel => IsPrimary ? "Oui" : "Non";
}

public static class WindowsMonitorManager
{
    private const uint MonitorInfoPrimary = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int CbSize;
        public Rect RcMonitor;
        public Rect RcWork;
        public uint DwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string SzDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, nint lprcMonitor, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayMonitors(
        nint hdc,
        nint lprcClip,
        MonitorEnumProc callback,
        nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(
        nint hMonitor,
        ref MonitorInfoEx lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DisplayDevice lpDisplayDevice,
        uint dwFlags);

    public static IReadOnlyList<WindowsMonitorInfo> GetMonitors()
    {
        var monitors = new List<WindowsMonitorInfo>();
        EnumDisplayMonitors(nint.Zero, nint.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MonitorInfoEx
            {
                CbSize = Marshal.SizeOf<MonitorInfoEx>(),
                SzDevice = string.Empty
            };

            if (!GetMonitorInfo(hMonitor, ref info))
                return true;

            var width = info.RcMonitor.Right - info.RcMonitor.Left;
            var height = info.RcMonitor.Bottom - info.RcMonitor.Top;
            if (width <= 0 || height <= 0)
                return true;

            monitors.Add(new WindowsMonitorInfo(
                Index: monitors.Count,
                DeviceName: info.SzDevice,
                Name: GetFriendlyName(info.SzDevice),
                X: info.RcMonitor.Left,
                Y: info.RcMonitor.Top,
                Width: width,
                Height: height,
                IsPrimary: (info.DwFlags & MonitorInfoPrimary) != 0,
                WorkX: info.RcWork.Left,
                WorkY: info.RcWork.Top,
                WorkWidth: info.RcWork.Right - info.RcWork.Left,
                WorkHeight: info.RcWork.Bottom - info.RcWork.Top));

            return true;
        }, nint.Zero);

        return monitors
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.X)
            .ThenBy(x => x.Y)
            .ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select((monitor, index) => monitor with { Index = index })
            .ToArray();
    }

    private static string GetFriendlyName(string deviceName)
    {
        var device = new DisplayDevice
        {
            Cb = Marshal.SizeOf<DisplayDevice>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceId = string.Empty,
            DeviceKey = string.Empty
        };

        return EnumDisplayDevices(deviceName, 0, ref device, 0) &&
               !string.IsNullOrWhiteSpace(device.DeviceString)
            ? device.DeviceString.Trim()
            : deviceName;
    }
}
