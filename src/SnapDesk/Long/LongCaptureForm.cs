using SnapDesk.Core;
using SnapDesk.UI;

namespace SnapDesk.Long;

/// <summary>
/// Uzun yakalama: seçilen bölgeyi kaydırarak tekrar tekrar yakalar, örtüşmeyi
/// bulup dikey olarak birleştirir. Sabit başlık yalnızca bir kez eklenir.
/// </summary>
public sealed class LongCaptureForm : Form
{
    private readonly Rectangle _region;
    private readonly Settings _settings;
    private readonly Label _status;
    private readonly Button _stop;
    private readonly Button _cancel;
    private readonly System.Windows.Forms.Timer _timer;
    private CancellationTokenSource? _cts;
    private DateTime _startedAt;
    private int _frames;
    private int _headerHeight;
    private bool _finished;
    private bool _canceled;
    private bool _stopRequested;
    private bool _dragging;
    private Point _dragOffset;

    public static void Start(Rectangle region, Settings settings)
    {
        var form = new LongCaptureForm(region, settings);
        form.Show();
        form.BeginCapture();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            // Kontrol çubuğunun yakalanan bölgeyle kesiştiğinde karelerin içine girmesini önle.
            SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture);
        }
        catch
        {
            // Eski Windows sürümlerinde desteklenmiyorsa yoksay
        }
    }

    private LongCaptureForm(Rectangle region, Settings settings)
    {
        _region = region;
        _settings = settings;

        UiFactory.StyleForm(this);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        ClientSize = new Size(Math.Min(Math.Max(region.Width, 620), 740), 74);
        BackColor = Theme.ToolbarBack;

        var screen = Screen.FromRectangle(region).WorkingArea;
        var x = Math.Clamp(region.Left, 8, Math.Max(8, screen.Right - Width - 8));
        var y = region.Bottom + 10;
        if (y + Height > screen.Bottom - 8)
        {
            y = region.Top - Height - 10;
        }

        if (y < screen.Top + 8)
        {
            y = Math.Min(screen.Bottom - Height - 8, Math.Max(screen.Top + 8, region.Bottom - Height - 10));
        }
        Location = new Point(x, y);

        _status = UiFactory.Label("Hazırlanıyor…");
        _status.Font = Theme.UiFontBold;
        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Location = new Point(16, 14);
        _status.Size = new Size(ClientSize.Width - 292, 20);

        var hint = UiFactory.Label("Sayfa kaydırılarak birleştiriliyor · çubuk ve imleç kayda girmez", small: true);
        hint.AutoSize = false;
        hint.AutoEllipsis = true;
        hint.Location = new Point(16, 38);
        hint.Size = new Size(ClientSize.Width - 292, 18);

        _stop = UiFactory.Button("Durdur ve birleştir", 160, 32, primary: true);
        _stop.Location = new Point(ClientSize.Width - 268, 21);
        _stop.Click += (_, _) => Finish();

        _cancel = UiFactory.Button("İptal", 88, 32);
        _cancel.Location = new Point(ClientSize.Width - 100, 21);
        _cancel.Click += (_, _) => CancelCapture();

        Controls.AddRange(new Control[] { _status, hint, _stop, _cancel });
        EnableDrag(this);
        EnableDrag(_status);
        EnableDrag(hint);

        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) => UpdateStatus();

        FormClosed += (_, _) =>
        {
            _cts?.Cancel();
            _timer.Stop();
        };
    }

    private async void BeginCapture()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _startedAt = DateTime.Now;
        _timer.Start();
        _status.Text = "Kaydırma başlıyor…";

        var originalCursor = Cursor.Position;
        Bitmap? canvas = null;
        Bitmap? previous = null;

        try
        {
            // İlk bekleme de iptal edilebilir olmalı; aksi hâlde ilk 350 ms
            // içinde "İptal"e basmak yakalanmamış bir istisna üretiyordu.
            await Task.Delay(350, token);

            var first = ScreenCapture.CaptureRegion(_region);
            canvas = new Bitmap(first);
            previous = first;
            _frames = 1;
            _headerHeight = 0;

            while (!token.IsCancellationRequested
                   && !_stopRequested
                   && _frames < 80
                   && canvas.Height < 30000)
            {
                ScrollDown();
                await Task.Delay(430, token);

                var current = ScreenCapture.CaptureRegion(_region);

                if (ImageStitcher.FramesNearlyEqual(previous, current))
                {
                    // Sayfa kaydırılamıyor (veya aynı kare); birleştirmeye
                    // devam etmek yinelenen içerik üretir.
                    current.Dispose();
                    break;
                }

                if (_frames == 1)
                {
                    _headerHeight = ImageStitcher.DetectFixedHeader(previous, current);
                }

                var overlap = ImageStitcher.FindVerticalOverlap(previous, current, _headerHeight, 30);
                if (overlap < 0)
                {
                    current.Dispose();
                    break;
                }

                var newRows = current.Height - _headerHeight - overlap;
                if (newRows < 3)
                {
                    current.Dispose();
                    break;
                }

                var merged = ImageStitcher.Append(canvas, current, _headerHeight, overlap);
                canvas.Dispose();
                canvas = merged;

                previous.Dispose();
                previous = current;
                _frames++;

                UpdateStatus();
            }
        }
        catch (OperationCanceledException)
        {
            // kullanıcı durdurdu/iptal etti
        }
        catch (Exception ex)
        {
            if (!_canceled && !IsDisposed)
            {
                _status.Text = "Yakalama hatası: " + ex.Message;
            }
        }
        finally
        {
            try
            {
                SetCursorPos(originalCursor.X, originalCursor.Y);
            }
            catch
            {
                // yoksay
            }
        }

        previous?.Dispose();

        if (_canceled || canvas is null)
        {
            canvas?.Dispose();
            _cts?.Dispose();
            _cts = null;
            return;
        }

        _timer.Stop();

        if (canvas.Height <= _region.Height + 8)
        {
            _status.Text = "İçerik kaydırılamadı; tek kare yakalandı.";
            ShowResult(canvas, scrolled: false);
            return;
        }

        ShowResult(canvas, scrolled: true);
    }

    private void Finish()
    {
        if (_finished || _stopRequested)
        {
            return;
        }

        // İlk kare henüz alınmadıysa iptal etmek yerine bir kare alıp bitir.
        if (_frames == 0)
        {
            _stopRequested = true;
            _stop.Enabled = false;
            _status.Text = "İlk kare alınıyor…";
            return;
        }

        _stop.Enabled = false;
        _status.Text = "Kareler birleştiriliyor…";

        // Sonucun atılmaması için yalnızca bekleme/lerleme döngüsünü durdur;
        // _finished yalnızca sonuç gösterilirken veya iptalde işaretlenir.
        _cts?.Cancel();
    }

    private void ShowResult(Bitmap canvas, bool scrolled)
    {
        if (_finished)
        {
            canvas.Dispose();
            return;
        }

        _finished = true;
        _timer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        var result = new LongResultForm(canvas, _settings, scrolled);
        result.Show();
        Close();
    }

    private void CancelCapture()
    {
        if (_canceled)
        {
            return;
        }

        _canceled = true;
        _finished = true;
        _cts?.Cancel();
        _timer.Stop();
        Close();
    }

    private void UpdateStatus()
    {
        if (_cts is null || _finished || IsDisposed)
        {
            return;
        }

        var elapsed = DateTime.Now - _startedAt;
        _status.Text = $"Uzun yakalama · {_frames} kare · {elapsed.TotalSeconds:0.0} sn" +
            (_headerHeight > 0 ? $" · sabit başlık {_headerHeight}px" : string.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using (var back = new SolidBrush(Theme.ToolbarBack))
        {
            e.Graphics.FillRectangle(back, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            e.Graphics.ResetClip();
        }

        using var border = new Pen(Theme.ToolbarBorder);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    private void ScrollDown()
    {
        var center = new Point(_region.Left + _region.Width / 2, _region.Top + _region.Height / 2);
        SetCursorPos(center.X, center.Y);
        mouse_event(MouseeventfWheel, 0, 0, -120 * 3, UIntPtr.Zero);
    }

    private void EnableDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragOffset = PointToClient(control.PointToScreen(e.Location));
        };
        control.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var screenPos = control.PointToScreen(e.Location);
            Location = new Point(screenPos.X - _dragOffset.X, screenPos.Y - _dragOffset.Y);
        };
        control.MouseUp += (_, _) => _dragging = false;
    }

    private const int MouseeventfWheel = 0x0800;
    private const uint WdaExcludeFromCapture = 0x00000011;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(int flags, int dx, int dy, int data, UIntPtr extraInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}

/// <summary>Uzun yakalama sonucu: kaydırılabilir önizleme + kaydet/kopyala/pin.</summary>
public sealed class LongResultForm : Form
{
    private readonly Bitmap _image;
    private readonly Settings _settings;
    private readonly Panel _viewport;
    private float _zoom = 1f;

    public LongResultForm(Bitmap image, Settings settings, bool scrolled)
    {
        _image = image;
        _settings = settings;

        UiFactory.StyleForm(this);
        Text = "SnapDesk - Uzun yakalama sonucu";
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(920, 720);
        MinimumSize = new Size(520, 400);

        var title = UiFactory.Label(scrolled
            ? $"Uzun yakalama tamamlandı · {image.Width} × {image.Height} px"
            : "İçerik kaymadığı için tek kare yakalandı");
        title.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        title.Location = new Point(16, 12);

        _viewport = new Panel
        {
            Location = new Point(16, 44),
            Size = new Size(ClientSize.Width - 32, ClientSize.Height - 108),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AutoScroll = true,
            BackColor = Color.FromArgb(18, 18, 22),
        };

        var picture = new PictureBox
        {
            Image = _image,
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = new Point(0, 0),
        };
        _viewport.Controls.Add(picture);

        var hint = UiFactory.Label("Ctrl + fare tekerleği: yakınlaştır", small: true);
        hint.Location = new Point(16, ClientSize.Height - 44);
        hint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

        var save = UiFactory.Button("Kaydet", 100, 32);
        save.Location = new Point(ClientSize.Width - 440, ClientSize.Height - 50);
        save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        save.Click += (_, _) => Save();

        var copy = UiFactory.Button("Kopyala", 100, 32, primary: true);
        copy.Location = new Point(ClientSize.Width - 332, ClientSize.Height - 50);
        copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        copy.Click += (_, _) => Copy();

        var pin = UiFactory.Button("Sabitle", 100, 32);
        pin.Location = new Point(ClientSize.Width - 224, ClientSize.Height - 50);
        pin.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        pin.Click += (_, _) =>
        {
            var copyImage = new Bitmap(_image);
            var screen = Screen.FromControl(this).WorkingArea;
            Pin.PinForm.ShowPin(copyImage, _settings,
                new Point(screen.Left + 40, screen.Top + 40));
            Close();
        };

        var close = UiFactory.Button("Kapat", 100, 32);
        close.Location = new Point(ClientSize.Width - 116, ClientSize.Height - 50);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { title, _viewport, hint, save, copy, pin, close });

        _viewport.MouseWheel += (_, e) =>
        {
            if ((ModifierKeys & Keys.Control) == 0)
            {
                return; // Ctrl yoksa normal dikey kaydırma
            }

            Zoom(e.Delta > 0 ? 1.1f : 1 / 1.1f);
        };
        _viewport.MouseEnter += (_, _) => _viewport.Focus();
        Shown += (_, _) => _viewport.Focus();

        FormClosed += (_, _) => _image.Dispose();
    }

    private void Zoom(float factor)
    {
        _zoom = Math.Clamp(_zoom * factor, 0.1f, 4f);
        if (_viewport.Controls.Count > 0 && _viewport.Controls[0] is PictureBox picture)
        {
            picture.Size = new Size(
                Math.Max(1, (int)(_image.Width * _zoom)),
                Math.Max(1, (int)(_image.Height * _zoom)));
            picture.SizeMode = PictureBoxSizeMode.StretchImage;
        }
    }

    private void Copy()
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

    private void Save()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Uzun yakalamayı kaydet",
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
}
