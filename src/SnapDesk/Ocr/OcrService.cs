using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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
/// Dil paketi yoksa sessizce İngilizceye düşmez; hangi dillerin kurulu
/// olduğunu ve ne yapılması gerektiğini söyleyen bir hata fırlatır.
/// </summary>
public static class OcrService
{
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

        using var prepared = Prepare(bitmap);

        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)prepared.Width,
            (uint)prepared.Height,
            96,
            96,
            ToBgra(prepared));
        await encoder.FlushAsync();

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

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

    /// <summary>
    /// Küçük görüntüleri büyütmek tanıma kalitesini belirgin artırır.
    /// Windows OCR en fazla 10000 piksel kenar kabul eder.
    /// </summary>
    private static Bitmap Prepare(Bitmap source)
    {
        var scale = 1f;

        if (source.Width < 1200 && source.Width > 0)
        {
            scale = Math.Min(3f, 1200f / source.Width);
        }

        var maxDimension = OcrEngine.MaxImageDimension;
        if (source.Width * scale > maxDimension || source.Height * scale > maxDimension)
        {
            scale = Math.Min(maxDimension / (float)source.Width, maxDimension / (float)source.Height);
        }

        if (scale <= 1.01f)
        {
            return new Bitmap(source);
        }

        var target = new Bitmap(
            Math.Max(1, (int)(source.Width * scale)),
            Math.Max(1, (int)(source.Height * scale)));
        using var graphics = Graphics.FromImage(target);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, new Rectangle(0, 0, target.Width, target.Height));
        return target;
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
