using SnapDesk.Core;

namespace SnapDesk;

/// <summary>Sistem tepsisi simgesi: sol tıkla yakala, sağ tıkla uygulama arayüzünü aç.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly SnapDeskApplication _app;
    private readonly NotifyIcon _icon;
    private readonly Icon _generatedIcon;

    public TrayIcon(SnapDeskApplication app)
    {
        _app = app;
        _generatedIcon = CreateIcon();

        _icon = new NotifyIcon
        {
            Icon = _generatedIcon,
            Text = "SnapDesk — Sol tık: yakala | Sağ tık: arayüz",
            Visible = false,
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _app.StartCaptureFromTray();
            }
            else if (e.Button == MouseButtons.Right)
            {
                _app.ShowSettings();
            }
        };
    }

    public void Show() => _icon.Visible = true;

    public void Refresh()
    {
        // Simge ipucu metni sabit; gelecekte dinamik güncelleme gerekirse buraya eklenir.
    }

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
        _generatedIcon.Dispose();
    }
}
