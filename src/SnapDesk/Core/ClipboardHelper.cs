using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnapDesk.Core;

/// <summary>
/// Doğrudan Win32 pano API'si (OpenClipboard / SetClipboardData) üzerinden
/// ham CF_DIB ve ham PNG verisi yazar. .NET DataObject serileştirme başlığı
/// eklemediği için tarayıcı, Electron, Office ve Windows Pano Geçmişi'nde
/// (Win+V) sorunsuz yapıştırma sağlar.
/// </summary>
public static class ClipboardHelper
{
    private const uint GmemMoveable = 0x0002;
    private const uint CfDib = 8;
    private const uint CfUnicodeText = 13;

    public static void CopyImage(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var pngBytes = EncodePng(bitmap);
        var dibBytes = EncodeDib32(bitmap);
        var pngFormat = RegisterClipboardFormat("PNG");

        OpenClipboardWithRetry();
        try
        {
            if (!EmptyClipboard())
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Pano temizlenemedi.");
            }

            SetGlobalData(CfDib, dibBytes);
            if (pngFormat != 0)
            {
                SetGlobalData(pngFormat, pngBytes);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void CopyText(string text)
    {
        text ??= string.Empty;
        var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");

        OpenClipboardWithRetry();
        try
        {
            if (!EmptyClipboard())
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Pano temizlenemedi.");
            }

            SetGlobalData(CfUnicodeText, bytes);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static void OpenClipboardWithRetry()
    {
        var lastError = 0;
        for (var attempt = 0; attempt < 15; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return;
            }

            lastError = Marshal.GetLastWin32Error();
            Thread.Sleep(50);
        }

        throw new Win32Exception(lastError, "Windows panosu başka bir uygulama tarafından kilitli.");
    }

    private static void SetGlobalData(uint format, byte[] data)
    {
        var hMem = GlobalAlloc(GmemMoveable, (UIntPtr)data.Length);
        if (hMem == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Pano belleği ayrılamadı.");
        }

        var ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            GlobalFree(hMem);
            throw new Win32Exception(err, "Pano belleği kilitlenemedi.");
        }

        try
        {
            Marshal.Copy(data, 0, ptr, data.Length);
        }
        finally
        {
            GlobalUnlock(hMem);
        }

        if (SetClipboardData(format, hMem) == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            GlobalFree(hMem);
            throw new Win32Exception(err, $"Pano biçimi yazılamadı ({format}).");
        }
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// 40 baytlık BITMAPINFOHEADER + alttan üste 32-bit BGRA pikseller (BI_RGB).
    /// Windows tüm GDI/Win32 uygulamaları için bundan CF_BITMAP ve CF_DIBV5 türetir.
    /// </summary>
    private static byte[] EncodeDib32(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var stride = width * 4;
        var pixelBytes = height * stride;
        var dib = new byte[40 + pixelBytes];

        // BITMAPINFOHEADER (40 bayt)
        BitConverter.TryWriteBytes(dib.AsSpan(0, 4), 40);          // biSize
        BitConverter.TryWriteBytes(dib.AsSpan(4, 4), width);       // biWidth
        BitConverter.TryWriteBytes(dib.AsSpan(8, 4), height);      // biHeight (>0 => bottom-up)
        BitConverter.TryWriteBytes(dib.AsSpan(12, 2), (short)1);   // biPlanes
        BitConverter.TryWriteBytes(dib.AsSpan(14, 2), (short)32);  // biBitCount
        BitConverter.TryWriteBytes(dib.AsSpan(16, 4), 0);          // biCompression = BI_RGB
        BitConverter.TryWriteBytes(dib.AsSpan(20, 4), pixelBytes); // biSizeImage

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y++)
            {
                // Bottom-up DIB: alt satır önce yazılır.
                var srcRow = data.Scan0 + (height - 1 - y) * data.Stride;
                Marshal.Copy(srcRow, dib, 40 + y * stride, stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return dib;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
