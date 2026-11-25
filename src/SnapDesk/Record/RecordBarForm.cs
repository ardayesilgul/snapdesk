using System.Diagnostics;
using SnapDesk.Core;
using SnapDesk.UI;

namespace SnapDesk.Record;

/// <summary>
/// Bölge video kaydı kontrol çubuğu (Apple Obsidian Dynamic Island stili).
/// </summary>
public sealed class RecordBarForm : Form
{
    private readonly Rectangle _region;
    private readonly Settings _settings;
    private readonly Recorder _recorder;
    private readonly Label _time;
    private readonly Label _status;
    private readonly Button _record;
    private readonly Button _pause;
    private readonly Button _stop;
    private readonly Button _cancel;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _started;
    private bool _finishing;
    private bool _dragging;
    private Point _dragOffset;
    private float _pulsePhase;

    public static void Start(Rectangle region, Settings settings)
    {
        var ffmpeg = Recorder.FindFfmpeg();
        if (ffmpeg is null)
        {
            MessageBox.Show(
                "Video kaydı için 'ffmpeg' gereklidir ancak sistemde bulunamadı.\n\n" +
                "Kurulum: PowerShell'de `winget install Gyan.FFmpeg` komutunu çalıştırın " +
                "veya ffmpeg.exe dosyasını C:\\ffmpeg\\bin klasörüne yerleştirin.",
                "SnapDesk — ffmpeg bulunamadı",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var form = new RecordBarForm(region, settings);
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
            // eski Windows sürümlerinde yoksay
        }
    }

    public RecordBarForm(Rectangle region, Settings settings)
    {
        _region = region;
        _settings = settings;
        _recorder = new Recorder();

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
        ClientSize = new Size(Math.Min(Math.Max(region.Width, 550), 630), 68);
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

        _time = UiFactory.Label("00:00");
        _time.Font = new Font("Consolas", 14.5f, FontStyle.Bold);
        _time.Location = new Point(34, 11);

        _status = UiFactory.Label($"Hazır · {_settings.RecordingFps} FPS · ses yok", small: true);
        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Location = new Point(18, 40);
        _status.Size = new Size(ClientSize.Width - 392, 18);

        var right = ClientSize.Width - 16;
        _cancel = UiFactory.Button("İptal", 76, 34);
        _cancel.BackColor = Theme.ToolbarBack;
        _cancel.Location = new Point(right - 76, 17);
        _cancel.Click += (_, _) => CancelRecording();

        _stop = UiFactory.Button("Durdur", 88, 34);
        _stop.BackColor = Theme.ToolbarBack;
        _stop.Location = new Point(right - 76 - 8 - 88, 17);
        _stop.Enabled = false;
        _stop.Click += (_, _) => StopRecording();

        _pause = UiFactory.Button("Duraklat", 88, 34);
        _pause.BackColor = Theme.ToolbarBack;
        _pause.Location = new Point(right - 76 - 8 - 88 - 8 - 88, 17);
        _pause.Enabled = false;
        _pause.Click += (_, _) => TogglePause();

        _record = UiFactory.Button("Kayıt", 84, 34, primary: true);
        _record.BackColor = Theme.ToolbarBack;
        _record.Location = new Point(right - 76 - 8 - 88 - 8 - 88 - 8 - 84, 17);
        _record.Click += (_, _) => StartRecording();

        Controls.AddRange(new Control[] { _time, _status, _record, _pause, _stop, _cancel });
        EnableDrag(this);
        EnableDrag(_time);
        EnableDrag(_status);

        _timer = new System.Windows.Forms.Timer { Interval = 120 };
        _timer.Tick += (_, _) =>
        {
            _pulsePhase += 0.38f;
            Tick();
        };
        _timer.Start();

        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
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
        _status.ForeColor = Color.FromArgb(245, 110, 110);
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
            _status.ForeColor = Color.FromArgb(245, 110, 110);
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
        Invalidate(new Rectangle(8, 8, 28, 28));

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
        Theme.PaintObsidianSurface(e.Graphics, Width, Height, 16f);

        var dotColor = !_started
            ? Color.FromArgb(115, 115, 125)
            : _recorder.IsPaused
                ? Color.FromArgb(245, 195, 80)
                : Color.FromArgb(255, 69, 58);

        if (_started && !_recorder.IsPaused)
        {
            var pulse = (float)(0.5 + 0.5 * Math.Sin(_pulsePhase));
            var haloAlpha = (int)(35 + pulse * 60);
            using var haloBrush = new SolidBrush(Color.FromArgb(haloAlpha, dotColor));
            e.Graphics.FillEllipse(haloBrush, 14f, 15f, 16f, 16f);
        }

        using (var dotBrush = new SolidBrush(dotColor))
        {
            e.Graphics.FillEllipse(dotBrush, 18f, 19f, 8f, 8f);
        }
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
