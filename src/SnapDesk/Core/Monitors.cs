using System.Runtime.InteropServices;

namespace SnapDesk.Core;

/// <summary>
/// Monitör numaralandırma. Tüm dikdörtgenler sanal ekranın fiziksel piksel
/// koordinatlarındadır (negatif değerler dahil).
/// </summary>
public static class Monitors
{
    public sealed record MonitorInfo(IntPtr Handle, Rectangle Bounds, Rectangle WorkArea, uint Dpi, bool Primary)
    {
        public double Scale => Dpi / 96.0;
        public string Label => $"{(Primary ? "Ana" : "Ekran")} {Bounds.Width}×{Bounds.Height} @ {Dpi} DPI";
    }

    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var list = new List<MonitorInfo>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            uint dpi = 96;
            try
            {
                if (GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpiX, out _) == 0)
                {
                    dpi = dpiX;
                }
            }
            catch
            {
                // Shcore yoksa 96 varsay.
            }

            var bounds = Rectangle.FromLTRB(
                info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
            var work = Rectangle.FromLTRB(
                info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);

            list.Add(new MonitorInfo(hMonitor, bounds, work, dpi, (info.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);

        return list.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    public static Rectangle VirtualBounds(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            return Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        }

        var left = monitors.Min(m => m.Bounds.Left);
        var top = monitors.Min(m => m.Bounds.Top);
        var right = monitors.Max(m => m.Bounds.Right);
        var bottom = monitors.Max(m => m.Bounds.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    public static MonitorInfo FromPoint(Point point, IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Bounds.Contains(point))
            {
                return monitor;
            }
        }

        return monitors.FirstOrDefault(m => m.Primary) ?? monitors.First();
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
}
