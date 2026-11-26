using System.Drawing.Drawing2D;
using SnapDesk.Core;
using SnapDesk.Editor;
using SnapDesk.Pin;
using SnapDesk.UI;

namespace SnapDesk;

/// <summary>
/// Uygulama yaşam döngüsü: tepsi simgesi, global kısayollar, yakalama oturumu.
/// </summary>
public sealed class SnapDeskApplication
{
    public static SnapDeskApplication? Current { get; private set; }

    private readonly Settings _settings;
    private readonly SingleInstance _instance;
    private readonly HotkeyManager _hotkeys = new();
    private readonly Control _ui = new();
    private TrayIcon? _tray;
    private CaptureSession? _session;
    private SettingsForm? _settingsForm;
    private System.Windows.Forms.Timer? _trayCaptureTimer;
    private bool _hotkeysSuspended;
    private bool _exiting;

    public Settings Settings => _settings;
    public IReadOnlyList<HotkeyManager.Registration> HotkeyResults { get; private set; }
        = Array.Empty<HotkeyManager.Registration>();

    public SnapDeskApplication(Settings settings, SingleInstance instance)
    {
        _settings = settings;
        _instance = instance;

        // Arka plandaki tek-örnek dinleyicisinden gelen komutları UI iş
        // parçacığına aktarmak için görünmez bir denetim.
        _ = _ui.Handle;

        CaptureSession.Created += TrackSession;
    }

    public void Start(bool showStartupBalloon = true)
    {
        Current = this;
        StartupRegistration.Sync(_settings.StartWithWindows);

        _tray = new TrayIcon(this);
        _tray.Show();

        _hotkeys.Pressed += OnHotkey;
        ApplyHotkeys(notify: false);

        _instance.StartListening();

        if (showStartupBalloon)
        {
            _tray.ShowBalloon(
                "SnapDesk hazır",
                $"Tepsi simgesine sol tık veya {_settings.CaptureHotkey} ile ekran yakalayın. Sağ tık ile arayüzü açın.");
        }
    }

    public void HandleCommand(string command)
    {
        if (_ui.InvokeRequired)
        {
            _ui.BeginInvoke(new Action(() => HandleCommand(command)));
            return;
        }

        switch (command.Trim().TrimStart('-').ToLowerInvariant())
        {
            case "capture":
            case "screenshot":
                StartCapture(CaptureIntent.Edit);
                break;
            case "pin":
                StartCapture(CaptureIntent.Pin);
                break;
            case "long":
                StartCapture(CaptureIntent.LongCapture);
                break;
            case "record":
                StartCapture(CaptureIntent.Record);
                break;
            case "ocr":
                StartCapture(CaptureIntent.Ocr);
                break;
            case "settings":
                ShowSettings();
                break;
            case "exit":
                Exit();
                break;
            case "tray":
            case "silent":
                break;
        }
    }

    private void OnHotkey(string action)
    {
        switch (action)
        {
            case "capture":
                StartCapture(CaptureIntent.Edit);
                break;

            case "pin":
                StartCapture(CaptureIntent.Pin);
                break;

            case "long":
                StartCapture(CaptureIntent.LongCapture);
                break;

            case "record":
                StartCapture(CaptureIntent.Record);
                break;
        }
    }

    /// <summary>
    /// Tepsi açılır menüsü veya uygulama arayüzü kapandıktan hemen sonra
    /// yakalamayı başlatır; açılır pencerenin ekran görüntüsüne girmesini önler.
    /// </summary>
    public void StartCaptureFromTray(CaptureIntent intent = CaptureIntent.Edit)
    {
        if (_session is not null)
        {
            _session.FocusOverlay();
            return;
        }

        if (_trayCaptureTimer is not null)
        {
            return;
        }

        var timer = new System.Windows.Forms.Timer { Interval = 180 };
        _trayCaptureTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(_trayCaptureTimer, timer))
            {
                _trayCaptureTimer = null;
            }
            timer.Dispose();
            if (!_exiting)
            {
                StartCapture(intent);
            }
        };
        timer.Start();
    }

    public void StartCapture(CaptureIntent intent = CaptureIntent.Edit)
    {
        if (_session is not null)
        {
            _session.FocusOverlay();
            return;
        }

        try
        {
            var session = new CaptureSession(_settings, intent: intent);
            _session = session;
            session.Start();
        }
        catch (Exception ex)
        {
            _session = null;
            MessageBox.Show(
                "Ekran yakalama başlatılamadı.\n\n" + ex.Message,
                "SnapDesk",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    public void RefreshActiveSessionToolbar()
    {
        _session?.RefreshToolbar();
    }

    private static void TrackSession(CaptureSession session)
    {
        var app = Current;
        if (app is null)
        {
            return;
        }

        app._session = session;
        session.Closed += () =>
        {
            if (ReferenceEquals(app._session, session))
            {
                app._session = null;
            }

            session.Dispose();
        };
    }

    public void ApplyHotkeys(bool notify)
    {
        var map = new Dictionary<string, string>
        {
            ["capture"] = _settings.CaptureHotkey,
            ["pin"] = _settings.PinHotkey,
            ["long"] = _settings.LongCaptureHotkey,
            ["record"] = _settings.RecordHotkey,
        };

        HotkeyResults = _hotkeys.Register(map);

        var failures = HotkeyResults.Where(r => !r.Success).ToList();
        _tray?.Refresh();
        _settingsForm?.ShowHotkeyStatus(failures);

        if (notify && failures.Count > 0)
        {
            var lines = failures.Select(f => $"• {f.Shortcut}: {f.ErrorText}");
            MessageBox.Show(
                "Bazı kısayollar kaydedilemedi (başka bir uygulama kullanıyor olabilir):\n\n" +
                string.Join(Environment.NewLine, lines) +
                "\n\nAyarlar'dan farklı bir kombinasyon seçin.",
                "SnapDesk — Kısayol çakışması",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    public void SuspendHotkeys(bool suspend)
    {
        if (_exiting || _hotkeysSuspended == suspend)
        {
            return;
        }

        _hotkeysSuspended = suspend;
        if (suspend)
        {
            _hotkeys.UnregisterAll();
            _settingsForm?.ShowHotkeysSuspended();
        }
        else
        {
            ApplyHotkeys(notify: false);
        }
    }

    public void ShowSettings(IWin32Window? owner = null)
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }

            _settingsForm.Show();
            _settingsForm.BringToFront();
            if (owner is null)
            {
                var prev = _settingsForm.TopMost;
                _settingsForm.TopMost = true;
                _settingsForm.TopMost = prev;
            }
            _settingsForm.Activate();
            return;
        }

        // Açık bir yakalama oturumu varsa pencere kaplamanın üstünde görünsün.
        owner ??= _session?.OwnerWindow;

        _settingsForm = new SettingsForm(this);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            if (!_hotkeysSuspended) return;
            _hotkeysSuspended = false;
            if (!_exiting)
            {
                ApplyHotkeys(notify: false);
            }
        };

        // Yakalama kaplaması tam ekran ve TopMost. Pencere sahibi
        // olmadan açılırsa kaplamanın arkasında görünmez kalır.
        if (owner is not null)
        {
            _settingsForm.Show(owner);
        }
        else
        {
            _settingsForm.Show();
            _settingsForm.BringToFront();
            _settingsForm.TopMost = true;
            _settingsForm.TopMost = false;
        }

        _settingsForm.Activate();
    }

    public void ShowPins() => PinForm.ShowAll();

    public void HidePins() => PinForm.HideAll();

    public void ClosePins() => PinForm.CloseAll();

    public void DisablePinClickThrough() => PinForm.DisableClickThrough();

    public void Exit()
    {
        _exiting = true;
        _trayCaptureTimer?.Stop();
        _trayCaptureTimer?.Dispose();
        _trayCaptureTimer = null;

        _session?.Dispose();
        _session = null;

        PinForm.CloseAll();

        _hotkeys.Dispose();
        _tray?.Dispose();
        _tray = null;

        Application.Exit();
    }
}
