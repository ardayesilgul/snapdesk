using System.Diagnostics;
using SnapDesk.Core;
using SnapDesk.Pin;
using SnapDesk.UI;

namespace SnapDesk.Record;

/// <summary>
/// Video kayıt çubuğu: başlat / duraklat / durdur / iptal. Kayıt bitince dosya
/// kalıcı klasöre taşınır, istenirse GIF'e çevrilir; iptalde geçici dosyalar silinir.
/// </summary>
public sealed class RecordBarForm : Form
{
    private readonly Rectangle _region;
    private readonly Settings _settings;
    private readonly Label _time;
    private readonly Label _status;
    private readonly Button _record;
    private readonly Button _pause;
    private readonly Button _stop;
    private readonly Button _cancel;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Recorder _recorder = new();
    private bool _started;
    private bool _finishing;
    private bool _dragging;
    private Point _dragOffset;

    public static void Start(Rectangle region, Settings settings)
    {
        if (Recorder.FindFfmpeg() is null)
        {
            MessageBox.Show(
                "Video kaydı için ffmpeg gerekli, ancak bulunamadı.\n\n" +
                "Kurulum: winget install Gyan.FFmpeg\n" +
                "Kurulumdan sonra SnapDesk'i yeniden başlatın.",
                "SnapDesk — Video kaydı",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var form = new RecordBarForm(region, settings);
        form.Show();
        form.Activate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            // Kayıt çubuğunun yakalanan bölgeyle kesiştiğinde videonun içine girmesini önle.
            SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture);
        }
        catch
        {
            // Eski Windows sürümlerinde desteklenmiyorsa yoksay
        }
    }

    private RecordBarForm(Rectangle region, Settings settings)
    {
        _region = region;
        _settings = settings;

        UiFactory.StyleForm(this);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        ClientSize = new Size(Math.Min(Math.Max(region.Width, 540), 620), 66);
        BackColor = Theme.ToolbarBack;

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

        _time = UiFactory.Label("00:00");
        _time.Font = new Font("Consolas", 15f, FontStyle.Bold);
        _time.Location = new Point(32, 9);

        _status = UiFactory.Label($"Hazır · {_settings.RecordingFps} FPS · ses yok", small: true);
        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Location = new Point(16, 39);
        _status.Size = new Size(ClientSize.Width - 384, 18);

        var right = ClientSize.Width - 14;
        _cancel = UiFactory.Button("İptal", 76, 32);
        _cancel.Location = new Point(right - 76, 17);
        _cancel.Click += (_, _) => CancelRecording();

        _stop = UiFactory.Button("Durdur", 88, 32);
        _stop.Location = new Point(right - 76 - 8 - 88, 17);
        _stop.Enabled = false;
        _stop.Click += (_, _) => StopRecording();

        _pause = UiFactory.Button("Duraklat", 88, 32);
        _pause.Location = new Point(right - 76 - 8 - 88 - 8 - 88, 17);
        _pause.Enabled = false;
        _pause.Click += (_, _) => TogglePause();

        _record = UiFactory.Button("Kayıt", 84, 32, primary: true);
        _record.Location = new Point(right - 76 - 8 - 88 - 8 - 88 - 8 - 84, 17);
        _record.Click += (_, _) => StartRecording();

        Controls.AddRange(new Control[] { _time, _status, _record, _pause, _stop, _cancel });
        EnableDrag(this);
        EnableDrag(_time);
        EnableDrag(_status);

        _timer = new System.Windows.Forms.Timer { Interval = 200 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        FormClosed += (_, _) =>
        {
            _timer.Stop();
            if (_started && !_finishing)
            {
                _recorder.Cancel();
            }

            _recorder.Dispose();
        };
    }

    private void StartRecording()
    {
        if (_started)
        {
            return;
        }

        if (!_recorder.Start(_region, _settings.RecordingFps, _settings.RecordingIncludeCursor))
        {
            MessageBox.Show(this, _recorder.LastError ?? "Kayıt başlatılamadı.", "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        _started = true;
        _record.Enabled = false;
        _pause.Enabled = true;
        _stop.Enabled = true;
        _status.Text = $"Kaydediliyor · {_settings.RecordingFps} FPS · ses yok";
        _status.ForeColor = Color.FromArgb(240, 110, 110);
        Invalidate();
    }

    private void TogglePause()
    {
        if (!_started)
        {
            return;
        }

        if (_recorder.IsPaused)
        {
            _recorder.Resume();
            _pause.Text = "Duraklat";
            _status.Text = $"Kaydediliyor · {_settings.RecordingFps} FPS · ses yok";
            _status.ForeColor = Color.FromArgb(240, 110, 110);
        }
        else
        {
            _recorder.Pause();
            _pause.Text = "Sürdür";
            _status.Text = "Duraklatıldı";
            _status.ForeColor = Color.FromArgb(245, 195, 80);
        }

        Invalidate();
    }

    private async void StopRecording()
    {
        if (!_started || _finishing)
        {
            return;
        }

        _finishing = true;
        _stop.Enabled = false;
        _pause.Enabled = false;
        _record.Enabled = false;
        _status.Text = "Birleştiriliyor…";

        var element = await Task.Run(() => _recorder.Stop());

        if (string.IsNullOrWhiteSpace(element) || !File.Exists(element))
        {
            MessageBox.Show(this, _recorder.LastError ?? "Kayıt tamamlanamadı.", "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        string target;
        try
        {
            Directory.CreateDirectory(_settings.SaveFolder);
            target = Path.Combine(_settings.SaveFolder, _settings.BuildFileName(DateTime.Now, "mp4"));
            File.Move(element, target, true);

            // Geçici segmentleri temizle
            try
            {
                var folder = Path.GetDirectoryName(element);
                if (folder is not null && Directory.Exists(folder) && folder.Contains("SnapDesk"))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch
            {
                // yoksay
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Kayıt taşınamadı: " + ex.Message, "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Video kaydedildi:\n{target}\n\nGIF olarak da dışa aktarılsın mı?",
            "SnapDesk — Kayıt tamamlandı",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (answer == DialogResult.Yes)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "GIF olarak dışa aktar",
                Filter = "GIF animasyon (*.gif)|*.gif",
                FileName = Path.GetFileNameWithoutExtension(target) + ".gif",
                InitialDirectory = _settings.SaveFolder,
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _status.Text = "GIF oluşturuluyor…";
                var ok = await Recorder.ExportGifAsync(target, dialog.FileName);
                if (!ok)
                {
                    MessageBox.Show(this, "GIF oluşturulamadı. ffmpeg kurulumunu kontrol edin.", "SnapDesk",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
        }
        catch
        {
            // yoksay
        }

        Close();
    }

    private void CancelRecording()
    {
        if (_started && !_finishing)
        {
            _recorder.Cancel();
        }

        _finishing = true;
        Close();
    }

    private void Tick()
    {
        if (!_started)
        {
            return;
        }

        var elapsed = _recorder.Elapsed;
        _time.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

        if (!_recorder.IsRecording && !_recorder.IsPaused && !_finishing)
        {
            _status.Text = "Kayıt durdu (ffmpeg kapandı).";
            _status.ForeColor = Color.FromArgb(240, 96, 96);
            _finishing = true;
            _timer.Stop();

            MessageBox.Show(this,
                "Kayıt beklenmedik şekilde durdu.\n\n" + (_recorder.LastError ?? "ffmpeg çıktısı alınamadı."),
                "SnapDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Theme.Prepare(e.Graphics);

        using (var back = new SolidBrush(Theme.ToolbarBack))
        {
            e.Graphics.FillRectangle(back, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
        }

        var dotColor = !_started
            ? Color.FromArgb(115, 115, 125)
            : _recorder.IsPaused
                ? Color.FromArgb(245, 195, 80)
                : Color.FromArgb(238, 76, 76);

        using (var dotBrush = new SolidBrush(dotColor))
        {
            e.Graphics.FillEllipse(dotBrush, 16, 15, 10, 10);
        }

        using var border = new Pen(Theme.ToolbarBorder);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
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

    private const uint WdaExcludeFromCapture = 0x00000011;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
