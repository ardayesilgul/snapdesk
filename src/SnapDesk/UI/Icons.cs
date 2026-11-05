using System.Drawing.Drawing2D;

namespace SnapDesk.UI;

public enum IconKind
{
    DragHandle,
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
    Undo,
    Redo,
    Crop,
    Video,
    Ocr,
    Pin,
    Save,
    Close,
    Copy,
    More,
    Play,
    Pause,
    Stop,
    Mic,
    Check,
    Settings,
    Folder,
    Move,
    Lock,
    Image,
    ZoomIn,
    ZoomOut,
    Refresh,
    Shadow,
    Ratio,
    Translate,
    Layers,
    Cursor,
    LongCapture,
}

/// <summary>
/// Özgün vektör ikonlar. Hiçbir simge dosyası kopyalanmaz; her biri burada
/// çizilen geometrik yollardan oluşur.
/// </summary>
public static class Icons
{
    public static void Draw(Graphics g, IconKind kind, RectangleF box, Color color, float stroke = 1.6f)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        using var brush = new SolidBrush(color);

        // 0..20 tasarım uzayını kutuya ölçekle.
        var scale = Math.Min(box.Width, box.Height) / 20f;
        g.TranslateTransform(box.Left + (box.Width - 20f * scale) / 2f, box.Top + (box.Height - 20f * scale) / 2f);
        g.ScaleTransform(scale, scale);

        switch (kind)
        {
            case IconKind.DragHandle:
                for (var row = 0; row < 3; row++)
                {
                    for (var col = 0; col < 2; col++)
                    {
                        g.FillEllipse(brush, 6f + col * 4f, 5f + row * 5f, 2.2f, 2.2f);
                    }
                }
                break;

            case IconKind.Rectangle:
                g.DrawRectangle(pen, 3.5f, 4.5f, 13f, 11f);
                break;

            case IconKind.Ellipse:
                g.DrawEllipse(pen, 3.5f, 4.5f, 13f, 11f);
                break;

            case IconKind.Pen:
                g.DrawLine(pen, 4.5f, 15.5f, 5f, 11.5f);
                g.DrawLine(pen, 5f, 11.5f, 13.5f, 3.5f);
                g.DrawLine(pen, 13.5f, 3.5f, 16.5f, 6.5f);
                g.DrawLine(pen, 16.5f, 6.5f, 8f, 14.5f);
                g.DrawLine(pen, 8f, 14.5f, 4.5f, 15.5f);
                break;

            case IconKind.Arrow:
                g.DrawLine(pen, 4f, 16f, 15.5f, 4.5f);
                g.DrawLine(pen, 15.5f, 4.5f, 10f, 4.8f);
                g.DrawLine(pen, 15.5f, 4.5f, 15.2f, 10.2f);
                break;

            case IconKind.Line:
                g.DrawLine(pen, 4f, 16f, 16f, 4f);
                break;

            case IconKind.Text:
                g.DrawLine(pen, 4.5f, 4.5f, 15.5f, 4.5f);
                g.DrawLine(pen, 10f, 4.5f, 10f, 16f);
                g.DrawLine(pen, 7.5f, 16f, 12.5f, 16f);
                break;

            case IconKind.Serial:
                g.DrawEllipse(pen, 3.5f, 3.5f, 13f, 13f);
                g.DrawLine(pen, 9.6f, 6.6f, 10.8f, 6f);
                g.DrawLine(pen, 10.8f, 6f, 10.8f, 14f);
                g.DrawLine(pen, 8.8f, 14f, 12.8f, 14f);
                break;

            case IconKind.Mosaic:
                for (var row = 0; row < 2; row++)
                {
                    for (var col = 0; col < 2; col++)
                    {
                        var x = 4f + col * 6.5f;
                        var y = 4f + row * 6.5f;
                        g.FillRectangle(row == col ? brush : brush, x, y, 5f, 5f);
                    }
                }
                break;

            case IconKind.Blur:
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(10f, 3f, 15f, 9f, 16.2f, 11.5f, 16.2f, 13f);
                    path.AddArc(3.8f, 6.8f, 12.4f, 12.4f, 0, 180);
                    path.AddBezier(3.8f, 13f, 3.8f, 11.5f, 5f, 9f, 10f, 3f);
                    g.DrawPath(pen, path);
                }
                break;

            case IconKind.Highlighter:
                g.DrawLine(pen, 4f, 15.5f, 7f, 12.5f);
                g.DrawLine(pen, 7f, 12.5f, 13.5f, 6f);
                g.DrawLine(pen, 13.5f, 6f, 16f, 8.5f);
                g.DrawLine(pen, 16f, 8.5f, 9.5f, 15f);
                g.DrawLine(pen, 9.5f, 15f, 4f, 15.5f);
                g.DrawLine(pen, 3.5f, 17.5f, 8.5f, 17.5f);
                break;

            case IconKind.Eraser:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(4f, 12.5f), new PointF(10f, 6.5f), new PointF(15.5f, 12f),
                    new PointF(9.5f, 18f), new PointF(4f, 12.5f),
                });
                g.DrawLine(pen, 8f, 8.5f, 13.5f, 14f);
                break;

            case IconKind.Undo:
                g.DrawArc(pen, 4f, 5f, 12f, 11f, 210, 200);
                g.DrawLine(pen, 4.6f, 11.8f, 4.2f, 6.6f);
                g.DrawLine(pen, 4.6f, 11.8f, 9.4f, 10.2f);
                break;

            case IconKind.Redo:
                g.DrawArc(pen, 4f, 5f, 12f, 11f, 130, 200);
                g.DrawLine(pen, 15.4f, 11.8f, 15.8f, 6.6f);
                g.DrawLine(pen, 15.4f, 11.8f, 10.6f, 10.2f);
                break;

            case IconKind.Crop:
                g.DrawLine(pen, 6f, 3f, 6f, 14f);
                g.DrawLine(pen, 6f, 14f, 17f, 14f);
                g.DrawLine(pen, 3f, 6f, 14f, 6f);
                g.DrawLine(pen, 14f, 6f, 14f, 17f);
                break;

            case IconKind.Video:
                g.DrawRectangle(pen, 3f, 6f, 10.5f, 8.5f);
                g.DrawPolygon(pen, new[]
                {
                    new PointF(13.5f, 9f), new PointF(17f, 6.8f), new PointF(17f, 13.6f), new PointF(13.5f, 11.5f),
                });
                break;

            case IconKind.Ocr:
                g.DrawLine(pen, 4f, 5f, 4f, 3.5f);
                g.DrawLine(pen, 4f, 3.5f, 6f, 3.5f);
                g.DrawLine(pen, 16f, 3.5f, 14f, 3.5f);
                g.DrawLine(pen, 16f, 3.5f, 16f, 5f);
                g.DrawLine(pen, 4f, 15f, 4f, 16.5f);
                g.DrawLine(pen, 4f, 16.5f, 6f, 16.5f);
                g.DrawLine(pen, 16f, 15f, 16f, 16.5f);
                g.DrawLine(pen, 16f, 16.5f, 14f, 16.5f);
                g.DrawLine(pen, 7f, 13.5f, 10f, 6f);
                g.DrawLine(pen, 10f, 6f, 13f, 13.5f);
                g.DrawLine(pen, 8.2f, 10.8f, 11.8f, 10.8f);
                break;

            case IconKind.Pin:
                g.DrawLine(pen, 12.5f, 3f, 17f, 7.5f);
                g.DrawLine(pen, 14.8f, 5.3f, 10.5f, 9.5f);
                g.DrawLine(pen, 10.5f, 9.5f, 12f, 11f);
                g.DrawLine(pen, 12f, 11f, 8.5f, 14.5f);
                g.DrawLine(pen, 8.5f, 14.5f, 6.5f, 12.5f);
                g.DrawLine(pen, 6.5f, 12.5f, 9.5f, 9f);
                g.DrawLine(pen, 9.5f, 9f, 11f, 10.5f);
                g.DrawLine(pen, 11f, 10.5f, 14.8f, 5.3f);
                g.DrawLine(pen, 8.5f, 14.5f, 4.5f, 17.5f);
                break;

            case IconKind.Save:
                g.DrawLine(pen, 10f, 3f, 10f, 12f);
                g.DrawLine(pen, 6.5f, 8.5f, 10f, 12f);
                g.DrawLine(pen, 13.5f, 8.5f, 10f, 12f);
                g.DrawLine(pen, 4f, 15f, 4f, 17f);
                g.DrawLine(pen, 16f, 15f, 16f, 17f);
                g.DrawLine(pen, 4f, 17f, 16f, 17f);
                break;

            case IconKind.Close:
                g.DrawLine(pen, 5f, 5f, 15f, 15f);
                g.DrawLine(pen, 15f, 5f, 5f, 15f);
                break;

            case IconKind.Copy:
                g.DrawRectangle(pen, 3.5f, 3.5f, 9f, 9f);
                g.DrawRectangle(pen, 7.5f, 7.5f, 9f, 9f);
                break;

            case IconKind.More:
                g.FillEllipse(brush, 4f, 8.8f, 2.6f, 2.6f);
                g.FillEllipse(brush, 8.7f, 8.8f, 2.6f, 2.6f);
                g.FillEllipse(brush, 13.4f, 8.8f, 2.6f, 2.6f);
                break;

            case IconKind.Play:
                g.FillPolygon(brush, new[]
                {
                    new PointF(6f, 3.8f), new PointF(16.5f, 10f), new PointF(6f, 16.2f),
                });
                break;

            case IconKind.Pause:
                g.FillRectangle(brush, 5.5f, 4f, 3.2f, 12f);
                g.FillRectangle(brush, 11.3f, 4f, 3.2f, 12f);
                break;

            case IconKind.Stop:
                g.FillRectangle(brush, 5f, 5f, 10f, 10f);
                break;

            case IconKind.Mic:
                DrawRoundedRectangle(g, pen, 7.5f, 3f, 5f, 8.5f, 2.5f);
                g.DrawArc(pen, 5f, 7.5f, 10f, 8f, 0, 180);
                g.DrawLine(pen, 10f, 15.5f, 10f, 17.5f);
                break;

            case IconKind.Check:
                g.DrawLine(pen, 4f, 10.5f, 8f, 14.5f);
                g.DrawLine(pen, 8f, 14.5f, 16f, 5.5f);
                break;

            case IconKind.Settings:
                for (var i = 0; i < 3; i++)
                {
                    var y = 5f + i * 5f;
                    g.DrawLine(pen, 3.5f, y, 16.5f, y);
                    var knobX = i == 0 ? 12.5f : i == 1 ? 7.5f : 11f;
                    g.FillEllipse(brush, knobX - 1.8f, y - 1.8f, 3.6f, 3.6f);
                }
                break;

            case IconKind.Folder:
                g.DrawLine(pen, 3f, 6f, 8f, 6f);
                g.DrawLine(pen, 8f, 6f, 9.5f, 8f);
                g.DrawLine(pen, 9.5f, 8f, 17f, 8f);
                g.DrawLine(pen, 17f, 8f, 17f, 15.5f);
                g.DrawLine(pen, 17f, 15.5f, 3f, 15.5f);
                g.DrawLine(pen, 3f, 15.5f, 3f, 6f);
                break;

            case IconKind.Move:
                g.DrawLine(pen, 10f, 3f, 10f, 17f);
                g.DrawLine(pen, 3f, 10f, 17f, 10f);
                g.DrawLine(pen, 10f, 3f, 7.8f, 5.2f);
                g.DrawLine(pen, 10f, 3f, 12.2f, 5.2f);
                g.DrawLine(pen, 10f, 17f, 7.8f, 14.8f);
                g.DrawLine(pen, 10f, 17f, 12.2f, 14.8f);
                g.DrawLine(pen, 3f, 10f, 5.2f, 7.8f);
                g.DrawLine(pen, 3f, 10f, 5.2f, 12.2f);
                g.DrawLine(pen, 17f, 10f, 14.8f, 7.8f);
                g.DrawLine(pen, 17f, 10f, 14.8f, 12.2f);
                break;

            case IconKind.Lock:
                DrawRoundedRectangle(g, pen, 5f, 9f, 10f, 8f, 1.5f);
                g.DrawArc(pen, 6.8f, 4f, 6.4f, 8f, 180, 180);
                break;

            case IconKind.Image:
                g.DrawRectangle(pen, 3.5f, 4.5f, 13f, 11f);
                g.DrawLine(pen, 3.5f, 13f, 7.5f, 9f);
                g.DrawLine(pen, 7.5f, 9f, 11f, 12.5f);
                g.DrawLine(pen, 11f, 12.5f, 13.5f, 10f);
                g.DrawLine(pen, 13.5f, 10f, 16.5f, 13f);
                g.FillEllipse(brush, 12f, 6.5f, 2.2f, 2.2f);
                break;

            case IconKind.ZoomIn:
            case IconKind.ZoomOut:
                g.DrawEllipse(pen, 3.5f, 3.5f, 10f, 10f);
                g.DrawLine(pen, 12.5f, 12.5f, 17f, 17f);
                g.DrawLine(pen, 6f, 8.5f, 11f, 8.5f);
                if (kind == IconKind.ZoomIn)
                {
                    g.DrawLine(pen, 8.5f, 6f, 8.5f, 11f);
                }
                break;

            case IconKind.Refresh:
                g.DrawArc(pen, 4f, 4f, 12f, 12f, 40, 280);
                g.DrawLine(pen, 15.3f, 4.4f, 16.6f, 8.6f);
                g.DrawLine(pen, 16.6f, 8.6f, 12.4f, 8f);
                break;

            case IconKind.Shadow:
                g.DrawRectangle(pen, 3.5f, 3.5f, 10f, 10f);
                g.DrawLine(pen, 17f, 7f, 17f, 16.5f);
                g.DrawLine(pen, 17f, 16.5f, 7.5f, 16.5f);
                break;

            case IconKind.Ratio:
                g.DrawRectangle(pen, 3.5f, 5.5f, 13f, 9f);
                g.DrawLine(pen, 8.5f, 5.5f, 8.5f, 14.5f);
                break;

            case IconKind.Translate:
                g.DrawLine(pen, 4f, 5f, 12f, 5f);
                g.DrawLine(pen, 8f, 3.5f, 8f, 5f);
                g.DrawArc(pen, 4.5f, 5f, 7f, 6f, 0, 180);
                g.DrawLine(pen, 10f, 17f, 13.5f, 8f);
                g.DrawLine(pen, 13.5f, 8f, 17f, 17f);
                g.DrawLine(pen, 11.2f, 13.5f, 15.8f, 13.5f);
                break;

            case IconKind.Layers:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(10f, 3.5f), new PointF(17f, 7.5f), new PointF(10f, 11.5f), new PointF(3f, 7.5f),
                });
                g.DrawLine(pen, 3f, 11f, 10f, 15f);
                g.DrawLine(pen, 17f, 11f, 10f, 15f);
                break;

            case IconKind.Pointer:
            case IconKind.Cursor:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(6f, 3.5f), new PointF(6f, 15.5f), new PointF(9.5f, 12.2f),
                    new PointF(12f, 17f), new PointF(14f, 16f), new PointF(11.5f, 11.4f), new PointF(16f, 11f),
                });
                break;

            case IconKind.LongCapture:
                DrawRoundedRectangle(g, pen, 4f, 3f, 12f, 14f, 1.8f);
                g.DrawLine(pen, 10f, 6f, 10f, 14f);
                g.DrawLine(pen, 7.5f, 11.5f, 10f, 14f);
                g.DrawLine(pen, 12.5f, 11.5f, 10f, 14f);
                break;
        }

        g.Restore(state);
    }

    private static void DrawRoundedRectangle(Graphics g, Pen pen, float x, float y, float w, float h, float r)
    {
        using var path = Theme.RoundedRect(new RectangleF(x, y, w, h), r);
        g.DrawPath(pen, path);
    }
}
