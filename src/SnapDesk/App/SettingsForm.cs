using System.Runtime.InteropServices;
using SnapDesk.Core;
using SnapDesk.Ocr;
using SnapDesk.UI;

namespace SnapDesk;

/// <summary>Ayarlar penceresi. Her alan gerçekten kaydedilir ve uygulanır.</summary>
public sealed class SettingsForm : Form
{
    private const int LabelX = 24;
    private const int InputX = 180;
    private const int ContentRight = 576;

    private readonly SnapDeskApplication _app;
    private readonly Settings _settings;
    private readonly HotkeyBox _capture;
    private readonly HotkeyBox _pin;
    private readonly HotkeyBox _long;
    private readonly HotkeyBox _record;
    private readonly TextBox _folder;
    private readonly ComboBox _format;
    private readonly TextBox _pattern;
    private readonly NumericUpDown _stroke;
    private readonly NumericUpDown _fontSize;
    private readonly NumericUpDown _block;
    private readonly NumericUpDown _fps;
    private readonly ComboBox _ocrLanguage;
    private readonly CheckBox _magnifier;
    private readonly CheckBox _confirmEscape;
    private readonly CheckBox _copyOnCapture;
    private readonly CheckBox _cursor;
    private readonly CheckBox _autoCopyOcr;
    private readonly CheckBox _barSerial;
    private readonly CheckBox _barUndoRedo;
    private readonly CheckBox _barCrop;
    private readonly CheckBox _barLong;
    private readonly CheckBox _barRecord;
    private readonly CheckBox _barOcr;
    private readonly CheckBox _barPin;
    private readonly Panel _colorSwatch;
    private readonly Label _hotkeyStatus;
    private readonly Panel _content;
    private readonly Panel _bottom;
    private readonly Label _ocrInfo;
    private Color _color;

    public SettingsForm(SnapDeskApplication app)
    {
        _app = app;
        _settings = app.Settings;
        _color = Color.FromArgb(_settings.DefaultColorArgb);

        UiFactory.StyleForm(this);
        Text = "SnapDesk — Ayarlar";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 740);
        MinimumSize = new Size(620, 740);
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedSingle;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.ToolbarBack,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        Controls.Add(layout);

        _bottom = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 24, 28),
            Margin = Padding.Empty,
        };
        _bottom.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.ToolbarBorder, 1f);
            e.Graphics.DrawLine(pen, 0, 0, _bottom.Width, 0);
        };
        layout.Controls.Add(_bottom, 0, 1);

        _content = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.ToolbarBack,
            Margin = Padding.Empty,
        };
        layout.Controls.Add(_content, 0, 0);

        var y = 16;

        AddHeader("Kısayollar", ref y);

        _capture = AddHotkeyRow("Ekran yakalama", _settings.CaptureHotkey, Settings.DefaultCaptureHotkey, ref y);
        _pin = AddHotkeyRow("Sabitleme", _settings.PinHotkey, Settings.DefaultPinHotkey, ref y);
        _long = AddHotkeyRow("Uzun yakalama", _settings.LongCaptureHotkey, Settings.DefaultLongHotkey, ref y);
        _record = AddHotkeyRow("Video kayıt", _settings.RecordHotkey, Settings.DefaultRecordHotkey, ref y);

        y += 2;
        _hotkeyStatus = UiFactory.Label(string.Empty, small: true);
        _hotkeyStatus.Location = new Point(LabelX, y);
        _hotkeyStatus.MaximumSize = new Size(ContentRight - LabelX, 36);
        _content.Controls.Add(_hotkeyStatus);
        y += 28;

        AddHeader("Kayıt", ref y);

        var folderLabel = UiFactory.Label("Klasör");
        folderLabel.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(folderLabel);

        _folder = UiFactory.TextBox();
        _folder.Location = new Point(InputX, y);
        _folder.Width = 304;
        _folder.Text = _settings.SaveFolder;
        _content.Controls.Add(_folder);

        var browse = UiFactory.Button("Gözat", 84, 26);
        browse.Location = new Point(InputX + 312, y - 1);
        browse.Click += (_, _) => BrowseFolder();
        _content.Controls.Add(browse);
        y += 38;

        _format = AddComboRow("Biçim", new[] { "png", "jpg" }, _settings.ImageFormat, ref y);
        _pattern = AddTextRow("Dosya adı", _settings.FileNamePattern, ref y);

        y += 2;
        var hint = UiFactory.Label("{yyyy}-{MM}-{dd} gibi tarih kalıpları kullanılabilir. Uzantı otomatik eklenir.", small: true);
        hint.Location = new Point(InputX, y);
        _content.Controls.Add(hint);
        y += 28;

        AddHeader("Varsayılanlar", ref y);

        var colorLabel = UiFactory.Label("Renk");
        colorLabel.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(colorLabel);

        _colorSwatch = new Panel
        {
            Location = new Point(InputX, y),
            Size = new Size(52, 26),
            BackColor = _color,
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
        };
        _colorSwatch.Click += (_, _) => PickColor();
        _content.Controls.Add(_colorSwatch);

        var colorHint = UiFactory.Label("tıkla → renk seç", small: true);
        colorHint.Location = new Point(InputX + 62, y + 4);
        _content.Controls.Add(colorHint);
        y += 38;

        _stroke = AddNumberRow("Çizgi kalınlığı", 1, 60, _settings.DefaultStrokeWidth, ref y);
        _fontSize = AddNumberRow("Metin boyutu", 8, 200, _settings.DefaultFontSize, ref y);
        _block = AddNumberRow("Mozaik bloğu", 2, 80, _settings.MosaicBlockSize, ref y);
        _fps = AddNumberRow("Kayıt FPS", 5, 60, _settings.RecordingFps, ref y);

        AddHeader("Metin tanıma", ref y);

        _ocrLanguage = UiFactory.Combo();
        _ocrLanguage.Location = new Point(InputX, y);
        _ocrLanguage.Width = 260;
        _ocrLanguage.Items.Add(new LanguageItem(string.Empty, "Otomatik (Türkçe varsa)"));
        _ocrLanguage.SelectedIndex = 0;

        var ocrLabel = UiFactory.Label("Dil");
        ocrLabel.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(ocrLabel);
        _content.Controls.Add(_ocrLanguage);
        y += 30;

        _ocrInfo = UiFactory.Label("Yerel Windows OCR motoru · çevrimdışı", small: true);
        _ocrInfo.Location = new Point(InputX, y);
        _content.Controls.Add(_ocrInfo);
        y += 28;

        AddHeader("Davranış", ref y);

        _magnifier = UiFactory.Check("Büyüteci göster", _settings.ShowMagnifier);
        _magnifier.Location = new Point(LabelX, y);
        _content.Controls.Add(_magnifier);
        y += 28;

        _confirmEscape = UiFactory.Check("Esc'de çıkış onayı iste", _settings.ConfirmOnEscape);
        _confirmEscape.Location = new Point(LabelX, y);
        _content.Controls.Add(_confirmEscape);
        y += 28;

        _copyOnCapture = UiFactory.Check("Bölge seçildiğinde otomatik olarak panoya kopyala", _settings.CopyToClipboardOnCapture);
        _copyOnCapture.Location = new Point(LabelX, y);
        _content.Controls.Add(_copyOnCapture);
        y += 28;

        _cursor = UiFactory.Check("Kayıtta fare imlecini göster", _settings.RecordingIncludeCursor);
        _cursor.Location = new Point(LabelX, y);
        _content.Controls.Add(_cursor);
        y += 28;

        _autoCopyOcr = UiFactory.Check("OCR sonucunu doğrudan panoya kopyala", _settings.AutoCopyOcrText);
        _autoCopyOcr.Location = new Point(LabelX, y);
        _content.Controls.Add(_autoCopyOcr);
        y += 34;

        AddHeader("Araç Çubuğu (Dinamik Ada)", ref y);

        _barSerial = UiFactory.Check("Numaralandırma aracını göster (N)", _settings.ShowToolbarSerial);
        _barSerial.Location = new Point(LabelX, y);
        _content.Controls.Add(_barSerial);
        y += 26;

        _barUndoRedo = UiFactory.Check("Geri al / İleri al düğmelerini göster (Ctrl+Z / Ctrl+Y)", _settings.ShowToolbarUndoRedo);
        _barUndoRedo.Location = new Point(LabelX, y);
        _content.Controls.Add(_barUndoRedo);
        y += 26;

        _barCrop = UiFactory.Check("Seçime kırp düğmesini göster", _settings.ShowToolbarCrop);
        _barCrop.Location = new Point(LabelX, y);
        _content.Controls.Add(_barCrop);
        y += 26;

        _barLong = UiFactory.Check("Uzun yakalama düğmesini göster (Ctrl+L)", _settings.ShowToolbarLongCapture);
        _barLong.Location = new Point(LabelX, y);
        _content.Controls.Add(_barLong);
        y += 26;

        _barRecord = UiFactory.Check("Video / GIF kayıt düğmesini göster (Ctrl+G)", _settings.ShowToolbarRecord);
        _barRecord.Location = new Point(LabelX, y);
        _content.Controls.Add(_barRecord);
        y += 26;

        _barOcr = UiFactory.Check("Metin tanıma (OCR) düğmesini göster (Shift+C)", _settings.ShowToolbarOcr);
        _barOcr.Location = new Point(LabelX, y);
        _content.Controls.Add(_barOcr);
        y += 26;

        _barPin = UiFactory.Check("Ekrana sabitleme (Pin) düğmesini göster (Ctrl+T)", _settings.ShowToolbarPin);
        _barPin.Location = new Point(LabelX, y);
        _content.Controls.Add(_barPin);
        y += 26;

        var barHint = UiFactory.Label("Gizlenen özelliklere '...' (Daha fazla) menüsünden veya kısayollarla erişebilirsiniz.", small: true);
        barHint.Location = new Point(LabelX, y);
        _content.Controls.Add(barHint);
        y += 34;

        var resetAll = UiFactory.Button("Varsayılanlara sıfırla", 152, 32);
        resetAll.Location = new Point(20, 13);
        resetAll.Click += (_, _) => ResetAllToDefaults();

        var save = UiFactory.Button("Kaydet", 110, 32, primary: true);
        save.Location = new Point(10, 13);
        save.Click += (_, _) => SaveSettings();

        var cancel = UiFactory.Button("İptal", 110, 32);
        cancel.Location = new Point(130, 13);
        cancel.Click += (_, _) => Close();

        _bottom.Controls.Add(resetAll);
        _bottom.Controls.Add(save);
        _bottom.Controls.Add(cancel);

        void LayoutButtons()
        {
            var left = Math.Max(180, _bottom.ClientSize.Width - 248);
            save.Left = left;
            cancel.Left = left + 118;
        }

        _bottom.Resize += (_, _) => LayoutButtons();
        LayoutButtons();

        AcceptButton = save;
        CancelButton = cancel;

        _content.AutoScrollMinSize = new Size(0, y + 8);
        Shown += async (_, _) => await LoadOcrLanguagesAsync();

        ShowHotkeyStatus(_app.HotkeyResults.Where(r => !r.Success).ToList());
    }

    private async Task LoadOcrLanguagesAsync()
    {
        IReadOnlyList<OcrLanguage> languages;
        try
        {
            languages = await Task.Run(OcrService.AvailableLanguages);
        }
        catch
        {
            if (!IsDisposed)
            {
                _ocrInfo.Text = "OCR dil listesi okunamadı.";
            }

            return;
        }

        if (IsDisposed)
        {
            return;
        }

        foreach (var language in languages)
        {
            var label = $"{language.DisplayName} ({language.Tag})";
            var exists = _ocrLanguage.Items
                .Cast<LanguageItem>()
                .Any(i => i.Tag.Equals(language.Tag, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                _ocrLanguage.Items.Add(new LanguageItem(language.Tag, label));
            }
        }

        var selected = _ocrLanguage.Items
            .Cast<LanguageItem>()
            .FirstOrDefault(i => i.Tag.Equals(_settings.OcrLanguage, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            _ocrLanguage.SelectedItem = selected;
        }

        _ocrInfo.Text = languages.Count == 0
            ? "Dil paketi yok — Ayarlar > Zaman ve dil'den ekleyin."
            : "Yerel Windows OCR motoru · çevrimdışı";
    }

    private void AddHeader(string text, ref int y)
    {
        var label = UiFactory.Label(text);
        label.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        label.Location = new Point(20, y);
        _content.Controls.Add(label);
        y += 28;

        var line = new Panel
        {
            Location = new Point(20, y - 5),
            Size = new Size(ContentRight - 20, 1),
            BackColor = Theme.ToolbarBorder,
        };
        _content.Controls.Add(line);
        y += 10;
    }

    private HotkeyBox AddHotkeyRow(string label, string value, string defaultValue, ref int y)
    {
        var text = UiFactory.Label(label);
        text.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(text);

        var box = new HotkeyBox
        {
            Location = new Point(InputX, y),
            Width = 180,
            Text = value,
        };
        _content.Controls.Add(box);

        var reset = UiFactory.Button("Sıfırla", 74, 25);
        reset.Location = new Point(InputX + 188, y - 1);
        reset.Click += (_, _) => box.Text = defaultValue;
        _content.Controls.Add(reset);

        y += 34;
        return box;
    }

    private ComboBox AddComboRow(string label, string[] values, string current, ref int y)
    {
        var text = UiFactory.Label(label);
        text.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(text);

        var combo = UiFactory.Combo();
        combo.Location = new Point(InputX, y);
        combo.Width = 180;
        combo.Items.AddRange(values);
        combo.SelectedItem = values.Contains(current) ? current : values[0];
        _content.Controls.Add(combo);

        y += 34;
        return combo;
    }

    private TextBox AddTextRow(string label, string value, ref int y)
    {
        var text = UiFactory.Label(label);
        text.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(text);

        var box = UiFactory.TextBox();
        box.Location = new Point(InputX, y);
        box.Width = 304;
        box.Text = value;
        _content.Controls.Add(box);

        y += 34;
        return box;
    }

    private NumericUpDown AddNumberRow(string label, int min, int max, int value, ref int y)
    {
        var text = UiFactory.Label(label);
        text.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(text);

        var numeric = new NumericUpDown
        {
            Location = new Point(InputX, y),
            Width = 96,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            BackColor = Theme.ToolbarBackAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.UiFont,
        };
        _content.Controls.Add(numeric);

        y += 34;
        return numeric;
    }

    private void ResetAllToDefaults()
    {
        var defaults = new Settings();
        defaults.Normalize();

        _capture.Text = Settings.DefaultCaptureHotkey;
        _pin.Text = Settings.DefaultPinHotkey;
        _long.Text = Settings.DefaultLongHotkey;
        _record.Text = Settings.DefaultRecordHotkey;
        _folder.Text = defaults.SaveFolder;
        _format.SelectedItem = defaults.ImageFormat;
        _pattern.Text = defaults.FileNamePattern;
        _color = Color.FromArgb(defaults.DefaultColorArgb);
        _colorSwatch.BackColor = _color;
        _stroke.Value = defaults.DefaultStrokeWidth;
        _fontSize.Value = defaults.DefaultFontSize;
        _block.Value = defaults.MosaicBlockSize;
        _fps.Value = defaults.RecordingFps;
        if (_ocrLanguage.Items.Count > 0) _ocrLanguage.SelectedIndex = 0;
        _magnifier.Checked = defaults.ShowMagnifier;
        _confirmEscape.Checked = defaults.ConfirmOnEscape;
        _copyOnCapture.Checked = defaults.CopyToClipboardOnCapture;
        _cursor.Checked = defaults.RecordingIncludeCursor;
        _autoCopyOcr.Checked = defaults.AutoCopyOcrText;
        _barSerial.Checked = defaults.ShowToolbarSerial;
        _barUndoRedo.Checked = defaults.ShowToolbarUndoRedo;
        _barCrop.Checked = defaults.ShowToolbarCrop;
        _barLong.Checked = defaults.ShowToolbarLongCapture;
        _barRecord.Checked = defaults.ShowToolbarRecord;
        _barOcr.Checked = defaults.ShowToolbarOcr;
        _barPin.Checked = defaults.ShowToolbarPin;
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { FullOpen = true, Color = _color, AnyColor = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _color = dialog.Color;
            _colorSwatch.BackColor = _color;
        }
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Kayıt klasörünü seçin",
            SelectedPath = Directory.Exists(_folder.Text) ? _folder.Text : _settings.SaveFolder,
            UseDescriptionForTitle = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folder.Text = dialog.SelectedPath;
        }
    }

    private void SaveSettings()
    {
        var shortcuts = new[] { _capture.Text, _pin.Text, _long.Text, _record.Text };
        foreach (var shortcut in shortcuts)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) continue;
            if (!HotkeyParser.TryParse(shortcut, out var modifiers, out var key))
            {
                MessageBox.Show(this,
                    $"'{shortcut}' geçerli bir kısayol değil. En az bir değiştirici tuş (Ctrl, Alt, Shift, Win) veya bir F tuşu/PrintScreen gerekir.",
                    "SnapDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var hasModifier =
                (modifiers & (HotkeyParser.ModControl | HotkeyParser.ModAlt | HotkeyParser.ModShift | HotkeyParser.ModWin)) != 0;
            var isFunctionKey = key is >= Keys.F1 and <= Keys.F24;
            if (!hasModifier && !isFunctionKey && key != Keys.PrintScreen)
            {
                MessageBox.Show(this,
                    $"'{shortcut}' geçerli bir kısayol değil. En az bir değiştirici tuş (Ctrl, Alt, Shift, Win) veya bir F tuşu/PrintScreen gerekir.",
                    "SnapDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var normalized = shortcuts
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Replace(" ", string.Empty).ToLowerInvariant())
            .ToList();
        if (normalized.Distinct().Count() != normalized.Count)
        {
            MessageBox.Show(this,
                "Aynı kısayol birden fazla eyleme atanamaz. Lütfen kombinasyonlardan birini değiştirin.",
                "SnapDesk - Kısayol çakışması", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.CaptureHotkey = string.IsNullOrWhiteSpace(_capture.Text) ? Settings.DefaultCaptureHotkey : _capture.Text;
        _settings.PinHotkey = string.IsNullOrWhiteSpace(_pin.Text) ? Settings.DefaultPinHotkey : _pin.Text;
        _settings.LongCaptureHotkey = string.IsNullOrWhiteSpace(_long.Text) ? Settings.DefaultLongHotkey : _long.Text;
        _settings.RecordHotkey = string.IsNullOrWhiteSpace(_record.Text) ? Settings.DefaultRecordHotkey : _record.Text;

        _settings.SaveFolder = _folder.Text;
        _settings.ImageFormat = _format.SelectedItem?.ToString() ?? "png";
        _settings.FileNamePattern = string.IsNullOrWhiteSpace(_pattern.Text)
            ? "SnapDesk_{yyyy-MM-dd_HH-mm-ss}"
            : _pattern.Text;
        _settings.DefaultColorArgb = _color.ToArgb();
        _settings.DefaultStrokeWidth = (int)_stroke.Value;
        _settings.DefaultFontSize = (int)_fontSize.Value;
        _settings.MosaicBlockSize = (int)_block.Value;
        _settings.RecordingFps = (int)_fps.Value;
        _settings.OcrLanguage = (_ocrLanguage.SelectedItem as LanguageItem)?.Tag ?? string.Empty;
        _settings.ShowMagnifier = _magnifier.Checked;
        _settings.ConfirmOnEscape = _confirmEscape.Checked;
        _settings.CopyToClipboardOnCapture = _copyOnCapture.Checked;
        _settings.RecordingIncludeCursor = _cursor.Checked;
        _settings.AutoCopyOcrText = _autoCopyOcr.Checked;
        _settings.ShowToolbarSerial = _barSerial.Checked;
        _settings.ShowToolbarUndoRedo = _barUndoRedo.Checked;
        _settings.ShowToolbarCrop = _barCrop.Checked;
        _settings.ShowToolbarLongCapture = _barLong.Checked;
        _settings.ShowToolbarRecord = _barRecord.Checked;
        _settings.ShowToolbarOcr = _barOcr.Checked;
        _settings.ShowToolbarPin = _barPin.Checked;

        try
        {
            Directory.CreateDirectory(_settings.SaveFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Klasör oluşturulamadı: " + ex.Message, "SnapDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.Normalize();
        _settings.Save();
        _app.ApplyHotkeys(notify: true);
        _app.RefreshActiveSessionToolbar();

        Close();
    }

    public void ShowHotkeyStatus(IReadOnlyList<HotkeyManager.Registration> failures)
    {
        if (IsDisposed)
        {
            return;
        }

        _hotkeyStatus.Text = failures.Count == 0
            ? "Tüm kısayollar kayıtlı."
            : "Kaydedilemeyen: " + string.Join(", ", failures.Select(f => f.Shortcut));
        _hotkeyStatus.ForeColor = failures.Count == 0 ? Theme.SubText : Color.FromArgb(240, 140, 110);
    }

    public void ShowHotkeysSuspended()
    {
        if (IsDisposed)
        {
            return;
        }

        _hotkeyStatus.Text = "Ayarlar açıkken global kısayollar geçici olarak devre dışı.";
        _hotkeyStatus.ForeColor = Theme.SubText;
    }

    private sealed record LanguageItem(string Tag, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Tuş kombinasyonunu doğrudan yakalayan kutu.</summary>
    private sealed class HotkeyBox : TextBox
    {
        public HotkeyBox()
        {
            ReadOnly = true;
            BackColor = Theme.ToolbarBackAlt;
            ForeColor = Theme.Text;
            BorderStyle = BorderStyle.FixedSingle;
            Font = Theme.UiFont;
            Cursor = Cursors.Hand;
        }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            {
                return;
            }

            if (e.KeyCode is Keys.Back or Keys.Delete)
            {
                Text = string.Empty;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                return;
            }

            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            var winDown =
                (GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                (GetAsyncKeyState(0x5C) & 0x8000) != 0;
            if (winDown) parts.Add("Win");

            var key = e.KeyCode;
            parts.Add(key switch
            {
                >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
                >= Keys.NumPad0 and <= Keys.NumPad9 => "Num" + (key - Keys.NumPad0),
                _ => key.ToString(),
            });

            Text = string.Join("+", parts);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
