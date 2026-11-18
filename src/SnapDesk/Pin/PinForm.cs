using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using SnapDesk.Core;
using SnapDesk.Editor;
using SnapDesk.Ocr;
using SnapDesk.UI;

namespace SnapDesk.Pin;

/// <summary>
/// Ekrana sabitlenen görüntü. Birden çok pin bağımsızdır: her biri kendi
/// yakınlaştırması, saydamlığı, en-üstte ve fare geçirgenliği durumuyla yaşar.
/// </summary>
public sealed class PinForm : Form
{
    private const int Glow = 15;
    private const int GwlexTransparent = 0x20;
    private const int GwlExstyle = -20;

    private static readonly List<PinForm> ActivePins = new();

    public static IReadOnlyList<PinForm> OpenPins => ActivePins;

    private readonly Settings _settings;
    private readonly Bitmap _image;
    private float _zoom = 1f;
    private double _opacity = 1.0;
    private bool _locked;
    private bool _clickThrough;
    private bool _dragging;
    private Point _dragOffset;

    public static void ShowPin(Bitmap image, Settings settings, Point origin)
    {
        var pin = new PinForm(image, settings, origin);
        ActivePins.Add(pin);
        pin.Show();
        pin.Activate();
    }

    private PinForm(Bitmap image, Settings settings, Point origin)
    {
        _image = image;
        _settings = settings;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(24, 24, 28);
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        ApplyZoom(1f, center: null);

        var size = Size;
        var screen = Screen.FromPoint(origin).WorkingArea;

        // Görünen görüntü seçimin tam üstüne gelsin; 15 px'lik kenar boşluğu
        // pencerenin dışında (bölge dışında) kalır ve görünmez.
        var left = Math.Clamp(
            origin.X - Glow,
            screen.Left - Glow,
            Math.Max(screen.Left - Glow, screen.Right - size.Width + Glow));
        var top = Math.Clamp(
            origin.Y - Glow,
            screen.Top - Glow,
            Math.Max(screen.Top - Glow, screen.Bottom - size.Height + Glow));
        Location = new Point(left, top);

        using var path = Theme.RoundedRect(
            new RectangleF(Glow, Glow, _image.Width * _zoom, _image.Height * _zoom), 8);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();

        FormClosed += (_, _) =>
        {
            ActivePins.Remove(this);
            _image.Dispose();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);

        var w = Math.Max(1, (int)Math.Round(_image.Width * _zoom));
        var h = Math.Max(1, (int)Math.Round(_image.Height * _zoom));
        var imageRect = new Rectangle(Glow, Glow, w, h);
        g.InterpolationMode = _zoom >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_image, imageRect);

        var borderRect = new RectangleF(Glow + 0.5f, Glow + 0.5f, w - 1f, h - 1f);
        Theme.DrawRounded(g, borderRect, 8, Color.FromArgb(150, 88, 94, 108), 1.2f);

        if (_locked)
        {
            var badge = new RectangleF(imageRect.Right - 24, imageRect.Top + 6, 18, 18);
            using var lockBrush = new SolidBrush(Color.FromArgb(215, 32, 32, 38));
            g.FillEllipse(lockBrush, badge);
            using var lockBorder = new Pen(Color.FromArgb(220, 255, 200, 80), 1f);
            g.DrawEllipse(lockBorder, badge);
            Icons.Draw(g, IconKind.Lock, RectangleF.Inflate(badge, -3f, -3f), Color.FromArgb(255, 205, 85), 1.4f);
        }
    }

    private void ApplyZoom(float zoom, Point? center)
    {
        zoom = Math.Clamp(zoom, 0.1f, 4f);

        var oldCenter = center ?? new Point(Left + Width / 2, Top + Height / 2);
        _zoom = zoom;

        ClientSize = new Size(
            (int)Math.Ceiling(_image.Width * _zoom) + Glow * 2,
            (int)Math.Ceiling(_image.Height * _zoom) + Glow * 2);

        if (center.HasValue)
        {
            Location = new Point(oldCenter.X - Width / 2, oldCenter.Y - Height / 2);
        }

        using var path = Theme.RoundedRect(
            new RectangleF(Glow, Glow, _image.Width * _zoom, _image.Height * _zoom), 8);
        Region?.Dispose();
        Region = new Region(path);
        Opacity = _opacity;
        Invalidate();
    }

    // ---------------------------------------------------------------- fare

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right)
        {
            ShowMenu(e.Location);
            return;
        }

        if (e.Button == MouseButtons.Left && !_locked)
        {
            _dragging = true;
            _dragOffset = e.Location;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            Location = new Point(
                PointToScreen(e.Location).X - _dragOffset.X,
                PointToScreen(e.Location).Y - _dragOffset.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if ((ModifierKeys & Keys.Control) != 0)
        {
            _opacity = Math.Clamp(_opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.25, 1.0);
            Opacity = _opacity;
            return;
        }

        var factor = e.Delta > 0 ? 1.1f : 1f / 1.1f;
        ApplyZoom(_zoom * factor, PointToScreen(e.Location));
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        ApplyZoom(1f, PointToScreen(PointToClient(Cursor.Position)));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.KeyCode)
        {
            case Keys.Escape:
                Close();
                break;

            case Keys.Space:
                Annotate();
                break;

            case Keys.E:
                OcrForm.ShowFor(new Bitmap(_image), Location, _settings, this);
                break;

            case Keys.M:
                ToggleClickThrough();
                break;

            case Keys.C when e.Control:
                CopyImage();
                break;

            case Keys.S when e.Control:
                SaveImage();
                break;

            case Keys.D0 when e.Control:
            case Keys.NumPad0 when e.Control:
                ApplyZoom(1f, PointToScreen(PointToClient(Cursor.Position)));
                break;

            case Keys.T:
                TopMost = !TopMost;
                break;

            case Keys.L:
                _locked = !_locked;
                Invalidate();
                break;

            case Keys.H:
                HidePin();
                break;

            case Keys.Oemplus:
            case Keys.Add:
                ApplyZoom(_zoom * 1.1f, PointToScreen(PointToClient(Cursor.Position)));
                break;

            case Keys.OemMinus:
            case Keys.Subtract:
                ApplyZoom(_zoom / 1.1f, PointToScreen(PointToClient(Cursor.Position)));
                break;
        }
    }

    // ---------------------------------------------------------------- eylemler

    private void ShowMenu(Point location)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.ToolbarBack,
            ForeColor = Theme.Text,
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = Theme.UiFont,
        };

        menu.Items.Add(Item("Metni tanı (E)", () => OcrForm.ShowFor(new Bitmap(_image), Location, _settings, this)));
        menu.Items.Add(Item("Resmi kopyala (Ctrl+C)", CopyImage));
        menu.Items.Add(Item("Resmi kaydet (Ctrl+S)", SaveImage));
        menu.Items.Add(Item("İşaretle (Space)", Annotate));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item($"%100 yakınlaştırma (şimdi %{(int)Math.Round(_zoom * 100)})", () =>
            ApplyZoom(1f, PointToScreen(PointToClient(Cursor.Position)))));

        var opacityMenu = new ToolStripMenuItem($"Saydamlık (%{(int)Math.Round(_opacity * 100)})");
        foreach (var pct in new[] { 100, 80, 60, 40, 25 })
        {
            var val = pct / 100.0;
            var sub = new ToolStripMenuItem($"%{pct}") { Checked = Math.Abs(_opacity - val) < 0.04 };
            sub.Click += (_, _) =>
            {
                _opacity = val;
                Opacity = _opacity;
            };
            opacityMenu.DropDownItems.Add(sub);
        }
        menu.Items.Add(opacityMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item(TopMost ? "En üstte: açık (T)" : "En üstte: kapalı (T)", () => TopMost = !TopMost));
        menu.Items.Add(Item(_locked ? "Kilidi aç (L)" : "Kilitle (L)", () =>
        {
            _locked = !_locked;
            Invalidate();
        }));
        menu.Items.Add(Item(_clickThrough ? "Fare geçirgenliğini kapat (M)" : "Fare geçirgenliği (M)", ToggleClickThrough));
        menu.Items.Add(Item("Gizle (H)", HidePin));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Bu pini kapat (Esc)", Close));
        menu.Items.Add(Item("Tüm pinleri kapat", CloseAll));

        menu.Closed += (_, _) => UiFactory.DisposeLater(menu);
        menu.Show(this, location);
    }

    private static ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    public void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        var style = GetWindowLong(Handle, GwlExstyle);
        style = _clickThrough ? style | GwlexTransparent : style & ~GwlexTransparent;
        SetWindowLong(Handle, GwlExstyle, style);
    }

    public bool IsClickThrough => _clickThrough;

    public void HidePin() => Hide();

    public static void ShowAll()
    {
        foreach (var pin in ActivePins.ToList())
        {
            if (pin.IsDisposed) continue;
            pin.Show();
            pin.BringToFront();
        }
    }

    public static void HideAll()
    {
        foreach (var pin in ActivePins.ToList())
        {
            if (pin.IsDisposed) continue;
            pin.Hide();
        }
    }

    public static void CloseAll()
    {
        foreach (var pin in ActivePins.ToList())
        {
            if (pin.IsDisposed) continue;
            pin.Close();
        }
    }

    public static void DisableClickThrough()
    {
        foreach (var pin in ActivePins.ToList())
        {
            if (pin.IsDisposed) continue;
            if (pin._clickThrough)
            {
                pin.ToggleClickThrough();
            }
        }
    }

    private void CopyImage()
    {
        try
        {
            ClipboardHelper.CopyImage(_image);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Panoya kopyalanamadı: " + ex.Message, "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveImage()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Pini kaydet",
            Filter = "PNG görüntü (*.png)|*.png|JPEG görüntü (*.jpg)|*.jpg",
            FileName = _settings.BuildFileName(DateTime.Now, "png"),
            InitialDirectory = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : null,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var format = extension is ".jpg" or ".jpeg"
                ? System.Drawing.Imaging.ImageFormat.Jpeg
                : System.Drawing.Imaging.ImageFormat.Png;
            _image.Save(dialog.FileName, format);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Görüntü kaydedilemedi: " + ex.Message, "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Annotate()
    {
        Hide();

        var copy = new Bitmap(_image);
        CaptureSession? session = null;
        session = new CaptureSession(_settings, copy, new Point(Left + Glow, Top + Glow), () =>
        {
            if (IsDisposed)
            {
                return;
            }

            if (session is { CompletedWithAction: true })
            {
                Close();
            }
            else
            {
                Show();
                Activate();
            }
        });

        session.Start();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
}
