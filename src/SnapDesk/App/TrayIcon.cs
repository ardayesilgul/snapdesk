using SnapDesk.Core;
using SnapDesk.Editor;
using SnapDesk.UI;

namespace SnapDesk;

/// <summary>Sistem tepsisi simgesi ve bağlam menüsü.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly SnapDeskApplication _app;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _generatedIcon;

    public TrayIcon(SnapDeskApplication app)
    {
        _app = app;

        _generatedIcon = CreateIcon();
        _menu = new ContextMenuStrip
        {
            BackColor = Theme.ToolbarBack,
            ForeColor = Theme.Text,
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = Theme.UiFont,
        };

        _icon = new NotifyIcon
        {
            Icon = _generatedIcon,
            Text = "SnapDesk — ekran görüntüsü ve düzenleme",
            Visible = false,
            ContextMenuStrip = _menu,
        };

        _icon.DoubleClick += (_, _) => _app.StartCapture();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _app.StartCapture();
            }
        };

        RebuildMenu();
    }

    public void Show() => _icon.Visible = true;

    public void Refresh() => RebuildMenu();

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = ToolTipIcon.Info;
            _icon.ShowBalloonTip(3500);
        }
        catch
        {
            // bildirim gösterilemediyse sorun değil
        }
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        var settings = _app.Settings;
        var capture = new ToolStripMenuItem($"Ekran yakala    {settings.CaptureHotkey}")
        {
            Font = Theme.UiFontBold,
        };
        capture.Click += (_, _) => _app.StartCapture();
        _menu.Items.Add(capture);

        _menu.Items.Add(Item($"Sabitle    {settings.PinHotkey}", () =>
            _app.StartCapture(CaptureIntent.Pin)));
        _menu.Items.Add(Item($"Uzun yakalama    {settings.LongCaptureHotkey}", () =>
            _app.StartCapture(CaptureIntent.LongCapture)));
        _menu.Items.Add(Item($"Video kaydet    {settings.RecordHotkey}", () =>
            _app.StartCapture(CaptureIntent.Record)));
        _menu.Items.Add(Item("Metni tanı (OCR)", () =>
            _app.StartCapture(CaptureIntent.Ocr)));

        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(Item("Pinleri göster", _app.ShowPins));
        _menu.Items.Add(Item("Pinleri gizle", _app.HidePins));
        _menu.Items.Add(Item("Tüm pinleri kapat", _app.ClosePins));
        _menu.Items.Add(Item("Fare geçirgenliğini kapat", _app.DisablePinClickThrough));

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Ayarlar…", () => _app.ShowSettings()));
        _menu.Items.Add(Item("Çıkış", _app.Exit));
    }

    private static ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Uygulama simgesi: Gömülü app.ico kaynağını veya exe simgesini yükler.</summary>
    private static Icon CreateIcon()
    {
        try
        {
            using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("SnapDesk.app.ico");
            if (stream is not null)
            {
                return new Icon(stream, 32, 32);
            }
        }
        catch
        {
            // Kaynak yüklenemezse exe'den çıkar
        }

        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted != null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
            // yoksay
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _generatedIcon.Dispose();
    }
}
