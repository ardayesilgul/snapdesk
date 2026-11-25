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

    [Fact]
    public async Task Recognize_PunctuationAndPathsInDarkMenu()
    {
        var langs = OcrService.AvailableLanguages();
        if (langs.Count == 0) return;

        var tag = OcrService.ResolveLanguage(null);
        using var bmp = new Bitmap(260, 175);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(14, 14, 16));
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using var font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb(228, 228, 232));
            g.DrawString("workspace", font, brush, 24, 12);
            g.DrawString("Sosyal içerik için klasör seç", font, brush, 24, 34);
            g.DrawString(@"C:\Users\Demo\Documents\Projects\app", font, brush, 24, 56);
            g.DrawString("Logoyu temizleyip siteye ekle", font, brush, 24, 78);
            g.DrawString("Excalidraw'da simge ikonu çiz", font, brush, 24, 100);
            g.DrawString("Export product explainer video", font, brush, 24, 122);
            g.DrawString("Show more", font, brush, 24, 144);
        }

        var result = await OcrService.RecognizeAsync(bmp, tag);
        Assert.Contains(@"C:\Users\Demo\Documents", result.Text);
        Assert.Contains("Excalidraw'da", result.Text);
    }

    [Fact]
    public void AllFormsAndControls_ConstructWithoutException()
    {
        var settings = new SnapDesk.Core.Settings();
        using var btn1 = SnapDesk.UI.UiFactory.Button("Test", 100, 32, primary: true);
        using var btn2 = SnapDesk.UI.UiFactory.Button("Test2", 100, 32, primary: false);
        using var chk1 = SnapDesk.UI.UiFactory.Check("Açık", @checked: true);
        using var chk2 = SnapDesk.UI.UiFactory.Check("Kapalı", @checked: false);
        using var cmb = SnapDesk.UI.UiFactory.Combo();
        using var txt1 = SnapDesk.UI.UiFactory.TextBox(multiline: false);
        using var txt2 = SnapDesk.UI.UiFactory.TextBox(multiline: true);

        using var ocrForm = new OcrForm(new Bitmap(120, 60), settings);
        using var longForm = new SnapDesk.Long.LongCaptureForm(new Rectangle(0, 0, 400, 300), settings);
        using var longRes = new SnapDesk.Long.LongResultForm(new Bitmap(200, 400), settings, scrolled: true);
        using var recForm = new SnapDesk.Record.RecordBarForm(new Rectangle(0, 0, 400, 300), settings);

        Assert.True(chk1.Checked);
        Assert.False(chk2.Checked);
        Assert.NotNull(ocrForm);
        Assert.NotNull(longForm);
        Assert.NotNull(longRes);
        Assert.NotNull(recForm);
    }
}
