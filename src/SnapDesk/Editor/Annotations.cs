using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SnapDesk.UI;

namespace SnapDesk.Editor;

public enum ToolKind
{
    Pointer,
    Rectangle,
    Ellipse,
    Pen,
    Arrow,
    Line,
    Text,
    Serial,
    Mosaic,
    Blur,
    Highlighter,
    Eraser,
}

/// <summary>
/// Ekran koordinatlarında (sanal ekran, fiziksel piksel) saklanan bir çizim.
/// Seçim taşınsa bile çizim görüntüdeki yerinde kalır.
/// </summary>
public abstract class Annotation
{
    public Color Color { get; set; } = Color.FromArgb(0xD8, 0x4A, 0x2F);
    public float Width { get; set; } = 4f;
    public bool Filled { get; set; }
    public DashStyle Dash { get; set; } = DashStyle.Solid;

    public abstract void Draw(Graphics g, PointF offset);

    public abstract RectangleF GetBounds();

    public abstract bool HitTest(PointF point, float tolerance);

    public abstract Annotation Clone();

    public virtual void Move(float dx, float dy)
    {
    }

    /// <summary>Seçili çizim tutamaçla boyutlandırılırken çağrılır.</summary>
    public virtual void SetBounds(RectangleF bounds)
    {
    }

    protected static float DistanceToSegment(PointF p, PointF a, PointF b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.0001f)
        {
            return Distance(p, a);
        }

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
    }

    protected static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

public sealed class RectangleAnnotation : Annotation
{
    public RectangleF Rect { get; set; }
    public bool IsEllipse { get; set; }
    public float CornerRadius { get; set; }

    public override void Draw(Graphics g, PointF offset)
    {
        var rect = new RectangleF(Rect.X + offset.X, Rect.Y + offset.Y, Rect.Width, Rect.Height);

        if (IsEllipse)
        {
            if (Filled)
            {
                using var fill = new SolidBrush(Color);
                g.FillEllipse(fill, rect);
            }

            using var pen = new Pen(Color, Width) { Alignment = PenAlignment.Center, DashStyle = Dash };
            g.DrawEllipse(pen, rect);
            return;
        }

        if (Filled)
        {
            using var path = Theme.RoundedRect(rect, CornerRadius);
            using var fill = new SolidBrush(Color);
            g.FillPath(fill, path);
            return;
        }

        if (CornerRadius > 0.5f)
        {
            using var path = Theme.RoundedRect(rect, CornerRadius);
            using var pen = new Pen(Color, Width) { LineJoin = LineJoin.Round, DashStyle = Dash };
            g.DrawPath(pen, path);
            return;
        }

        using var stroke = new Pen(Color, Width) { LineJoin = LineJoin.Round, DashStyle = Dash };
        g.DrawRectangle(stroke, rect.X, rect.Y, rect.Width, rect.Height);
    }

    public override RectangleF GetBounds() => Rect;

    public override bool HitTest(PointF point, float tolerance)
    {
        if (Filled)
        {
            return Rect.Contains(point);
        }

        var expanded = RectangleF.Inflate(Rect, tolerance + Width / 2f, tolerance + Width / 2f);
        if (!expanded.Contains(point))
        {
            return false;
        }

        var inner = RectangleF.Inflate(Rect, -(tolerance + Width / 2f), -(tolerance + Width / 2f));
        if (IsEllipse)
        {
            return !inner.Contains(point);
        }

        return !inner.Contains(point);
    }

    public override Annotation Clone() => new RectangleAnnotation
    {
        Rect = Rect,
        IsEllipse = IsEllipse,
        CornerRadius = CornerRadius,
        Color = Color,
        Width = Width,
        Filled = Filled,
        Dash = Dash,
    };

    public override void Move(float dx, float dy)
    {
        Rect = new RectangleF(Rect.X + dx, Rect.Y + dy, Rect.Width, Rect.Height);
    }

    public override void SetBounds(RectangleF bounds)
    {
        Rect = bounds;
    }
}

public sealed class PenAnnotation : Annotation
{
    public List<PointF> Points { get; } = new();
    public bool IsHighlighter { get; set; }

    public override void Draw(Graphics g, PointF offset)
    {
        if (Points.Count == 0)
        {
            return;
        }

        var color = IsHighlighter ? Color.FromArgb(110, Color) : Color;
        var width = IsHighlighter ? Math.Max(14f, Width * 3.6f) : Width;

        using var pen = new Pen(color, width)
        {
            StartCap = IsHighlighter ? LineCap.Flat : LineCap.Round,
            EndCap = IsHighlighter ? LineCap.Flat : LineCap.Round,
            LineJoin = LineJoin.Round,
            DashStyle = IsHighlighter ? DashStyle.Solid : Dash,
        };

        if (Points.Count == 1)
        {
            using var brush = new SolidBrush(color);
            if (IsHighlighter)
            {
                g.FillRectangle(brush, Points[0].X + offset.X - width / 2f, Points[0].Y + offset.Y - width / 2f, width, width);
            }
            else
            {
                g.FillEllipse(brush, Points[0].X + offset.X - width / 2f, Points[0].Y + offset.Y - width / 2f, width, width);
            }
            return;
        }

        var mapped = Points.Select(p => new PointF(p.X + offset.X, p.Y + offset.Y)).ToArray();

        // Çok fazla nokta varsa yumuşatmak için seyrelt (fosforlu şerit her zaman düz çizilir).
        if (!IsHighlighter && mapped.Length > 3)
        {
            g.DrawCurve(pen, mapped, 0.4f);
        }
        else
        {
            g.DrawLines(pen, mapped);
        }
    }

    public override RectangleF GetBounds()
    {
        if (Points.Count == 0)
        {
            return RectangleF.Empty;
        }

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        foreach (var p in Points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        if (IsHighlighter && maxY - minY < 2f)
        {
            var half = Math.Max(14f, Width * 3.6f) / 2f;
            minY -= half;
            maxY += half;
        }

        return RectangleF.FromLTRB(minX, minY, Math.Max(minX + 1f, maxX), Math.Max(minY + 1f, maxY));
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        var strokeWidth = IsHighlighter ? Math.Max(14f, Width * 3.6f) : Width;
        var threshold = Math.Max(tolerance, strokeWidth / 2f + 2f);
        for (var i = 1; i < Points.Count; i++)
        {
            if (DistanceToSegment(point, Points[i - 1], Points[i]) <= threshold)
            {
                return true;
            }
        }

        return Points.Count == 1 && Distance(point, Points[0]) <= threshold;
    }

    public override Annotation Clone()
    {
        var clone = new PenAnnotation
        {
            IsHighlighter = IsHighlighter,
            Color = Color,
            Width = Width,
            Filled = Filled,
            Dash = Dash,
        };
        clone.Points.AddRange(Points);
        return clone;
    }

    public override void Move(float dx, float dy)
    {
        for (var i = 0; i < Points.Count; i++)
        {
            Points[i] = new PointF(Points[i].X + dx, Points[i].Y + dy);
        }
    }

    public override void SetBounds(RectangleF bounds)
    {
        var old = GetBounds();
        if (old.IsEmpty) return;

        if (IsHighlighter && Points.Count == 2)
        {
            var midY = bounds.Top + bounds.Height / 2f;
            var leftFirst = Points[0].X <= Points[1].X;
            Points[0] = new PointF(leftFirst ? bounds.Left : bounds.Right, midY);
            Points[1] = new PointF(leftFirst ? bounds.Right : bounds.Left, midY);
            return;
        }

        var scaleX = old.Width >= 1f ? bounds.Width / old.Width : 0f;
        var scaleY = old.Height >= 1f ? bounds.Height / old.Height : 0f;

        for (var i = 0; i < Points.Count; i++)
        {
            Points[i] = new PointF(
                bounds.Left + (Points[i].X - old.Left) * scaleX,
                bounds.Top + (Points[i].Y - old.Top) * scaleY);
        }
    }
}

public sealed class ArrowAnnotation : Annotation
{
    public PointF Start { get; set; }
    public PointF End { get; set; }
    public bool ShowHead { get; set; } = true;

    public override void Draw(Graphics g, PointF offset)
    {
        var start = new PointF(Start.X + offset.X, Start.Y + offset.Y);
        var end = new PointF(End.X + offset.X, End.Y + offset.Y);

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1f) return;

        var ux = dx / length;
        var uy = dy / length;
        var head = Math.Max(Width * 3.2f, 12f);
        head = Math.Min(head, length * 0.9f);

        var baseX = end.X - ux * head;
        var baseY = end.Y - uy * head;
        var nx = -uy;
        var ny = ux;
        var half = head * 0.42f;

        using var pen = new Pen(Color, Width)
        {
            StartCap = LineCap.Round,
            EndCap = ShowHead ? LineCap.Flat : LineCap.Round,
            LineJoin = LineJoin.Round,
            DashStyle = Dash,
        };
        using var fill = new SolidBrush(Color);

        g.DrawLine(pen, start, ShowHead ? new PointF(baseX, baseY) : end);
        if (ShowHead)
        {
            g.FillPolygon(fill, new[]
            {
                end,
                new PointF(baseX + nx * half, baseY + ny * half),
                new PointF(baseX - nx * half, baseY - ny * half),
            });
        }
    }

    public override RectangleF GetBounds()
    {
        var minX = Math.Min(Start.X, End.X);
        var minY = Math.Min(Start.Y, End.Y);
        var maxX = Math.Max(Start.X, End.X);
        var maxY = Math.Max(Start.Y, End.Y);
        return RectangleF.FromLTRB(minX, minY, Math.Max(minX + 1f, maxX), Math.Max(minY + 1f, maxY));
    }

    public override bool HitTest(PointF point, float tolerance)
        => DistanceToSegment(point, Start, End) <= Math.Max(tolerance, Width + 3f);

    public override Annotation Clone() => new ArrowAnnotation
    {
        Start = Start,
        End = End,
        ShowHead = ShowHead,
        Color = Color,
        Width = Width,
        Dash = Dash,
    };

    public override void Move(float dx, float dy)
    {
        Start = new PointF(Start.X + dx, Start.Y + dy);
        End = new PointF(End.X + dx, End.Y + dy);
    }

    public override void SetBounds(RectangleF bounds)
    {
        var old = GetBounds();
        var scaleX = old.Width >= 1f ? bounds.Width / old.Width : 0f;
        var scaleY = old.Height >= 1f ? bounds.Height / old.Height : 0f;

        Start = new PointF(
            bounds.Left + (Start.X - old.Left) * scaleX,
            bounds.Top + (Start.Y - old.Top) * scaleY);
        End = new PointF(
            bounds.Left + (End.X - old.Left) * scaleX,
            bounds.Top + (End.Y - old.Top) * scaleY);
    }
}

public sealed class TextAnnotation : Annotation
{
    public string Text { get; set; } = string.Empty;
    public PointF Location { get; set; }
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 24f;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Outline { get; set; }

    private static readonly Bitmap MeasureSurface = new(1, 1);
    private static readonly Graphics MeasureGraphics = Graphics.FromImage(MeasureSurface);

    public Font BuildFont() => new(
        FontFamily,
        FontSize,
        (Bold ? FontStyle.Bold : FontStyle.Regular) | (Italic ? FontStyle.Italic : FontStyle.Regular),
        GraphicsUnit.Pixel);

    public SizeF Measure()
    {
        using var font = BuildFont();
        var size = MeasureGraphics.MeasureString(Text.Length == 0 ? " " : Text, font, int.MaxValue, StringFormat.GenericTypographic);
        return new SizeF(MathF.Ceiling(size.Width) + 2, MathF.Ceiling(size.Height * 0.92f) + 2);
    }

    public override void Draw(Graphics g, PointF offset)
    {
        if (string.IsNullOrEmpty(Text)) return;

        using var font = BuildFont();
        var location = new PointF(Location.X + offset.X, Location.Y + offset.Y);

        if (Outline)
        {
            using var path = new GraphicsPath();
            path.AddString(Text, font.FontFamily, (int)font.Style, font.Size, location, StringFormat.GenericTypographic);
            using var pen = new Pen(Color, Math.Max(1f, Width / 3f));
            g.DrawPath(pen, path);
            return;
        }

        using var brush = new SolidBrush(Color);
        var prevHint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.DrawString(Text, font, brush, location, StringFormat.GenericTypographic);
        g.TextRenderingHint = prevHint;
    }

    public override RectangleF GetBounds()
    {
        var size = Measure();
        return new RectangleF(Location.X, Location.Y, size.Width, size.Height);
    }

    public override bool HitTest(PointF point, float tolerance)
        => RectangleF.Inflate(GetBounds(), tolerance, tolerance).Contains(point);

    public override Annotation Clone() => new TextAnnotation
    {
        Text = Text,
        Location = Location,
        FontFamily = FontFamily,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        Outline = Outline,
        Color = Color,
        Width = Width,
        Dash = Dash,
    };

    public override void Move(float dx, float dy)
        => Location = new PointF(Location.X + dx, Location.Y + dy);

    public override void SetBounds(RectangleF bounds)
    {
        var old = GetBounds();
        if (old.Height > 1)
        {
            FontSize = Math.Clamp(FontSize * (bounds.Height / old.Height), 6f, 400f);
        }

        Location = bounds.Location;
    }
}

public sealed class SerialAnnotation : Annotation
{
    public int Number { get; set; } = 1;
    public PointF Center { get; set; }
    public float Radius { get; set; } = 15f;

    public override void Draw(Graphics g, PointF offset)
    {
        var center = new PointF(Center.X + offset.X, Center.Y + offset.Y);
        var rect = new RectangleF(center.X - Radius, center.Y - Radius, Radius * 2, Radius * 2);

        using var fill = new SolidBrush(Filled ? Color : Color.FromArgb(235, Color));
        g.FillEllipse(fill, rect);

        if (!Filled)
        {
            using var pen = new Pen(Color.FromArgb(255, 255, 255), Math.Max(1.5f, Width / 3f));
            g.DrawEllipse(pen, rect);
        }

        using var font = new Font("Segoe UI", Radius * 1.05f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Filled ? Color.FromArgb(255, 255, 255) : Color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(Number.ToString(), font, textBrush, rect, format);
    }

    public override RectangleF GetBounds()
        => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);

    public override bool HitTest(PointF point, float tolerance)
        => Distance(point, Center) <= Radius + tolerance;

    public override Annotation Clone() => new SerialAnnotation
    {
        Number = Number,
        Center = Center,
        Radius = Radius,
        Color = Color,
        Width = Width,
        Filled = Filled,
        Dash = Dash,
    };

    public override void Move(float dx, float dy)
        => Center = new PointF(Center.X + dx, Center.Y + dy);

    public override void SetBounds(RectangleF bounds)
    {
        Center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        Radius = Math.Max(6f, Math.Min(bounds.Width, bounds.Height) / 2f);
    }
}

/// <summary>
/// Mozaik/bulanıklaştırma. Piksellenmiş görüntü, çizim anında dondurulmuş
/// ekran görüntüsünden üretilir ve çizimle birlikte saklanır (geri alma dahil).
/// </summary>
public sealed class MosaicAnnotation : Annotation
{
    public RectangleF Rect { get; set; }
    public Bitmap? Patch { get; set; }
    public bool IsBlur { get; set; }
    public int BlockSize { get; set; } = 12;

    public override void Draw(Graphics g, PointF offset)
    {
        var destination = new RectangleF(Rect.X + offset.X, Rect.Y + offset.Y, Rect.Width, Rect.Height);
        if (destination.Width <= 1f || destination.Height <= 1f) return;

        if (Patch is not null)
        {
            var prevInterpolation = g.InterpolationMode;
            var prevOffset = g.PixelOffsetMode;
            g.InterpolationMode = IsBlur ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(Patch, destination);
            g.InterpolationMode = prevInterpolation;
            g.PixelOffsetMode = prevOffset;

            // Hafif estetik sınır çizgisi: kullanıcının nereye mozaik/bulanıklık uyguladığını net görmesini sağlar
            using var borderPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(borderPen, destination.X, destination.Y, destination.Width, destination.Height);
        }
        else
        {
            // Çizim/sürükleme anında Patch henüz oluşmamışken:
            // Tıpkı Ctrl+1 seçim dikdörtgeni gibi belirgin mavi çerçeve, saydam dolgu, tutamaçlar ve boyut göstergesi çiz
            using var fillBrush = new SolidBrush(Color.FromArgb(36, 0, 122, 255));
            g.FillRectangle(fillBrush, destination);

            using var pen = new Pen(Color.FromArgb(220, 0, 122, 255), 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, destination.X, destination.Y, destination.Width, destination.Height);

            // Köşe tutamaçları
            var handleSize = 6f;
            var handles = new[]
            {
                new PointF(destination.Left, destination.Top),
                new PointF(destination.Right, destination.Top),
                new PointF(destination.Right, destination.Bottom),
                new PointF(destination.Left, destination.Bottom),
            };

            using var handleFill = new SolidBrush(Color.FromArgb(0, 122, 255));
            using var handleBorder = new Pen(Color.White, 1.2f);
            foreach (var h in handles)
            {
                var hRect = new RectangleF(h.X - handleSize / 2f, h.Y - handleSize / 2f, handleSize, handleSize);
                g.FillRectangle(handleFill, hRect);
                g.DrawRectangle(handleBorder, hRect.X, hRect.Y, hRect.Width, hRect.Height);
            }

            // Boyut etiketi
            var badgeText = $"{Math.Round(destination.Width)} × {Math.Round(destination.Height)} px · {(IsBlur ? "Bulanıklaştır" : "Mozaik")}";
            var badgeSize = g.MeasureString(badgeText, SnapDesk.UI.Theme.SmallFont);
            var badgeY = destination.Top - badgeSize.Height - 8f;
            if (badgeY < 4f) badgeY = destination.Top + 6f;
            var badgeRect = new RectangleF(destination.Left, badgeY, badgeSize.Width + 12f, badgeSize.Height + 4f);

            using var badgePath = SnapDesk.UI.Theme.RoundedRect(badgeRect, 4f);
            using var badgeBack = new SolidBrush(Color.FromArgb(210, 24, 24, 28));
            using var badgeStroke = new Pen(Color.FromArgb(60, 255, 255, 255), 1f);
            g.FillPath(badgeBack, badgePath);
            g.DrawPath(badgeStroke, badgePath);
            TextRenderer.DrawText(g, badgeText, SnapDesk.UI.Theme.SmallFont, Rectangle.Round(badgeRect), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    public override RectangleF GetBounds() => Rect;

    public override bool HitTest(PointF point, float tolerance) => Rect.Contains(point);

    public override Annotation Clone() => new MosaicAnnotation
    {
        Rect = Rect,
        Patch = Patch is null ? null : (Bitmap)Patch.Clone(),
        IsBlur = IsBlur,
        BlockSize = BlockSize,
        Color = Color,
        Width = Width,
        Dash = Dash,
    };

    public override void Move(float dx, float dy)
        => Rect = new RectangleF(Rect.X + dx, Rect.Y + dy, Rect.Width, Rect.Height);

    public override void SetBounds(RectangleF bounds)
        => Rect = bounds;
}
