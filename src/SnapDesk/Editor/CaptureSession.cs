using System.Diagnostics;
using System.Drawing.Drawing2D;
using SnapDesk.Core;
using SnapDesk.Long;
using SnapDesk.Ocr;
using SnapDesk.Pin;
using SnapDesk.Record;
using SnapDesk.UI;

namespace SnapDesk.Editor;

public enum CaptureIntent
{
    Edit,
    Pin,
    LongCapture,
    Record,
    Ocr,
}

public enum ColorFormat
{
    Hex,
    Rgb,
    Dec,
}

/// <summary>
/// Tek bir yakalama/düzenleme oturumu. Donmuş ekran görüntüsü, seçim, çizimler,
/// geri/ileri alma ve tüm eylemler burada yönetilir.
/// </summary>
public sealed partial class CaptureSession : IDisposable
{
    private readonly Settings _settings;
    private readonly Action? _onClosed;

    private Bitmap _frozen;
    private Rectangle _virtualBounds;
    private Point _imageOrigin;
    private readonly List<MonitorInfo> _monitors = new();
    private readonly List<OverlayForm> _overlays = new();
    private EditorToolbarForm? _toolbar;

    private bool _closed;
    private bool _disposed;

    public CaptureIntent Intent { get; }
    public bool CompletedWithAction { get; private set; }
    public ColorFormat MagnifierColorFormat { get; private set; } = ColorFormat.Hex;
    private string? _copiedColorFeedback;
    private DateTime _copiedColorUntil;

    // Seçim ve çizim durumu
    public Rectangle Selection { get; private set; }
    public bool HasSelection { get; private set; }
    public List<Annotation> Annotations { get; } = new();
    public Annotation? SelectedAnnotation { get; private set; }

    public ToolKind Tool { get; private set; } = ToolKind.Pointer;
    public Color CurrentColor { get; set; }
    public float CurrentWidth { get; set; }
    public float CurrentFontSize { get; set; }
    public bool Filled { get; set; }
    public float CornerRadius { get; set; }
    public DashStyle Dash { get; set; } = DashStyle.Solid;
    public int MosaicBlock { get; set; }
    public bool MosaicBlur { get; set; }
    public string FontFamily { get; set; } = "Segoe UI";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Outline { get; set; }
    public int NextSerial { get; set; } = 1;
    public bool ShapeIsEllipse { get; set; }
    public bool PenIsHighlighter { get; set; }
    public bool ArrowIsLine { get; set; }

    public Settings Settings => _settings;
    public Rectangle VirtualBounds => _virtualBounds;
    public IReadOnlyList<MonitorInfo> MonitorList => _monitors;
    public Bitmap FrozenImage => _frozen;

    /// <summary>Yeni bir oturum oluşturulduğunda (kırpma sonrası yeni oturum dahil).</summary>
    public static event Action<CaptureSession>? Created;

    /// <summary>Oturum kapandığında.</summary>
    public event Action? Closed;

    private enum DragMode
    {
        None,
        Selecting,
        MovingSelection,
        ResizingSelection,
        Drawing,
        MovingAnnotation,
        ResizingAnnotation,
        MovingText,
    }

    private DragMode _dragMode = DragMode.None;
    private Point _dragStart;
    private Rectangle _initialSelection;
    private int _resizeHandle = -1;
    private Annotation? _activeAnnotation;
    private PenAnnotation? _activePen;
    private bool _gestureUndoPushed;
    private Point _lastMouse;
    private Point _pressPoint;
    private bool _hasMouse;
    private WindowEnumerator.WindowInfo? _hoverWindow;
    private PointF _annotationDragOrigin;
    private RectangleF _annotationStartBounds;
    private Annotation? _annotationResizeInitial;
    private int _annotationResizeHandle = -1;

    // Metin düzenleme
    private bool _textEditing;
    private string _textBuffer = string.Empty;
    private int _caret;
    private Point _textOrigin;
    private bool _caretOn = true;
    private System.Windows.Forms.Timer? _caretTimer;

    // Geri / ileri
    private readonly History<EditorSnapshot> _history = new(MaxUndo);
    private const int MaxUndo = 40;

    private sealed class EditorSnapshot
    {
        public List<Annotation> Annotations { get; init; } = new();
        public Rectangle Selection { get; init; }
        public bool HasSelection { get; init; }
        public int SelectedIndex { get; init; } = -1;
        public int NextSerial { get; init; }
    }

    public CaptureSession(Settings settings, Action? onClosed = null, CaptureIntent intent = CaptureIntent.Edit)
    {
        _settings = settings;
        _onClosed = onClosed;
        Intent = intent;

        _monitors.AddRange(Monitors.Enumerate());
        _virtualBounds = Monitors.VirtualBounds(_monitors);
        _frozen = ScreenCapture.CaptureVirtualScreen(out var captured);
        _virtualBounds = captured;
        _imageOrigin = captured.Location;

        CurrentColor = Color.FromArgb(settings.DefaultColorArgb);
        CurrentWidth = settings.DefaultStrokeWidth;
        CurrentFontSize = settings.DefaultFontSize;
        MosaicBlock = settings.MosaicBlockSize;

        Created?.Invoke(this);
    }

    /// <summary>Var olan bir görüntüyü düzenlemek için (kırpma, pin üzerinde işaretleme).</summary>
    public CaptureSession(Settings settings, Bitmap image, Point screenOrigin, Action? onClosed = null)
    {
        _settings = settings;
        _onClosed = onClosed;

        _monitors.AddRange(Monitors.Enumerate());
        _virtualBounds = new Rectangle(screenOrigin, image.Size);
        _imageOrigin = screenOrigin;
        _frozen = image;

        CurrentColor = Color.FromArgb(settings.DefaultColorArgb);
        CurrentWidth = settings.DefaultStrokeWidth;
        CurrentFontSize = settings.DefaultFontSize;
        MosaicBlock = settings.MosaicBlockSize;

        Selection = new Rectangle(screenOrigin, image.Size);
        HasSelection = true;

        Created?.Invoke(this);
    }

    public void Start()
    {
        _lastMouse = Cursor.Position;
        _hasMouse = true;

        foreach (var monitor in _monitors)
        {
            var overlay = new OverlayForm(this, monitor);
            _overlays.Add(overlay);
        }

        foreach (var overlay in _overlays)
        {
            overlay.Show();
            overlay.BringToFront();
        }

        if (_overlays.Count > 0)
        {
            _overlays[0].Activate();
            _overlays[0].Focus();
        }

        if (HasSelection)
        {
            EnsureToolbar();
            PositionToolbar();
        }

        InvalidateAll();
    }

    /// <summary>Seçim yokken tam ekran seçim modu.</summary>
    public void ClearSelection()
    {
        HasSelection = false;
        Selection = Rectangle.Empty;
        SelectedAnnotation = null;
        _toolbar?.Hide();
        InvalidateAll();
    }

    public void SetSelection(Rectangle rect, bool showToolbar = true)
    {
        rect = Rectangle.Intersect(rect, _virtualBounds);
        if (rect.Width < 1 || rect.Height < 1)
        {
            ClearSelection();
            return;
        }

        Selection = rect;
        HasSelection = true;
        SelectedAnnotation = null;
        _toolbar?.ResetUserPosition();

        if (showToolbar)
        {
            EnsureToolbar();
            PositionToolbar();
        }

        InvalidateAll();
    }

    public void SelectTool(ToolKind tool)
    {
        CommitTextIfAny();
        Tool = tool;
        SelectedAnnotation = null;

        switch (tool)
        {
            case ToolKind.Rectangle:
                ShapeIsEllipse = false;
                break;
            case ToolKind.Ellipse:
                ShapeIsEllipse = true;
                break;
            case ToolKind.Pen:
                PenIsHighlighter = false;
                break;
            case ToolKind.Highlighter:
                PenIsHighlighter = true;
                break;
            case ToolKind.Arrow:
                ArrowIsLine = false;
                break;
            case ToolKind.Line:
                ArrowIsLine = true;
                break;
            case ToolKind.Mosaic:
                MosaicBlur = false;
                break;
            case ToolKind.Blur:
                MosaicBlur = true;
                break;
        }

        UpdateCursor();
        _toolbar?.OnSessionChanged();
        PositionToolbar();
        InvalidateAll();
    }

    public void RefreshToolbar()
    {
        _toolbar?.OnSessionChanged();
        PositionToolbar();
    }

    private void EnsureToolbar()
    {
        if (_toolbar is null || _toolbar.IsDisposed)
        {
            _toolbar = new EditorToolbarForm(this);
            _toolbar.Show();
        }

        _toolbar.Visible = true;
        _toolbar.BringToFront();
    }

    public void PositionToolbar(bool force = false)
    {
        if (_toolbar is null || _toolbar.IsDisposed || !HasSelection)
        {
            return;
        }

        // Kullanıcı çubuğu elle taşıdıysa seçenek değişikliklerinde yerini koru.
        if (!force && _toolbar.UserPositioned)
        {
            _toolbar.BringToFront();
            return;
        }

        var screen = Screen.FromPoint(new Point(Selection.Left + Selection.Width / 2, Selection.Top + Selection.Height / 2));
        var work = screen.WorkingArea;
        var size = _toolbar.Size;

        var x = Selection.Left + (Selection.Width - size.Width) / 2;
        var y = Selection.Bottom + 10;

        if (y + size.Height > work.Bottom - 6)
        {
            y = Selection.Top - size.Height - 10;
        }

        if (y < work.Top + 6)
        {
            // Seçim ekranı kaplıyorsa araç çubuğunu seçimin içine yerleştir.
            y = Math.Min(work.Bottom - size.Height - 6, Selection.Top + 8);
        }

        x = Math.Clamp(x, work.Left + 6, Math.Max(work.Left + 6, work.Right - size.Width - 6));

        _toolbar.Location = new Point(x, y);
        _toolbar.BringToFront();
    }

    // ---------------------------------------------------------------- eylemler

    public void CopyAndClose()
    {
        try
        {
            using var image = CompositeSelection();
            ClipboardHelper.CopyImage(image);
            CompletedWithAction = true;
            CloseSession();
        }
        catch (Exception ex)
        {
            ShowError("Panoya kopyalanamadı", ex);
        }
    }

    public void Save()
    {
        try
        {
            using var image = CompositeSelection();
            using var dialog = new SaveFileDialog
            {
                Title = "Görüntüyü kaydet",
                Filter = "PNG görüntü (*.png)|*.png|JPEG görüntü (*.jpg)|*.jpg",
                FilterIndex = _settings.ImageFormat == "jpg" ? 2 : 1,
                InitialDirectory = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : null,
                FileName = _settings.BuildFileName(DateTime.Now, _settings.ImageFormat),
                OverwritePrompt = true,
                AddExtension = true,
            };

            if (dialog.ShowDialog(OwnerWindow) != DialogResult.OK)
            {
                FocusOverlay();
                return;
            }

            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var format = extension == ".jpg" || extension == ".jpeg"
                ? System.Drawing.Imaging.ImageFormat.Jpeg
                : System.Drawing.Imaging.ImageFormat.Png;

            var folder = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            image.Save(dialog.FileName, format);

            CompletedWithAction = true;
            CloseSession();
        }
        catch (Exception ex)
        {
            ShowError("Görüntü kaydedilemedi", ex);
        }
    }

    public void Pin()
    {
        try
        {
            var image = CompositeSelection();
            var origin = Selection.Location;
            CompletedWithAction = true;
            CloseSession();
            PinForm.ShowPin(image, _settings, origin);
        }
        catch (Exception ex)
        {
            ShowError("Görüntü sabitlenemedi", ex);
        }
    }

    public void RunOcr()
    {
        try
        {
            var image = CompositeSelection();
            var origin = Selection.Location;
            if (Intent == CaptureIntent.Ocr)
            {
                CompletedWithAction = true;
                CloseSession();
                OcrForm.ShowFor(image, origin, _settings, null);
            }
            else
            {
                OcrForm.ShowFor(image, origin, _settings, OwnerWindow);
            }
        }
        catch (Exception ex)
        {
            ShowError("Metin çıkartma başlatılamadı", ex);
        }
    }

    public void CropToSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        try
        {
            var cropped = CompositeSelection();
            var origin = Selection.Location;

            CompletedWithAction = true;
            CloseSession();

            var session = new CaptureSession(_settings, cropped, origin);
            session.Start();
        }
        catch (Exception ex)
        {
            ShowError("Kırpma başarısız", ex);
        }
    }

    public void StartLongCapture()
    {
        if (!HasSelection)
        {
            return;
        }

        var region = Selection;
        var settings = _settings;
        CompletedWithAction = true;
        CloseSession();

        try
        {
            LongCaptureForm.Start(region, settings);
        }
        catch (Exception ex)
        {
            ShowError("Uzun yakalama başlatılamadı", ex);
        }
    }

    public void StartRecording()
    {
        if (!HasSelection)
        {
            return;
        }

        var region = Selection;
        var settings = _settings;
        CompletedWithAction = true;
        CloseSession();

        try
        {
            RecordBarForm.Start(region, settings);
        }
        catch (Exception ex)
        {
            ShowError("Kayıt başlatılamadı", ex);
        }
    }

    public void CopyAllToClipboard()
    {
        try
        {
            using var image = CompositeSelection();
            ClipboardHelper.CopyImage(image);
        }
        catch (Exception ex)
        {
            ShowError("Panoya kopyalanamadı", ex);
        }
    }

    /// <summary>Seçim + çizimlerden nihai görüntüyü üretir.</summary>
    public Bitmap CompositeSelection()
    {
        CommitTextIfAny();

        var rect = HasSelection
            ? Selection
            : new Rectangle(_imageOrigin, _frozen.Size);

        rect = Rectangle.Intersect(rect, new Rectangle(_imageOrigin, _frozen.Size));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            rect = new Rectangle(_imageOrigin, _frozen.Size);
        }

        var result = new Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        graphics.DrawImage(
            _frozen,
            new Rectangle(0, 0, rect.Width, rect.Height),
            new Rectangle(rect.X - _imageOrigin.X, rect.Y - _imageOrigin.Y, rect.Width, rect.Height),
            GraphicsUnit.Pixel);

        var offset = new PointF(-rect.X, -rect.Y);
        foreach (var annotation in Annotations)
        {
            annotation.Draw(graphics, offset);
        }

        return result;
    }

    public string DescribeSelection()
        => HasSelection ? $"{Selection.Width} × {Selection.Height}" : string.Empty;

    public void CloseSession()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        _caretTimer?.Stop();
        _caretTimer?.Dispose();
        _caretTimer = null;

        if (_toolbar is not null)
        {
            _toolbar.Hide();
            _toolbar.Dispose();
            _toolbar = null;
        }

        foreach (var overlay in _overlays)
        {
            overlay.Hide();
            overlay.Dispose();
        }

        _overlays.Clear();

        Closed?.Invoke();
        _onClosed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CloseSession();
        _frozen.Dispose();

        foreach (var annotation in Annotations)
        {
            if (annotation is MosaicAnnotation { Patch: not null } mosaic)
            {
                mosaic.Patch.Dispose();
            }
        }
    }

    private void ShowError(string title, Exception ex)
    {
        MessageBox.Show(
            $"{title}.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
            "SnapDesk",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    // ------------------------------------------------------------- geri / ileri

    internal void PushUndo()
    {
        _history.Push(CaptureSnapshot());
        _toolbar?.OnSessionChanged();
    }

    private EditorSnapshot CaptureSnapshot() => new()
    {
        Annotations = Annotations.Select(a => a.Clone()).ToList(),
        Selection = Selection,
        HasSelection = HasSelection,
        SelectedIndex = SelectedAnnotation is not null ? Annotations.IndexOf(SelectedAnnotation) : -1,
        NextSerial = NextSerial,
    };

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        foreach (var annotation in Annotations)
        {
            if (annotation is MosaicAnnotation { Patch: not null } mosaic)
            {
                mosaic.Patch.Dispose();
            }
        }

        Annotations.Clear();
        Annotations.AddRange(snapshot.Annotations.Select(a => a.Clone()));
        Selection = snapshot.Selection;
        HasSelection = snapshot.HasSelection;
        SelectedAnnotation = snapshot.SelectedIndex >= 0 && snapshot.SelectedIndex < Annotations.Count
            ? Annotations[snapshot.SelectedIndex]
            : null;
        NextSerial = snapshot.NextSerial;
        SyncSessionFromSelectedAnnotation();

        if (HasSelection)
        {
            EnsureToolbar();
            PositionToolbar();
        }
        else
        {
            _toolbar?.Hide();
        }

        InvalidateAll();
        _toolbar?.OnSessionChanged();
    }

    public void SyncSessionFromSelectedAnnotation()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        CurrentColor = SelectedAnnotation.Color;
        CurrentWidth = SelectedAnnotation.Width;
        Filled = SelectedAnnotation.Filled;
        Dash = SelectedAnnotation.Dash;

        switch (SelectedAnnotation)
        {
            case RectangleAnnotation rect:
                CornerRadius = rect.CornerRadius;
                break;
            case TextAnnotation text:
                FontFamily = text.FontFamily;
                CurrentFontSize = text.FontSize;
                Bold = text.Bold;
                Italic = text.Italic;
                Outline = text.Outline;
                break;
            case MosaicAnnotation mosaic:
                MosaicBlur = mosaic.IsBlur;
                MosaicBlock = mosaic.BlockSize;
                break;
        }
    }

    public void UpdateSelectedAnnotation(Action<Annotation> update)
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        PushUndo();
        update(SelectedAnnotation);
        if (SelectedAnnotation is MosaicAnnotation mosaic)
        {
            BuildMosaicPatch(mosaic);
        }

        InvalidateAll();
        _toolbar?.OnSessionChanged();
    }

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public void Undo()
    {
        var snapshot = _history.Undo(CaptureSnapshot());
        if (snapshot is null)
        {
            return;
        }

        RestoreSnapshot(snapshot);
    }

    public void Redo()
    {
        var snapshot = _history.Redo(CaptureSnapshot());
        if (snapshot is null)
        {
            return;
        }

        RestoreSnapshot(snapshot);
    }

    public void ClearAnnotations()
    {
        if (Annotations.Count == 0)
        {
            return;
        }

        PushUndo();
        Annotations.Clear();
        SelectedAnnotation = null;
        NextSerial = 1;
        InvalidateAll();
        _toolbar?.OnSessionChanged();
    }

    public void DeleteSelectedAnnotation()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        PushUndo();
        Annotations.Remove(SelectedAnnotation);
        SelectedAnnotation = null;
        InvalidateAll();
        _toolbar?.OnSessionChanged();
    }

    private void InvalidateAll()
    {
        foreach (var overlay in _overlays)
        {
            overlay.Invalidate();
        }
    }

    /// <summary>Araç çubuğundan yapılan değişikliklerden sonra kaplamaları tazeler.</summary>
    public void RefreshOverlays() => InvalidateAll();

    /// <summary>
    /// Kaplamalardan biri. Alt pencerelerin (ör. ayarlar) tam ekran, en üstteki
    /// kaplamanın arkasında kalmasını önlemek için sahip olarak kullanılır.
    /// </summary>
    public IWin32Window? OwnerWindow
        => _overlays.FirstOrDefault(o => o.Monitor.Bounds.Contains(Cursor.Position))
           ?? _overlays.FirstOrDefault();

    /// <summary>Diyalog/menü kapandıktan sonra klavye odağını kaplamaya döndürür.</summary>
    public void FocusOverlay()
    {
        if (_overlays.Count == 0)
        {
            return;
        }

        var target = _overlays.FirstOrDefault(o => o.Monitor.Bounds.Contains(Cursor.Position)) ?? _overlays[0];
        target.Activate();
        target.Focus();

        // Kaplama tam ekran ve en üstte; etkinleştirilince araç çubuğunun
        // üstüne çıkar. Çubuğu yeniden öne al ki görünür kalsın.
        _toolbar?.BringToFront();
    }

    private void UpdateCursor()
    {
        var cursor = Tool switch
        {
            ToolKind.Text => Cursors.IBeam,
            ToolKind.Eraser => Cursors.No,
            ToolKind.Pointer => Cursors.Default,
            _ => Cursors.Cross,
        };

        foreach (var overlay in _overlays)
        {
            overlay.Cursor = cursor;
        }
    }
}
