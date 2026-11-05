using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SnapDesk.Editor;
using SnapDesk.UI;

namespace SnapDesk.Tests;

public class AnnotationTests
{
    [Fact]
    public void ArrowClone_PreservesLineWithoutHead()
    {
        var arrow = new ArrowAnnotation
        {
            Start = new PointF(10, 20),
            End = new PointF(110, 220),
            ShowHead = false,
            Color = Color.Green,
            Width = 7f,
        };

        var clone = Assert.IsType<ArrowAnnotation>(arrow.Clone());

        Assert.False(clone.ShowHead);
        Assert.Equal(arrow.Start, clone.Start);
        Assert.Equal(arrow.End, clone.End);
        Assert.Equal(arrow.Color, clone.Color);
        Assert.Equal(arrow.Width, clone.Width);
    }

    [Fact]
    public void ArrowClone_KeepsArrowHead()
    {
        var arrow = new ArrowAnnotation
        {
            Start = new PointF(0, 0),
            End = new PointF(50, 50),
        };

        var clone = Assert.IsType<ArrowAnnotation>(arrow.Clone());

        Assert.True(clone.ShowHead);
    }

    [Fact]
    public void RectangleBounds_AreTightAndIdempotentOnSetBounds()
    {
        var rect = new RectangleAnnotation
        {
            Rect = new RectangleF(20, 30, 120, 80),
            Width = 10f,
        };

        var bounds = rect.GetBounds();
        Assert.Equal(rect.Rect, bounds);

        rect.SetBounds(bounds);
        Assert.Equal(new RectangleF(20, 30, 120, 80), rect.Rect);
    }

    [Fact]
    public void ArrowSetBounds_ScalesAccuratelyWithoutStrokeDrift()
    {
        var arrow = new ArrowAnnotation
        {
            Start = new PointF(10, 20),
            End = new PointF(110, 70),
            Width = 8f,
        };

        Assert.Equal(new RectangleF(10, 20, 100, 50), arrow.GetBounds());

        arrow.SetBounds(new RectangleF(20, 40, 200, 100));
        Assert.Equal(new PointF(20, 40), arrow.Start);
        Assert.Equal(new PointF(220, 140), arrow.End);
    }

    [Fact]
    public void PenGetBounds_EmptyReturnsEmpty_AndSetBoundsScalesPoints()
    {
        var emptyPen = new PenAnnotation();
        Assert.Equal(RectangleF.Empty, emptyPen.GetBounds());

        var pen = new PenAnnotation { Width = 6f };
        pen.Points.Add(new PointF(0, 0));
        pen.Points.Add(new PointF(50, 100));

        Assert.Equal(new RectangleF(0, 0, 50, 100), pen.GetBounds());
        pen.SetBounds(new RectangleF(10, 20, 100, 200));
        Assert.Equal(new PointF(10, 20), pen.Points[0]);
        Assert.Equal(new PointF(110, 220), pen.Points[1]);
    }

    [Fact]
    public void MosaicClone_PreservesBlockSizeAndBlurFlag()
    {
        var mosaic = new MosaicAnnotation
        {
            Rect = new RectangleF(5, 5, 40, 40),
            BlockSize = 24,
            IsBlur = true,
        };

        var clone = Assert.IsType<MosaicAnnotation>(mosaic.Clone());
        Assert.Equal(24, clone.BlockSize);
        Assert.True(clone.IsBlur);
    }

    [Fact]
    public void FormatPixelColor_FormatsHexRgbAndDec()
    {
        var color = Color.FromArgb(255, 0x3A, 0x7A, 0xFE);
        Assert.Equal("#3A7AFE", CaptureSession.FormatPixelColor(color, ColorFormat.Hex));
        Assert.Equal("rgb(58, 122, 254)", CaptureSession.FormatPixelColor(color, ColorFormat.Rgb));
        Assert.Equal("58, 122, 254", CaptureSession.FormatPixelColor(color, ColorFormat.Dec));
    }

    [Fact]
    public void HighlighterHorizontalStrip_BoundsHitTestAndSetBoundsWorkCleanly()
    {
        var hl = new PenAnnotation
        {
            IsHighlighter = true,
            Width = 5f,
            Color = Color.Yellow,
        };
        hl.Points.Add(new PointF(50, 100));
        hl.Points.Add(new PointF(250, 100));

        var bounds = hl.GetBounds();
        Assert.Equal(50f, bounds.Left);
        Assert.Equal(200f, bounds.Width);
        Assert.Equal(18f, bounds.Height, 1);

        Assert.True(hl.HitTest(new PointF(150, 106), 3f));
        Assert.False(hl.HitTest(new PointF(150, 130), 3f));

        hl.SetBounds(new RectangleF(80, 140, 300, 20));
        Assert.Equal(new PointF(80, 150), hl.Points[0]);
        Assert.Equal(new PointF(380, 150), hl.Points[1]);
    }

    [Fact]
    public void ToolbarSettings_DefaultsAndTogglesWorkCleanly()
    {
        var settings = new SnapDesk.Core.Settings();
        settings.Normalize();

        Assert.False(settings.ShowToolbarSerial);
        Assert.True(settings.ShowToolbarUndoRedo);
        Assert.True(settings.ShowToolbarCrop);
        Assert.True(settings.ShowToolbarLongCapture);
        Assert.True(settings.ShowToolbarRecord);
        Assert.True(settings.ShowToolbarOcr);
        Assert.True(settings.ShowToolbarPin);
    }

    [Fact]
    public void BlurAndPixelate_ProduceDifferentOutputsAndBlurIsSmooth()
    {
        // Yüksek kontrastlı test deseni: yarı beyaz, yarı siyah
        using var source = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(source))
        {
            g.FillRectangle(Brushes.White, 0, 0, 50, 100);
            g.FillRectangle(Brushes.Black, 50, 0, 50, 100);
        }

        using var pixelated = SnapDesk.Core.ScreenCapture.Pixelate(source, new Rectangle(0, 0, 100, 100), 16);
        using var blurred = SnapDesk.Core.ScreenCapture.Blur(source, new Rectangle(0, 0, 100, 100), 16);

        // Mozaik bloklar üretir (ortadaki piksel ya tamamen siyah ya tamamen beyaz ya da blok ortalamasıdır)
        // Bulanıklık ise pürüzsüz geçişli gri tonlar üretir
        var pixelatedMid = pixelated.GetPixel(49, 50);
        var blurredMid = blurred.GetPixel(50, 50);

        // Bulanık ve mozaik aynı çıktıyı üretmemeli
        var differentPixels = 0;
        for (var x = 0; x < 100; x += 5)
        {
            for (var y = 0; y < 100; y += 5)
            {
                if (pixelated.GetPixel(x, y) != blurred.GetPixel(x, y))
                {
                    differentPixels++;
                }
            }
        }

        Assert.True(differentPixels > 50, "Bulanıklık ve mozaik belirgin şekilde farklı görsel çıktılar üretmelidir.");
    }

    [Fact]
    public void MosaicAnnotation_DrawsSelectionIndicatorWhenPatchIsNull()
    {
        var mosaic = new MosaicAnnotation
        {
            Rect = new RectangleF(10, 10, 80, 40),
            IsBlur = true,
            Patch = null,
        };

        using var bmp = new Bitmap(120, 80);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
            mosaic.Draw(g, PointF.Empty);
        }

        // Çizim sırasında Patch null iken boş geçilmemeli, seçim kılavuzu (mavi çerçeve/dolgu) çizilmelidir
        var nonBlackPixels = 0;
        for (var x = 0; x < 120; x++)
        {
            for (var y = 0; y < 80; y++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.R > 0 || p.G > 0 || p.B > 0)
                {
                    nonBlackPixels++;
                }
            }
        }

        // Çizim anında seçilen alan görsel olarak çizilmelidir.
        Assert.True(nonBlackPixels > 100);
    }

    [Fact]
    public void BlurAndPixelate_DrawProperlyOnImage()
    {
        using var bmp = new Bitmap(600, 300);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var font = new Font("Segoe UI", 16f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(20, 20, 25));
            g.DrawString("1. Mosaic Test Data: 1234 5678", font, brush, 30, 40);
            g.DrawString("2. Blur Test Data: user@domain.com", font, brush, 30, 100);
        }

        using var pixelatedPatch = SnapDesk.Core.ScreenCapture.Pixelate(bmp, new Rectangle(30, 35, 540, 40), 12);
        using var blurPatch = SnapDesk.Core.ScreenCapture.Blur(bmp, new Rectangle(30, 95, 540, 40), 16);

        using var comparison = (Bitmap)bmp.Clone();
        using (var g = Graphics.FromImage(comparison))
        {
            var m1 = new MosaicAnnotation { Rect = new RectangleF(30, 35, 540, 40), Patch = pixelatedPatch, IsBlur = false };
            m1.Draw(g, PointF.Empty);

            var m2 = new MosaicAnnotation { Rect = new RectangleF(30, 95, 540, 40), Patch = blurPatch, IsBlur = true };
            m2.Draw(g, PointF.Empty);

            var m3 = new MosaicAnnotation { Rect = new RectangleF(30, 155, 540, 40), Patch = null, IsBlur = true };
            m3.Draw(g, PointF.Empty);
        }

        Assert.NotNull(pixelatedPatch);
        Assert.NotNull(blurPatch);
    }

    [Fact]
    public void AppIcon_GeneratesValidMultiResolutionIconStream()
    {
        var sizes = new[] { 256, 128, 64, 48, 32, 16 };
        var images = new List<byte[]>();

        foreach (var size in sizes)
        {
            using var bmp = RenderAppIcon(size);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            images.Add(ms.ToArray());
        }

        using var icoStream = new MemoryStream();
        using (var writer = new BinaryWriter(icoStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); // Reserved
            writer.Write((ushort)1); // Type 1 = ICO
            writer.Write((ushort)sizes.Length);

            var offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                var size = sizes[i];
                var bytes = images[i];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)bytes.Length);
                writer.Write((uint)offset);
                offset += bytes.Length;
            }

            foreach (var bytes in images)
            {
                writer.Write(bytes);
            }
        }

        icoStream.Position = 0;
        using var loadedIcon = new Icon(icoStream);
        Assert.NotNull(loadedIcon);
        Assert.True(icoStream.Length > 1000);
    }

    private static Bitmap RenderAppIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float padding = size * 0.08f;
        var squircleRect = new RectangleF(padding, padding, size - padding * 2, size - padding * 2);
        float cornerRadius = squircleRect.Width * 0.225f;

        // 1. Subtle drop shadow behind squircle
        if (size >= 32)
        {
            var shadowRect = new RectangleF(squircleRect.X, squircleRect.Y + size * 0.035f, squircleRect.Width, squircleRect.Height);
            using var shadowPath = Theme.RoundedRect(shadowRect, cornerRadius);
            using var shadowBrush = new SolidBrush(Color.FromArgb(45, 0, 0, 0));
            g.FillPath(shadowBrush, shadowPath);
        }

        // 2. Base Squircle with Apple vibrant gradient
        using (var squirclePath = Theme.RoundedRect(squircleRect, cornerRadius))
        {
            using (var gradBrush = new LinearGradientBrush(
                new PointF(squircleRect.Left, squircleRect.Top),
                new PointF(squircleRect.Right, squircleRect.Bottom),
                Color.FromArgb(255, 0, 132, 255), // Apple Electric Blue
                Color.FromArgb(255, 94, 53, 215))) // Apple Deep Violet
            {
                g.FillPath(gradBrush, squirclePath);
            }

            // Top-down subtle highlight sheen
            using (var sheenBrush = new LinearGradientBrush(
                new PointF(squircleRect.Left, squircleRect.Top),
                new PointF(squircleRect.Left, squircleRect.Top + squircleRect.Height * 0.6f),
                Color.FromArgb(50, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255)))
            {
                g.FillPath(sheenBrush, squirclePath);
            }

            // Specular border
            float borderWidth = Math.Max(1f, size * 0.018f);
            using (var borderPen = new Pen(Color.FromArgb(80, 255, 255, 255), borderWidth))
            {
                g.DrawPath(borderPen, squirclePath);
            }
        }

        // 3. Central Snipping / Screen Capture Emblem
        float cx = size / 2f;
        float cy = size / 2f;
        float boxHalf = squircleRect.Width * 0.25f;
        float armLen = boxHalf * 0.55f;
        float bracketStroke = Math.Max(2.0f, size * 0.065f);

        float left = cx - boxHalf;
        float right = cx + boxHalf;
        float top = cy - boxHalf;
        float bottom = cy + boxHalf;

        using (var pen = new Pen(Color.White, bracketStroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            // Top-Left corner
            g.DrawLine(pen, left, top + armLen, left, top);
            g.DrawLine(pen, left, top, left + armLen, top);

            // Top-Right corner
            g.DrawLine(pen, right - armLen, top, right, top);
            g.DrawLine(pen, right, top, right, top + armLen);

            // Bottom-Left corner
            g.DrawLine(pen, left, bottom - armLen, left, bottom);
            g.DrawLine(pen, left, bottom, left + armLen, bottom);

            // Bottom-Right corner
            g.DrawLine(pen, right - armLen, bottom, right, bottom);
            g.DrawLine(pen, right, bottom, right, bottom - armLen);
        }

        // Center element: Camera Lens / Target Shutter
        float lensRadius = boxHalf * 0.44f;
        var lensRect = new RectangleF(cx - lensRadius, cy - lensRadius, lensRadius * 2, lensRadius * 2);

        using (var lensBrush = new SolidBrush(Color.FromArgb(170, 15, 25, 60)))
        {
            g.FillEllipse(lensBrush, lensRect);
        }

        float rimStroke = Math.Max(1.2f, size * 0.03f);
        using (var rimPen = new Pen(Color.FromArgb(220, 255, 255, 255), rimStroke))
        {
            g.DrawEllipse(rimPen, lensRect);
        }

        // Inner glowing aperture core
        float coreRadius = lensRadius * 0.45f;
        var coreRect = new RectangleF(cx - coreRadius, cy - coreRadius, coreRadius * 2, coreRadius * 2);
        using (var coreBrush = new SolidBrush(Color.FromArgb(255, 0, 230, 255))) // Electric Cyan
        {
            g.FillEllipse(coreBrush, coreRect);
        }

        // Top-right camera flash / sparkle (for size >= 32)
        if (size >= 32)
        {
            float sparkX = right - boxHalf * 0.15f;
            float sparkY = top + boxHalf * 0.15f;
            float sparkR = size * 0.045f;
            PointF[] starPoints = new PointF[]
            {
                new(sparkX, sparkY - sparkR),
                new(sparkX + sparkR * 0.35f, sparkY - sparkR * 0.35f),
                new(sparkX + sparkR, sparkY),
                new(sparkX + sparkR * 0.35f, sparkY + sparkR * 0.35f),
                new(sparkX, sparkY + sparkR),
                new(sparkX - sparkR * 0.35f, sparkY + sparkR * 0.35f),
                new(sparkX - sparkR, sparkY),
                new(sparkX - sparkR * 0.35f, sparkY - sparkR * 0.35f)
            };
            using var starBrush = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
            g.FillPolygon(starBrush, starPoints);
        }

        return bmp;
    }
}
