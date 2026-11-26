using System.Drawing.Drawing2D;
using SnapDesk.Core;
using SnapDesk.UI;

namespace SnapDesk.Ocr;

/// <summary>
/// Metin tanıma penceresi (Apple Obsidian & Linear tasarım dili).
/// </summary>
public sealed class OcrForm : Form
{
    private readonly Bitmap _image;
    private readonly Settings _settings;
    private readonly TextBox _text;
    private readonly ComboBox _language;
    private readonly Label _status;
    private readonly Panel _preview;
    private readonly Button _copy;
    private readonly Button _copyOnly;
    private readonly Button _save;
    private readonly Button _rescan;
    private readonly CheckBox _keepLines;
    private readonly CheckBox _showImage;
    private readonly CheckBox _autoCopy;
    private IReadOnlyList<OcrLanguage> _languages = Array.Empty<OcrLanguage>();
    private string _lastLanguageTag = string.Empty;
    private bool _busy;

    public OcrForm(Bitmap image, Settings settings)
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
        Text = "SnapDesk — Metni Çıkart";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 640);
        MinimumSize = new Size(680, 480);
        ShowInTaskbar = true;
        KeyPreview = true;

        var title = UiFactory.Label("Metni Çıkart");
        title.Font = Theme.TitleFont;
        title.Location = new Point(22, 18);

        var subtitle = UiFactory.Label("Yerel OCR · Çevrimdışı", small: true);
        subtitle.ForeColor = Theme.MutedText;
        subtitle.Location = new Point(24, 44);

        var languageLabel = UiFactory.Label("Dil", small: true);
        languageLabel.Location = new Point(210, 26);

        _language = UiFactory.Combo();
        _language.Location = new Point(236, 22);
        _language.Width = 216;
        _language.SelectedIndexChanged += (_, _) =>
        {
            if (_language.SelectedItem is LanguageItem item && item.Tag != _lastLanguageTag)
            {
                Recognize(item.Tag);
            }
        };

        _rescan = UiFactory.Button("Yeniden tara", 112, 30);
        _rescan.Location = new Point(462, 20);
        _rescan.Click += (_, _) => Recognize(_lastLanguageTag);

        _showImage = UiFactory.Check("Görseli göster", true);
        _showImage.Location = new Point(588, 24);
        _showImage.CheckedChanged += (_, _) => UpdatePreviewVisibility();

        _preview = UiFactory.CardPanel(Color.FromArgb(14, 14, 18), 10f);
        _preview.Location = new Point(22, 74);
        _preview.Width = ClientSize.Width - 44;
        _preview.Height = 164;
        _preview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _preview.Paint += PaintPreview;

        _text = UiFactory.TextBox(multiline: true);
        _text.Location = new Point(30, 258);
        _text.Size = new Size(ClientSize.Width - 60, ClientSize.Height - 258 - 110);
        _text.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _text.Font = new Font("Segoe UI", 10.5f);
        _text.WordWrap = true;
        _text.Text = string.Empty;

        _keepLines = UiFactory.Check("Satır sonlarını koru", true);
        _keepLines.Location = new Point(22, ClientSize.Height - 86);
        _keepLines.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _keepLines.CheckedChanged += (_, _) => ApplyLayout();

        _autoCopy = UiFactory.Check("Sonraki sefer doğrudan kopyala", settings.AutoCopyOcrText);
        _autoCopy.Location = new Point(196, ClientSize.Height - 86);
        _autoCopy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _autoCopy.CheckedChanged += (_, _) =>
        {
            _settings.AutoCopyOcrText = _autoCopy.Checked;
            _settings.Save();
        };

        _status = UiFactory.Label(string.Empty, small: true);
        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Location = new Point(22, ClientSize.Height - 44);
        _status.Size = new Size(ClientSize.Width - 496, 22);
        _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        _save = UiFactory.Button("Kaydet (.txt)", 108, 34);
        _save.Location = new Point(ClientSize.Width - 468, ClientSize.Height - 52);
        _save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _save.Click += (_, _) => SaveText();

        _copyOnly = UiFactory.Button("Kopyala", 98, 34);
        _copyOnly.Location = new Point(ClientSize.Width - 352, ClientSize.Height - 52);
        _copyOnly.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _copyOnly.Click += (_, _) => CopyOnly();

        _copy = UiFactory.Button("Kopyala ve kapat", 136, 34, primary: true);
        _copy.Location = new Point(ClientSize.Width - 246, ClientSize.Height - 52);
        _copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _copy.Click += (_, _) => CopyAndClose();

        var close = UiFactory.Button("Kapat", 80, 34);
        close.Location = new Point(ClientSize.Width - 102, ClientSize.Height - 52);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            title, subtitle, languageLabel, _language, _rescan, _showImage, _preview, _text,
            _keepLines, _autoCopy, _status, _save, _copyOnly, _copy, close,
        });

        Shown += (_, _) => LoadLanguages();
        KeyDown += OnKey;
        FormClosed += (_, _) => _image.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Theme.Prepare(g);

        // Üst başlık ayırıcı çizgisi ve alt eylem çubuğu ayırıcı çizgisi
        using var divider = new Pen(Theme.DividerLine, 1f);
        g.DrawLine(divider, 22, 64, ClientSize.Width - 22, 64);
        g.DrawLine(divider, 22, ClientSize.Height - 96, ClientSize.Width - 22, ClientSize.Height - 96);
    }

    public static void ShowFor(Bitmap image, Point origin, Settings settings, IWin32Window? owner = null)
    {
        var form = new OcrForm(image, settings);

        if (owner is Form ownerForm && !ownerForm.IsDisposed)
        {
            form.Owner = ownerForm;
            form.StartPosition = FormStartPosition.CenterParent;
            form.FormClosed += (_, _) =>
            {
                if (ownerForm.IsDisposed)
                {
                    return;
                }

                try
                {
                    ownerForm.BeginInvoke((Action)(() =>
                    {
                        if (!ownerForm.IsDisposed)
                        {
                            ownerForm.Activate();
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    // Sahip penceresi kapanıyorsa yoksay
                }
            };
        }

        form.TopMost = true;
        form.Show();
        form.Activate();
    }

    private async void LoadLanguages()
    {
        _status.Text = "Dil paketleri yükleniyor…";
        _copy.Enabled = false;
        _copyOnly.Enabled = false;
        _rescan.Enabled = false;

        _languages = await Task.Run(OcrService.AvailableLanguages);

        if (_languages.Count == 0)
        {
            _status.Text = "Hiçbir OCR dil paketi bulunamadı.";
            _text.Text =
                "Bu bilgisayarda OCR dil paketi yüklü değil.\r\n\r\n" +
                "Kurulum: Ayarlar > Zaman ve dil > Dil ve bölge > Dil ekle, ardından " +
                "dil seçeneklerinde 'Temel yazma' bileşenini yükle.\r\n\r\n" +
                "Kurulumdan sonra bu pencereyi kapatıp yeniden aç.";
            _text.ReadOnly = true;
            _copy.Enabled = false;
            _copyOnly.Enabled = false;
            return;
        }

        foreach (var language in _languages)
        {
            _language.Items.Add(new LanguageItem(language.Tag, $"{language.DisplayName} ({language.Tag})"));
        }

        var preferred = string.IsNullOrWhiteSpace(_settings.OcrLanguage)
            ? null
            : _settings.OcrLanguage;

        string selectedTag;
        try
        {
            selectedTag = OcrService.ResolveLanguage(preferred);
        }
        catch (OcrUnavailableException ex)
        {
            selectedTag = _languages[0].Tag;
            _status.Text = ex.Message;
        }

        var index = _languages.ToList().FindIndex(l => l.Tag == selectedTag);
        _language.SelectedIndex = Math.Max(0, index);
        ActiveControl = _text;

        Recognize(selectedTag);
    }

    private async void Recognize(string languageTag)
    {
        if (_busy || string.IsNullOrWhiteSpace(languageTag))
        {
            return;
        }

        _busy = true;
        _rescan.Enabled = false;
        _copy.Enabled = false;
        _copyOnly.Enabled = false;
        _status.Text = "Tanınıyor…";
        _text.ReadOnly = true;
        _text.Text = string.Empty;

        try
        {
            var result = await OcrService.RecognizeAsync(_image, languageTag);
            _lastLanguageTag = result.LanguageTag;
            _status.Text = result.Lines.Count == 0
                ? "Metin bulunamadı. Daha büyük bir alan seçmeyi deneyin."
                : $"{result.Lines.Count} satır · dil: {result.LanguageTag}";

            _rawLines = result.Lines;
            ApplyLayout();

            _text.ReadOnly = false;
            _copy.Enabled = result.Text.Length > 0;
            _copyOnly.Enabled = result.Text.Length > 0;
            _settings.OcrLanguage = result.LanguageTag;
            _settings.Save();

            if (_settings.AutoCopyOcrText && _text.Text.Trim().Length > 0)
            {
                ClipboardHelper.CopyText(_text.Text.Trim());
                _status.Text = "Panoya kopyalandı · " + _status.Text;
            }
        }
        catch (OcrUnavailableException ex)
        {
            _status.Text = ex.Message;
            _text.ReadOnly = false;
            _text.Text = ex.Message + "\r\n\r\nKurulu diller: " +
                string.Join(", ", ex.Available.Select(l => l.Tag));
        }
        catch (Exception ex)
        {
            _status.Text = "Metin tanıma başarısız: " + ex.Message;
            _text.ReadOnly = false;
            _text.Text = "Metin tanıma tamamlanamadı.\r\n\r\n" + ex;
        }
        finally
        {
            _busy = false;
            _rescan.Enabled = true;
        }
    }

    private IReadOnlyList<string> _rawLines = Array.Empty<string>();

    private void ApplyLayout()
    {
        if (_rawLines.Count == 0)
        {
            return;
        }

        _text.Text = _keepLines.Checked
            ? string.Join(Environment.NewLine, _rawLines)
            : string.Join(" ", _rawLines);
    }

    private void PaintPreview(object? sender, PaintEventArgs e)
    {
        if (!_showImage.Checked || _image.Width <= 0 || _image.Height <= 0)
        {
            return;
        }

        var g = e.Graphics;
        Theme.Prepare(g);

        var available = new Rectangle(12, 10, _preview.Width - 24, _preview.Height - 20);
        var scale = Math.Min(available.Width / (float)_image.Width, available.Height / (float)_image.Height);
        var width = Math.Max(1, (int)(_image.Width * scale));
        var height = Math.Max(1, (int)(_image.Height * scale));
        var x = available.Left + (available.Width - width) / 2;
        var y = available.Top + (available.Height - height) / 2;

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(_image, new Rectangle(x, y, width, height));
        using var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
        g.DrawRectangle(pen, x, y, width, height);

        // Sağ altta boyut rozeti
        var dimText = $"{_image.Width} × {_image.Height} px";
        var dimSize = TextRenderer.MeasureText(g, dimText, Theme.SmallFont);
        var badgeRect = new RectangleF(_preview.Width - dimSize.Width - 22, _preview.Height - dimSize.Height - 14, dimSize.Width + 12, dimSize.Height + 6);
        Theme.FillRounded(g, badgeRect, 5f, Color.FromArgb(200, 24, 24, 30));
        Theme.DrawRounded(g, badgeRect, 5f, Color.FromArgb(40, 255, 255, 255), 1f);
        TextRenderer.DrawText(
            g, dimText, Theme.SmallFont, Rectangle.Round(badgeRect), Theme.SubText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void UpdatePreviewVisibility()
    {
        _preview.Visible = _showImage.Checked;
        _text.Top = _showImage.Checked ? 258 : 84;
        _text.Height = ClientSize.Height - _text.Top - 110;
        Invalidate();
    }

    private void CopyOnly()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            ClipboardHelper.CopyText(text);
            _status.Text = "Metin panoya kopyalandı.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Panoya kopyalanamadı: " + ex.Message, "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyAndClose()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            ClipboardHelper.CopyText(text);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Panoya kopyalanamadı: " + ex.Message, "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveText()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Metni kaydet",
            Filter = "Metin dosyası (*.txt)|*.txt",
            FileName = _settings.BuildFileName(DateTime.Now, "txt"),
            InitialDirectory = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : null,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _text.Text, new System.Text.UTF8Encoding(true));
            _status.Text = "Kaydedildi: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Metin kaydedilemedi: " + ex.Message, "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
        else if (e.Control && e.KeyCode == Keys.C && !_text.Focused)
        {
            CopyAndClose();
        }
    }

    private sealed record LanguageItem(string Tag, string Label)
    {
        public override string ToString() => Label;
    }
}
