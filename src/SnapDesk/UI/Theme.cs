using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SnapDesk.UI;

/// <summary>
/// Uygulama görsel teması, renk katmanları ve tipografi hiyerarşisi (Apple Obsidian & Linear).
/// </summary>
public static class Theme
{
    // Katmanlı yüzey paleti (Derinlik hiyerarşisi: 0 = Pencere, 1 = Kart, 2 = Girdi/Kontrol, 3 = Hover)
    public static readonly Color WindowBack = Color.FromArgb(18, 18, 22);
    public static readonly Color ToolbarBack = Color.FromArgb(22, 22, 26);
    public static readonly Color SurfaceCard = Color.FromArgb(27, 27, 33);
    public static readonly Color ToolbarBackAlt = Color.FromArgb(34, 34, 42);
    public static readonly Color SurfaceInput = Color.FromArgb(34, 34, 42);
    public static readonly Color SurfaceHover = Color.FromArgb(46, 46, 56);

    // Kenarlıklar ve speküler ışık katmanları
    public static readonly Color ToolbarBorder = Color.FromArgb(48, 255, 255, 255);
    public static readonly Color CardBorder = Color.FromArgb(26, 255, 255, 255);
    public static readonly Color InputBorder = Color.FromArgb(44, 255, 255, 255);
    public static readonly Color SpecularTop = Color.FromArgb(36, 255, 255, 255);
    public static readonly Color DividerLine = Color.FromArgb(20, 255, 255, 255);

    // Etkileşim durumları
    public static readonly Color Hover = Color.FromArgb(34, 255, 255, 255);
    public static readonly Color Active = Color.FromArgb(0, 122, 255);
    public static readonly Color Accent = Color.FromArgb(10, 132, 255);
    public static readonly Color AppleBlue = Color.FromArgb(0, 122, 255);
    public static readonly Color AppleBlueHover = Color.FromArgb(30, 144, 255);
    public static readonly Color AppleBluePress = Color.FromArgb(0, 100, 220);
    public static readonly Color AppleRedHover = Color.FromArgb(64, 255, 69, 58);

    // Metin hiyerarşisi
    public static readonly Color Text = Color.FromArgb(245, 245, 247);
    public static readonly Color SubText = Color.FromArgb(156, 156, 168);
    public static readonly Color MutedText = Color.FromArgb(112, 112, 124);
    public static readonly Color Disabled = Color.FromArgb(88, 88, 96);

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
        => new("Segoe UI", size, style, GraphicsUnit.Point);

    public static readonly Font TitleFont = new("Segoe UI", 12.5f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font SectionFont = new("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font UiFont = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font UiFontBold = new("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font SmallFont = new("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font MonoFont = new("Consolas", 10f, FontStyle.Bold, GraphicsUnit.Point);

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

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
        using (var backBrush = new SolidBrush(Color.FromArgb(248, 22, 22, 26)))
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
