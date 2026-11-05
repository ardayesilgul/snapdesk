using System.Diagnostics;
using System.Drawing.Drawing2D;
using SnapDesk.Core;
using SnapDesk.UI;

namespace SnapDesk.Editor;

public sealed partial class CaptureSession
{
    // ------------------------------------------------------------------ fare

    public void OnMouseDown(OverlayForm form, Point screen, MouseButtons button, Keys modifiers)
    {
        _lastMouse = screen;
        _hasMouse = true;
        _pressPoint = screen;
        _dragStart = screen;
        _gestureUndoPushed = false;

        if (button == MouseButtons.Right)
        {
            ShowContextMenu(screen);
            return;
        }

        if (button != MouseButtons.Left)
        {
            return;
        }

        form.Capture = true;

        if (_textEditing)
        {
            CommitTextIfAny();
        }

        if (!HasSelection)
        {
            StartNewSelection(screen);
            return;
        }

        // Seçim tutamacı mı?
        var selectionOnScreen = Selection;
        var handles = HandleRects(new Rectangle(0, 0, selectionOnScreen.Width, selectionOnScreen.Height));
        var localPoint = new PointF(screen.X - selectionOnScreen.Left, screen.Y - selectionOnScreen.Top);
        var handleIndex = HitHandle(handles, localPoint);

        if (handleIndex >= 0)
        {
            PushUndo();
            _gestureUndoPushed = true;
            _dragMode = DragMode.ResizingSelection;
            _resizeHandle = handleIndex;
            _initialSelection = Selection;
            _toolbar?.ResetUserPosition();
            return;
        }

        // Seçili çizimin tutamacı mı? (Yalnızca Seç/Taşı aracındayken)
        if (Tool == ToolKind.Pointer && SelectedAnnotation is not null)
        {
            var bounds = SelectedAnnotation.GetBounds();
            var annotationHandles = HandleRects(Rectangle.Round(bounds));
            var screenHandles = annotationHandles
                .Select(r => new RectangleF(r.X, r.Y, r.Width, r.Height))
                .ToArray();
            var hit = HitHandle(screenHandles, screen);
            if (hit >= 0)
            {
                PushUndo();
                _gestureUndoPushed = true;
                _dragMode = DragMode.ResizingAnnotation;
                _annotationResizeHandle = hit;
                _annotationStartBounds = bounds;
                _annotationResizeInitial = SelectedAnnotation.Clone();
                return;
            }
        }

        if (!Selection.Contains(screen))
        {
            // Seçim dışına tıklama: yeni seçim başlat.
            StartNewSelection(screen);
            return;
        }

        switch (Tool)
        {
            case ToolKind.Pointer:
                var hitAnnotation = HitTestAnnotation(screen);
                if (hitAnnotation is not null)
                {
                    SelectedAnnotation = hitAnnotation;
                    SyncSessionFromSelectedAnnotation();
                    PushUndo();
                    _gestureUndoPushed = true;
                    _dragMode = DragMode.MovingAnnotation;
                    _annotationDragOrigin = screen;
                    _toolbar?.OnSessionChanged();
                    PositionToolbar();
                    InvalidateAll();
                    return;
                }

                if (SelectedAnnotation is not null)
                {
                    SelectedAnnotation = null;
                    _toolbar?.OnSessionChanged();
                    PositionToolbar();
                    InvalidateAll();
                }

                _dragMode = DragMode.MovingSelection;
                _initialSelection = Selection;
                _toolbar?.ResetUserPosition();
                return;

            case ToolKind.Eraser:
                EraseAt(screen);
                _dragMode = DragMode.None;
                return;

            case ToolKind.Text:
                BeginText(screen);
                return;

            default:
                BeginAnnotation(screen);
                return;
        }
    }

    public void OnMouseMove(OverlayForm form, Point screen, MouseButtons buttons)
    {
        _lastMouse = screen;
        _hasMouse = true;

        switch (_dragMode)
        {
            case DragMode.Selecting:
                Selection = Normalize(screen);
                HasSelection = Selection.Width > 0 && Selection.Height > 0;
                InvalidateAll();
                return;

            case DragMode.MovingSelection:
                // İlk gerçek harekette geri alma noktası al; sadece tıklama
                // geri alma yığınını kirletmesin.
                if (!_gestureUndoPushed && (screen.X != _dragStart.X || screen.Y != _dragStart.Y))
                {
                    PushUndo();
                    _gestureUndoPushed = true;
                }

                var dx = screen.X - _dragStart.X;
                var dy = screen.Y - _dragStart.Y;
                var moved = new Rectangle(
                    _initialSelection.X + dx,
                    _initialSelection.Y + dy,
                    _initialSelection.Width,
                    _initialSelection.Height);
                moved = ClampToVirtual(moved);
                Selection = moved;
                PositionToolbar();
                InvalidateAll();
                return;

            case DragMode.ResizingSelection:
                ResizeSelection(screen);
                PositionToolbar();
                InvalidateAll();
                return;

            case DragMode.MovingAnnotation:
                if (SelectedAnnotation is not null)
                {
                    var adx = screen.X - _annotationDragOrigin.X;
                    var ady = screen.Y - _annotationDragOrigin.Y;
                    SelectedAnnotation.Move(adx, ady);
                    if (SelectedAnnotation is MosaicAnnotation movedMosaic)
                    {
                        BuildMosaicPatch(movedMosaic);
                    }
                    _annotationDragOrigin = screen;
                    InvalidateAll();
                }
                return;

            case DragMode.ResizingAnnotation:
                ResizeAnnotation(screen);
                InvalidateAll();
                return;

            case DragMode.Drawing:
                UpdateDrawing(screen);
                InvalidateAll();
                return;

            case DragMode.None:
                if (Tool == ToolKind.Eraser && buttons == MouseButtons.Left)
                {
                    EraseAt(screen);
                }

                UpdateHover(screen);
                UpdateCursorForPoint(screen);
                InvalidateAll();
                return;
        }
    }

    public void OnMouseUp(OverlayForm form, Point screen, MouseButtons button)
    {
        form.Capture = false;

        if (button != MouseButtons.Left)
        {
            return;
        }

        var mode = _dragMode;
        _dragMode = DragMode.None;

        switch (mode)
        {
            case DragMode.Selecting:
                FinishSelection(screen);
                break;

            case DragMode.Drawing:
                FinishDrawing(screen);
                break;

            case DragMode.MovingSelection:
            case DragMode.ResizingSelection:
            case DragMode.MovingAnnotation:
            case DragMode.ResizingAnnotation:
                PositionToolbar();
                _toolbar?.OnSessionChanged();
                InvalidateAll();
                break;
        }

        _resizeHandle = -1;
        _annotationResizeHandle = -1;
        _annotationResizeInitial = null;
        UpdateCursorForPoint(screen);

        if (_textEditing)
        {
            FocusOverlay();
        }
    }

    public void OnDoubleClick(OverlayForm form, Point screen, MouseButtons button)
    {
        if (button != MouseButtons.Left)
        {
            return;
        }

        // Metin çizimini düzenlemek için çift tıkla.
        var hit = HitTestAnnotation(screen);
        if (hit is TextAnnotation text)
        {
            PushUndo();
            Annotations.Remove(text);
            BeginText(Point.Round(text.Location), text.Text, text);
            InvalidateAll();
            return;
        }

        var window = WindowEnumerator.TopWindowAt(screen, (uint)Environment.ProcessId);
        if (window is not null)
        {
            SetSelection(Rectangle.Intersect(window.Bounds, _virtualBounds));
        }
    }

    public void OnMouseWheel(OverlayForm form, Point screen, int delta)
    {
        if (!HasSelection)
        {
            return;
        }

        var step = delta > 0 ? 1 : -1;

        if (Tool is ToolKind.Pen or ToolKind.Highlighter or ToolKind.Arrow or ToolKind.Line or ToolKind.Rectangle or ToolKind.Ellipse)
        {
            CurrentWidth = Math.Clamp(CurrentWidth + step, 1f, 60f);
            _toolbar?.OnSessionChanged();
            InvalidateAll();
        }
    }

    // ------------------------------------------------------------- seçim

    private void StartNewSelection(Point screen)
    {
        SelectedAnnotation = null;
        HasSelection = false;
        Selection = Rectangle.Empty;
        _dragMode = DragMode.Selecting;
        _dragStart = screen;
        _toolbar?.ResetUserPosition();
        _toolbar?.Hide();
        InvalidateAll();
    }

    private Rectangle Normalize(Point current)
    {
        var left = Math.Min(_dragStart.X, current.X);
        var top = Math.Min(_dragStart.Y, current.Y);
        var right = Math.Max(_dragStart.X, current.X);
        var bottom = Math.Max(_dragStart.Y, current.Y);
        return Rectangle.Intersect(Rectangle.FromLTRB(left, top, right, bottom), _virtualBounds);
    }

    private Rectangle ClampToVirtual(Rectangle rect)
    {
        // Taşımada boyut korunur, yalnızca konum sınırlanır. Sınır, ekranın
        // kendisi değil bu oturumun görüntüsüdür: kırpılmış bir oturumda seçim
        // görüntünün dışına taşamaz.
        var bounds = _virtualBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return rect;
        }

        var x = Math.Clamp(rect.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - rect.Width));
        var y = Math.Clamp(rect.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - rect.Height));
        return new Rectangle(x, y, rect.Width, rect.Height);
    }

    private void FinishSelection(Point screen)
    {
        var distance = Math.Abs(screen.X - _pressPoint.X) + Math.Abs(screen.Y - _pressPoint.Y);
        if (distance <= 3)
        {
            // Tıklama: altındaki pencereyi seç.
            var window = WindowEnumerator.TopWindowAt(screen, (uint)Environment.ProcessId);
            if (window is not null)
            {
                SetSelection(Rectangle.Intersect(window.Bounds, _virtualBounds), showToolbar: Intent == CaptureIntent.Edit);
                OnSelectionCompleted();
            }
            else
            {
                ClearSelection();
            }
            return;
        }

        var rect = Normalize(screen);
        if (rect.Width < 2 || rect.Height < 2)
        {
            ClearSelection();
            return;
        }

        SetSelection(rect, showToolbar: Intent == CaptureIntent.Edit);
        OnSelectionCompleted();
    }

    private void OnSelectionCompleted()
    {
        if (!HasSelection)
        {
            return;
        }

        switch (Intent)
        {
            case CaptureIntent.Pin:
                Pin();
                return;
            case CaptureIntent.LongCapture:
                StartLongCapture();
                return;
            case CaptureIntent.Record:
                StartRecording();
                return;
            case CaptureIntent.Ocr:
                RunOcr();
                return;
        }

        if (_settings.CopyToClipboardOnCapture)
        {
            CopyAllToClipboard();
        }
    }

    private void ResizeSelection(Point screen)
    {
        var left = _initialSelection.Left;
        var top = _initialSelection.Top;
        var right = _initialSelection.Right;
        var bottom = _initialSelection.Bottom;

        switch (_resizeHandle)
        {
            case 0: left = screen.X; top = screen.Y; break;
            case 1: top = screen.Y; break;
            case 2: right = screen.X; top = screen.Y; break;
            case 3: right = screen.X; break;
            case 4: right = screen.X; bottom = screen.Y; break;
            case 5: bottom = screen.Y; break;
            case 6: left = screen.X; bottom = screen.Y; break;
            case 7: left = screen.X; break;
        }

        if (right - left < 8)
        {
            if (_resizeHandle is 0 or 6 or 7) left = right - 8;
            else right = left + 8;
        }

        if (bottom - top < 8)
        {
            if (_resizeHandle is 0 or 1 or 2) top = bottom - 8;
            else bottom = top + 8;
        }

        Selection = Rectangle.Intersect(Rectangle.FromLTRB(left, top, right, bottom), _virtualBounds);
        HasSelection = Selection.Width >= 8 && Selection.Height >= 8;
    }

    private void ResizeAnnotation(Point screen)
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        var left = _annotationStartBounds.Left;
        var top = _annotationStartBounds.Top;
        var right = _annotationStartBounds.Right;
        var bottom = _annotationStartBounds.Bottom;

        switch (_annotationResizeHandle)
        {
            case 0: left = screen.X; top = screen.Y; break;
            case 1: top = screen.Y; break;
            case 2: right = screen.X; top = screen.Y; break;
            case 3: right = screen.X; break;
            case 4: right = screen.X; bottom = screen.Y; break;
            case 5: bottom = screen.Y; break;
            case 6: left = screen.X; bottom = screen.Y; break;
            case 7: left = screen.X; break;
        }

        if (right - left < 6)
        {
            if (_annotationResizeHandle is 0 or 6 or 7) left = right - 6;
            else right = left + 6;
        }

        if (bottom - top < 6)
        {
            if (_annotationResizeHandle is 0 or 1 or 2) top = bottom - 6;
            else bottom = top + 6;
        }

        var targetBounds = RectangleF.FromLTRB(left, top, right, bottom);

        switch (SelectedAnnotation)
        {
            case PenAnnotation pen when _annotationResizeInitial is PenAnnotation initPen:
                pen.Points.Clear();
                pen.Points.AddRange(initPen.Points);
                pen.SetBounds(targetBounds);
                break;

            case ArrowAnnotation arrow when _annotationResizeInitial is ArrowAnnotation initArrow:
                arrow.Start = initArrow.Start;
                arrow.End = initArrow.End;
                arrow.SetBounds(targetBounds);
                break;

            case TextAnnotation text when _annotationResizeInitial is TextAnnotation initText:
                text.Location = initText.Location;
                text.FontSize = initText.FontSize;
                text.SetBounds(targetBounds);
                break;

            default:
                SelectedAnnotation.SetBounds(targetBounds);
                break;
        }

        if (SelectedAnnotation is MosaicAnnotation mosaic)
        {
            BuildMosaicPatch(mosaic);
        }
    }

    // ------------------------------------------------------------- çizim

    private void BeginAnnotation(Point screen)
    {
        PushUndo();
        _gestureUndoPushed = true;
        _dragMode = DragMode.Drawing;

        switch (Tool)
        {
            case ToolKind.Rectangle:
            case ToolKind.Ellipse:
                _activeAnnotation = new RectangleAnnotation
                {
                    Rect = new RectangleF(screen, Size.Empty),
                    IsEllipse = Tool == ToolKind.Ellipse,
                    Color = CurrentColor,
                    Width = CurrentWidth,
                    Filled = Filled,
                    CornerRadius = CornerRadius,
                    Dash = Dash,
                };
                Annotations.Add(_activeAnnotation);
                break;

            case ToolKind.Arrow:
            case ToolKind.Line:
                _activeAnnotation = new ArrowAnnotation
                {
                    Start = screen,
                    End = screen,
                    Color = CurrentColor,
                    Width = CurrentWidth,
                    ShowHead = Tool == ToolKind.Arrow,
                    Dash = Dash,
                };
                Annotations.Add(_activeAnnotation);
                break;

            case ToolKind.Pen:
            case ToolKind.Highlighter:
                var pen = new PenAnnotation
                {
                    Color = CurrentColor,
                    Width = CurrentWidth,
                    IsHighlighter = Tool == ToolKind.Highlighter,
                    Dash = Dash,
                };
                pen.Points.Add(screen);
                if (Tool == ToolKind.Highlighter)
                {
                    pen.Points.Add(screen);
                }
                _activePen = pen;
                _activeAnnotation = pen;
                Annotations.Add(pen);
                break;

            case ToolKind.Serial:
                _activeAnnotation = new SerialAnnotation
                {
                    Center = screen,
                    Number = NextSerial,
                    Color = CurrentColor,
                    Width = CurrentWidth,
                    Radius = Math.Max(12f, CurrentWidth * 3.2f),
                    Filled = true,
                };
                Annotations.Add(_activeAnnotation);
                break;

            case ToolKind.Mosaic:
            case ToolKind.Blur:
                _activeAnnotation = new MosaicAnnotation
                {
                    Rect = new RectangleF(screen, Size.Empty),
                    Color = CurrentColor,
                    Width = CurrentWidth,
                    IsBlur = Tool == ToolKind.Blur,
                    BlockSize = MosaicBlock,
                };
                Annotations.Add(_activeAnnotation);
                break;
        }

        InvalidateAll();
    }

    private void UpdateDrawing(Point screen)
    {
        switch (_activeAnnotation)
        {
            case RectangleAnnotation rect:
                rect.Rect = RectangleF.FromLTRB(
                    Math.Min(_dragStart.X, screen.X),
                    Math.Min(_dragStart.Y, screen.Y),
                    Math.Max(_dragStart.X, screen.X),
                    Math.Max(_dragStart.Y, screen.Y));
                break;

            case ArrowAnnotation arrow:
                arrow.End = screen;

                // Shift: 15° adımlarla açı kilidi.
                if ((Control.ModifierKeys & Keys.Shift) != 0)
                {
                    var dx = arrow.End.X - arrow.Start.X;
                    var dy = arrow.End.Y - arrow.Start.Y;
                    var angle = Math.Atan2(dy, dx);
                    var snapped = Math.Round(angle / (Math.PI / 12)) * (Math.PI / 12);
                    var length = Math.Sqrt(dx * dx + dy * dy);
                    arrow.End = new PointF(
                        arrow.Start.X + (float)(Math.Cos(snapped) * length),
                        arrow.Start.Y + (float)(Math.Sin(snapped) * length));
                }
                break;

            case PenAnnotation pen:
                if (pen.IsHighlighter)
                {
                    var endPoint = new PointF(screen.X, _dragStart.Y);
                    if (pen.Points.Count < 2)
                    {
                        pen.Points.Add(endPoint);
                    }
                    else
                    {
                        pen.Points[1] = endPoint;
                    }
                }
                else if (pen.Points.Count == 0 || Distance(pen.Points[^1], screen) >= 1.5f)
                {
                    pen.Points.Add(screen);
                }
                break;

            case MosaicAnnotation mosaic:
                mosaic.Rect = RectangleF.FromLTRB(
                    Math.Min(_dragStart.X, screen.X),
                    Math.Min(_dragStart.Y, screen.Y),
                    Math.Max(_dragStart.X, screen.X),
                    Math.Max(_dragStart.Y, screen.Y));
                break;

            case SerialAnnotation serial:
                serial.Center = screen;
                break;
        }
    }

    private void FinishDrawing(Point screen)
    {
        if (_activeAnnotation is null)
        {
            return;
        }

        switch (_activeAnnotation)
        {
            case RectangleAnnotation rect:
                if (rect.Rect.Width < 3 || rect.Rect.Height < 3)
                {
                    Annotations.Remove(rect);
                }
                break;

            case ArrowAnnotation arrow:
                if (Distance(arrow.Start, arrow.End) < 4)
                {
                    Annotations.Remove(arrow);
                }
                break;

            case PenAnnotation pen when pen.IsHighlighter:
                if (pen.Points.Count < 2 || Math.Abs(pen.Points[^1].X - pen.Points[0].X) < 3f)
                {
                    Annotations.Remove(pen);
                }
                break;

            case MosaicAnnotation mosaic:
                if (mosaic.Rect.Width < 4 || mosaic.Rect.Height < 4)
                {
                    Annotations.Remove(mosaic);
                }
                else
                {
                    BuildMosaicPatch(mosaic);
                    SelectedAnnotation = mosaic;
                }
                break;

            case SerialAnnotation serial:
                if (Math.Abs(screen.X - _pressPoint.X) + Math.Abs(screen.Y - _pressPoint.Y) > 4)
                {
                    serial.Center = screen;
                }
                NextSerial++;
                break;
        }

        _activeAnnotation = null;
        _activePen = null;
        _toolbar?.OnSessionChanged();
        InvalidateAll();
    }

    private void BuildMosaicPatch(MosaicAnnotation mosaic)
    {
        var rect = Rectangle.Round(mosaic.Rect);
        rect = Rectangle.Intersect(rect, new Rectangle(_imageOrigin, _frozen.Size));
        if (rect.Width < 2 || rect.Height < 2)
        {
            Annotations.Remove(mosaic);
            return;
        }

        var source = new Rectangle(
            rect.X - _imageOrigin.X,
            rect.Y - _imageOrigin.Y,
            rect.Width,
            rect.Height);

        mosaic.Rect = rect;
        mosaic.Patch?.Dispose();
        var block = mosaic.BlockSize > 0 ? mosaic.BlockSize : MosaicBlock;
        mosaic.Patch = mosaic.IsBlur
            ? ScreenCapture.Blur(_frozen, source, Math.Max(4, block))
            : ScreenCapture.Pixelate(_frozen, source, Math.Max(2, block));
    }

    private void EraseAt(Point screen)
    {
        var hit = HitTestAnnotation(screen);
        if (hit is null)
        {
            return;
        }

        if (!_gestureUndoPushed)
        {
            PushUndo();
            _gestureUndoPushed = true;
        }

        Annotations.Remove(hit);
        if (SelectedAnnotation == hit)
        {
            SelectedAnnotation = null;
        }

        _toolbar?.OnSessionChanged();
        InvalidateAll();
    }

    private Annotation? HitTestAnnotation(Point screen)
    {
        for (var i = Annotations.Count - 1; i >= 0; i--)
        {
            if (Annotations[i].HitTest(screen, 3f))
            {
                return Annotations[i];
            }
        }

        return null;
    }

    private void UpdateHover(Point screen)
    {
        if (HasSelection)
        {
            return;
        }

        _hoverWindow = WindowEnumerator.TopWindowAt(screen, (uint)Environment.ProcessId);
    }

    private void UpdateCursorForPoint(Point screen)
    {
        if (_overlays.Count == 0)
        {
            return;
        }

        Cursor cursor;

        if (HasSelection)
        {
            var local = new PointF(screen.X - Selection.Left, screen.Y - Selection.Top);
            var handle = HitHandle(HandleRects(new Rectangle(0, 0, Selection.Width, Selection.Height)), local);
            if (handle >= 0)
            {
                cursor = ResizeCursors[handle];
            }
            else if (SelectedAnnotation is not null && TryHitAnnotationHandle(screen, out var annHandle))
            {
                cursor = ResizeCursors[annHandle];
            }
            else if (Selection.Contains(screen))
            {
                cursor = Tool switch
                {
                    ToolKind.Pointer => Cursors.SizeAll,
                    ToolKind.Text => Cursors.IBeam,
                    ToolKind.Eraser => Cursors.No,
                    _ => Cursors.Cross,
                };
            }
            else
            {
                cursor = Cursors.Cross;
            }
        }
        else
        {
            cursor = Cursors.Cross;
        }

        foreach (var overlay in _overlays)
        {
            overlay.Cursor = cursor;
        }
    }

    private bool TryHitAnnotationHandle(Point screen, out int handleIndex)
    {
        if (SelectedAnnotation is null)
        {
            handleIndex = -1;
            return false;
        }

        handleIndex = HitHandle(HandleRects(Rectangle.Round(SelectedAnnotation.GetBounds())), screen);
        return handleIndex >= 0;
    }

    // ------------------------------------------------------------- metin

    private void BeginText(Point location, string initial = "", TextAnnotation? editing = null)
    {
        SelectedAnnotation = null;
        _textEditing = true;
        _textBuffer = initial;
        _caret = initial.Length;
        _textOrigin = location;
        _editingText = editing;

        if (editing is not null)
        {
            CurrentColor = editing.Color;
            CurrentFontSize = editing.FontSize;
            FontFamily = editing.FontFamily;
            Bold = editing.Bold;
            Italic = editing.Italic;
            Outline = editing.Outline;
        }

        if (_caretTimer is null)
        {
            _caretTimer = new System.Windows.Forms.Timer { Interval = 530 };
            _caretTimer.Tick += (_, _) =>
            {
                _caretOn = !_caretOn;
                InvalidateAll();
            };
        }

        _caretOn = true;
        _caretTimer.Start();
        _toolbar?.OnSessionChanged();
        FocusOverlay();
        InvalidateAll();
    }

    private TextAnnotation? _editingText;

    private void CommitTextIfAny()
    {
        if (!_textEditing)
        {
            return;
        }

        _textEditing = false;
        _caretTimer?.Stop();

        var text = _textBuffer.TrimEnd('\n', '\r');
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (_editingText is null)
            {
                PushUndo();
            }

            if (_editingText is not null)
            {
                _editingText.Text = text;
                _editingText.Location = _textOrigin;
                _editingText.Color = CurrentColor;
                _editingText.FontSize = CurrentFontSize;
                _editingText.FontFamily = FontFamily;
                _editingText.Bold = Bold;
                _editingText.Italic = Italic;
                _editingText.Outline = Outline;
                _editingText.Width = CurrentWidth;
                Annotations.Add(_editingText);
                SelectedAnnotation = Tool == ToolKind.Pointer ? _editingText : null;
            }
            else
            {
                var annotation = new TextAnnotation
                {
                    Text = text,
                    Location = _textOrigin,
                    Color = CurrentColor,
                    FontSize = CurrentFontSize,
                    FontFamily = FontFamily,
                    Bold = Bold,
                    Italic = Italic,
                    Outline = Outline,
                    Width = CurrentWidth,
                };
                Annotations.Add(annotation);
                SelectedAnnotation = Tool == ToolKind.Pointer ? annotation : null;
            }
        }

        _editingText = null;
        _textBuffer = string.Empty;
        _caret = 0;
        _toolbar?.OnSessionChanged();
        InvalidateAll();
    }

    private void CancelText()
    {
        _textEditing = false;
        _caretTimer?.Stop();
        _textBuffer = string.Empty;
        _caret = 0;

        // Çift tıklayarak düzenlenen metin, düzenleme başlarken listeden
        // çıkarılmıştı. Esc düzenlemeyi iptal eder, metni silmez.
        var cancelled = _editingText;
        _editingText = null;
        if (cancelled is not null && !Annotations.Contains(cancelled))
        {
            Annotations.Add(cancelled);
            SelectedAnnotation = cancelled;
        }

        _toolbar?.OnSessionChanged();
        InvalidateAll();
    }

    private void DrawTextEditing(Graphics g, PointF offset)
    {
        if (!_textEditing)
        {
            return;
        }

        using var font = new Font(
            FontFamily,
            CurrentFontSize,
            (Bold ? FontStyle.Bold : FontStyle.Regular) | (Italic ? FontStyle.Italic : FontStyle.Regular),
            GraphicsUnit.Pixel);

        var display = _textBuffer.Length == 0 ? " " : _textBuffer;
        var location = new PointF(_textOrigin.X + offset.X, _textOrigin.Y + offset.Y);

        if (Outline && _textBuffer.Length > 0)
        {
            var outlineColor = (CurrentColor.R * 299 + CurrentColor.G * 587 + CurrentColor.B * 114) / 1000 > 150
                ? Color.Black
                : Color.White;
            using var outlineBrush = new SolidBrush(outlineColor);
            for (var ox = -1; ox <= 1; ox++)
            {
                for (var oy = -1; oy <= 1; oy++)
                {
                    if (ox == 0 && oy == 0) continue;
                    g.DrawString(display, font, outlineBrush, new PointF(location.X + ox, location.Y + oy), StringFormat.GenericTypographic);
                }
            }
        }

        using var brush = new SolidBrush(CurrentColor);
        g.DrawString(display, font, brush, location, StringFormat.GenericTypographic);

        var size = g.MeasureString(display, font, int.MaxValue, StringFormat.GenericTypographic);
        var lineHeight = font.GetHeight(g);
        var width = Math.Max(size.Width, 6f);
        var height = Math.Max(size.Height, lineHeight);

        // Kesikli düzenleme çerçevesi
        using (var pen = new Pen(Color.FromArgb(190, Theme.Accent), 1f) { DashStyle = DashStyle.Dash })
        {
            g.DrawRectangle(pen, location.X - 3, location.Y - 3, width + 6, height + 6);
        }

        if (!_caretOn)
        {
            return;
        }

        // Şapka konumu
        var beforeCaret = _textBuffer[..Math.Clamp(_caret, 0, _textBuffer.Length)];
        var lastLineStart = beforeCaret.LastIndexOf('\n') + 1;
        var lineIndex = beforeCaret.Count(c => c == '\n');
        var lineText = beforeCaret[lastLineStart..];

        var prefix = g.MeasureString(lineText, font, int.MaxValue, StringFormat.GenericTypographic).Width;
        var caretX = location.X + prefix;
        var caretY = location.Y + lineIndex * lineHeight;

        using var caretPen = new Pen(CurrentColor, 1.6f);
        g.DrawLine(caretPen, caretX, caretY + 1, caretX, caretY + lineHeight - 1);
    }

    // ------------------------------------------------------------- klavye

    public bool OnKeyDown(KeyEventArgs e)
    {
        if (_textEditing)
        {
            return HandleTextKeyDown(e);
        }

        switch (e.KeyCode)
        {
            case Keys.Escape:
                if (_dragMode != DragMode.None)
                {
                    CancelDrag();
                    return true;
                }

                if (SelectedAnnotation is not null)
                {
                    SelectedAnnotation = null;
                    _toolbar?.OnSessionChanged();
                    PositionToolbar();
                    InvalidateAll();
                    return true;
                }

                RequestCancel();
                return true;

            case Keys.Enter:
                CopyAndClose();
                return true;

            case Keys.Delete:
            case Keys.Back:
                DeleteSelectedAnnotation();
                return true;

            case Keys.ShiftKey:
                if (!HasSelection || _dragMode == DragMode.Selecting)
                {
                    CycleColorFormat();
                    return true;
                }
                break;

            case Keys.Left:
            case Keys.Right:
            case Keys.Up:
            case Keys.Down:
                if (!HasSelection || _dragMode == DragMode.Selecting)
                {
                    MoveCursorByKey(e.KeyCode, (e.Modifiers & Keys.Shift) != 0);
                }
                else if (SelectedAnnotation is not null)
                {
                    NudgeSelectedAnnotation(e.KeyCode, (e.Modifiers & Keys.Shift) != 0);
                }
                else
                {
                    NudgeSelection(e.KeyCode, (e.Modifiers & Keys.Shift) != 0);
                }
                return true;
        }

        if ((e.Modifiers & Keys.Control) != 0)
        {
            switch (e.KeyCode)
            {
                case Keys.C:
                    CopyAndClose();
                    return true;
                case Keys.S:
                    Save();
                    return true;
                case Keys.T:
                    Pin();
                    return true;
                case Keys.Z:
                    if ((e.Modifiers & Keys.Shift) != 0)
                    {
                        ClearAnnotations();
                    }
                    else
                    {
                        Undo();
                    }
                    return true;
                case Keys.Y:
                    Redo();
                    return true;
                case Keys.A:
                    SetSelection(_virtualBounds, showToolbar: Intent == CaptureIntent.Edit);
                    OnSelectionCompleted();
                    return true;
                case Keys.L:
                    StartLongCapture();
                    return true;
                case Keys.G:
                    StartRecording();
                    return true;
                case Keys.Oemplus:
                case Keys.Add:
                    ZoomToolbarSize(1);
                    return true;
                case Keys.OemMinus:
                case Keys.Subtract:
                    ZoomToolbarSize(-1);
                    return true;
            }
        }

        if ((e.Modifiers & Keys.Shift) != 0 && e.KeyCode == Keys.C)
        {
            RunOcr();
            return true;
        }

        if (!HasSelection || _dragMode == DragMode.Selecting)
        {
            switch (e.KeyCode)
            {
                case Keys.C when e.Modifiers == Keys.None:
                    CopyCurrentPixelColor();
                    return true;
                case Keys.W:
                    MoveCursorByKey(Keys.Up, (e.Modifiers & Keys.Shift) != 0);
                    return true;
                case Keys.S when (e.Modifiers & Keys.Control) == 0:
                    MoveCursorByKey(Keys.Down, (e.Modifiers & Keys.Shift) != 0);
                    return true;
                case Keys.A when (e.Modifiers & Keys.Control) == 0:
                    MoveCursorByKey(Keys.Left, (e.Modifiers & Keys.Shift) != 0);
                    return true;
                case Keys.D:
                    MoveCursorByKey(Keys.Right, (e.Modifiers & Keys.Shift) != 0);
                    return true;
            }
        }

        switch (e.KeyCode)
        {
            case Keys.R: SelectTool(ToolKind.Rectangle); return true;
            case Keys.O: SelectTool(ToolKind.Ellipse); return true;
            case Keys.P: SelectTool(ToolKind.Pen); return true;
            case Keys.A: SelectTool(ToolKind.Arrow); return true;
            case Keys.L: SelectTool(ToolKind.Line); return true;
            case Keys.T: SelectTool(ToolKind.Text); return true;
            case Keys.N: SelectTool(ToolKind.Serial); return true;
            case Keys.M: SelectTool(ToolKind.Mosaic); return true;
            case Keys.B: SelectTool(ToolKind.Blur); return true;
            case Keys.H: SelectTool(ToolKind.Highlighter); return true;
            case Keys.X: SelectTool(ToolKind.Eraser); return true;
            case Keys.V: SelectTool(ToolKind.Pointer); return true;
        }

        return false;
    }

    private void ZoomToolbarSize(int direction)
    {
        _toolbar?.ScaleBy(direction);
        PositionToolbar();
    }

    private bool HandleTextKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                CancelText();
                return true;

            case Keys.Enter:
                if ((e.Modifiers & Keys.Shift) != 0)
                {
                    InsertText("\n");
                }
                else
                {
                    CommitTextIfAny();
                }
                return true;

            case Keys.Back:
                if (_caret > 0)
                {
                    _textBuffer = _textBuffer.Remove(_caret - 1, 1);
                    _caret--;
                    _caretOn = true;
                    InvalidateAll();
                }
                return true;

            case Keys.Delete:
                if (_caret < _textBuffer.Length)
                {
                    _textBuffer = _textBuffer.Remove(_caret, 1);
                    _caretOn = true;
                    InvalidateAll();
                }
                return true;

            case Keys.Left:
                _caret = Math.Max(0, _caret - 1);
                _caretOn = true;
                InvalidateAll();
                return true;

            case Keys.Right:
                _caret = Math.Min(_textBuffer.Length, _caret + 1);
                _caretOn = true;
                InvalidateAll();
                return true;

            case Keys.Home:
                _caret = 0;
                _caretOn = true;
                InvalidateAll();
                return true;

            case Keys.End:
                _caret = _textBuffer.Length;
                _caretOn = true;
                InvalidateAll();
                return true;
        }

        return false;
    }

    public bool OnKeyPress(KeyPressEventArgs e)
    {
        if (!_textEditing)
        {
            return false;
        }

        if (e.KeyChar == '\r')
        {
            return true;
        }

        if (e.KeyChar == '\b')
        {
            return true;
        }

        if (e.KeyChar >= ' ' || e.KeyChar == '\t')
        {
            InsertText(e.KeyChar.ToString());
        }

        return true;
    }

    private void InsertText(string text)
    {
        _textBuffer = _textBuffer.Insert(_caret, text);
        _caret += text.Length;
        _caretOn = true;
        InvalidateAll();
    }

    private void NudgeSelection(Keys key, bool large)
    {
        if (!HasSelection)
        {
            return;
        }

        var step = large ? 10 : 1;
        var dx = key == Keys.Left ? -step : key == Keys.Right ? step : 0;
        var dy = key == Keys.Up ? -step : key == Keys.Down ? step : 0;

        Selection = ClampToVirtual(new Rectangle(Selection.X + dx, Selection.Y + dy, Selection.Width, Selection.Height));
        PositionToolbar();
        InvalidateAll();
    }

    private void NudgeSelectedAnnotation(Keys key, bool large)
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        var step = large ? 10 : 1;
        var dx = key == Keys.Left ? -step : key == Keys.Right ? step : 0;
        var dy = key == Keys.Up ? -step : key == Keys.Down ? step : 0;

        PushUndo();
        SelectedAnnotation.Move(dx, dy);
        if (SelectedAnnotation is MosaicAnnotation mosaic)
        {
            BuildMosaicPatch(mosaic);
        }

        InvalidateAll();
    }

    private void MoveCursorByKey(Keys key, bool large)
    {
        var step = large ? 10 : 1;
        var dx = key == Keys.Left ? -step : key == Keys.Right ? step : 0;
        var dy = key == Keys.Up ? -step : key == Keys.Down ? step : 0;
        var pos = Cursor.Position;
        var next = new Point(
            Math.Clamp(pos.X + dx, _virtualBounds.Left, _virtualBounds.Right - 1),
            Math.Clamp(pos.Y + dy, _virtualBounds.Top, _virtualBounds.Bottom - 1));
        Cursor.Position = next;
        _lastMouse = next;
        _hasMouse = true;
        UpdateHover(next);
        InvalidateAll();
    }

    public void CycleColorFormat()
    {
        MagnifierColorFormat = MagnifierColorFormat switch
        {
            ColorFormat.Hex => ColorFormat.Rgb,
            ColorFormat.Rgb => ColorFormat.Dec,
            _ => ColorFormat.Hex,
        };
        InvalidateAll();
    }

    public static string FormatPixelColor(Color pixel, ColorFormat format) => format switch
    {
        ColorFormat.Rgb => $"rgb({pixel.R}, {pixel.G}, {pixel.B})",
        ColorFormat.Dec => $"{pixel.R}, {pixel.G}, {pixel.B}",
        _ => $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}",
    };

    private void CopyCurrentPixelColor()
    {
        var pixelPoint = new Point(_lastMouse.X - _imageOrigin.X, _lastMouse.Y - _imageOrigin.Y);
        if (pixelPoint.X < 0 || pixelPoint.Y < 0 || pixelPoint.X >= _frozen.Width || pixelPoint.Y >= _frozen.Height)
        {
            return;
        }

        var pixel = _frozen.GetPixel(pixelPoint.X, pixelPoint.Y);
        var formatted = FormatPixelColor(pixel, MagnifierColorFormat);
        try
        {
            ClipboardHelper.CopyText(formatted);
            _copiedColorFeedback = $"✓ Kopyalandı: {formatted}";
            _copiedColorUntil = DateTime.UtcNow.AddSeconds(1.8);
            InvalidateAll();
        }
        catch
        {
            // pano meşgulse sessizce geç
        }
    }

    private void CancelDrag()
    {
        if (_dragMode == DragMode.Drawing)
        {
            if (_activeAnnotation is not null)
            {
                Annotations.Remove(_activeAnnotation);
            }

            _activeAnnotation = null;
            _activePen = null;
        }

        _dragMode = DragMode.None;

        if (_gestureUndoPushed)
        {
            Undo();
            _gestureUndoPushed = false;
        }

        InvalidateAll();
    }

    public void RequestCancel()
    {
        if (_settings.ConfirmOnEscape)
        {
            var result = ConfirmDialog.Ask(
                "Ekran görüntüsünden çıkılsın mı?",
                "Seçim ve çizimler kaydedilmedi.",
                "Bir daha sorma");

            if (result.DontAskAgain)
            {
                _settings.ConfirmOnEscape = false;
                _settings.Save();
            }

            if (!result.Confirmed)
            {
                return;
            }
        }

        CloseSession();
    }

    private void ShowContextMenu(Point screen)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.ToolbarBack,
            ForeColor = Theme.Text,
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = Theme.UiFont,
        };

        menu.Items.Add(MenuItem("Kopyala (Ctrl+C)", () => CopyAndClose()));
        menu.Items.Add(MenuItem("Kaydet (Ctrl+S)", Save));
        menu.Items.Add(MenuItem("Ekrana sabitle (Ctrl+T)", Pin));
        menu.Items.Add(MenuItem("Metni tanı (Shift+C)", RunOcr));
        menu.Items.Add(MenuItem("Uzun yakalama (Ctrl+L)", StartLongCapture));
        menu.Items.Add(MenuItem("Video kaydet (Ctrl+G)", StartRecording));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("Tüm çizimleri temizle (Ctrl+Shift+Z)", ClearAnnotations));
        menu.Items.Add(MenuItem("İptal (Esc)", RequestCancel));

        menu.Closed += (_, _) => UiFactory.DisposeLater(menu);
        menu.Show(screen);
    }

    private static ToolStripMenuItem MenuItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
