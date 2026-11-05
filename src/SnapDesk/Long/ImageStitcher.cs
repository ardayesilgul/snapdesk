using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnapDesk.Long;

/// <summary>
/// Dikey görüntü birleştirme. Örtüşme, üst görüntünün alt satırları ile alt
/// görüntünün üst satırları karşılaştırılarak bulunur. Sabit başlık (yapışkan
/// başlık) tespit edilirse yalnızca ilk karede tutulur.
/// </summary>
public static class ImageStitcher
{
    /// <summary>İki kare arasındaki dikey örtüşme satır sayısı; bulunamazsa -1.</summary>
    public static int FindVerticalOverlap(Bitmap top, Bitmap bottom, int contentTop = 0, int minOverlap = 30, int tolerance = 14)
    {
        if (top.Width != bottom.Width || top.Height != bottom.Height)
        {
            return -1;
        }

        var width = top.Width;
        var height = top.Height;
        contentTop = Math.Clamp(contentTop, 0, height / 3);
        var contentHeight = height - contentTop;

        if (contentHeight < minOverlap + 8)
        {
            return -1;
        }

        var topData = ReadPixels(top);
        var bottomData = ReadPixels(bottom);

        var stride = topData.Stride;
        // İçerik hiç kaymadıysa örtüşme içerik yüksekliğinin tamamıdır.
        // -8 ile sınırlamak, sabit sayfalarda her karede 8 sahte satır ekliyordu.
        var maxOverlap = contentHeight;

        // Alt karenin içerik başındaki ince bir şerit, üst karede aranır.
        const int probeRows = 6;
        var columnStep = Math.Max(1, width / 240);
        var bestScore = double.MaxValue;
        var bestOverlap = -1;

        for (var overlap = maxOverlap; overlap >= minOverlap; overlap--)
        {
            var startRow = contentTop + contentHeight - overlap;
            var score = RowDifference(
                topData.Bytes, stride, startRow,
                bottomData.Bytes, stride, contentTop,
                probeRows, columnStep, width);

            if (score < bestScore)
            {
                bestScore = score;
                bestOverlap = overlap;

                if (score < 1.5)
                {
                    break;
                }
            }
        }

        if (bestOverlap < 0 || bestScore > tolerance)
        {
            return -1;
        }

        // Ek bantlarla doğrula: örtüşmenin 1/3 ve 2/3 noktaları.
        var verifyRows = 4;
        var a1 = contentTop + contentHeight - bestOverlap + bestOverlap / 3;
        var b1 = contentTop + bestOverlap / 3;
        var a2 = contentTop + contentHeight - bestOverlap + bestOverlap * 2 / 3;
        var b2 = contentTop + bestOverlap * 2 / 3;

        var check1 = RowDifference(topData.Bytes, stride, a1, bottomData.Bytes, stride, b1, verifyRows, columnStep, width);
        var check2 = RowDifference(topData.Bytes, stride, a2, bottomData.Bytes, stride, b2, verifyRows, columnStep, width);

        if (check1 > tolerance * 2 || check2 > tolerance * 2)
        {
            return -1;
        }

        return bestOverlap;
    }

    /// <summary>
    /// Üstte sabit kalan bant yüksekliği. İki karede de aynı olan satır sayısı.
    /// 8 satırdan azsa sabit başlık yok sayılır; karenin 1/4'ü ile sınırlıdır.
    /// </summary>
    public static int DetectFixedHeader(Bitmap first, Bitmap second, int tolerance = 10)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return 0;
        }

        var limit = Math.Max(8, first.Height / 4);
        var a = ReadPixels(first);
        var b = ReadPixels(second);

        var rowStep = 1;
        var columnStep = Math.Max(1, first.Width / 200);
        var header = 0;

        for (var row = 0; row < limit; row += rowStep)
        {
            var diff = RowDifference(a.Bytes, a.Stride, row, b.Bytes, b.Stride, row, 1, columnStep, first.Width);
            if (diff > tolerance)
            {
                break;
            }

            header = row + 1;
        }

        return header >= 8 ? header : 0;
    }

    /// <summary>
    /// İki kare neredeyse aynı mı? Sayfa kaydırılamadıysa örtüşme aramak
    /// yerine yakalamayı bitirmek için kullanılır. İmleç farkı gibi küçük
    /// değişiklikler tolerans içinde kalır.
    /// </summary>
    public static bool FramesNearlyEqual(Bitmap first, Bitmap second, double tolerance = 1.5)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return false;
        }

        var a = ReadPixels(first);
        var b = ReadPixels(second);
        var columnStep = Math.Max(1, first.Width / 200);
        var diff = RowDifference(
            a.Bytes, a.Stride, 0,
            b.Bytes, b.Stride, 0,
            first.Height, columnStep, first.Width);

        return diff <= tolerance;
    }

    /// <summary>Üst görüntüye, alt görüntünün yeni satırlarını ekler.</summary>
    public static Bitmap Append(Bitmap canvas, Bitmap frame, int contentTop, int overlap)
    {
        var newRows = frame.Height - contentTop - overlap;
        if (newRows <= 0)
        {
            return new Bitmap(canvas);
        }

        var result = new Bitmap(canvas.Width, canvas.Height + newRows, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(canvas, new Rectangle(0, 0, canvas.Width, canvas.Height));

        graphics.DrawImage(
            frame,
            new Rectangle(0, canvas.Height, frame.Width, newRows),
            new Rectangle(0, contentTop + overlap, frame.Width, newRows),
            GraphicsUnit.Pixel);

        return result;
    }

    private sealed record PixelData(byte[] Bytes, int Stride);

    private static PixelData ReadPixels(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var bytes = new byte[bitmap.Height * bitmap.Width * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bitmap.Width * 4, bitmap.Width * 4);
            }

            return new PixelData(bytes, bitmap.Width * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static double RowDifference(
        byte[] a, int aStride, int aRow,
        byte[] b, int bStride, int bRow,
        int rows, int columnStep, int width)
    {
        long total = 0;
        long samples = 0;

        for (var r = 0; r < rows; r++)
        {
            var aOffset = (aRow + r) * aStride;
            var bOffset = (bRow + r) * bStride;

            if (aOffset < 0 || bOffset < 0 ||
                aOffset + aStride > a.Length || bOffset + bStride > b.Length)
            {
                return double.MaxValue;
            }

            for (var x = 0; x < width; x += columnStep)
            {
                var indexA = aOffset + x * 4;
                var indexB = bOffset + x * 4;

                total += Math.Abs(a[indexA] - b[indexB]);
                total += Math.Abs(a[indexA + 1] - b[indexB + 1]);
                total += Math.Abs(a[indexA + 2] - b[indexB + 2]);
                samples += 3;
            }
        }

        return samples == 0 ? double.MaxValue : total / (double)samples;
    }
}
