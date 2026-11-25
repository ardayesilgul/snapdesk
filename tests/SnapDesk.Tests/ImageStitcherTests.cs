using System.Drawing;
using SnapDesk.Long;

namespace SnapDesk.Tests;

public class ImageStitcherTests
{
    private static Color RowColor(int row)
    {
        // Satır başına farklı, tekrarsız renk: düzgün desenli sahte eşleşmeleri önler.
        var hash = (uint)row * 2654435761u;
        hash ^= hash >> 15;
        return Color.FromArgb(255, (int)(hash & 0xFF), (int)((hash >> 8) & 0xFF), (int)((hash >> 16) & 0xFF));
    }

    private static Bitmap Frame(int width, int height, int startRow)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            var color = RowColor(startRow + y);
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, color);
            }
        }

        return bitmap;
    }

    [Fact]
    public void FindVerticalOverlap_DetectsShiftedSlice()
    {
        using var top = Frame(64, 300, 0);
        using var bottom = Frame(64, 300, 100);

        var overlap = ImageStitcher.FindVerticalOverlap(top, bottom);

        Assert.Equal(200, overlap);
    }

    [Fact]
    public void FindVerticalOverlap_UnrelatedFrames_ReturnsMinusOne()
    {
        using var top = Frame(64, 300, 0);
        using var bottom = Frame(64, 300, 1000);

        Assert.Equal(-1, ImageStitcher.FindVerticalOverlap(top, bottom));
    }

    [Fact]
    public void FindVerticalOverlap_IdenticalFrames_ReturnsWholeContent()
    {
        using var top = Frame(64, 300, 0);
        using var bottom = Frame(64, 300, 0);

        var overlap = ImageStitcher.FindVerticalOverlap(top, bottom);

        Assert.Equal(300, overlap);
        using var stitched = ImageStitcher.Append(top, bottom, contentTop: 0, overlap: overlap);
        Assert.Equal(300, stitched.Height);
    }

    [Fact]
    public void FindVerticalOverlap_StaticPageWithFixedHeader_AddsNoRows()
    {
        using var first = Frame(64, 300, 0);
        using var second = Frame(64, 300, 0);
        using var a = OverlayHeader(first, 40);
        using var b = OverlayHeader(second, 40);

        var overlap = ImageStitcher.FindVerticalOverlap(a, b, contentTop: 40);

        Assert.Equal(260, overlap);
        using var stitched = ImageStitcher.Append(a, b, contentTop: 40, overlap: overlap);
        Assert.Equal(300, stitched.Height);
    }

    [Fact]
    public void FramesNearlyEqual_IdenticalTrue_DifferentFalse()
    {
        using var first = Frame(64, 300, 0);
        using var identical = Frame(64, 300, 0);
        using var shifted = Frame(64, 300, 40);

        Assert.True(ImageStitcher.FramesNearlyEqual(first, identical));
        Assert.False(ImageStitcher.FramesNearlyEqual(first, shifted));
    }

    [Fact]
    public void Append_AddsOnlyNewRows()
    {
        using var canvas = Frame(64, 300, 0);
        using var next = Frame(64, 300, 100);

        using var stitched = ImageStitcher.Append(canvas, next, contentTop: 0, overlap: 200);

        Assert.Equal(400, stitched.Height);
        Assert.Equal(RowColor(350).ToArgb(), stitched.GetPixel(0, 350).ToArgb());
        Assert.Equal(canvas.GetPixel(0, 0).ToArgb(), stitched.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Append_WithFixedHeader_SkipsHeaderInNewRows()
    {
        using var canvas = Frame(64, 300, 0);
        using var next = Frame(64, 300, 100);

        using var stitched = ImageStitcher.Append(canvas, next, contentTop: 40, overlap: 200);

        Assert.Equal(360, stitched.Height);
    }

    [Fact]
    public void DetectFixedHeader_FindsConstantBand()
    {
        using var first = Frame(64, 300, 0);
        using var second = Frame(64, 300, 50);

        using var a = OverlayHeader(first, 40);
        using var b = OverlayHeader(second, 40);

        Assert.Equal(40, ImageStitcher.DetectFixedHeader(a, b));
    }

    [Fact]
    public void FindVerticalOverlap_WithWhitespaceBands_DoesNotFalseBreak()
    {
        // Gerçek web sayfalarındaki boş beyaz şeritler yanlış erken eşleşmeye yol açmamalıdır.
        using var top = new Bitmap(200, 400);
        using var bottom = new Bitmap(200, 400);

        void DrawPage(Bitmap bmp, int scrollY)
        {
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            using var font = new Font("Segoe UI", 12f);
            using var brush = new SolidBrush(Color.Black);
            for (var y = 0; y < 800; y += 60)
            {
                var screenY = y - scrollY;
                if (screenY > -30 && screenY < bmp.Height + 30)
                {
                    g.DrawString($"Satır bloğu #{y}", font, brush, 16, screenY);
                }
            }
        }

        DrawPage(top, 0);
        DrawPage(bottom, 180);

        var overlap = ImageStitcher.FindVerticalOverlap(top, bottom);
        Assert.InRange(overlap, 218, 222);
    }

    [Fact]
    public void FindVerticalOverlap_WithAnimatedVideo_AlignsAndUpdatesCoherently()
    {
        using var frame1 = Frame(200, 400, 0);
        using var frame2 = Frame(200, 400, 120);

        // Video oynatıcı alanı: 1. karede kırmızı, 2. karede mavi (aynı video 200..340 -> 80..220 konumunda)
        using (var g1 = Graphics.FromImage(frame1))
        using (var b1 = new SolidBrush(Color.Crimson))
        {
            g1.FillRectangle(b1, 20, 200, 160, 140);
        }

        using (var g2 = Graphics.FromImage(frame2))
        using (var b2 = new SolidBrush(Color.RoyalBlue))
        {
            g2.FillRectangle(b2, 20, 80, 160, 140);
        }

        var overlap = ImageStitcher.FindVerticalOverlap(frame1, frame2);
        Assert.Equal(280, overlap);

        using var stitched = ImageStitcher.Append(frame1, frame2, contentTop: 0, overlap: overlap);
        Assert.Equal(520, stitched.Height);
        // Video alanı bölünmüş kırmızı/mavi şeritler yerine tutarlı tek kare (RoyalBlue) olmalı
        Assert.Equal(Color.RoyalBlue.ToArgb(), stitched.GetPixel(100, 220).ToArgb());
        Assert.Equal(Color.RoyalBlue.ToArgb(), stitched.GetPixel(100, 330).ToArgb());
    }

    [Fact]
    public void FindVerticalOverlap_WithLargeWhiteMarginsAndMarquee_DoesNotRepeatSections()
    {
        using var frame1 = new Bitmap(400, 500);
        using var frame2 = new Bitmap(400, 500);

        void Draw(Bitmap bmp, int scrollY, int marqueeX)
        {
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(248, 249, 250));
            using var font = new Font("Segoe UI", 18f, FontStyle.Bold);
            using var dark = new SolidBrush(Color.FromArgb(20, 20, 25));
            using var accent = new SolidBrush(Color.FromArgb(0, 122, 255));

            // Üst başlık + CTA butonları (altında büyük boş beyaz alan var; kaydırma sonrası da kısmen görünür)
            var titleY = 190 - scrollY;
            if (titleY > -60 && titleY < bmp.Height)
            {
                g.DrawString("THINK IT, ACTION.", font, dark, 90, titleY);
                g.FillRectangle(dark, 110, titleY + 50, 80, 24);
                g.FillRectangle(accent, 200, titleY + 50, 90, 24);
            }

            // Yatay kayan marquee kartları (scroll sırasında yana kayar)
            var marqueeY = 320 - scrollY;
            if (marqueeY > -50 && marqueeY < bmp.Height)
            {
                for (var i = 0; i < 4; i++)
                {
                    g.FillRectangle(accent, (marqueeX + i * 95) % 360, marqueeY, 75, 36);
                }
            }

            // Alt başlık
            var footerY = 540 - scrollY;
            if (footerY > -50 && footerY < bmp.Height)
            {
                g.DrawString("The Philosophy", font, dark, 110, footerY);
            }
        }

        Draw(frame1, scrollY: 0, marqueeX: 10);
        Draw(frame2, scrollY: 180, marqueeX: 45);

        var overlap = ImageStitcher.FindVerticalOverlap(frame1, frame2);
        Assert.Equal(320, overlap);
    }

    private static Bitmap OverlayHeader(Bitmap source, int rows)
    {
        var copy = new Bitmap(source);
        using var graphics = Graphics.FromImage(copy);
        using var brush = new SolidBrush(Color.White);
        graphics.FillRectangle(brush, 0, 0, copy.Width, rows);
        return copy;
    }
}
