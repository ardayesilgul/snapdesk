using System.Runtime.InteropServices;
using System.Text;

namespace SnapDesk.Core;

/// <summary>
/// Pencere altında kalan gerçek pencereyi bulmak için yardımcılar.
/// Kendi pencerelerimiz ve görünmez yardımcı pencereler elenir.
/// </summary>
public static class WindowEnumerator
{
    private const uint GaRoot = 2;
    private const int DwmwaExtendedFrameBounds = 9;

    public sealed record WindowInfo(IntPtr Handle, string Title, string ClassName, Rectangle Bounds, uint ProcessId);

    public static WindowInfo? WindowAt(Point screenPoint, IntPtr? excludeHandle = null)
    {
        var handle = WindowFromPoint(new POINT { X = screenPoint.X, Y = screenPoint.Y });
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var root = GetAncestor(handle, GaRoot);
        if (root == IntPtr.Zero)
        {
            root = handle;
        }

        if (excludeHandle.HasValue && root == excludeHandle.Value)
        {
            return null;
        }

        var info = Describe(root);
        if (info is null)
        {
            return null;
        }

        // Masaüstü / kabuk pencereleri seçim hedefi değildir.
        if (info.ClassName is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return null;
        }

        if (info.Bounds.Width < 8 || info.Bounds.Height < 8)
        {
            return null;
        }

        return info;
    }

    public static WindowInfo? Describe(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle) || !IsWindowVisible(handle))
        {
            return null;
        }

        if (IsIconic(handle))
        {
            return null;
        }

        var bounds = GetFrameBounds(handle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var title = new StringBuilder(512);
        GetWindowText(handle, title, title.Capacity);

        var className = new StringBuilder(256);
        GetClassName(handle, className, className.Capacity);

        GetWindowThreadProcessId(handle, out var pid);

        var exStyle = GetWindowLong(handle, -20);
        if ((exStyle & 0x00000080) != 0) // WS_EX_TOOLWINDOW
        {
            return null;
        }

        return new WindowInfo(handle, title.ToString(), className.ToString(), bounds, pid);
    }

    public static Rectangle GetFrameBounds(IntPtr handle)
    {
        if (DwmGetWindowAttribute(handle, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<RECT>()) == 0)
        {
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        if (GetWindowRect(handle, out rect))
        {
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        return Rectangle.Empty;
    }

    /// <summary>
    /// Verilen noktadaki en üstteki yabancı pencere. EnumWindows z-sırasıyla
    /// (üstten alta) gezdiği için ilk eşleşme doğru penceredir; kendi
    /// pencerelerimiz ve kaplanmış (cloaked) UWP pencereleri elenir.
    /// </summary>
    public static WindowInfo? TopWindowAt(Point screenPoint, uint excludeProcessId)
    {
        WindowInfo? found = null;

        EnumWindows((handle, _) =>
        {
            var info = Describe(handle);
            if (info is null)
            {
                return true;
            }

            if (info.ProcessId == excludeProcessId)
            {
                return true;
            }

            if (info.ClassName is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return true;
            }

            if (IsCloaked(handle))
            {
                return true;
            }

            if (!info.Bounds.Contains(screenPoint))
            {
                return true;
            }

            found = info;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private static bool IsCloaked(IntPtr handle)
    {
        try
        {
            if (DwmGetWindowAttributeInt(handle, 14 /* DWMWA_CLOAKED */, out var cloaked, sizeof(int)) == 0)
            {
                return cloaked != 0;
            }
        }
        catch
        {
            // dwmapi yoksa cloaked kabul etme
        }

        return false;
    }

    public static IReadOnlyList<WindowInfo> VisibleWindows()    {
        var list = new List<WindowInfo>();
        EnumWindows((h, _) =>
        {
            var info = Describe(h);
            if (info is not null && !string.IsNullOrWhiteSpace(info.Title))
            {
                list.Add(info);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeInt(IntPtr hWnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
}
