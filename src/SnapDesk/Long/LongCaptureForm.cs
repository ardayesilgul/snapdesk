using SnapDesk.Core;
using SnapDesk.UI;

namespace SnapDesk.Long;

/// <summary>
/// Uzun (kaydırmalı) yakalama kontrol çubuğu (Apple Obsidian Dynamic Island stili).
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
    private bool _finished;
    private bool _canceled;
    private bool _stopRequested;
    private int _frames;
    private int _headerHeight;
    private DateTime _startedAt;
    private bool _dragging;
    private Point _dragOffset;
    private float _pulsePhase;

    public static void Start(Rectangle region, Settings settings)
    {
        var form = new LongCaptureForm(region, settings);
        form.Show();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture);
        }
        catch
        {
            // eski yapılarda yoksay
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginCapture();
    }

    public LongCaptureForm(Rectangle region, Settings settings)
    {
        _region = region;
        _settings = settings;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        ClientSize = new Size(Math.Min(Math.Max(region.Width, 620), 740), 72);
        BackColor = Theme.ToolbarBack;

        UiFactory.ApplyRoundedRegion(this, 16);
        UiFactory.AttachSmoothEntrance(this);
        Resize += (_, _) => UiFactory.ApplyRoundedRegion(this, 16);

        var screen = Screen.FromRectangle(region).WorkingArea;
        var x = Math.Clamp(region.Left, screen.Left + 8, Math.Max(screen.Left + 8, screen.Right - Width - 8));
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
        _status.Location = new Point(34, 15);
        _status.Size = new Size(ClientSize.Width - 314, 20);

        var hint = UiFactory.Label("Sayfa kaydırılarak birleştiriliyor · çubuk ve imleç kayda girmez", small: true);
        hint.AutoSize = false;
        hint.AutoEllipsis = true;
        hint.Location = new Point(34, 38);
        hint.Size = new Size(ClientSize.Width - 314, 18);

        _stop = UiFactory.Button("Durdur ve birleştir", 162, 34, primary: true);
        _stop.BackColor = Theme.ToolbarBack;
        _stop.Location = new Point(ClientSize.Width - 270, 19);
        _stop.Click += (_, _) => Finish();

        _cancel = UiFactory.Button("İptal", 88, 34);
        _cancel.BackColor = Theme.ToolbarBack;
        _cancel.Location = new Point(ClientSize.Width - 100, 19);
        _cancel.Click += (_, _) => CancelCapture();

        Controls.AddRange(new Control[] { _status, hint, _stop, _cancel });
        EnableDrag(this);
        EnableDrag(_status);
        EnableDrag(hint);

        _timer = new System.Windows.Forms.Timer { Interval = 120 };
        _timer.Tick += (_, _) =>
        {
            _pulsePhase += 0.35f;
            UpdateStatus();
        };

        FormClosed += (_, _) =>
        {
            _cts?.Cancel();
            _timer.Stop();
            _timer.Dispose();
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
            await Task.Delay(350, token);

            var first = ScreenCapture.CaptureRegion(_region);
            canvas = new Bitmap(first);
            previous = first;
            _frames = 1;
            _headerHeight = 0;

            var stalledScrolls = 0;
            while (!token.IsCancellationRequested
                   && !_stopRequested
                   && _frames < 160
                   && canvas.Height < 30000)
            {
                ScrollDown();
                await Task.Delay(480, token);

                var current = ScreenCapture.CaptureRegion(_region);

                if (ImageStitcher.FramesNearlyEqual(previous, current))
                {
                    // Sayfa geç yükleniyor veya kaydırma yumuşatma animasyonu yeni başlıyor olabilir; 1 kez daha dene.
                    current.Dispose();
                    ScrollDown();
                    await Task.Delay(480, token);
                    current = ScreenCapture.CaptureRegion(_region);

                    if (ImageStitcher.FramesNearlyEqual(previous, current))
                    {
                        current.Dispose();
                        break;
                    }
                }

                if (_frames == 1)
                {
                    _headerHeight = ImageStitcher.DetectFixedHeader(previous, current);
                }

                var overlap = ImageStitcher.FindVerticalOverlap(previous, current, _headerHeight, 24);
                var newRows = overlap >= 0 ? current.Height - _headerHeight - overlap : -1;

                if (overlap < 0)
                {
                    // Kaydırma çok hızlı geçtiyse veya animasyon ekranı tamamen değiştirdiyse
                    // previous'ı bozmadan bir kez daha bekle/kontrol et; atlama (boşluk) oluşturma.
                    stalledScrolls++;
                    current.Dispose();
                    if (stalledScrolls <= 3)
                    {
                        await Task.Delay(350, token);
                        continue;
                    }

                    break;
                }

                if (newRows < 3)
                {
                    // Sayfa henüz dikeyde ilerlemediyse (scroll-hijack bileşeni); previous'ı güncelleyip devam et.
                    stalledScrolls++;
                    if (stalledScrolls <= 4)
                    {
                        previous.Dispose();
                        previous = current;
                        continue;
                    }

                    current.Dispose();
                    break;
                }

                stalledScrolls = 0;

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

        if (_frames == 0)
        {
            _stopRequested = true;
            _stop.Enabled = false;
            _status.Text = "İlk kare alınıyor…";
            return;
        }

        _stop.Enabled = false;
        _status.Text = "Kareler birleştiriliyor…";
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
        Theme.PaintObsidianSurface(e.Graphics, Width, Height, 16f);

        // Sol üstte mikro-animasyonlu nabız noktası
        var pulse = (float)(0.5 + 0.5 * Math.Sin(_pulsePhase));
        var haloAlpha = (int)(30 + pulse * 55);
        using (var haloBrush = new SolidBrush(Color.FromArgb(haloAlpha, Theme.AppleBlue)))
        {
            e.Graphics.FillEllipse(haloBrush, 13f, 16f, 14f, 14f);
        }

        using (var dotBrush = new SolidBrush(Theme.AppleBlue))
        {
            e.Graphics.FillEllipse(dotBrush, 16f, 19f, 8f, 8f);
        }
    }

    private void ScrollDown()
    {
        var center = new Point(_region.Left + _region.Width / 2, _region.Top + _region.Height / 2);
        SetCursorPos(center.X, center.Y);
        var notches = _region.Height < 320 ? 1 : _region.Height < 650 ? 2 : 3;
        mouse_event(MouseeventfWheel, 0, 0, -120 * notches, UIntPtr.Zero);
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

/// <summary>Uzun yakalama sonucu: tam sayfa sığdırılmış önizleme + kaydet/kopyala/pin.</summary>
public sealed class LongResultForm : Form
{
    private readonly Bitmap _image;
    private readonly Settings _settings;
    private readonly Panel _viewport;
    private readonly PictureBox _picture;
    private float _zoom = 1f;
    private bool _userZoomed;

    public LongResultForm(Bitmap image, Settings settings, bool scrolled)
    {
        _image = image;
        _settings = settings;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        UiFactory.StyleForm(this);
        UiFactory.AttachSmoothEntrance(this);
        Text = "SnapDesk — Uzun Yakalama Sonucu";
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(920, 720);
        MinimumSize = new Size(560, 420);

        var title = UiFactory.Label(scrolled
            ? $"Uzun Yakalama · {image.Width} × {image.Height} px"
            : "İçerik kaymadığı için tek kare yakalandı");
        title.Font = Theme.TitleFont;
        title.Location = new Point(22, 16);

        _viewport = UiFactory.CardPanel(Color.FromArgb(14, 14, 18), 10f);
        _viewport.Location = new Point(22, 54);
        _viewport.Size = new Size(ClientSize.Width - 44, ClientSize.Height - 122);
        _viewport.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _viewport.AutoScroll = true;
        _viewport.Padding = new Padding(8);

        _picture = new PictureBox
        {
            Image = _image,
            SizeMode = PictureBoxSizeMode.StretchImage,
            Location = new Point(8, 8),
        };
        _viewport.Controls.Add(_picture);

        var hint = UiFactory.Label("Ctrl + fare tekerleği ile yakınlaştır", small: true);
        hint.Location = new Point(22, ClientSize.Height - 44);
        hint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

        var save = UiFactory.Button("Kaydet", 104, 34);
        save.Location = new Point(ClientSize.Width - 458, ClientSize.Height - 52);
        save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        save.Click += (_, _) => Save();

        var copy = UiFactory.Button("Kopyala", 104, 34, primary: true);
        copy.Location = new Point(ClientSize.Width - 346, ClientSize.Height - 52);
        copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        copy.Click += (_, _) => Copy();

        var pin = UiFactory.Button("Sabitle", 104, 34);
        pin.Location = new Point(ClientSize.Width - 234, ClientSize.Height - 52);
        pin.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        pin.Click += (_, _) =>
        {
            var copyImage = new Bitmap(_image);
            var screen = Screen.FromControl(this).WorkingArea;
            Pin.PinForm.ShowPin(copyImage, _settings,
                new Point(screen.Left + 40, screen.Top + 40));
            Close();
        };

        var close = UiFactory.Button("Kapat", 100, 34);
        close.Location = new Point(ClientSize.Width - 122, ClientSize.Height - 52);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { title, _viewport, hint, save, copy, pin, close });

        _viewport.MouseWheel += (_, e) =>
        {
            if ((ModifierKeys & Keys.Control) == 0)
            {
                return;
            }

            if (e is HandledMouseEventArgs hme)
            {
                hme.Handled = true;
            }

            _userZoomed = true;
            Zoom(e.Delta > 0 ? 1.15f : 1 / 1.15f);
        };
        _viewport.MouseEnter += (_, _) => _viewport.Focus();
        _viewport.Resize += (_, _) =>
        {
            if (!_userZoomed)
            {
                FitToViewport();
            }
            else
            {
                CenterPicture();
            }
        };
        Shown += (_, _) =>
        {
            FitToViewport();
            _viewport.Focus();
        };

        FormClosed += (_, _) => _image.Dispose();
    }

    private void FitToViewport()
    {
        if (_image.Width <= 0 || _image.Height <= 0) return;
        var availW = Math.Max(40, _viewport.ClientSize.Width - 24);
        var availH = Math.Max(40, _viewport.ClientSize.Height - 24);
        var fit = Math.Min(availW / (float)_image.Width, availH / (float)_image.Height);
        ApplyZoom(Math.Clamp(fit, 0.02f, 1f));
    }

    private void Zoom(float factor) => ApplyZoom(Math.Clamp(_zoom * factor, 0.02f, 4f));

    private void ApplyZoom(float zoom)
    {
        _zoom = zoom;
        _picture.Size = new Size(
            Math.Max(1, (int)Math.Round(_image.Width * _zoom)),
            Math.Max(1, (int)Math.Round(_image.Height * _zoom)));
        CenterPicture();
    }

    private void CenterPicture()
    {
        var x = Math.Max(8, (_viewport.ClientSize.Width - _picture.Width) / 2);
        var y = Math.Max(8, (_viewport.ClientSize.Height - _picture.Height) / 2);
        _picture.Location = new Point(x, y);
    }

    private void Copy()
    {
        try
        {
            ClipboardHelper.CopyImage(_image);
            Close();
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
            var format = Path.GetExtension(dialog.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                ? System.Drawing.Imaging.ImageFormat.Jpeg
                : System.Drawing.Imaging.ImageFormat.Png;
            _image.Save(dialog.FileName, format);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Kaydedilemedi: " + ex.Message, "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
