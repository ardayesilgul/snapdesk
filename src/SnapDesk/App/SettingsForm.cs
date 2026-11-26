using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using SnapDesk.Core;
using SnapDesk.Editor;
using SnapDesk.Ocr;
using SnapDesk.UI;

namespace SnapDesk;

/// <summary>
/// Yatay, sıfır kaydırma çubuklu Apple System Settings & OLED Zinc SnapDesk kontrol merkezi.
/// </summary>
public sealed class SettingsForm : Form
{
    private const int SidebarWidth = 212;
    private const int ContentWidth = 628;
    private const int BodyHeight = 472;
    private const int FooterHeight = 68;
    private const int PageMargin = 26;
    private const int CardWidth = 576;

    private readonly SnapDeskApplication _app;
    private readonly Settings _settings;

    private readonly Panel _sidebar;
    private readonly Panel _headerBar;
    private readonly Label _headerTitle;
    private readonly Label _headerSubtitle;
    private readonly Panel _viewport;
    private readonly Panel _footer;
    private readonly List<SidebarTab> _tabs = new();
    private readonly List<Panel> _pages = new();
    private readonly List<(string Title, string Subtitle)> _pageHeaders = new();

    private readonly HotkeyBox _capture;
    private readonly HotkeyBox _pin;
    private readonly HotkeyBox _long;
    private readonly HotkeyBox _record;
    private readonly Label _hotkeyStatus;

    private readonly TextBox _folder;
    private readonly UiFactory.AppleSegmentedControl _format;
    private readonly TextBox _pattern;
    private readonly LinearStepper _fps;
    private readonly UiFactory.AppleToggleSwitch _cursor;

    private readonly UiFactory.AppleToggleSwitch _startWithWindows;
    private readonly UiFactory.AppleToggleSwitch _magnifier;
    private readonly UiFactory.AppleToggleSwitch _confirmEscape;
    private readonly UiFactory.AppleToggleSwitch _copyOnCapture;
    private readonly UiFactory.ApplePicker _ocrLanguage;
    private readonly Label _ocrInfo;
    private readonly UiFactory.AppleToggleSwitch _autoCopyOcr;

    private readonly ColorSwatchPill _colorSwatch;
    private readonly LinearStepper _stroke;
    private readonly LinearStepper _fontSize;
    private readonly LinearStepper _block;
    private readonly UiFactory.AppleToggleSwitch _barSerial;
    private readonly UiFactory.AppleToggleSwitch _barUndoRedo;
    private readonly UiFactory.AppleToggleSwitch _barCrop;
    private readonly UiFactory.AppleToggleSwitch _barLong;
    private readonly UiFactory.AppleToggleSwitch _barRecord;
    private readonly UiFactory.AppleToggleSwitch _barOcr;
    private readonly UiFactory.AppleToggleSwitch _barPin;

    private BentoTile? _pinTile;
    private BentoTile? _longTile;
    private BentoTile? _recordTile;
    private HeroCaptureCard? _heroCard;
    private Color _color;

    public SettingsForm(SnapDeskApplication app)
    {
        _app = app;
        _settings = app.Settings;
        _color = Color.FromArgb(_settings.DefaultColorArgb);

        UiFactory.StyleForm(this);
        Text = "SnapDesk";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(SidebarWidth + ContentWidth, BodyHeight + FooterHeight);
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedSingle;

        _sidebar = BuildSidebar();
        _footer = BuildFooter();
        _headerBar = new Panel
        {
            Location = new Point(SidebarWidth, 0),
            Size = new Size(ContentWidth, 76),
            BackColor = Theme.WindowBack,
        };
        _headerTitle = MakeLabel(string.Empty, Theme.TitleFont, Theme.Text, Theme.WindowBack, PageMargin, 20);
        _headerSubtitle = MakeLabel(string.Empty, Theme.SmallFont, Theme.SubText, Theme.WindowBack, PageMargin, 48);
        var exitPill = new ExitAppPill();
        exitPill.Location = new Point(ContentWidth - PageMargin - exitPill.Width, 21);
        exitPill.ExitClicked += () => _app.Exit();
        _headerBar.Controls.Add(_headerTitle);
        _headerBar.Controls.Add(_headerSubtitle);
        _headerBar.Controls.Add(exitPill);

        _viewport = new Panel
        {
            Location = new Point(SidebarWidth, 76),
            Size = new Size(ContentWidth, BodyHeight - 76),
            BackColor = Theme.WindowBack,
        };

        Controls.Add(_sidebar);
        Controls.Add(_headerBar);
        Controls.Add(_viewport);
        Controls.Add(_footer);

        // 5 modüler sayfa — hiçbiri dikey kaydırmaya ihtiyaç duymaz.
        var startPage = CreatePage("Hızlı Başlat", "Tek tıkla ekran yakalayın veya sabitlenmiş pinleri yönetin.");
        BuildStartPage(startPage);

        var shortcutsPage = CreatePage("Kısayollar", "Her yerden çalışan global klavye kısayollarını düzenleyin.");
        (_capture, _pin, _long, _record, _hotkeyStatus) = BuildShortcutsPage(shortcutsPage);

        var behaviorPage = CreatePage("Yakalama ve Metni Çıkart", "Başlangıç davranışı, yakalama yardımcıları ve çevrimdışı OCR.");
        (_startWithWindows, _magnifier, _confirmEscape, _copyOnCapture, _ocrLanguage, _ocrInfo, _autoCopyOcr) =
            BuildBehaviorOcrPage(behaviorPage);

        var outputPage = CreatePage("Kayıt ve Çıktı", "Varsayılan klasör, görüntü biçimi ve ekran kaydı kare hızı.");
        (_folder, _format, _pattern, _fps, _cursor) = BuildOutputPage(outputPage);

        var appearancePage = CreatePage("Görünüm ve Çubuk", "Çizim varsayılanları ve yüzen Dinamik Ada araç düğmeleri.");
        (_colorSwatch, _stroke, _fontSize, _block, _barSerial, _barUndoRedo, _barCrop, _barLong, _barRecord, _barOcr, _barPin) =
            BuildAppearancePage(appearancePage);

        SelectTab(0);
        Shown += async (_, _) => await LoadOcrLanguagesAsync();
        ShowHotkeyStatus(_app.HotkeyResults.Where(r => !r.Success).ToList());
    }

    // ----------------------------------------------------------------- Sidebar & Shell

    private Panel BuildSidebar()
    {
        var sidebar = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(SidebarWidth, BodyHeight + FooterHeight),
            BackColor = Theme.ToolbarBack,
        };
        sidebar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Theme.Prepare(g);

            // Sağ kenar saç teli ayırıcı
            using var divPen = new Pen(Color.FromArgb(24, 255, 255, 255), 1f);
            g.DrawLine(divPen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);

            // Marka ikonu (gömülü app.ico)
            var iconRect = new Rectangle(18, 23, 28, 28);
            if (Icon is not null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawIcon(Icon, iconRect);
            }
        };

        var brandTitle = MakeLabel("SnapDesk", Theme.Ui(11.5f, FontStyle.Bold), Theme.Text, Theme.ToolbarBack, 54, 19);
        var brandState = MakeLabel("Ekran Aracı", Theme.SmallFont, Theme.MutedText, Theme.ToolbarBack, 54, 41);
        sidebar.Controls.Add(brandTitle);
        sidebar.Controls.Add(brandState);

        var items = new (string Label, IconKind Icon)[]
        {
            ("Başlat", IconKind.Image),
            ("Kısayollar", IconKind.Layers),
            ("Yakalama ve OCR", IconKind.Ocr),
            ("Kayıt ve Çıktı", IconKind.Folder),
            ("Görünüm ve Çubuk", IconKind.Settings),
        };

        var y = 86;
        for (var i = 0; i < items.Length; i++)
        {
            var idx = i;
            var tab = new SidebarTab(items[i].Label, items[i].Icon)
            {
                Location = new Point(12, y),
                Size = new Size(SidebarWidth - 25, 36),
            };
            tab.Click += (_, _) => SelectTab(idx);
            _tabs.Add(tab);
            sidebar.Controls.Add(tab);
            y += 42;
        }

        var versionNote = MakeLabel("v0.9.0", Theme.SmallFont, Theme.MutedText, Theme.ToolbarBack, 20, BodyHeight + 25);
        sidebar.Controls.Add(versionNote);

        return sidebar;
    }

    private Panel BuildFooter()
    {
        var footer = new Panel
        {
            Location = new Point(SidebarWidth, BodyHeight),
            Size = new Size(ContentWidth, FooterHeight),
            BackColor = Theme.ToolbarBack,
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(22, 255, 255, 255), 1f);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        var resetAll = UiFactory.Button("Varsayılanlara sıfırla", 154, 34);
        resetAll.BackColor = Theme.ToolbarBack;
        resetAll.Location = new Point(PageMargin, 17);
        resetAll.Click += (_, _) => ResetAllToDefaults();

        var cancel = UiFactory.Button("Vazgeç", 96, 34);
        cancel.BackColor = Theme.ToolbarBack;
        cancel.Location = new Point(ContentWidth - PageMargin - 108 - 10 - 96, 17);
        cancel.Click += (_, _) => Close();

        var save = UiFactory.Button("Kaydet", 108, 34, primary: true);
        save.BackColor = Theme.ToolbarBack;
        save.Location = new Point(ContentWidth - PageMargin - 108, 17);
        save.Click += (_, _) => SaveSettings();

        footer.Controls.Add(resetAll);
        footer.Controls.Add(cancel);
        footer.Controls.Add(save);

        AcceptButton = save;
        CancelButton = cancel;
        return footer;
    }

    public void SelectTab(int index)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            var active = i == index;
            _tabs[i].Active = active;
            _pages[i].Visible = active;
        }

        if (index >= 0 && index < _pageHeaders.Count)
        {
            _headerTitle.Text = _pageHeaders[index].Title;
            _headerSubtitle.Text = _pageHeaders[index].Subtitle;
        }
    }

    private Panel CreatePage(string title, string subtitle)
    {
        var page = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(ContentWidth, BodyHeight - 76),
            BackColor = Theme.WindowBack,
            Visible = false,
            Padding = new Padding(0),
        };

        _pageHeaders.Add((title, subtitle));
        _pages.Add(page);
        _viewport.Controls.Add(page);
        return page;
    }

    // ----------------------------------------------------------------- Page 0: Başlat

    private void BuildStartPage(Panel page)
    {
        _heroCard = new HeroCaptureCard(_settings.CaptureHotkey)
        {
            Location = new Point(PageMargin, 4),
            Size = new Size(CardWidth, 88),
        };
        _heroCard.ActionTriggered += () => TriggerCaptureFromUi(CaptureIntent.Edit);

        var heroBtn = UiFactory.Button("Ekran Yakala", 142, 36, primary: true);
        heroBtn.BackColor = Theme.SurfaceCard;
        heroBtn.Location = new Point(CardWidth - 142 - 18, 26);
        heroBtn.Click += (_, _) => TriggerCaptureFromUi(CaptureIntent.Edit);
        _heroCard.Controls.Add(heroBtn);
        page.Controls.Add(_heroCard);

        const int tileW = 282;
        const int tileH = 82;
        const int gap = 12;

        _pinTile = new BentoTile(IconKind.Pin, "Sabitle", "Seçimi ekranda en üstte tut", _settings.PinHotkey)
        {
            Location = new Point(PageMargin, 106),
            Size = new Size(tileW, tileH),
        };
        _pinTile.Click += (_, _) => TriggerCaptureFromUi(CaptureIntent.Pin);

        _longTile = new BentoTile(IconKind.LongCapture, "Uzun Yakalama", "Kaydırmalı dikey sayfa birleştirme", _settings.LongCaptureHotkey)
        {
            Location = new Point(PageMargin + tileW + gap, 106),
            Size = new Size(tileW, tileH),
        };
        _longTile.Click += (_, _) => TriggerCaptureFromUi(CaptureIntent.LongCapture);

        _recordTile = new BentoTile(IconKind.Video, "Video Kaydı", "Seçili bölgeyi MP4 / GIF kaydet", _settings.RecordHotkey)
        {
            Location = new Point(PageMargin, 106 + tileH + gap),
            Size = new Size(tileW, tileH),
        };
        _recordTile.Click += (_, _) => TriggerCaptureFromUi(CaptureIntent.Record);

        var ocrTile = new BentoTile(IconKind.Ocr, "Metni Çıkart", "Görseldeki metni seç ve kopyala", "Shift+C")
        {
            Location = new Point(PageMargin + tileW + gap, 106 + tileH + gap),
            Size = new Size(tileW, tileH),
        };
        ocrTile.Click += (_, _) => TriggerCaptureFromUi(CaptureIntent.Ocr);

        page.Controls.Add(_pinTile);
        page.Controls.Add(_longTile);
        page.Controls.Add(_recordTile);
        page.Controls.Add(ocrTile);

        var pinBar = new GroupedCard(Array.Empty<int>())
        {
            Location = new Point(PageMargin, 296),
            Size = new Size(CardWidth, 64),
        };

        var pinTitle = MakeLabel("Ekran Pinleri", Theme.UiFontBold, Theme.Text, Theme.SurfaceCard, 18, 13);
        var pinHint = MakeLabel("Sol tık tepsi: yakala · Sağ tık: arayüz", Theme.SmallFont, Theme.MutedText, Theme.SurfaceCard, 18, 34);
        pinBar.Controls.Add(pinTitle);
        pinBar.Controls.Add(pinHint);

        var pinActions = new (string Text, Action Handler, int Width)[]
        {
            ("Göster", _app.ShowPins, 66),
            ("Gizle", _app.HidePins, 60),
            ("Kapat", _app.ClosePins, 62),
            ("Kilidi aç", _app.DisablePinClickThrough, 82),
        };
        var px = CardWidth - 16 - pinActions.Sum(a => a.Width) - 6 * (pinActions.Length - 1);
        foreach (var (text, handler, width) in pinActions)
        {
            var btn = UiFactory.Button(text, width, 28);
            btn.BackColor = Theme.SurfaceCard;
            btn.Location = new Point(px, 18);
            btn.Click += (_, _) => handler();
            pinBar.Controls.Add(btn);
            px += width + 6;
        }

        page.Controls.Add(pinBar);
    }

    // ----------------------------------------------------------------- Page 1: Kısayollar

    private (HotkeyBox Capture, HotkeyBox Pin, HotkeyBox Long, HotkeyBox Record, Label Status) BuildShortcutsPage(Panel page)
    {
        var card = new GroupedCard(new[] { 54, 54, 54 })
        {
            Location = new Point(PageMargin, 4),
            Size = new Size(CardWidth, 216),
        };

        var capture = AddHotkeyRow(card, 0, "Ekran yakalama", "Bölge seçimi, pencere yakalama ve çizim", _settings.CaptureHotkey, Settings.DefaultCaptureHotkey);
        var pin = AddHotkeyRow(card, 54, "Sabitleme", "Seçilen alanı masaüstünde sabitle", _settings.PinHotkey, Settings.DefaultPinHotkey);
        var longCap = AddHotkeyRow(card, 108, "Uzun yakalama", "Kaydırmalı belge ve sayfa birleştirme", _settings.LongCaptureHotkey, Settings.DefaultLongHotkey);
        var record = AddHotkeyRow(card, 162, "Video kayıt", "Seçili alanda MP4 / GIF kayıt çubuğu", _settings.RecordHotkey, Settings.DefaultRecordHotkey);

        var status = MakeLabel("Tüm kısayollar etkin.", Theme.SmallFont, Theme.MutedText, Theme.WindowBack, PageMargin + 4, 236);
        status.MaximumSize = new Size(CardWidth, 32);

        page.Controls.Add(card);
        page.Controls.Add(status);
        return (capture, pin, longCap, record, status);
    }

    private HotkeyBox AddHotkeyRow(Panel card, int rowTop, string title, string subtitle, string value, string defaultValue)
    {
        AddRowLabels(card, 18, rowTop + 9, title, subtitle);

        var box = new HotkeyBox
        {
            Location = new Point(CardWidth - 18 - 72 - 8 - 178, rowTop + 12),
            Size = new Size(178, 30),
            Value = value,
        };
        box.GotFocus += (_, _) => _app.SuspendHotkeys(true);
        box.LostFocus += (_, _) => _app.SuspendHotkeys(false);

        var reset = UiFactory.Button("Sıfırla", 72, 30);
        reset.BackColor = Theme.SurfaceCard;
        reset.Location = new Point(CardWidth - 18 - 72, rowTop + 12);
        reset.Click += (_, _) => box.Value = defaultValue;

        card.Controls.Add(box);
        card.Controls.Add(reset);
        return box;
    }

    // ----------------------------------------------------------------- Page 2: Yakalama & OCR

    private (
        UiFactory.AppleToggleSwitch StartWithWindows,
        UiFactory.AppleToggleSwitch Magnifier,
        UiFactory.AppleToggleSwitch ConfirmEscape,
        UiFactory.AppleToggleSwitch CopyOnCapture,
        UiFactory.ApplePicker OcrLanguage,
        Label OcrInfo,
        UiFactory.AppleToggleSwitch AutoCopyOcr)
        BuildBehaviorOcrPage(Panel page)
    {
        AddSectionCaption(page, "Davranış", PageMargin, 2);

        var behaviorCard = new GroupedCard(new[] { 46, 46, 46 })
        {
            Location = new Point(PageMargin, 24),
            Size = new Size(CardWidth, 184),
        };

        var startWithWindows = AddToggleRow(behaviorCard, 0, 46, "Windows açılışında başlat", "Oturum başında tepsi simgesini hazır beklet", _settings.StartWithWindows);
        var magnifier = AddToggleRow(behaviorCard, 46, 46, "Piksel büyüteci", "Yakalama sırasında 9x büyüteç ve renk kodu göster", _settings.ShowMagnifier);
        var confirmEscape = AddToggleRow(behaviorCard, 92, 46, "Çıkışta onay iste", "Üzerinde çizim varken Esc ile kapatırken uyar", _settings.ConfirmOnEscape);
        var copyOnCapture = AddToggleRow(behaviorCard, 138, 46, "Panoya otomatik kopyala", "Bölge seçildiği anda görseli panoya aktar", _settings.CopyToClipboardOnCapture);

        AddSectionCaption(page, "Metni Çıkart (OCR)", PageMargin, 226);

        var ocrCard = new GroupedCard(new[] { 54 })
        {
            Location = new Point(PageMargin, 248),
            Size = new Size(CardWidth, 102),
        };

        var ocrInfo = AddRowLabels(ocrCard, 18, 9, "Tanıma dili", "Yerel Windows OCR motoru · çevrimdışı");

        var combo = UiFactory.Combo();
        combo.Location = new Point(CardWidth - 18 - 248, 12);
        combo.Width = 248;
        combo.Items.Add(new LanguageItem(string.Empty, "Otomatik (Türkçe varsa)"));
        combo.SelectedIndex = 0;
        ocrCard.Controls.Add(combo);

        var autoCopyOcr = AddToggleRow(ocrCard, 54, 48, "Sonucu otomatik kopyala", "Tanınan metni doğrudan panoya kopyala", _settings.AutoCopyOcrText);

        page.Controls.Add(behaviorCard);
        page.Controls.Add(ocrCard);
        return (startWithWindows, magnifier, confirmEscape, copyOnCapture, combo, ocrInfo, autoCopyOcr);
    }

    // ----------------------------------------------------------------- Page 3: Kayıt & Çıktı

    private (
        TextBox Folder,
        UiFactory.AppleSegmentedControl Format,
        TextBox Pattern,
        LinearStepper Fps,
        UiFactory.AppleToggleSwitch Cursor)
        BuildOutputPage(Panel page)
    {
        AddSectionCaption(page, "Dosya ve Biçim", PageMargin, 2);

        var fileCard = new GroupedCard(new[] { 52, 52 })
        {
            Location = new Point(PageMargin, 24),
            Size = new Size(CardWidth, 166),
        };

        AddRowLabels(fileCard, 18, 17, "Kayıt klasörü", null);
        var folder = UiFactory.TextBox();
        folder.Location = new Point(202, 17);
        folder.Width = 260;
        folder.Text = _settings.SaveFolder;
        var browse = UiFactory.Button("Gözat…", 80, 30);
        browse.BackColor = Theme.SurfaceCard;
        browse.Location = new Point(CardWidth - 18 - 80, 11);
        browse.Click += (_, _) => BrowseFolder();
        fileCard.Controls.Add(folder);
        fileCard.Controls.Add(browse);

        AddRowLabels(fileCard, 18, 61, "Görüntü biçimi", "Kaydetme ve hızlı dışa aktarma formatı");
        var format = UiFactory.Segmented("PNG", "JPG");
        format.Location = new Point(CardWidth - 18 - 152, 63);
        format.SelectValue(_settings.ImageFormat);
        fileCard.Controls.Add(format);

        AddRowLabels(fileCard, 18, 115, "Dosya adı kalıbı", "Tarih kalıpları: {yyyy}-{MM}-{dd}");
        var pattern = UiFactory.TextBox();
        pattern.Location = new Point(304, 125);
        pattern.Width = CardWidth - 25 - 304;
        pattern.Text = _settings.FileNamePattern;
        fileCard.Controls.Add(pattern);

        AddSectionCaption(page, "Video Kaydı", PageMargin, 208);

        var videoCard = new GroupedCard(new[] { 52 })
        {
            Location = new Point(PageMargin, 230),
            Size = new Size(CardWidth, 100),
        };

        AddRowLabels(videoCard, 18, 9, "Kare hızı", "MP4 ve GIF kaydı kare saniye hızı");
        var fps = new LinearStepper(5, 60, _settings.RecordingFps, "FPS")
        {
            Location = new Point(CardWidth - 18 - 132, 12),
            Size = new Size(132, 28),
        };
        videoCard.Controls.Add(fps);

        var cursor = AddToggleRow(videoCard, 52, 48, "Fare imlecini göster", "Video kaydında fare hareketlerini dahil et", _settings.RecordingIncludeCursor);

        page.Controls.Add(fileCard);
        page.Controls.Add(videoCard);
        return (folder, format, pattern, fps, cursor);
    }

    // ----------------------------------------------------------------- Page 4: Görünüm & Çubuk

    private (
        ColorSwatchPill ColorSwatch,
        LinearStepper Stroke,
        LinearStepper FontSize,
        LinearStepper Block,
        UiFactory.AppleToggleSwitch BarSerial,
        UiFactory.AppleToggleSwitch BarUndoRedo,
        UiFactory.AppleToggleSwitch BarCrop,
        UiFactory.AppleToggleSwitch BarLong,
        UiFactory.AppleToggleSwitch BarRecord,
        UiFactory.AppleToggleSwitch BarOcr,
        UiFactory.AppleToggleSwitch BarPin)
        BuildAppearancePage(Panel page)
    {
        AddSectionCaption(page, "Çizim Varsayılanları", PageMargin, 2);

        var drawCard = new GroupedCard(new[] { 56 }, verticalSplitX: CardWidth / 2)
        {
            Location = new Point(PageMargin, 24),
            Size = new Size(CardWidth, 112),
        };

        var half = CardWidth / 2;
        AddRowLabels(drawCard, 18, 19, "Varsayılan renk", null);
        var swatch = new ColorSwatchPill(_color)
        {
            Location = new Point(half - 18 - 64, 14),
            Size = new Size(64, 28),
        };
        swatch.Click += (_, _) => PickColor();
        drawCard.Controls.Add(swatch);

        AddRowLabels(drawCard, half + 18, 19, "Çizgi kalınlığı", null);
        var stroke = new LinearStepper(1, 60, _settings.DefaultStrokeWidth, "px")
        {
            Location = new Point(CardWidth - 18 - 132, 14),
            Size = new Size(132, 28),
        };
        drawCard.Controls.Add(stroke);

        AddRowLabels(drawCard, 18, 75, "Metin boyutu", null);
        var fontSize = new LinearStepper(8, 200, _settings.DefaultFontSize, "pt")
        {
            Location = new Point(half - 18 - 132, 70),
            Size = new Size(132, 28),
        };
        drawCard.Controls.Add(fontSize);

        AddRowLabels(drawCard, half + 18, 75, "Mozaik bloğu", null);
        var block = new LinearStepper(4, 80, _settings.MosaicBlockSize, "px")
        {
            Location = new Point(CardWidth - 18 - 132, 70),
            Size = new Size(132, 28),
        };
        drawCard.Controls.Add(block);

        AddSectionCaption(page, "Yüzen Araç Çubuğu (Dinamik Ada)", PageMargin, 154);

        var barCard = new GroupedCard(new[] { 44, 44, 44 }, verticalSplitX: half)
        {
            Location = new Point(PageMargin, 176),
            Size = new Size(CardWidth, 176),
        };

        var barSerial = AddCompactToggleCell(barCard, 0, 0, half, "Numaralandırma (N)", _settings.ShowToolbarSerial);
        var barUndoRedo = AddCompactToggleCell(barCard, 0, 44, half, "Geri / İleri al", _settings.ShowToolbarUndoRedo);
        var barCrop = AddCompactToggleCell(barCard, 0, 88, half, "Seçime kırp", _settings.ShowToolbarCrop);
        var barLong = AddCompactToggleCell(barCard, 0, 132, half, "Uzun yakalama (Ctrl+L)", _settings.ShowToolbarLongCapture);

        var barRecord = AddCompactToggleCell(barCard, half, 0, half, "Video / GIF kayıt (Ctrl+G)", _settings.ShowToolbarRecord);
        var barOcr = AddCompactToggleCell(barCard, half, 44, half, "Metni çıkart (Shift+C)", _settings.ShowToolbarOcr);
        var barPin = AddCompactToggleCell(barCard, half, 88, half, "Ekrana sabitle (Ctrl+T)", _settings.ShowToolbarPin);

        var barNote = MakeLabel("Gizlenen araçlara '...' menüsünden erişilir.", Theme.SmallFont, Theme.MutedText, Theme.SurfaceCard, half + 18, 146);
        barCard.Controls.Add(barNote);

        page.Controls.Add(drawCard);
        page.Controls.Add(barCard);

        return (swatch, stroke, fontSize, block, barSerial, barUndoRedo, barCrop, barLong, barRecord, barOcr, barPin);
    }

    // ----------------------------------------------------------------- Shared Row Builders

    private static Label MakeLabel(string text, Font font, Color color, Color backColor, int x, int y)
    {
        var label = UiFactory.Label(text);
        label.Font = font;
        label.ForeColor = color;
        label.BackColor = backColor;
        label.Location = new Point(x, y);
        return label;
    }

    private static void AddSectionCaption(Panel page, string text, int x, int y)
        => page.Controls.Add(MakeLabel(text, Theme.SectionFont, Theme.SubText, Theme.WindowBack, x + 4, y));

    private static Label AddRowLabels(Panel card, int x, int y, string title, string? subtitle)
    {
        var titleLbl = MakeLabel(title, Theme.UiFontMedium, Theme.Text, Theme.SurfaceCard, x, y);
        card.Controls.Add(titleLbl);

        if (string.IsNullOrEmpty(subtitle))
        {
            return titleLbl;
        }

        var subLbl = MakeLabel(subtitle, Theme.SmallFont, Theme.MutedText, Theme.SurfaceCard, x, y + 18);
        card.Controls.Add(subLbl);
        return subLbl;
    }

    private static UiFactory.AppleToggleSwitch AddToggleRow(
        Panel card, int rowTop, int rowHeight, string title, string subtitle, bool value)
    {
        AddRowLabels(card, 18, rowTop + Math.Max(4, (rowHeight - 32) / 2), title, subtitle);
        var toggle = UiFactory.Toggle(value);
        toggle.Location = new Point(card.Width - 18 - toggle.Width, rowTop + (rowHeight - toggle.Height) / 2);
        card.Controls.Add(toggle);
        return toggle;
    }

    private static UiFactory.AppleToggleSwitch AddCompactToggleCell(
        Panel card, int colX, int rowTop, int colWidth, string title, bool value)
    {
        var label = MakeLabel(title, Theme.UiFont, Theme.Text, Theme.SurfaceCard, colX + 18, rowTop + 13);
        card.Controls.Add(label);

        var toggle = UiFactory.Toggle(value);
        toggle.Location = new Point(colX + colWidth - 18 - toggle.Width, rowTop + 11);
        card.Controls.Add(toggle);
        return toggle;
    }

    // ----------------------------------------------------------------- Actions & Persistence

    private void TriggerCaptureFromUi(CaptureIntent intent)
    {
        Close();
        _app.StartCaptureFromTray(intent);
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

    private void ResetAllToDefaults()
    {
        var defaults = new Settings();
        defaults.Normalize();

        _capture.Value = Settings.DefaultCaptureHotkey;
        _pin.Value = Settings.DefaultPinHotkey;
        _long.Value = Settings.DefaultLongHotkey;
        _record.Value = Settings.DefaultRecordHotkey;
        _folder.Text = defaults.SaveFolder;
        _format.SelectValue(defaults.ImageFormat);
        _pattern.Text = defaults.FileNamePattern;
        _color = Color.FromArgb(defaults.DefaultColorArgb);
        _colorSwatch.Color = _color;
        _stroke.Value = defaults.DefaultStrokeWidth;
        _fontSize.Value = defaults.DefaultFontSize;
        _block.Value = defaults.MosaicBlockSize;
        _fps.Value = defaults.RecordingFps;
        if (_ocrLanguage.Items.Count > 0) _ocrLanguage.SelectedIndex = 0;
        _startWithWindows.Checked = defaults.StartWithWindows;
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
        _settings.ImageFormat = _format.SelectedValue.Equals("JPG", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
        _settings.FileNamePattern = string.IsNullOrWhiteSpace(_pattern.Text)
            ? "SnapDesk_{yyyy-MM-dd_HH-mm-ss}"
            : _pattern.Text;
        _settings.DefaultColorArgb = _color.ToArgb();
        _settings.DefaultStrokeWidth = _stroke.Value;
        _settings.DefaultFontSize = _fontSize.Value;
        _settings.MosaicBlockSize = _block.Value;
        _settings.RecordingFps = _fps.Value;
        _settings.OcrLanguage = (_ocrLanguage.SelectedItem as LanguageItem)?.Tag ?? string.Empty;
        _settings.StartWithWindows = _startWithWindows.Checked;
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
        StartupRegistration.Sync(_settings.StartWithWindows);
        _app.ApplyHotkeys(notify: true);
        _app.RefreshActiveSessionToolbar();

        _heroCard?.UpdateHotkey(_settings.CaptureHotkey);
        _pinTile?.UpdateHotkey(_settings.PinHotkey);
        _longTile?.UpdateHotkey(_settings.LongCaptureHotkey);
        _recordTile?.UpdateHotkey(_settings.RecordHotkey);

        Close();
    }

    public void ShowHotkeyStatus(IReadOnlyList<HotkeyManager.Registration> failures)
    {
        if (IsDisposed)
        {
            return;
        }

        _hotkeyStatus.Text = failures.Count == 0
            ? "Tüm kısayollar etkin."
            : "Kaydedilemeyen: " + string.Join(", ", failures.Select(f => f.Shortcut));
        _hotkeyStatus.ForeColor = failures.Count == 0 ? Theme.MutedText : Color.FromArgb(245, 130, 100);
    }

    public void ShowHotkeysSuspended()
    {
        if (IsDisposed)
        {
            return;
        }

        _hotkeyStatus.Text = "Yeni kısayol atamak için tuş kombinasyonuna basın (Esc: iptal)…";
        _hotkeyStatus.ForeColor = Theme.AppleBlueHover;
    }

    private sealed record LanguageItem(string Tag, string Label)
    {
        public override string ToString() => Label;
    }

    // ----------------------------------------------------------------- Custom Controls

    private static Color BlendColor(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * t),
            (int)Math.Round(from.R + (to.R - from.R) * t),
            (int)Math.Round(from.G + (to.G - from.G) * t),
            (int)Math.Round(from.B + (to.B - from.B) * t));
    }

    private sealed class ExitAppPill : Control
    {
        private bool _hovered;
        private bool _pressed;
        private float _hoverAnim;
        private readonly System.Windows.Forms.Timer _animTimer;
        public event Action? ExitClicked;

        public ExitAppPill()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            Size = new Size(88, 30);
            BackColor = Theme.WindowBack;
            Cursor = Cursors.Hand;
            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (_, _) =>
            {
                var target = _hovered ? 1f : 0f;
                var next = _hoverAnim + (target - _hoverAnim) * 0.28f;
                if (Math.Abs(target - next) < 0.02f)
                {
                    _hoverAnim = target;
                    _animTimer.Stop();
                }
                else
                {
                    _hoverAnim = next;
                }
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            _animTimer.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            _animTimer.Start();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var trigger = _pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            _pressed = false;
            Invalidate();
            if (trigger)
            {
                ExitClicked?.Invoke();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.WindowBack);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            var fillBase = Color.FromArgb(19, 20, 24);
            var fillHover = Color.FromArgb(38, 22, 24);
            var fillPress = Color.FromArgb(48, 20, 22);
            var fill = _pressed ? fillPress : BlendColor(fillBase, fillHover, _hoverAnim);

            var border = BlendColor(
                Color.FromArgb(26, 255, 255, 255),
                Color.FromArgb(110, 255, 92, 82),
                _hoverAnim);

            Theme.FillRounded(g, rect, 15f, fill);
            Theme.DrawRounded(g, rect, 15f, border, 1f);

            var exitColor = BlendColor(Theme.SubText, Color.FromArgb(255, 138, 132), _hoverAnim);
            Icons.Draw(g, IconKind.Exit, new RectangleF(13f, (Height - 13f) / 2f, 13f, 13f), exitColor, 1.45f);
            Theme.DrawText(
                g, "Çıkış", Theme.SmallFont,
                new RectangleF(32f, 0f, Width - 42f, Height),
                exitColor, StringAlignment.Near, StringAlignment.Center);
        }
    }

    private sealed class SidebarTab : Control
    {
        private readonly string _text;
        private readonly IconKind _icon;
        private readonly bool _dangerOnHover;
        private readonly System.Windows.Forms.Timer _animTimer;
        private bool _active;
        private bool _hovered;
        private float _hoverAnim;

        public SidebarTab(string text, IconKind icon, bool dangerOnHover = false)
        {
            _text = text;
            _icon = icon;
            _dangerOnHover = dangerOnHover;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.ToolbarBack;
            Cursor = Cursors.Hand;
            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (_, _) =>
            {
                var target = _hovered ? 1f : 0f;
                var next = _hoverAnim + (target - _hoverAnim) * 0.28f;
                if (Math.Abs(target - next) < 0.02f)
                {
                    _hoverAnim = target;
                    _animTimer.Stop();
                }
                else
                {
                    _hoverAnim = next;
                }
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value) return;
                _active = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            _animTimer.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _animTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.ToolbarBack);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            if (_active)
            {
                Theme.FillRounded(g, rect, 8f, Color.FromArgb(28, 29, 36));
                Theme.DrawRounded(g, rect, 8f, Color.FromArgb(30, 255, 255, 255), 1f);
                Theme.FillRounded(g, new RectangleF(3f, 8f, 3f, Height - 16f), 1.5f, Theme.AppleBlue);
            }
            else if (_hoverAnim > 0.01f)
            {
                var alpha = (int)Math.Round((_dangerOnHover ? 34 : 18) * _hoverAnim);
                var hoverFill = _dangerOnHover
                    ? Color.FromArgb(alpha, 255, 69, 58)
                    : Color.FromArgb(alpha, 255, 255, 255);
                Theme.FillRounded(g, rect, 8f, hoverFill);
            }

            var iconColor = _active
                ? Theme.Accent
                : _dangerOnHover
                    ? BlendColor(Theme.SubText, Color.FromArgb(255, 105, 97), _hoverAnim)
                    : BlendColor(Theme.SubText, Theme.Text, _hoverAnim * 0.65f);

            var textColor = _active
                ? Theme.Text
                : _dangerOnHover
                    ? BlendColor(Theme.SubText, Color.FromArgb(255, 145, 138), _hoverAnim)
                    : BlendColor(Theme.SubText, Theme.Text, _hoverAnim);

            Icons.Draw(g, _icon, new RectangleF(13f, (Height - 16f) / 2f, 16f, 16f), iconColor, 1.5f);
            Theme.DrawText(
                g, _text, _active ? Theme.UiFontBold : Theme.UiFontMedium,
                new RectangleF(37, 0, Math.Max(10, Width - 43), Height), textColor,
                StringAlignment.Near, StringAlignment.Center);
        }
    }

    private sealed class GroupedCard : Panel
    {
        private readonly int[] _rowHeights;
        private readonly int _verticalSplitX;

        public GroupedCard(int[] rowHeights, int verticalSplitX = 0)
        {
            _rowHeights = rowHeights;
            _verticalSplitX = verticalSplitX;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.SurfaceCard;
            BorderStyle = BorderStyle.None;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.WindowBack);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(g, rect, 12f, Theme.SurfaceCard);

            using var linePen = new Pen(Color.FromArgb(16, 255, 255, 255), 1f);
            var y = 0;
            foreach (var h in _rowHeights)
            {
                y += h;
                if (y > 0 && y < Height)
                {
                    g.DrawLine(linePen, 16, y, Width - 17, y);
                }
            }

            if (_verticalSplitX > 0 && _verticalSplitX < Width)
            {
                g.DrawLine(linePen, _verticalSplitX, 12, _verticalSplitX, Height - 13);
            }

            using var specPen = new Pen(Color.FromArgb(22, 255, 255, 255), 1f);
            g.DrawLine(specPen, rect.Left + 12f, rect.Top + 0.5f, Math.Max(rect.Left + 12f, rect.Right - 12f), rect.Top + 0.5f);

            Theme.DrawRounded(g, rect, 12f, Theme.CardBorder, 1f);
            base.OnPaint(e);
        }
    }

    private sealed class HeroCaptureCard : Panel
    {
        private string _hotkey;
        private bool _hovered;
        private bool _pressed;
        private float _hoverAnim;
        private readonly System.Windows.Forms.Timer _animTimer;
        public event Action? ActionTriggered;

        public HeroCaptureCard(string hotkey)
        {
            _hotkey = hotkey;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.SurfaceCard;
            Cursor = Cursors.Hand;
            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (_, _) =>
            {
                var target = _hovered ? 1f : 0f;
                var next = _hoverAnim + (target - _hoverAnim) * 0.26f;
                if (Math.Abs(target - next) < 0.02f)
                {
                    _hoverAnim = target;
                    _animTimer.Stop();
                }
                else
                {
                    _hoverAnim = next;
                }
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        public void UpdateHotkey(string hotkey)
        {
            _hotkey = hotkey;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; _animTimer.Start(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; _pressed = false; _animTimer.Start(); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var trigger = _pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            _pressed = false;
            Invalidate();
            if (trigger)
            {
                ActionTriggered?.Invoke();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.WindowBack);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            var cardFill = _pressed
                ? Color.FromArgb(15, 16, 20)
                : BlendColor(Theme.SurfaceCard, Color.FromArgb(24, 26, 33), _hoverAnim);
            var cardBorder = BlendColor(
                Color.FromArgb(52, 10, 132, 255),
                Color.FromArgb(135, 10, 132, 255),
                _hoverAnim);

            Theme.FillRounded(g, rect, 12f, cardFill);
            Theme.DrawRounded(g, rect, 12f, cardBorder, 1f);

            var iconBox = new RectangleF(18f, 22f, 44f, 44f);
            var boxFill = BlendColor(Color.FromArgb(28, 10, 132, 255), Color.FromArgb(46, 10, 132, 255), _hoverAnim);
            Theme.FillRounded(g, iconBox, 10f, boxFill);
            Theme.DrawRounded(g, iconBox, 10f, Color.FromArgb(60, 10, 132, 255), 1f);
            Icons.Draw(g, IconKind.Crop, RectangleF.Inflate(iconBox, -11f, -11f), Theme.Accent, 1.8f);

            var titleFont = Theme.Ui(11f, FontStyle.Bold);
            Theme.DrawText(
                g, "Ekran Yakala", titleFont,
                new RectangleF(76, 19, 220, 22), Theme.Text,
                StringAlignment.Near, StringAlignment.Center);

            if (!string.IsNullOrWhiteSpace(_hotkey))
            {
                var titleW = Theme.MeasureTextWidth(g, "Ekran Yakala", titleFont);
                var badgeW = Theme.MeasureTextWidth(g, _hotkey, Theme.SmallFont) + 16f;
                var badgeRect = new RectangleF(76f + titleW + 12f, 20f, badgeW, 20f);
                Theme.FillRounded(g, badgeRect, 5f, Color.FromArgb(30, 31, 38));
                Theme.DrawRounded(g, badgeRect, 5f, Color.FromArgb(26, 255, 255, 255), 1f);
                Theme.DrawText(g, _hotkey, Theme.SmallFont, badgeRect, Theme.SubText, StringAlignment.Center, StringAlignment.Center);
            }

            Theme.DrawText(
                g, "Bölge seçin, pencere yakalayın veya düzenleyin", Theme.UiFont,
                new RectangleF(76, 43, 336, 22), Theme.SubText,
                StringAlignment.Near, StringAlignment.Center);
        }
    }

    private sealed class BentoTile : Control
    {
        private readonly IconKind _icon;
        private readonly string _title;
        private readonly string _subtitle;
        private string _hotkey;
        private bool _hovered;
        private bool _pressed;
        private float _hoverAnim;
        private readonly System.Windows.Forms.Timer _animTimer;

        public BentoTile(IconKind icon, string title, string subtitle, string hotkey)
        {
            _icon = icon;
            _title = title;
            _subtitle = subtitle;
            _hotkey = hotkey;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.WindowBack;
            Cursor = Cursors.Hand;
            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (_, _) =>
            {
                var target = _hovered ? 1f : 0f;
                var next = _hoverAnim + (target - _hoverAnim) * 0.26f;
                if (Math.Abs(target - next) < 0.02f)
                {
                    _hoverAnim = target;
                    _animTimer.Stop();
                }
                else
                {
                    _hoverAnim = next;
                }
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        public void UpdateHotkey(string hotkey)
        {
            _hotkey = hotkey;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; _animTimer.Start(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; _pressed = false; _animTimer.Start(); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.WindowBack);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            var fill = _pressed
                ? Color.FromArgb(14, 14, 17)
                : BlendColor(Theme.SurfaceCard, Color.FromArgb(24, 25, 31), _hoverAnim);
            var border = BlendColor(Theme.CardBorder, Color.FromArgb(50, 255, 255, 255), _hoverAnim);

            Theme.FillRounded(g, rect, 12f, fill);
            Theme.DrawRounded(g, rect, 12f, border, 1f);

            var iconBox = new RectangleF(14f, 16f, 32f, 32f);
            Theme.FillRounded(g, iconBox, 8f, BlendColor(Color.FromArgb(28, 29, 36), Color.FromArgb(34, 38, 48), _hoverAnim));
            Icons.Draw(g, _icon, RectangleF.Inflate(iconBox, -7f, -7f), BlendColor(Theme.Text, Theme.Accent, _hoverAnim), 1.6f);

            if (!string.IsNullOrWhiteSpace(_hotkey))
            {
                var badgeW = Theme.MeasureTextWidth(g, _hotkey, Theme.SmallFont) + 14f;
                var badgeRect = new RectangleF(Width - 14f - badgeW, 17f, badgeW, 20f);
                Theme.FillRounded(g, badgeRect, 5f, Color.FromArgb(26, 27, 34));
                Theme.DrawRounded(g, badgeRect, 5f, Color.FromArgb(22, 255, 255, 255), 1f);
                Theme.DrawText(g, _hotkey, Theme.SmallFont, badgeRect, Theme.SubText, StringAlignment.Center, StringAlignment.Center);
            }

            Theme.DrawText(
                g, _title, Theme.UiFontBold,
                new RectangleF(56, 17, Width - 126, 20), Theme.Text,
                StringAlignment.Near, StringAlignment.Center);

            Theme.DrawText(
                g, _subtitle, Theme.SmallFont,
                new RectangleF(56, 39, Width - 70, 20), Theme.MutedText,
                StringAlignment.Near, StringAlignment.Center, ellipsis: true);
        }
    }

    private sealed class LinearStepper : Control
    {
        private readonly int _min;
        private readonly int _max;
        private readonly string _unit;
        private int _value;
        private int _hoverZone;

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

        protected override void OnPaintBackground(PaintEventArgs e) { }

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
            if (e.X < 32) Value -= step;
            else if (e.X > Width - 32) Value += step;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceCard);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(g, rect, 7f, Theme.SurfaceInput);
            Theme.DrawRounded(g, rect, 7f, Theme.InputBorder, 1f);

            if (_hoverZone == -1) Theme.FillRounded(g, new RectangleF(1.5f, 1.5f, 29f, Height - 3f), 6f, Theme.Hover);
            else if (_hoverZone == 1) Theme.FillRounded(g, new RectangleF(Width - 30.5f, 1.5f, 29f, Height - 3f), 6f, Theme.Hover);

            Theme.DrawText(g, "−", Theme.UiFontBold, new RectangleF(0, 0, 32, Height),
                _value > _min ? Theme.Text : Theme.Disabled, StringAlignment.Center, StringAlignment.Center);

            Theme.DrawText(g, $"{_value} {_unit}", Theme.UiFontMedium, new RectangleF(32, 0, Width - 64, Height),
                Theme.Text, StringAlignment.Center, StringAlignment.Center);

            Theme.DrawText(g, "+", Theme.UiFontBold, new RectangleF(Width - 32, 0, 32, Height),
                _value < _max ? Theme.Text : Theme.Disabled, StringAlignment.Center, StringAlignment.Center);
        }
    }

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

        protected override void OnPaintBackground(PaintEventArgs e) { }

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

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }

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

        protected override void OnPaintBackground(PaintEventArgs e) { }

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

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }
        protected override bool IsInputKey(Keys keyData) => true;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            if (e.KeyCode is Keys.Back or Keys.Delete) { Value = string.Empty; return; }
            if (e.KeyCode == Keys.Escape) { Parent?.Focus(); return; }

            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            var winDown = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
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
                : _hovered ? Theme.SurfaceHover : Theme.SurfaceInput;
            var border = Focused
                ? Theme.AppleBlue
                : _hovered ? Color.FromArgb(80, 255, 255, 255) : Theme.InputBorder;

            Theme.FillRounded(g, rect, 7f, fill);
            Theme.DrawRounded(g, rect, 7f, border, Focused ? 1.4f : 1f);

            var display = string.IsNullOrWhiteSpace(_value)
                ? (Focused ? "Tuş kombinasyonu…" : "Atanmadı")
                : _value;
            var textColor = string.IsNullOrWhiteSpace(_value) ? Theme.MutedText : Theme.Text;

            Theme.DrawText(g, display, Font, ClientRectangle, textColor, StringAlignment.Center, StringAlignment.Center);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
