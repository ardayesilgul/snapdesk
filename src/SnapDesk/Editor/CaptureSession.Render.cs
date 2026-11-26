using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SnapDesk.Core;
using SnapDesk.UI;

namespace SnapDesk.Editor;

public sealed partial class CaptureSession
{
    private const int HandleSize = 8;

    public void Render(Graphics g, OverlayForm form)
    {
        var monitor = form.Monitor.Bounds;
        Theme.Prepare(g);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.FillRectangle(Brushes.Black, 0, 0, monitor.Width, monitor.Height);
        g.CompositingMode = CompositingMode.SourceOver;

        // 1) Donmuş görüntünün bu monitöre düşen parçası
        var imageRect = new Rectangle(_imageOrigin, _frozen.Size);
        var visible = Rectangle.Intersect(monitor, imageRect);
        if (visible.Width > 0 && visible.Height > 0)
        {
            var source = new Rectangle(
                visible.X - _imageOrigin.X,
                visible.Y - _imageOrigin.Y,
                visible.Width,
                visible.Height);
            var destination = new Rectangle(
                visible.X - monitor.X,
                visible.Y - monitor.Y,
                visible.Width,
                visible.Height);

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_frozen, destination, source, GraphicsUnit.Pixel);
        }

        // 2) Seçim dışını karart (alfa: 120)
        if (HasSelection)
        {
            var selectionOnMonitor = Rectangle.Intersect(Selection, monitor);
            var sel = new Rectangle(
                selectionOnMonitor.X - monitor.X,
                selectionOnMonitor.Y - monitor.Y,
                selectionOnMonitor.Width,
                selectionOnMonitor.Height);

            if (sel.Width > 0 && sel.Height > 0)
            {
                using var dim = new SolidBrush(Theme.Dim);
                g.FillRectangle(dim, 0, 0, monitor.Width, Math.Max(0, sel.Top));
                g.FillRectangle(dim, 0, sel.Bottom, monitor.Width, Math.Max(0, monitor.Height - sel.Bottom));
                g.FillRectangle(dim, 0, sel.Top, Math.Max(0, sel.Left), sel.Height);
                g.FillRectangle(dim, sel.Right, sel.Top, Math.Max(0, monitor.Width - sel.Right), sel.Height);
            }
            else
            {
                using var dim = new SolidBrush(Theme.Dim);
                g.FillRectangle(dim, 0, 0, monitor.Width, monitor.Height);
            }
        }
        else
        {
            using var dim = new SolidBrush(Theme.Dim);
            g.FillRectangle(dim, 0, 0, monitor.Width, monitor.Height);
        }

        var offset = new PointF(-monitor.X, -monitor.Y);

        // 3) Çizimler (seçim alanına kırpılır)
        if (HasSelection)
        {
            var clip = new Rectangle(
                Selection.X - monitor.X,
                Selection.Y - monitor.Y,
                Selection.Width,
                Selection.Height);

            var state = g.Save();
            g.SetClip(clip);
            foreach (var annotation in Annotations)
            {
                annotation.Draw(g, offset);
            }

            DrawTextEditing(g, offset);
            g.Restore(state);
        }

        if (!HasSelection)
        {
            DrawHoverWindow(g, offset);
            DrawMagnifier(g, monitor);
            DrawHints(g, monitor);
            return;
        }

        // 4) Seçim çerçevesi ve tutamaçlar
        DrawSelection(g, monitor);

        // 5) Seçili çizim çerçevesi
        if (SelectedAnnotation is not null)
        {
            DrawAnnotationSelection(g, monitor, SelectedAnnotation);
        }

        DrawSizeLabel(g, monitor);
        DrawMagnifier(g, monitor);

        if (Tool == ToolKind.Pointer && Annotations.Count == 0)
        {
            DrawHints(g, monitor);
        }
    }

    private void DrawSelection(Graphics g, Rectangle monitor)
    {
        var rect = new Rectangle(
            Selection.X - monitor.X,
            Selection.Y - monitor.Y,
            Selection.Width,
            Selection.Height);

        using var pen = new Pen(Theme.SelectionBorder, 1f);
        g.DrawRectangle(pen, rect);

        // Tutamaçlar
        foreach (var handle in HandleRects(rect))
        {
            using var fill = new SolidBrush(Theme.HandleFill);
            using var border = new Pen(Theme.HandleBorder, 1.5f);
            g.FillEllipse(fill, handle);
            g.DrawEllipse(border, handle);
        }
    }

    private void DrawAnnotationSelection(Graphics g, Rectangle monitor, Annotation annotation)
    {
        var bounds = annotation.GetBounds();
        var rect = Rectangle.Round(new RectangleF(
            bounds.X - monitor.X,
            bounds.Y - monitor.Y,
            bounds.Width,
            bounds.Height));

        using var pen = new Pen(Theme.SelectionBorder, 1f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, rect);

        foreach (var handle in HandleRects(rect))
        {
            using var fill = new SolidBrush(Theme.Accent);
            using var border = new Pen(Color.White, 1f);
            g.FillRectangle(fill, handle);
            g.DrawRectangle(border, Rectangle.Round(handle));
        }
    }

    private void DrawSizeLabel(Graphics g, Rectangle monitor)
    {
        var text = $"{Selection.X},{Selection.Y}   {Selection.Width} × {Selection.Height} px";
        var font = Theme.SmallFont;
        var size = g.MeasureString(text, font);

        var x = Selection.Left - monitor.X;
        var y = Selection.Top - monitor.Y - size.Height - 10;

        if (y < 4)
        {
            y = Selection.Top - monitor.Y + 8;
            x += 8;
        }

        var box = new RectangleF(x, y, size.Width + 14, size.Height + 6);

        using var path = Theme.RoundedRect(box, 4);
        using var back = new SolidBrush(Color.FromArgb(190, 24, 24, 28));
        g.FillPath(back, path);

        using var brush = new SolidBrush(Theme.Text);
        g.DrawString(text, font, brush, box.Left + 7, box.Top + 3);
    }

    private void DrawHoverWindow(Graphics g, PointF offset)
    {
        if (_hoverWindow is null || !_hasMouse)
        {
            return;
        }

        var bounds = _hoverWindow.Bounds;
        var rect = new Rectangle(
            (int)(bounds.X + offset.X),
            (int)(bounds.Y + offset.Y),
            bounds.Width,
            bounds.Height);

        using var pen = new Pen(Theme.SelectionBorder, 2f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, rect);

        var title = string.IsNullOrWhiteSpace(_hoverWindow.Title) ? "Pencere" : _hoverWindow.Title;
        var font = Theme.SmallFont;
        var size = g.MeasureString(title, font);
        var label = new RectangleF(rect.Left, Math.Max(0, rect.Top - size.Height - 8), size.Width + 12, size.Height + 5);

        using var path = Theme.RoundedRect(label, 3);
        using var back = new SolidBrush(Color.FromArgb(210, 24, 24, 28));
        g.FillPath(back, path);
        using var brush = new SolidBrush(Theme.Text);
        g.DrawString(title, font, brush, label.Left + 6, label.Top + 2);
    }

    private void DrawMagnifier(Graphics g, Rectangle monitor)
    {
        if (!_settings.ShowMagnifier || !_hasMouse || !monitor.Contains(_lastMouse))
        {
            return;
        }

        // Seçim tamamlandıktan sonra büyüteç gizlenir; yalnızca bölge seçilirken
        // ve henüz seçim yokken gösterilir.
        if (_dragMode != DragMode.None && _dragMode != DragMode.Selecting)
        {
            return;
        }

        if (HasSelection && _dragMode != DragMode.Selecting)
        {
            return;
        }

        const int gridCells = 21;
        const int zoomFactor = 8;
        const int zoomArea = gridCells * zoomFactor; // 168 px
        const int padding = 10;
        const int width = zoomArea + padding * 2;    // 188 px
        const int height = zoomArea + 66;            // 234 px

        var x = _lastMouse.X - monitor.X + 24;
        var y = _lastMouse.Y - monitor.Y + 24;

        if (x + width > monitor.Width - 8) x = _lastMouse.X - monitor.X - width - 24;
        if (y + height > monitor.Height - 8) y = _lastMouse.Y - monitor.Y - height - 24;
        x = Math.Max(8, x);
        y = Math.Max(8, y);

        var panel = new Rectangle(x, y, width, height);

        using (var path = Theme.RoundedRect(panel, 8))
        using (var back = new SolidBrush(Color.FromArgb(238, 22, 22, 26)))
        using (var border = new Pen(Color.FromArgb(82, 82, 92), 1f))
        {
            g.FillPath(back, path);
            g.DrawPath(border, path);
        }

        var zoomRect = new Rectangle(panel.Left + padding, panel.Top + padding, zoomArea, zoomArea);

        var sourcePoint = new Point(_lastMouse.X - _imageOrigin.X, _lastMouse.Y - _imageOrigin.Y);
        const int half = gridCells / 2;
        var sourceRect = new Rectangle(
            sourcePoint.X - half,
            sourcePoint.Y - half,
            gridCells,
            gridCells);

        var state = g.Save();
        g.SetClip(zoomRect);
        using (var zoomBack = new SolidBrush(Color.FromArgb(14, 14, 18)))
        {
            g.FillRectangle(zoomBack, zoomRect);
        }

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var clamped = Rectangle.Intersect(sourceRect, new Rectangle(0, 0, _frozen.Width, _frozen.Height));
        if (clamped.Width > 0 && clamped.Height > 0)
        {
            var destRect = new Rectangle(
                zoomRect.Left + (clamped.Left - sourceRect.Left) * zoomFactor,
                zoomRect.Top + (clamped.Top - sourceRect.Top) * zoomFactor,
                clamped.Width * zoomFactor,
                clamped.Height * zoomFactor);
            g.DrawImage(_frozen, destRect, clamped, GraphicsUnit.Pixel);
        }

        // İnce piksel ızgarası
        using (var gridPen = new Pen(Color.FromArgb(22, 255, 255, 255), 1f))
        {
            for (var i = 1; i < gridCells; i++)
            {
                var gx = zoomRect.Left + i * zoomFactor;
                var gy = zoomRect.Top + i * zoomFactor;
                g.DrawLine(gridPen, gx, zoomRect.Top, gx, zoomRect.Bottom);
                g.DrawLine(gridPen, zoomRect.Left, gy, zoomRect.Right, gy);
            }
        }

        // Merkez piksel ve eksen çizgileri
        var centerCell = new Rectangle(
            zoomRect.Left + half * zoomFactor,
            zoomRect.Top + half * zoomFactor,
            zoomFactor,
            zoomFactor);

        using (var axisPen = new Pen(Color.FromArgb(110, 58, 122, 254), 1f))
        {
            var cx = centerCell.Left + zoomFactor / 2;
            var cy = centerCell.Top + zoomFactor / 2;
            g.DrawLine(axisPen, zoomRect.Left, cy, centerCell.Left - 1, cy);
            g.DrawLine(axisPen, centerCell.Right + 1, cy, zoomRect.Right, cy);
            g.DrawLine(axisPen, cx, zoomRect.Top, cx, centerCell.Top - 1);
            g.DrawLine(axisPen, cx, centerCell.Bottom + 1, cx, zoomRect.Bottom);
        }

        using (var centerOuter = new Pen(Color.FromArgb(230, 255, 80, 80), 1.5f))
        {
            g.DrawRectangle(centerOuter, centerCell);
        }

        g.Restore(state);

        using (var framePen = new Pen(Color.FromArgb(72, 72, 82), 1f))
        {
            g.DrawRectangle(framePen, zoomRect);
        }

        var font = Theme.SmallFont;
        using var textBrush = new SolidBrush(Color.FromArgb(232, 232, 238));

        var infoY = zoomRect.Bottom + 7;
        var coordinateText = $"{_lastMouse.X}, {_lastMouse.Y}";
        g.DrawString(coordinateText, font, textBrush, panel.Left + padding, infoY);

        var colorText = string.Empty;
        var pixelPoint = new Point(_lastMouse.X - _imageOrigin.X, _lastMouse.Y - _imageOrigin.Y);
        if (pixelPoint.X >= 0 && pixelPoint.Y >= 0 && pixelPoint.X < _frozen.Width && pixelPoint.Y < _frozen.Height)
        {
            var pixel = _frozen.GetPixel(pixelPoint.X, pixelPoint.Y);
            colorText = FormatPixelColor(pixel, MagnifierColorFormat);
            var swatchRect = new Rectangle(panel.Right - padding - 13, infoY + 2, 13, 13);
            using var swatch = new SolidBrush(pixel);
            g.FillRectangle(swatch, swatchRect);
            using var swatchBorder = new Pen(Color.FromArgb(170, 170, 180));
            g.DrawRectangle(swatchBorder, swatchRect);
        }

        if (!string.IsNullOrEmpty(colorText))
        {
            var rgbSize = g.MeasureString(colorText, font);
            g.DrawString(colorText, font, textBrush, panel.Right - padding - 17 - rgbSize.Width, infoY);
        }

        var hintY = zoomRect.Bottom + 27;
        if (_copiedColorFeedback is not null && DateTime.UtcNow < _copiedColorUntil)
        {
            using var okBrush = new SolidBrush(Color.FromArgb(110, 225, 145));
            g.DrawString(_copiedColorFeedback, font, okBrush, panel.Left + padding, hintY);
        }
        else
        {
            using var hintBrush = new SolidBrush(Color.FromArgb(156, 156, 168));
            g.DrawString("Shift: biçim · C: rengi kopyala", font, hintBrush, panel.Left + padding, hintY);
        }
    }

    private void DrawHints(Graphics g, Rectangle monitor)
    {
        if (!_hasMouse)
        {
            return;
        }

        var items = new (string Key, string Text)[]
        {
            ("Sürükle", "Bölge seç  ·  Tıkla: pencere seç"),
            ("Shift+C", "Metni algıla (OCR)"),
            ("Ctrl+T", "Ekrana sabitle (Pin)"),
            ("Ctrl+L", "Uzun yakalama  ·  Ctrl+G: Kayıt"),
            ("Ctrl+Z", "Geri al  ·  Enter: Kopyala"),
            ("Esc", "İptal"),
        };

        const int rowHeight = 22;
        const int keyColWidth = 68;
        using var keyFont = new Font("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Point);
        var textFont = Theme.SmallFont;

        float maxTextWidth = 0f;
        foreach (var item in items)
        {
            var measured = g.MeasureString(item.Text, textFont).Width;
            if (measured > maxTextWidth)
            {
                maxTextWidth = measured;
            }
        }

        var width = Math.Max(304, (int)Math.Ceiling(keyColWidth + maxTextWidth + 34));
        var extraHeader = Intent != CaptureIntent.Edit ? 26 : 0;
        var height = items.Length * rowHeight + 16 + extraHeader;

        var x = 16;
        var y = monitor.Height - height - 16;

        if (HasSelection)
        {
            var selLocal = new Rectangle(
                Selection.X - monitor.X,
                Selection.Y - monitor.Y,
                Selection.Width,
                Selection.Height);
            var bottomLeftZone = new Rectangle(x - 8, y - 8, width + 16, height + 16);
            var inflatedSel = Rectangle.Inflate(selLocal, 16, 56);
            if (inflatedSel.IntersectsWith(bottomLeftZone))
            {
                y = 16;
                var topLeftZone = new Rectangle(x - 8, y - 8, width + 16, height + 16);
                if (inflatedSel.IntersectsWith(topLeftZone))
                {
                    // Seçim hem sol altı hem sol üstü kaplıyorsa ipucu kutusunu gizle.
                    return;
                }
            }
        }

        var panel = new Rectangle(x, y, width, height);
        using (var path = Theme.RoundedRect(panel, 8))
        using (var back = new SolidBrush(Color.FromArgb(216, 22, 22, 26)))
        using (var border = new Pen(Color.FromArgb(72, 72, 82), 1f))
        {
            g.FillPath(back, path);
            g.DrawPath(border, path);
        }

        var startY = panel.Top + 8;
        if (Intent != CaptureIntent.Edit)
        {
            var modeTitle = Intent switch
            {
                CaptureIntent.Pin => "Sabitleme için bölge seçin",
                CaptureIntent.LongCapture => "Uzun yakalama için bölge seçin",
                CaptureIntent.Record => "Video/GIF kaydı için bölge seçin",
                CaptureIntent.Ocr => "Metni çıkartmak için bölge seçin",
                _ => string.Empty,
            };
            using var modeBrush = new SolidBrush(Color.FromArgb(120, 180, 255));
            g.DrawString(modeTitle, keyFont, modeBrush, panel.Left + 12, startY + 2);
            startY += extraHeader;
        }

        using var keyBrush = new SolidBrush(Color.FromArgb(215, 228, 255));
        using var keyBack = new SolidBrush(Color.FromArgb(52, 56, 68));
        using var keyBorder = new Pen(Color.FromArgb(82, 88, 104), 1f);
        using var textBrush = new SolidBrush(Color.FromArgb(195, 195, 205));
        using var keyFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        for (var i = 0; i < items.Length; i++)
        {
            var rowY = startY + i * rowHeight;
            var badgeRect = new RectangleF(panel.Left + 10, rowY + 1, keyColWidth - 6, rowHeight - 4);
            using (var badgePath = Theme.RoundedRect(badgeRect, 4))
            {
                g.FillPath(keyBack, badgePath);
                g.DrawPath(keyBorder, badgePath);
            }

            g.DrawString(items[i].Key, keyFont, keyBrush, badgeRect, keyFormat);
            g.DrawString(items[i].Text, textFont, textBrush, panel.Left + 14 + keyColWidth, rowY + 2);
        }
    }

    // ------------------------------------------------------------- tutamaçlar

    private static RectangleF[] HandleRects(Rectangle rect)
    {
        const float size = HandleSize;
        const float half = size / 2f;

        return new[]
        {
            new RectangleF(rect.Left - half, rect.Top - half, size, size),
            new RectangleF(rect.Left + rect.Width / 2f - half, rect.Top - half, size, size),
            new RectangleF(rect.Right - half, rect.Top - half, size, size),
            new RectangleF(rect.Right - half, rect.Top + rect.Height / 2f - half, size, size),
            new RectangleF(rect.Right - half, rect.Bottom - half, size, size),
            new RectangleF(rect.Left + rect.Width / 2f - half, rect.Bottom - half, size, size),
            new RectangleF(rect.Left - half, rect.Bottom - half, size, size),
            new RectangleF(rect.Left - half, rect.Top + rect.Height / 2f - half, size, size),
        };
    }

    private static int HitHandle(RectangleF[] handles, PointF point)
    {
        for (var i = 0; i < handles.Length; i++)
        {
            var inflated = RectangleF.Inflate(handles[i], 3, 3);
            if (inflated.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private static readonly Cursor[] ResizeCursors =
    {
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
    };
}
