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
    public static int FindVerticalOverlap(Bitmap top, Bitmap bottom, int contentTop = 0, int minOverlap = 24, int tolerance = 14)
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
        var maxOverlap = contentHeight;
        // Sağdaki kaydırma çubuğu (scrollbar) hareketinin eşleşmeyi bozmaması için kenarı hariç tut.
        var compareWidth = width > 100 ? width - Math.Min(18, width / 10) : width;
        var columnStep = Math.Max(1, compareWidth / 240);

        // Alt karedeki her satırın detay/varyans bilgisini çıkar; boş beyaz/koyu şeritlerin
        // yanlış eşleşmeye (buton/kart tekrarına) yol açmasını önler.
        var rowVar = ComputeRowVariances(bottomData.Bytes, stride, contentTop, contentHeight, columnStep, compareWidth);
        const int probeRows = 8;
        var probeOffset = FindBestProbeOffsetFromVariances(rowVar, contentHeight / 2, probeRows);

        var bestScore = double.MaxValue;
        var bestOverlap = -1;
        var fallbackScore = double.MaxValue;
        var fallbackMatches = 0;
        var fallbackOverlap = -1;

        for (var overlap = maxOverlap; overlap >= minOverlap; overlap--)
        {
            var useOffset = overlap >= probeOffset + probeRows ? probeOffset : 0;
            var startRow = contentTop + contentHeight - overlap + useOffset;
            var probeScore = RowDifference(
                topData.Bytes, stride, startRow,
                bottomData.Bytes, stride, contentTop + useOffset,
                probeRows, columnStep, compareWidth);

            // Örtüşme boyunca eşit dağıtılmış 24 satır bandını kontrol et;
            // ayrıca gerçek içerikli (varyans > 6) satırların eşleşme sayısını ayrı izle.
            const int sampleCount = 24;
            var diffs = new double[sampleCount];
            var sumDiff = 0.0;
            var matchCount = 0;
            var detailedTotal = 0;
            var detailedMatchCount = 0;
            var usableSpan = Math.Max(1, overlap - 2);

            for (var s = 0; s < sampleCount; s++)
            {
                var relRow = s * usableSpan / sampleCount;
                var d = RowDifference(
                    topData.Bytes, stride, contentTop + contentHeight - overlap + relRow,
                    bottomData.Bytes, stride, contentTop + relRow,
                    2, columnStep, compareWidth);
                diffs[s] = d;
                sumDiff += d;

                var isDetailed = rowVar[relRow] > 6.0 || (relRow + 1 < rowVar.Length && rowVar[relRow + 1] > 6.0);
                if (isDetailed)
                {
                    detailedTotal++;
                    if (d <= tolerance)
                    {
                        detailedMatchCount++;
                    }
                }

                if (d <= tolerance)
                {
                    matchCount++;
                }
            }

            Array.Sort(diffs);
            var secondWorstDiff = diffs[^2];
            var worstDiff = diffs[^1];

            // Katman 1: Normal / statik sayfa eşleşmesi.
            if (probeScore <= tolerance * 1.4 && secondWorstDiff <= tolerance * 1.6)
            {
                var combined = (probeScore + sumDiff / sampleCount + secondWorstDiff) / 3.0;
                if (combined < bestScore)
                {
                    bestScore = combined;
                    bestOverlap = overlap;

                    if (combined < 0.25 && worstDiff < 1.5)
                    {
                        break;
                    }
                }

                continue;
            }

            // Katman 2: Video/animasyon oynuyorsa satırların en az %35'i ve varsa en az 2 detaylı satır eşleşmeli.
            // Boş beyaz alanların kendi arasında eşleşip bölüm tekrarlamasını önler.
            var minRequiredMatches = Math.Max(4, sampleCount * 35 / 100);
            var minDetailedRequired = detailedTotal >= 2 ? 2 : 0;
            if (matchCount >= minRequiredMatches && detailedMatchCount >= minDetailedRequired)
            {
                var matchedAvg = diffs.Take(matchCount).Average();
                var weightedMatches = detailedMatchCount * 3 + matchCount;
                if (weightedMatches > fallbackMatches || (weightedMatches == fallbackMatches && matchedAvg < fallbackScore))
                {
                    fallbackMatches = weightedMatches;
                    fallbackScore = matchedAvg;
                    fallbackOverlap = overlap;
                }
            }
        }

        return bestOverlap >= 0 ? bestOverlap : fallbackOverlap;
    }

    private static double[] ComputeRowVariances(
        byte[] bytes, int stride, int contentTop, int contentHeight,
        int columnStep, int width)
    {
        var variances = new double[contentHeight];
        var prevMean = 0.0;
        for (var r = 0; r < contentHeight; r++)
        {
            var rowOffset = (contentTop + r) * stride;
            if (rowOffset < 0 || rowOffset + stride > bytes.Length) break;

            long sum = 0;
            long sqSum = 0;
            long count = 0;
            for (var x = 0; x < width; x += columnStep)
            {
                var idx = rowOffset + x * 4;
                var lum = (bytes[idx] + bytes[idx + 1] + bytes[idx + 2]) / 3;
                sum += lum;
                sqSum += lum * (long)lum;
                count++;
            }

            if (count > 0)
            {
                var mean = sum / (double)count;
                var horizVar = sqSum / (double)count - mean * mean;
                var vertDiff = r > 0 ? Math.Abs(mean - prevMean) : 0.0;
                variances[r] = Math.Max(horizVar, vertDiff * vertDiff);
                prevMean = mean;
            }
        }

        return variances;
    }

    private static int FindBestProbeOffsetFromVariances(double[] variances, int searchHeight, int probeRows)
    {
        var bestOffset = 0;
        var bestScore = -1.0;
        var maxOffset = Math.Max(0, Math.Min(variances.Length, searchHeight) - probeRows);

        for (var offset = 0; offset <= maxOffset; offset += 2)
        {
            var sum = 0.0;
            for (var r = 0; r < probeRows; r++)
            {
                sum += variances[offset + r];
            }

            if (sum > bestScore)
            {
                bestScore = sum;
                bestOffset = offset;
            }
        }

        return bestOffset;
    }

    private static List<int> SelectDetailedRows(double[] variances, int overlap, int targetCount)
    {
        var maxRow = Math.Max(1, Math.Min(variances.Length - 2, overlap - 2));
        var detailed = new List<int>(targetCount);

        // Önce gerçekten içerik/metin/kenarlık taşıyan (varyans > 6) satırları eşit aralıklarla al.
        var candidateDetailed = new List<int>();
        for (var r = 0; r <= maxRow; r++)
        {
            if (variances[r] > 6.0)
            {
                candidateDetailed.Add(r);
            }
        }

        if (candidateDetailed.Count >= targetCount)
        {
            for (var i = 0; i < targetCount; i++)
            {
                detailed.Add(candidateDetailed[i * candidateDetailed.Count / targetCount]);
            }

            return detailed;
        }

        // Yeterli detaylı satır yoksa tümünü ekle, kalanı eşit dağıtılmış satırlarla tamamla.
        detailed.AddRange(candidateDetailed);
        var step = Math.Max(1, maxRow / Math.Max(1, targetCount - detailed.Count));
        for (var r = 0; r <= maxRow && detailed.Count < targetCount; r += step)
        {
            if (!detailed.Contains(r))
            {
                detailed.Add(r);
            }
        }

        if (detailed.Count == 0)
        {
            detailed.Add(0);
        }

        return detailed;
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
        var compareWidth = first.Width > 100 ? first.Width - Math.Min(18, first.Width / 10) : first.Width;
        var columnStep = Math.Max(1, compareWidth / 200);
        var header = 0;

        for (var row = 0; row < limit; row += rowStep)
        {
            var diff = RowDifference(a.Bytes, a.Stride, row, b.Bytes, b.Stride, row, 1, columnStep, compareWidth);
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
        var compareWidth = first.Width > 100 ? first.Width - Math.Min(18, first.Width / 10) : first.Width;
        var columnStep = Math.Max(1, compareWidth / 200);
        var diff = RowDifference(
            a.Bytes, a.Stride, 0,
            b.Bytes, b.Stride, 0,
            first.Height, columnStep, compareWidth);

        return diff <= tolerance;
    }

    /// <summary>
    /// Üst görüntüye alt görüntünün yeni satırlarını ekler ve örtüşme bölgesini
    /// son kareyle günceller. Böylece kaydırma sırasında oynayan video/animasyonlar
    /// yatay olarak bölünmek yerine tutarlı tek bir kare olarak görünür.
    /// </summary>
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

        var refreshStart = Math.Max(0, canvas.Height - overlap);
        var drawRows = overlap + newRows;
        graphics.DrawImage(
            frame,
            new Rectangle(0, refreshStart, frame.Width, drawRows),
            new Rectangle(0, contentTop, frame.Width, drawRows),
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
