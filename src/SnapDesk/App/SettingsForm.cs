using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using SnapDesk.Core;
using SnapDesk.Ocr;
using SnapDesk.UI;

namespace SnapDesk;

/// <summary>
/// Ayarlar penceresi (Apple System Settings & Linear kart hiyerarşisi).
/// </summary>
public sealed class SettingsForm : Form
{
    private const int CardLeft = 20;
    private const int CardRight = 592;
    private const int LabelX = 36;
    private const int InputX = 196;

    private readonly SnapDeskApplication _app;
    private readonly Settings _settings;
    private readonly HotkeyBox _capture;
    private readonly HotkeyBox _pin;
    private readonly HotkeyBox _long;
    private readonly HotkeyBox _record;
    private readonly TextBox _folder;
    private readonly ComboBox _format;
    private readonly TextBox _pattern;
    private readonly LinearStepper _stroke;
    private readonly LinearStepper _fontSize;
    private readonly LinearStepper _block;
    private readonly LinearStepper _fps;
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
    private readonly ColorSwatchPill _colorSwatch;
    private readonly Label _hotkeyStatus;
    private readonly ScrollCardPanel _content;
    private readonly Panel _bottom;
    private readonly Label _ocrInfo;
    private readonly List<(int Top, int Bottom)> _cardGroups = new();
    private Color _color;

    public SettingsForm(SnapDeskApplication app)
    {
        _app = app;
        _settings = app.Settings;
        _color = Color.FromArgb(_settings.DefaultColorArgb);

        UiFactory.StyleForm(this);
        UiFactory.AttachSmoothEntrance(this);
        Text = "SnapDesk — Ayarlar";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(632, 750);
        MinimumSize = new Size(632, 640);
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedSingle;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.WindowBack,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
        Controls.Add(layout);

        _bottom = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.ToolbarBack,
            Margin = Padding.Empty,
        };
        _bottom.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.DividerLine, 1f);
            e.Graphics.DrawLine(pen, 0, 0, _bottom.Width, 0);
        };
        layout.Controls.Add(_bottom, 0, 1);

        _content = new ScrollCardPanel(_cardGroups, CardLeft, CardRight)
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.WindowBack,
            Margin = Padding.Empty,
        };
        layout.Controls.Add(_content, 0, 0);

        var y = 18;

        // 1) Kısayollar Kartı
        AddSectionHeader("Kısayollar", ref y);
        var cardStart = BeginCard(ref y);

        _capture = AddHotkeyRow("Ekran yakalama", _settings.CaptureHotkey, Settings.DefaultCaptureHotkey, ref y);
        _pin = AddHotkeyRow("Sabitleme", _settings.PinHotkey, Settings.DefaultPinHotkey, ref y);
        _long = AddHotkeyRow("Uzun yakalama", _settings.LongCaptureHotkey, Settings.DefaultLongHotkey, ref y);
        _record = AddHotkeyRow("Video kayıt", _settings.RecordHotkey, Settings.DefaultRecordHotkey, ref y);

        y += 2;
        _hotkeyStatus = UiFactory.Label(string.Empty, small: true);
        _hotkeyStatus.Location = new Point(LabelX, y);
        _hotkeyStatus.MaximumSize = new Size(CardRight - LabelX - 16, 32);
        _content.Controls.Add(_hotkeyStatus);
        y += 22;
        EndCard(cardStart, ref y);

        // 2) Kayıt Kartı
        AddSectionHeader("Kayıt ve Dosya", ref y);
        cardStart = BeginCard(ref y);

        var folderLabel = UiFactory.Label("Klasör");
        folderLabel.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(folderLabel);

        _folder = UiFactory.TextBox();
        _folder.Location = new Point(InputX + 6, y + 4);
        _folder.Width = 282;
        _folder.Text = _settings.SaveFolder;
        _content.Controls.Add(_folder);

        var browse = UiFactory.Button("Gözat", 78, 28);
        browse.BackColor = Theme.SurfaceCard;
        browse.Location = new Point(InputX + 302, y - 1);
        browse.Click += (_, _) => BrowseFolder();
        _content.Controls.Add(browse);
        y += 38;

        _format = AddComboRow("Biçim", new[] { "png", "jpg" }, _settings.ImageFormat, ref y);
        _pattern = AddTextRow("Dosya adı", _settings.FileNamePattern, ref y);

        var hint = UiFactory.Label("{yyyy}-{MM}-{dd} gibi tarih kalıpları kullanılabilir. Uzantı otomatik eklenir.", small: true);
        hint.ForeColor = Theme.MutedText;
        hint.Location = new Point(InputX, y);
        _content.Controls.Add(hint);
        y += 22;
        EndCard(cardStart, ref y);

        // 3) Varsayılanlar Kartı
        AddSectionHeader("Çizim ve Kayıt Varsayılanları", ref y);
        cardStart = BeginCard(ref y);

        var colorLabel = UiFactory.Label("Varsayılan renk");
        colorLabel.Location = new Point(LabelX, y + 5);
        _content.Controls.Add(colorLabel);

        _colorSwatch = new ColorSwatchPill(_color)
        {
            Location = new Point(InputX, y),
            Size = new Size(64, 28),
        };
        _colorSwatch.Click += (_, _) => PickColor();
        _content.Controls.Add(_colorSwatch);

        var colorHint = UiFactory.Label("Değiştirmek için tıklayın", small: true);
        colorHint.ForeColor = Theme.MutedText;
        colorHint.Location = new Point(InputX + 74, y + 6);
        _content.Controls.Add(colorHint);
        y += 38;

        _stroke = AddStepperRow("Çizgi kalınlığı", 1, 60, _settings.DefaultStrokeWidth, "px", ref y);
        _fontSize = AddStepperRow("Metin boyutu", 8, 200, _settings.DefaultFontSize, "pt", ref y);
        _block = AddStepperRow("Mozaik bloğu", 2, 80, _settings.MosaicBlockSize, "px", ref y);
        _fps = AddStepperRow("Kayıt kare hızı", 5, 60, _settings.RecordingFps, "FPS", ref y);
        EndCard(cardStart, ref y);

        // 4) Metin Tanıma (OCR) Kartı
        AddSectionHeader("Metin Tanıma (OCR)", ref y);
        cardStart = BeginCard(ref y);

        _ocrLanguage = UiFactory.Combo();
        _ocrLanguage.Location = new Point(InputX, y);
        _ocrLanguage.Width = 264;
        _ocrLanguage.Items.Add(new LanguageItem(string.Empty, "Otomatik (Türkçe varsa)"));
        _ocrLanguage.SelectedIndex = 0;

        var ocrLabel = UiFactory.Label("Tanıma dili");
        ocrLabel.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(ocrLabel);
        _content.Controls.Add(_ocrLanguage);
        y += 32;

        _ocrInfo = UiFactory.Label("Yerel Windows OCR motoru · çevrimdışı", small: true);
        _ocrInfo.ForeColor = Theme.MutedText;
        _ocrInfo.Location = new Point(InputX, y);
        _content.Controls.Add(_ocrInfo);
        y += 22;
        EndCard(cardStart, ref y);

        // 5) Davranış Kartı
        AddSectionHeader("Davranış", ref y);
        cardStart = BeginCard(ref y);

        _magnifier = AddCheckRow("Yakalama sırasında piksel büyütecini göster", _settings.ShowMagnifier, ref y);
        _confirmEscape = AddCheckRow("Çizim varken Esc ile çıkışta onay iste", _settings.ConfirmOnEscape, ref y);
        _copyOnCapture = AddCheckRow("Bölge seçildiğinde otomatik olarak panoya kopyala", _settings.CopyToClipboardOnCapture, ref y);
        _cursor = AddCheckRow("Video kaydında fare imlecini göster", _settings.RecordingIncludeCursor, ref y);
        _autoCopyOcr = AddCheckRow("OCR sonucunu doğrudan panoya kopyala", _settings.AutoCopyOcrText, ref y);
        EndCard(cardStart, ref y);

        // 6) Araç Çubuğu (Dinamik Ada) Kartı
        AddSectionHeader("Araç Çubuğu (Dinamik Ada)", ref y);
        cardStart = BeginCard(ref y);

        _barSerial = AddCheckRow("Numaralandırma aracını göster (N)", _settings.ShowToolbarSerial, ref y);
        _barUndoRedo = AddCheckRow("Geri al / İleri al düğmelerini göster (Ctrl+Z / Ctrl+Y)", _settings.ShowToolbarUndoRedo, ref y);
        _barCrop = AddCheckRow("Seçime kırp düğmesini göster", _settings.ShowToolbarCrop, ref y);
        _barLong = AddCheckRow("Uzun yakalama düğmesini göster (Ctrl+L)", _settings.ShowToolbarLongCapture, ref y);
        _barRecord = AddCheckRow("Video / GIF kayıt düğmesini göster (Ctrl+G)", _settings.ShowToolbarRecord, ref y);
        _barOcr = AddCheckRow("Metin tanıma (OCR) düğmesini göster (Shift+C)", _settings.ShowToolbarOcr, ref y);
        _barPin = AddCheckRow("Ekrana sabitleme (Pin) düğmesini göster (Ctrl+T)", _settings.ShowToolbarPin, ref y);

        y += 4;
        var barHint = UiFactory.Label("Gizlenen özelliklere '...' (Daha fazla) menüsünden veya kısayollarla erişebilirsiniz.", small: true);
        barHint.ForeColor = Theme.MutedText;
        barHint.Location = new Point(LabelX, y);
        _content.Controls.Add(barHint);
        y += 22;
        EndCard(cardStart, ref y);

        var resetAll = UiFactory.Button("Varsayılanlara sıfırla", 156, 34);
        resetAll.BackColor = Theme.ToolbarBack;
        resetAll.Location = new Point(20, 14);
        resetAll.Click += (_, _) => ResetAllToDefaults();

        var save = UiFactory.Button("Kaydet", 112, 34, primary: true);
        save.BackColor = Theme.ToolbarBack;
        save.Location = new Point(10, 14);
        save.Click += (_, _) => SaveSettings();

        var cancel = UiFactory.Button("İptal", 104, 34);
        cancel.BackColor = Theme.ToolbarBack;
        cancel.Location = new Point(130, 14);
        cancel.Click += (_, _) => Close();

        _bottom.Controls.Add(resetAll);
        _bottom.Controls.Add(save);
        _bottom.Controls.Add(cancel);

        void LayoutButtons()
        {
            var left = Math.Max(190, _bottom.ClientSize.Width - 244);
            save.Left = left;
            cancel.Left = left + 120;
        }

        _bottom.Resize += (_, _) => LayoutButtons();
        LayoutButtons();

        AcceptButton = save;
        CancelButton = cancel;

        _content.AutoScrollMinSize = new Size(0, y + 16);
        Shown += async (_, _) => await LoadOcrLanguagesAsync();

        ShowHotkeyStatus(_app.HotkeyResults.Where(r => !r.Success).ToList());
    }

    private void AddSectionHeader(string text, ref int y)
    {
        var label = UiFactory.Label(text);
        label.Font = Theme.SectionFont;
        label.ForeColor = Theme.SubText;
        label.Location = new Point(CardLeft + 4, y);
        _content.Controls.Add(label);
        y += 24;
    }

    private static int BeginCard(ref int y)
    {
        var top = y;
        y += 14;
        return top;
    }

    private void EndCard(int top, ref int y)
    {
        y += 8;
        _cardGroups.Add((top, y));
        y += 16;
    }

    private CheckBox AddCheckRow(string text, bool isChecked, ref int y)
    {
        var check = UiFactory.Check(text, isChecked);
        check.BackColor = Theme.SurfaceCard;
        check.Location = new Point(LabelX, y);
        _content.Controls.Add(check);
        y += 30;
        return check;
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

    private HotkeyBox AddHotkeyRow(string label, string value, string defaultValue, ref int y)
    {
        var text = UiFactory.Label(label);
        text.Location = new Point(LabelX, y + 5);
        _content.Controls.Add(text);

        var box = new HotkeyBox
        {
            Location = new Point(InputX, y),
            Size = new Size(184, 28),
            Value = value,
        };
        _content.Controls.Add(box);

        var reset = UiFactory.Button("Sıfırla", 74, 28);
        reset.BackColor = Theme.SurfaceCard;
        reset.Location = new Point(InputX + 192, y);
        reset.Click += (_, _) => box.Value = defaultValue;
        _content.Controls.Add(reset);

        y += 36;
        return box;
    }

    private ComboBox AddComboRow(string label, string[] values, string current, ref int y)
    {
        var text = UiFactory.Label(label);
        text.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(text);

        var combo = UiFactory.Combo();
        combo.Location = new Point(InputX, y);
        combo.Width = 184;
        combo.Items.AddRange(values);
        combo.SelectedItem = values.Contains(current) ? current : values[0];
        _content.Controls.Add(combo);

        y += 36;
        return combo;
    }

    private TextBox AddTextRow(string label, string value, ref int y)
    {
        var text = UiFactory.Label(label);
        text.Location = new Point(LabelX, y + 4);
        _content.Controls.Add(text);

        var box = UiFactory.TextBox();
        box.Location = new Point(InputX + 6, y + 4);
        box.Width = 282;
        box.Text = value;
        _content.Controls.Add(box);

        y += 34;
        return box;
    }

    private LinearStepper AddStepperRow(string label, int min, int max, int value, string unit, ref int y)
    {
        var text = UiFactory.Label(label);
        text.Location = new Point(LabelX, y + 5);
        _content.Controls.Add(text);

        var stepper = new LinearStepper(min, max, value, unit)
        {
            Location = new Point(InputX, y),
            Size = new Size(132, 28),
        };
        _content.Controls.Add(stepper);

        y += 36;
        return stepper;
    }

    private void ResetAllToDefaults()
    {
        var defaults = new Settings();
        defaults.Normalize();

        _capture.Value = Settings.DefaultCaptureHotkey;
        _pin.Value = Settings.DefaultPinHotkey;
        _long.Value = Settings.DefaultLongHotkey;
        _record.Value = Settings.DefaultRecordHotkey;
        _folder.Text = defaults.SaveFolder;
        _format.SelectedItem = defaults.ImageFormat;
        _pattern.Text = defaults.FileNamePattern;
        _color = Color.FromArgb(defaults.DefaultColorArgb);
        _colorSwatch.Color = _color;
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
            _colorSwatch.Color = _color;
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
        var shortcuts = new[] { _capture.Value, _pin.Value, _long.Value, _record.Value };
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
                "SnapDesk — Kısayol çakışması", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.CaptureHotkey = string.IsNullOrWhiteSpace(_capture.Value) ? Settings.DefaultCaptureHotkey : _capture.Value;
        _settings.PinHotkey = string.IsNullOrWhiteSpace(_pin.Value) ? Settings.DefaultPinHotkey : _pin.Value;
        _settings.LongCaptureHotkey = string.IsNullOrWhiteSpace(_long.Value) ? Settings.DefaultLongHotkey : _long.Value;
        _settings.RecordHotkey = string.IsNullOrWhiteSpace(_record.Value) ? Settings.DefaultRecordHotkey : _record.Value;

        _settings.SaveFolder = _folder.Text;
        _settings.ImageFormat = _format.SelectedItem?.ToString() ?? "png";
        _settings.FileNamePattern = string.IsNullOrWhiteSpace(_pattern.Text)
            ? "SnapDesk_{yyyy-MM-dd_HH-mm-ss}"
            : _pattern.Text;
        _settings.DefaultColorArgb = _color.ToArgb();
        _settings.DefaultStrokeWidth = _stroke.Value;
        _settings.DefaultFontSize = _fontSize.Value;
        _settings.MosaicBlockSize = _block.Value;
        _settings.RecordingFps = _fps.Value;
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
        _hotkeyStatus.ForeColor = failures.Count == 0 ? Theme.MutedText : Color.FromArgb(245, 130, 100);
    }

    public void ShowHotkeysSuspended()
    {
        if (IsDisposed)
        {
            return;
        }

        _hotkeyStatus.Text = "Ayarlar açıkken global kısayollar geçici olarak devre dışı.";
        _hotkeyStatus.ForeColor = Theme.MutedText;
    }

    private sealed record LanguageItem(string Tag, string Label)
    {
        public override string ToString() => Label;
    }

    // ----------------------------------------------------------------- ScrollCardPanel

    private sealed class ScrollCardPanel : Panel
    {
        private readonly List<(int Top, int Bottom)> _cards;
        private readonly int _left;
        private readonly int _right;

        public ScrollCardPanel(List<(int Top, int Bottom)> cards, int left, int right)
        {
            _cards = cards;
            _left = left;
            _right = right;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Theme.Prepare(g);

            var scrollY = AutoScrollPosition.Y;
            var width = _right - _left;

            foreach (var (top, bottom) in _cards)
            {
                var rect = new RectangleF(_left + 0.5f, top + scrollY + 0.5f, width - 1f, bottom - top - 1f);
                if (rect.Bottom < 0 || rect.Top > Height) continue;

                Theme.FillRounded(g, rect, 10f, Theme.SurfaceCard);
                Theme.DrawRounded(g, rect, 10f, Theme.CardBorder, 1f);
            }
        }
    }

    // ----------------------------------------------------------------- LinearStepper

    private sealed class LinearStepper : Control
    {
        private readonly int _min;
        private readonly int _max;
        private readonly string _unit;
        private int _value;
        private int _hoverZone; // 0 = none, -1 = minus, 1 = plus

        public LinearStepper(int min, int max, int value, string unit)
        {
            _min = min;
            _max = max;
            _unit = unit;
            _value = Math.Clamp(value, min, max);

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.SurfaceCard;
            Cursor = Cursors.Hand;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Clamp(value, _min, _max);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var zone = e.X < 32 ? -1 : e.X > Width - 32 ? 1 : 0;
            if (zone != _hoverZone)
            {
                _hoverZone = zone;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverZone = 0;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            var step = (ModifierKeys & Keys.Shift) != 0 ? 5 : 1;
            if (e.X < 32)
            {
                Value -= step;
            }
            else if (e.X > Width - 32)
            {
                Value += step;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceCard);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(g, rect, 7f, Theme.SurfaceInput);
            Theme.DrawRounded(g, rect, 7f, Theme.InputBorder, 1f);

            if (_hoverZone == -1)
            {
                Theme.FillRounded(g, new RectangleF(1.5f, 1.5f, 29f, Height - 3f), 6f, Theme.Hover);
            }
            else if (_hoverZone == 1)
            {
                Theme.FillRounded(g, new RectangleF(Width - 30.5f, 1.5f, 29f, Height - 3f), 6f, Theme.Hover);
            }

            TextRenderer.DrawText(g, "−", Theme.UiFontBold, new Rectangle(0, 0, 32, Height),
                _value > _min ? Theme.Text : Theme.Disabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, $"{_value} {_unit}", Theme.UiFont, new Rectangle(32, 0, Width - 64, Height),
                Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, "+", Theme.UiFontBold, new Rectangle(Width - 32, 0, 32, Height),
                _value < _max ? Theme.Text : Theme.Disabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ----------------------------------------------------------------- ColorSwatchPill

    private sealed class ColorSwatchPill : Control
    {
        private Color _color;
        private bool _hovered;

        public ColorSwatchPill(Color color)
        {
            _color = color;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.SurfaceCard;
            Cursor = Cursors.Hand;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceCard);

            var rect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
            Theme.FillRounded(g, rect, 7f, _color);
            Theme.DrawRounded(g, rect, 7f, _hovered ? Color.White : Color.FromArgb(90, 255, 255, 255), _hovered ? 1.5f : 1f);
        }
    }

    // ----------------------------------------------------------------- HotkeyBox

    private sealed class HotkeyBox : Control
    {
        private string _value = string.Empty;
        private bool _hovered;

        public HotkeyBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
            BackColor = Theme.SurfaceCard;
            Font = Theme.UiFontBold;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
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
                Value = string.Empty;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                Parent?.Focus();
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

            var keyName = e.KeyCode is >= Keys.D0 and <= Keys.D9
                ? ((int)e.KeyCode - (int)Keys.D0).ToString()
                : e.KeyCode.ToString();
            parts.Add(keyName);
            Value = string.Join("+", parts);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceCard);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            var fill = Focused
                ? Color.FromArgb(24, Theme.AppleBlue)
                : _hovered
                    ? Theme.SurfaceHover
                    : Theme.SurfaceInput;
            var border = Focused
                ? Theme.AppleBlue
                : _hovered
                    ? Color.FromArgb(80, 255, 255, 255)
                    : Theme.InputBorder;

            Theme.FillRounded(g, rect, 7f, fill);
            Theme.DrawRounded(g, rect, 7f, border, Focused ? 1.4f : 1f);

            var display = string.IsNullOrWhiteSpace(_value)
                ? (Focused ? "Tuş kombinasyonu…" : "Atanmadı")
                : _value;
            var textColor = string.IsNullOrWhiteSpace(_value) ? Theme.MutedText : Theme.Text;

            TextRenderer.DrawText(
                g, display, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
