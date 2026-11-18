using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SnapDesk.Ocr;

public sealed record OcrLanguage(string Tag, string DisplayName);

public sealed record OcrResult(string Text, IReadOnlyList<string> Lines, string LanguageTag);

public sealed class OcrUnavailableException : Exception
{
    public OcrUnavailableException(string message, IReadOnlyList<OcrLanguage> available)
        : base(message)
    {
        Available = available;
    }

    public IReadOnlyList<OcrLanguage> Available { get; }
}

/// <summary>
/// Windows.Media.Ocr üzerinden yerel, çevrimdışı metin tanıma.
/// Küçük/ince kırpmalarda boyutlandırma, kenar boşluğu (quiet zone) ve
/// düşük kontrastlı/koyu temalı görüntülerde kontrast/tersleme desteği uygular.
/// </summary>
public static class OcrService
{
    private const int PaddingPx = 40;
    private const float MaxScale = 4.5f;

    public static IReadOnlyList<OcrLanguage> AvailableLanguages()
        => OcrEngine.AvailableRecognizerLanguages
            .Select(l => new OcrLanguage(l.LanguageTag, l.DisplayName))
            .ToList();

    public static bool IsSupported(string tag)
    {
        try
        {
            return OcrEngine.IsLanguageSupported(new Language(tag));
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveLanguage(string? preferred)
    {
        var available = OcrEngine.AvailableRecognizerLanguages;
        if (available.Count == 0)
        {
            throw new OcrUnavailableException(
                "Bu bilgisayarda hiçbir OCR dil paketi yüklü değil. " +
                "Ayarlar > Zaman ve dil > Dil ve bölge bölümünden bir dil ekleyip " +
                "dil özelliklerinde 'Temel yazma' bileşenini yükleyin.",
                Array.Empty<OcrLanguage>());
        }

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = available.FirstOrDefault(l =>
                l.LanguageTag.Equals(preferred, StringComparison.OrdinalIgnoreCase) ||
                l.LanguageTag.StartsWith(preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.LanguageTag;
            }

            throw new OcrUnavailableException(
                $"'{preferred}' OCR dil paketi bu bilgisayarda yüklü değil. " +
                "Ayarlar > Zaman ve dil > Dil ve bölge bölümünden ilgili dili ekleyip " +
                "dil özelliklerinde 'Temel yazma' bileşenini yükleyin.",
                AvailableLanguages());
        }

        var turkish = available.FirstOrDefault(l => l.LanguageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase));
        if (turkish is not null)
        {
            return turkish.LanguageTag;
        }

        return available[0].LanguageTag;
    }

    public static async Task<OcrResult> RecognizeAsync(Bitmap bitmap, string languageTag)
    {
        var language = new Language(languageTag);
        if (!OcrEngine.IsLanguageSupported(language))
        {
            throw new OcrUnavailableException(
                $"'{languageTag}' dil paketi yüklü değil.",
                AvailableLanguages());
        }

        var engine = OcrEngine.TryCreateFromLanguage(language)
            ?? throw new OcrUnavailableException(
                $"'{languageTag}' için OCR motoru oluşturulamadı.",
                AvailableLanguages());

        var bgColor = SampleEdgeBackground(bitmap);
        using var prepared = Prepare(bitmap, bgColor);
        var primary = await RunEngineAsync(engine, prepared, languageTag);

        // Küçük veya boş sonuç veren kırpmalarda gri ton + kontrast germe (+ koyu zeminde tersleme) dene.
        var isSmallCapture = bitmap.Width * bitmap.Height <= 450_000;
        if (primary.Lines.Count == 0 || isSmallCapture)
        {
            using var enhanced = EnhanceForOcr(prepared, bgColor);
            var secondary = await RunEngineAsync(engine, enhanced, languageTag);
            if (ScoreResult(secondary) > ScoreResult(primary))
            {
                return secondary;
            }
        }

        return primary;
    }

    private static async Task<OcrResult> RunEngineAsync(OcrEngine engine, Bitmap bitmap, string languageTag)
    {
        var bytes = ToBgra(bitmap);
        using var softwareBitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            bitmap.Width,
            bitmap.Height,
            BitmapAlphaMode.Ignore);
        softwareBitmap.CopyFromBuffer(bytes.AsBuffer());

        var result = await engine.RecognizeAsync(softwareBitmap);

        var lines = result.Lines
            .Select(line => string.Join(" ", line.Words.Select(w => w.Text)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var text = string.Join(Environment.NewLine, lines);

        if (string.IsNullOrWhiteSpace(text))
        {
            text = result.Text ?? string.Empty;
        }

        return new OcrResult(text, lines, languageTag);
    }

    private static int ScoreResult(OcrResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return 0;
        }

        var alphaNum = 0;
        foreach (var ch in result.Text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                alphaNum++;
            }
        }

        return alphaNum * 2 + result.Lines.Count;
    }

    /// <summary>
    /// Küçük veya ince yatay şeritleri hem genişlik hem yüksekliğe göre büyütür
    /// ve kenarlara arka plan renginde sessiz bölge (padding) ekler.
    /// </summary>
    private static Bitmap Prepare(Bitmap source, Color bgColor)
    {
        var scaleW = source.Width < 1200 && source.Width > 0 ? 1200f / source.Width : 1f;
        var scaleH = source.Height < 160 && source.Height > 0 ? 160f / source.Height : 1f;
        var scale = Math.Min(MaxScale, Math.Max(1f, Math.Max(scaleW, scaleH)));

        var maxDimension = (int)OcrEngine.MaxImageDimension - PaddingPx * 2;
        if (source.Width * scale > maxDimension || source.Height * scale > maxDimension)
        {
            scale = Math.Min(maxDimension / (float)source.Width, maxDimension / (float)source.Height);
            scale = Math.Max(1f, scale);
        }

        var scaledW = Math.Max(1, (int)Math.Round(source.Width * scale));
        var scaledH = Math.Max(1, (int)Math.Round(source.Height * scale));
        var pad = Math.Min(PaddingPx, Math.Max(16, (int)(Math.Min(scaledW, scaledH) * 0.25f)));

        var target = new Bitmap(scaledW + pad * 2, scaledH + pad * 2, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(target);
        graphics.Clear(bgColor);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(
            source,
            new Rectangle(pad, pad, scaledW, scaledH),
            0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel,
            attrs);

        return target;
    }

    /// <summary>
    /// Gri tonlama + otomatik kontrast germe uygular; zemin koyuysa metni
    /// koyu-üzeri-açık formata çevirerek tanıma oranını artırır.
    /// </summary>
    private static Bitmap EnhanceForOcr(Bitmap source, Color bgColor)
    {
        var width = source.Width;
        var height = source.Height;
        var srcBytes = ToBgra(source);
        var dstBytes = new byte[srcBytes.Length];

        var bgLum = (int)(0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B);
        var invert = bgLum < 128;

        var minLum = 255;
        var maxLum = 0;
        var pixelCount = width * height;
        var lum = new byte[pixelCount];

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;
            var l = (byte)Math.Clamp((int)(0.114 * srcBytes[idx] + 0.587 * srcBytes[idx + 1] + 0.299 * srcBytes[idx + 2]), 0, 255);
            lum[i] = l;
            if (l < minLum) minLum = l;
            if (l > maxLum) maxLum = l;
        }

        var range = Math.Max(1, maxLum - minLum);
        for (var i = 0; i < pixelCount; i++)
        {
            var stretched = (lum[i] - minLum) * 255 / range;
            if (invert)
            {
                stretched = 255 - stretched;
            }

            var val = (byte)Math.Clamp(stretched, 0, 255);
            var idx = i * 4;
            dstBytes[idx] = val;
            dstBytes[idx + 1] = val;
            dstBytes[idx + 2] = val;
            dstBytes[idx + 3] = 255;
        }

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(dstBytes, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
            }
        }
        finally
        {
            result.UnlockBits(data);
        }

        return result;
    }

    private static Color SampleEdgeBackground(Bitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return Color.White;
        }

        var samplesR = new List<byte>(64);
        var samplesG = new List<byte>(64);
        var samplesB = new List<byte>(64);

        var stepX = Math.Max(1, bitmap.Width / 16);
        var stepY = Math.Max(1, bitmap.Height / 16);

        for (var x = 0; x < bitmap.Width; x += stepX)
        {
            var top = bitmap.GetPixel(x, 0);
            samplesR.Add(top.R); samplesG.Add(top.G); samplesB.Add(top.B);

            var bottom = bitmap.GetPixel(x, bitmap.Height - 1);
            samplesR.Add(bottom.R); samplesG.Add(bottom.G); samplesB.Add(bottom.B);
        }

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            var left = bitmap.GetPixel(0, y);
            samplesR.Add(left.R); samplesG.Add(left.G); samplesB.Add(left.B);

            var right = bitmap.GetPixel(bitmap.Width - 1, y);
            samplesR.Add(right.R); samplesG.Add(right.G); samplesB.Add(right.B);
        }

        samplesR.Sort();
        samplesG.Sort();
        samplesB.Sort();

        var mid = samplesR.Count / 2;
        return Color.FromArgb(samplesR[mid], samplesG[mid], samplesB[mid]);
    }

    private static byte[] ToBgra(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var bytes = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bitmap.Width * 4, bitmap.Width * 4);
            }

            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
