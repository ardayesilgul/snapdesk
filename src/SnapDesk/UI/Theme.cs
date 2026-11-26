using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SnapDesk.UI;

/// <summary>
/// Uygulama görsel teması, renk katmanları ve gömülü Inter tipografi hiyerarşisi (OLED Zinc + Apple).
/// </summary>
public static class Theme
{
    private static readonly PrivateFontCollection EmbeddedFonts = LoadEmbeddedFonts();
    private static readonly FontFamily? InterRegularFamily = FindFamily("Inter");
    private static readonly FontFamily? InterMediumFamily = FindFamily("Inter Medium");
    private static readonly FontFamily? InterSemiBoldFamily = FindFamily("Inter SemiBold");
    // Katmanlı OLED Zinc yüzey paleti (0 = Tuval, 1 = Sidebar, 2 = Kart, 3 = Girdi, 4 = Hover)
    public static readonly Color WindowBack = Color.FromArgb(8, 9, 10);
    public static readonly Color ToolbarBack = Color.FromArgb(13, 14, 17);
    public static readonly Color SurfaceCard = Color.FromArgb(17, 17, 19);
    public static readonly Color ToolbarBackAlt = Color.FromArgb(24, 24, 29);
    public static readonly Color SurfaceInput = Color.FromArgb(24, 24, 29);
    public static readonly Color SurfaceHover = Color.FromArgb(34, 34, 41);

    // Kenarlıklar ve speküler ışık katmanları
    public static readonly Color ToolbarBorder = Color.FromArgb(40, 255, 255, 255);
    public static readonly Color CardBorder = Color.FromArgb(20, 255, 255, 255);
    public static readonly Color InputBorder = Color.FromArgb(34, 255, 255, 255);
    public static readonly Color SpecularTop = Color.FromArgb(30, 255, 255, 255);
    public static readonly Color DividerLine = Color.FromArgb(18, 255, 255, 255);

    // Etkileşim durumları
    public static readonly Color Hover = Color.FromArgb(28, 255, 255, 255);
    public static readonly Color Active = Color.FromArgb(0, 122, 255);
    public static readonly Color Accent = Color.FromArgb(10, 132, 255);
    public static readonly Color AppleBlue = Color.FromArgb(0, 122, 255);
    public static readonly Color AppleBlueHover = Color.FromArgb(30, 144, 255);
    public static readonly Color AppleBluePress = Color.FromArgb(0, 100, 220);
    public static readonly Color AppleRedHover = Color.FromArgb(64, 255, 69, 58);

    // Metin hiyerarşisi
    public static readonly Color Text = Color.FromArgb(244, 244, 245);
    public static readonly Color SubText = Color.FromArgb(161, 161, 170);
    public static readonly Color MutedText = Color.FromArgb(113, 113, 122);
    public static readonly Color Disabled = Color.FromArgb(82, 82, 91);

    // Yakalama ve çizim renkleri
    public static readonly Color SelectionBorder = Color.FromArgb(64, 140, 255);
    public static readonly Color Dim = Color.FromArgb(120, 0, 0, 0);
    public static readonly Color HandleFill = Color.FromArgb(64, 140, 255);
    public static readonly Color HandleBorder = Color.FromArgb(255, 255, 255);
    public static readonly Color PanelShadow = Color.FromArgb(90, 0, 0, 0);

    public static readonly Color Red = Color.FromArgb(0xD8, 0x4A, 0x2F);
    public static readonly Color Yellow = Color.FromArgb(0xE3, 0xC3, 0x63);
    public static readonly Color Green = Color.FromArgb(0x6A, 0xAC, 0x61);
    public static readonly Color Blue = Color.FromArgb(0x5E, 0x91, 0xE6);
    public static readonly Color Black = Color.FromArgb(0x19, 0x19, 0x19);
    public static readonly Color White = Color.FromArgb(0xFF, 0xFF, 0xFF);

    public static readonly Color[] Palette = { Red, Yellow, Green, Blue, Black, White };

    public static Font Ui(float size = 9f, FontStyle style = FontStyle.Regular)
    {
        if (style == FontStyle.Bold)
        {
            if (InterSemiBoldFamily is not null)
            {
                return new Font(InterSemiBoldFamily, size, FontStyle.Regular, GraphicsUnit.Point);
            }
            if (InterRegularFamily is not null)
            {
                return new Font(InterRegularFamily, size, FontStyle.Bold, GraphicsUnit.Point);
            }
            return new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point);
        }

        if (InterRegularFamily is not null)
        {
            return new Font(InterRegularFamily, size, style, GraphicsUnit.Point);
        }

        return new Font("Segoe UI", size, style, GraphicsUnit.Point);
    }

    public static Font Medium(float size = 9f)
        => InterMediumFamily is not null
            ? new Font(InterMediumFamily, size, FontStyle.Regular, GraphicsUnit.Point)
            : Ui(size);

    public static readonly Font TitleFont = Ui(13.5f, FontStyle.Bold);
    public static readonly Font SectionFont = Ui(9.5f, FontStyle.Bold);
    public static readonly Font UiFont = Ui(9f);
    public static readonly Font UiFontMedium = Medium(9f);
    public static readonly Font UiFontBold = Ui(9f, FontStyle.Bold);
    public static readonly Font SmallFont = Ui(8.2f);
    public static readonly Font MonoFont = new("Consolas", 10f, FontStyle.Bold, GraphicsUnit.Point);

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static void DrawText(
        Graphics g,
        string text,
        Font font,
        RectangleF bounds,
        Color color,
        StringAlignment horizontal = StringAlignment.Near,
        StringAlignment vertical = StringAlignment.Center,
        bool ellipsis = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None,
        };
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, bounds, format);
    }

    public static float MeasureTextWidth(Graphics g, string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        using var format = StringFormat.GenericTypographic;
        return g.MeasureString(text, font, 2000, format).Width;
    }

    private static FontFamily? FindFamily(string name)
        => EmbeddedFonts.Families.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static PrivateFontCollection LoadEmbeddedFonts()
    {
        var collection = new PrivateFontCollection();
        var assembly = typeof(Theme).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in names)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null) continue;
                var data = new byte[stream.Length];
                stream.ReadExactly(data);

                var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    var ptr = handle.AddrOfPinnedObject();
                    collection.AddMemoryFont(ptr, data.Length);
                    _ = AddFontMemResourceEx(ptr, (uint)data.Length, IntPtr.Zero, out _);
                }
                finally
                {
                    handle.Free();
                }
            }
            catch
            {
                // Font yüklenemezse sistem fontuna düş.
            }
        }

        return collection;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pcFonts);

    public static Color Lerp(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * t),
            (int)Math.Round(from.R + (to.R - from.R) * t),
            (int)Math.Round(from.G + (to.G - from.G) * t),
            (int)Math.Round(from.B + (to.B - from.B) * t));
    }

    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0f || rect.Width <= 0f || rect.Height <= 0f)
        {
            path.AddRectangle(rect);
            return path;
        }

        var d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, RectangleF rect, float radius, Color color)
    {
        using var path = RoundedRect(rect, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, RectangleF rect, float radius, Color color, float width = 1f)
    {
        using var path = RoundedRect(rect, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// Yüzen ada / iletişim pencereleri için Apple Obsidian Glass yüzeyini
    /// (yuvarlatılmış gövde, ince dış kenarlık ve üst speküler parlaklık çizgisi) çizer.
    /// </summary>
    public static void PaintObsidianSurface(Graphics g, int width, int height, float cornerRadius = 16f)
    {
        Prepare(g);
        var rect = new RectangleF(0.5f, 0.5f, width - 1f, height - 1f);
        using var path = RoundedRect(rect, cornerRadius);
        using (var backBrush = new SolidBrush(Color.FromArgb(248, 16, 17, 20)))
        {
            g.FillPath(backBrush, path);
        }

        using (var specPen = new Pen(SpecularTop, 1f))
        {
            g.DrawLine(specPen, cornerRadius, 1f, Math.Max(cornerRadius, width - cornerRadius), 1f);
        }

        using (var borderPen = new Pen(ToolbarBorder, 1f))
        {
            g.DrawPath(borderPen, path);
        }
    }

    public static string WithAlpha(Color color, int alpha) => string.Empty;

    public static Color Alpha(Color color, int alpha) => Color.FromArgb(alpha, color);
}
