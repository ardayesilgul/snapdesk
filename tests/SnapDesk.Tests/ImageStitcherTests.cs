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

    private static Bitmap OverlayHeader(Bitmap source, int rows)
    {
        var copy = new Bitmap(source);
        using var graphics = Graphics.FromImage(copy);
        using var brush = new SolidBrush(Color.White);
        graphics.FillRectangle(brush, 0, 0, copy.Width, rows);
        return copy;
    }
}
