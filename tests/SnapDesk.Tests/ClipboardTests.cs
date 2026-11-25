using System.Drawing;
using System.Runtime.InteropServices;
using SnapDesk.Core;

namespace SnapDesk.Tests;

public class ClipboardTests
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClipboardFormatName(uint format, System.Text.StringBuilder lpszFormatName, int cchMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    private static T RunSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new Exception("STA failed", error);
        }
        return result;
    }

    [Fact]
    public void CopyImage_WritesValidWin32DibAndPngBytes()
    {
        var report = RunSta(() =>
        {
            using var bmp = new Bitmap(40, 30, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(255, 10, 120, 240));
            }

            ClipboardHelper.CopyImage(bmp);

            var pngFmt = RegisterClipboardFormat("PNG");
            var formats = new List<string>();
            byte[]? pngBytes = null;
            uint dibSize = 0;

            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    uint fmt = 0;
                    while ((fmt = EnumClipboardFormats(fmt)) != 0)
                    {
                        var sb = new System.Text.StringBuilder(128);
                        var len = GetClipboardFormatName(fmt, sb, sb.Capacity);
                        var name = len > 0 ? sb.ToString() : $"Std:{fmt}";
                        formats.Add(name);

                        if (fmt == 8 /* CF_DIB */ || fmt == 17 /* CF_DIBV5 */)
                        {
                            var h = GetClipboardData(fmt);
                            if (h != IntPtr.Zero)
                            {
                                dibSize = (uint)GlobalSize(h);
                            }
                        }
                        else if (fmt == pngFmt)
                        {
                            var h = GetClipboardData(fmt);
                            if (h != IntPtr.Zero)
                            {
                                var sz = (int)GlobalSize(h);
                                var ptr = GlobalLock(h);
                                if (ptr != IntPtr.Zero)
                                {
                                    try
                                    {
                                        pngBytes = new byte[sz];
                                        Marshal.Copy(ptr, pngBytes, 0, sz);
                                    }
                                    finally
                                    {
                                        GlobalUnlock(h);
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }

            return (formats, pngBytes, dibSize);
        });

        Assert.NotNull(report.pngBytes);
        Assert.True(report.pngBytes!.Length > 8, $"Formats: {string.Join(", ", report.formats)}");
        // PNG magic header: 89 50 4E 47 0D 0A 1A 0A
        Assert.Equal(0x89, report.pngBytes[0]);
        Assert.Equal(0x50, report.pngBytes[1]);
        Assert.Equal(0x4E, report.pngBytes[2]);
        Assert.Equal(0x47, report.pngBytes[3]);
        Assert.True(report.dibSize > 40, $"DIB missing or empty. Formats: {string.Join(", ", report.formats)}");

        // WinForms Clipboard API'nin de görüntüyü sorunsuz okuyabildiğini doğrula
        var winformsOk = RunSta(() =>
        {
            if (!Clipboard.ContainsImage()) return false;
            using var img = Clipboard.GetImage() as Bitmap;
            if (img is null || img.Width != 40 || img.Height != 30) return false;
            var px = img.GetPixel(10, 10);
            return px.R == 10 && px.G == 120 && px.B == 240;
        });
        Assert.True(winformsOk);
    }

    [Fact]
    public void CopyText_WritesUnicodeText()
    {
        var readBack = RunSta(() =>
        {
            ClipboardHelper.CopyText("SnapDesk Türkçe ğüşöçı");
            return Clipboard.GetText();
        });

        Assert.Equal("SnapDesk Türkçe ğüşöçı", readBack);
    }
}
