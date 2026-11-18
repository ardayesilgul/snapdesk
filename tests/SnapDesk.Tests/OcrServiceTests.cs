using System.Drawing;
using System.Drawing.Text;
using SnapDesk.Ocr;

namespace SnapDesk.Tests;

public class OcrServiceTests
{
    private static Bitmap RenderText(string text, int width, int height, float fontSize, Color bg, Color fg, int x = 2, int y = 2)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(bg);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(fg);
        g.DrawString(text, font, brush, x, y);
        return bmp;
    }

    [Fact]
    public async Task Recognize_TinyTightlyCroppedText()
    {
        var langs = OcrService.AvailableLanguages();
        if (langs.Count == 0) return;

        var tag = OcrService.ResolveLanguage(null);
        using var bmp = RenderText("149,90 TL", 80, 18, 12f, Color.White, Color.Black, 1, 1);
        var result = await OcrService.RecognizeAsync(bmp, tag);

        Assert.Contains("149", result.Text);
    }

    [Fact]
    public async Task Recognize_DarkModeSmallText()
    {
        var langs = OcrService.AvailableLanguages();
        if (langs.Count == 0) return;

        var tag = OcrService.ResolveLanguage(null);
        using var bmp = RenderText("ayarlar.json", 110, 22, 13f, Color.FromArgb(30, 30, 34), Color.FromArgb(220, 220, 225), 2, 2);
        var result = await OcrService.RecognizeAsync(bmp, tag);

        Assert.Contains("ayarlar", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recognize_WideThinStrip()
    {
        var langs = OcrService.AvailableLanguages();
        if (langs.Count == 0) return;

        var tag = OcrService.ResolveLanguage(null);
        using var bmp = RenderText("https://github.com/ardayesilgul/snapdesk", 1300, 24, 13f, Color.White, Color.FromArgb(30, 30, 30), 4, 3);
        var result = await OcrService.RecognizeAsync(bmp, tag);

        Assert.Contains("snapdesk", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recognize_ColoredButtonText()
    {
        var langs = OcrService.AvailableLanguages();
        if (langs.Count == 0) return;

        var tag = OcrService.ResolveLanguage(null);
        using var bmp = RenderText("Kopyala", 84, 28, 13f, Color.FromArgb(0, 122, 255), Color.White, 12, 5);
        var result = await OcrService.RecognizeAsync(bmp, tag);

        Assert.Contains("Kopyala", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recognize_LowContrastSmallTurkishText()
    {
        var langs = OcrService.AvailableLanguages();
        if (langs.Count == 0) return;

        var tag = OcrService.ResolveLanguage(null);
        using var bmp = RenderText("Şifre değiştirmek için tıklayın", 220, 22, 11f, Color.FromArgb(40, 42, 48), Color.FromArgb(145, 150, 160), 3, 3);
        var result = await OcrService.RecognizeAsync(bmp, tag);

        Assert.Contains("tıklayın", result.Text, StringComparison.OrdinalIgnoreCase);
    }
}
