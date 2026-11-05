using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnapDesk.Core;

/// <summary>
/// Ekran görüntüsü alma. Tüm koordinatlar sanal ekranın fiziksel pikselleridir
/// (proses PerMonitorV2 DPI farkındadır, bu yüzden 1:1 eşleşir).
/// </summary>
public static class ScreenCapture
{
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>Tüm monitörleri kapsayan sanal ekranı yakalar.</summary>
    public static Bitmap CaptureVirtualScreen(out Rectangle bounds)
    {
        bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            bounds = new Rectangle(0, 0, 1920, 1080);
        }

        return CaptureRegion(bounds);
    }

    /// <summary>Verilen fiziksel piksel bölgesini yakalar. Negatif koordinat desteklidir.</summary>
    public static Bitmap CaptureRegion(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentException("Yakalama bölgesi geçersiz.", nameof(region));
        }

        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        var destination = graphics.GetHdc();
        var source = GetDC(IntPtr.Zero);

        try
        {
            // CAPTUREBLT katmanlı pencereleri (araç ipuçları, gölgeler) de dahil eder.
            if (!BitBlt(destination, 0, 0, region.Width, region.Height, source,
                        region.Left, region.Top, SRCCOPY | CAPTUREBLT))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Ekran yakalanamadı (BitBlt hatası {error}).");
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, source);
            graphics.ReleaseHdc(destination);
        }

        return bitmap;
    }

    /// <summary>
    /// Tek bir pencereyi yakalar. Donanım hızlandırmalı pencerelerde PrintWindow
    /// boş dönebilir; o durumda ekrandan bölge yakalamaya düşer.
    /// </summary>
    public static Bitmap? CaptureWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            return null;
        }

        if (IsIconic(handle))
        {
            return null;
        }

        if (!GetWindowRect(handle, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(handle, hdc, PW_RENDERFULLCONTENT))
                {
                    return CaptureRegion(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        // PrintWindow bazı uygulamalarda tamamen siyah döner; bu durumda ekran kopyası daha doğrudur.
        if (IsMostlyBlank(bitmap))
        {
            bitmap.Dispose();
            return CaptureRegion(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
        }

        return bitmap;
    }

    private static bool IsMostlyBlank(Bitmap bitmap)
    {
        var sampleStep = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 40);
        var nonBlack = 0;
        var total = 0;

        for (var y = 0; y < bitmap.Height; y += sampleStep)
        {
            for (var x = 0; x < bitmap.Width; x += sampleStep)
            {
                var pixel = bitmap.GetPixel(x, y);
                total++;
                if (pixel.R > 6 || pixel.G > 6 || pixel.B > 6 || pixel.A > 6)
                {
                    nonBlack++;
                }
            }
        }

        return total > 0 && nonBlack < total / 100;
    }

    /// <summary>Pikselleri bloklayarak mozaik görüntü üretir.</summary>
    public static Bitmap Pixelate(Bitmap source, Rectangle region, int blockSize)
    {
        var result = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        blockSize = Math.Max(2, blockSize);

        var width = region.Width;
        var height = region.Height;
        var pixels = new byte[width * height * 4];

        var data = source.LockBits(region, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * width * 4, width * 4);
            }
        }
        finally
        {
            source.UnlockBits(data);
        }

        var output = new byte[pixels.Length];
        for (var by = 0; by < height; by += blockSize)
        {
            for (var bx = 0; bx < width; bx += blockSize)
            {
                long r = 0, g = 0, b = 0, a = 0;
                var count = 0;

                var maxY = Math.Min(by + blockSize, height);
                var maxX = Math.Min(bx + blockSize, width);

                for (var y = by; y < maxY; y++)
                {
                    for (var x = bx; x < maxX; x++)
                    {
                        var index = (y * width + x) * 4;
                        b += pixels[index];
                        g += pixels[index + 1];
                        r += pixels[index + 2];
                        a += pixels[index + 3];
                        count++;
                    }
                }

                if (count == 0) continue;

                var avgB = (byte)(b / count);
                var avgG = (byte)(g / count);
                var avgR = (byte)(r / count);
                var avgA = (byte)(a / count);

                for (var y = by; y < maxY; y++)
                {
                    for (var x = bx; x < maxX; x++)
                    {
                        var index = (y * width + x) * 4;
                        output[index] = avgB;
                        output[index + 1] = avgG;
                        output[index + 2] = avgR;
                        output[index + 3] = avgA;
                    }
                }
            }
        }

        var resultData = result.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(output, y * width * 4, resultData.Scan0 + y * resultData.Stride, width * 4);
            }
        }
        finally
        {
            result.UnlockBits(resultData);
        }

        return result;
    }

    /// <summary>Bölgeyi ipeksi, yumuşak Gauss / kutu bulanıklığıyla pürüzsüzleştirir.</summary>
    public static Bitmap Blur(Bitmap source, Rectangle region, int radius)
    {
        var width = Math.Max(1, region.Width);
        var height = Math.Max(1, region.Height);

        // 1. Kaynak bölgeyi kırp
        using var cropped = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(cropped))
        {
            g.DrawImage(source, new Rectangle(0, 0, width, height), region, GraphicsUnit.Pixel);
        }

        // 2. Çözünürlüğü yarıya indirerek bulanıklık yayılımını ve hızını artır
        var downScale = Math.Clamp(radius / 4, 2, 6);
        var dw = Math.Max(2, width / downScale);
        var dh = Math.Max(2, height / downScale);

        using var downsampled = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(downsampled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(cropped, 0, 0, dw, dh);
        }

        // 3. İki yönlü (yatay + dikey) 2 geçişli separable kutu bulanıklığı
        var pixels = new byte[dw * dh * 4];
        var data = downsampled.LockBits(new Rectangle(0, 0, dw, dh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < dh; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * dw * 4, dw * 4);
            }
        }
        finally
        {
            downsampled.UnlockBits(data);
        }

        var temp = new byte[pixels.Length];
        var blurRadius = Math.Clamp(radius / 3, 2, 8);

        // Geçiş 1
        BoxBlurHorizontal(pixels, temp, dw, dh, blurRadius);
        BoxBlurVertical(temp, pixels, dw, dh, blurRadius);

        // Geçiş 2 (Gauss eğrisini elde etmek için)
        BoxBlurHorizontal(pixels, temp, dw, dh, blurRadius);
        BoxBlurVertical(temp, pixels, dw, dh, blurRadius);

        using var blurredSmall = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
        var resData = blurredSmall.LockBits(new Rectangle(0, 0, dw, dh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < dh; y++)
            {
                Marshal.Copy(pixels, y * dw * 4, resData.Scan0 + y * resData.Stride, dw * 4);
            }
        }
        finally
        {
            blurredSmall.UnlockBits(resData);
        }

        // 4. Orijinal boyuta yüksek kaliteli bicubic interpolasyonla genişlet
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(blurredSmall, new Rectangle(0, 0, width, height));
        }

        return result;
    }

    private static void BoxBlurHorizontal(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (var y = 0; y < h; y++)
        {
            var row = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                int b = 0, g = 0, rCol = 0, a = 0;
                var count = 0;
                var minK = Math.Max(0, x - r);
                var maxK = Math.Min(w - 1, x + r);

                for (var k = minK; k <= maxK; k++)
                {
                    var idx = row + k * 4;
                    b += src[idx];
                    g += src[idx + 1];
                    rCol += src[idx + 2];
                    a += src[idx + 3];
                    count++;
                }

                var outIdx = row + x * 4;
                dst[outIdx] = (byte)(b / count);
                dst[outIdx + 1] = (byte)(g / count);
                dst[outIdx + 2] = (byte)(rCol / count);
                dst[outIdx + 3] = (byte)(a / count);
            }
        }
    }

    private static void BoxBlurVertical(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                int b = 0, g = 0, rCol = 0, a = 0;
                var count = 0;
                var minK = Math.Max(0, y - r);
                var maxK = Math.Min(h - 1, y + r);

                for (var k = minK; k <= maxK; k++)
                {
                    var idx = (k * w + x) * 4;
                    b += src[idx];
                    g += src[idx + 1];
                    rCol += src[idx + 2];
                    a += src[idx + 3];
                    count++;
                }

                var outIdx = (y * w + x) * 4;
                dst[outIdx] = (byte)(b / count);
                dst[outIdx + 1] = (byte)(g / count);
                dst[outIdx + 2] = (byte)(rCol / count);
                dst[outIdx + 3] = (byte)(a / count);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr hDest, int xDest, int yDest, int width, int height,
        IntPtr hSrc, int xSrc, int ySrc, int rop);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hDc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
