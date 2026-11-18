using System.Diagnostics;
using SnapDesk.UI;

namespace SnapDesk.Editor;

/// <summary>
/// Seçimin altında açılan iki satırlık düzenleme çubuğu: üstte araçlar,
/// altta seçili aracın seçenekleri. Pencere odağı çalmaz (WS_EX_NOACTIVATE),
/// böylece kaplama klavye kısayollarını almaya devam eder.
/// </summary>
public sealed class EditorToolbarForm : Form
{
    private enum HitKind
    {
        Icon,
        Text,
        Swatch,
        Label,
        Separator,
        Slider,
        Check,
    }

    private sealed class Hit
    {
        public required string Id { get; init; }
        public required Rectangle Bounds { get; init; }
        public HitKind Kind { get; init; } = HitKind.Icon;
        public IconKind? Icon { get; init; }
        public string? Text { get; init; }
        public Color? SwatchColor { get; init; }
        public string Tip { get; init; } = string.Empty;
        public bool Active { get; init; }
        public bool Enabled { get; init; } = true;
        public bool Chevron { get; init; }
        public int Value { get; init; }
        public int MaxValue { get; init; } = 100;
    }

    private readonly CaptureSession _session;
    private readonly List<Hit> _hits = new();
    private readonly List<Rectangle> _groupBoxes = new();
    private readonly ToolTip _tip;
    private readonly System.Windows.Forms.Timer _tipTimer;
    private float _scale = 1f;
    private string? _hoverId;
    private string? _sliderId;
    private string _pendingTip = string.Empty;
    private bool _draggingForm;
    private Point _dragOffset;
    private bool _userPositioned;
    private bool _menuOpen;

    private int S(int value) => (int)Math.Round(value * _scale);

    public EditorToolbarForm(CaptureSession session)
    {
        _session = session;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.ToolbarBack;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        _tip = new ToolTip
        {
            OwnerDraw = true,
            ShowAlways = true,
            InitialDelay = 420,
            ReshowDelay = 80,
            AutoPopDelay = 6000,
            BackColor = Theme.ToolbarBack,
            ForeColor = Theme.Text,
        };

        _tip.Popup += (_, e) =>
        {
            var text = string.IsNullOrEmpty(_pendingTip) ? " " : _pendingTip;
            var size = TextRenderer.MeasureText(text, Theme.UiFont);
            e.ToolTipSize = new Size(size.Width + 18, size.Height + 12);
        };

        _tip.Draw += (_, e) =>
        {
            using var back = new SolidBrush(Color.FromArgb(245, Theme.ToolbarBack));
            e.Graphics.FillRectangle(back, e.Bounds);
            using var pen = new Pen(Theme.ToolbarBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(
                e.Graphics, e.ToolTipText, Theme.UiFont, e.Bounds, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        _tipTimer = new System.Windows.Forms.Timer { Interval = 380 };
        _tipTimer.Tick += (_, _) =>
        {
            _tipTimer.Stop();
            if (IsDisposed || !Visible || _hoverId is null || string.IsNullOrEmpty(_pendingTip))
            {
                return;
            }

            var hit = _hits.FirstOrDefault(h => h.Id == _hoverId);
            if (hit is null)
            {
                return;
            }

            // Üst satır ipuçlarını çubuğun üstünde, alt satır ipuçlarını çubuğun altında göster
            // ki ikinci satırdaki seçeneklerin üzeri hiçbir zaman kapanmasın.
            var tipY = hit.Bounds.Top < S(44) ? -S(32) : Height + S(4);
            _tip.Show(_pendingTip, this, new Point(hit.Bounds.Left, tipY), 5000);
        };

        RebuildLayout();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    public bool UserPositioned => _userPositioned;

    /// <summary>Seçim yeniden oluşturulduğunda/taşındığında konumu yeniden hesapla.</summary>
    public void ResetUserPosition() => _userPositioned = false;

    public void ScaleBy(int direction)
    {
        _scale = Math.Clamp(_scale + direction * 0.1f, 0.8f, 1.5f);
        OnSessionChanged();
    }

    public void OnSessionChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        RebuildLayout();
        Invalidate();
        _session.PositionToolbar();
    }

    private void UpdateFormRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        var cornerRadius = S(18);
        using var path = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), cornerRadius);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateFormRegion();
    }

    // ----------------------------------------------------------------- düzen

    private void RebuildLayout()
    {
        _hits.Clear();
        _groupBoxes.Clear();

        var settings = _session.Settings;
        var pad = S(8);
        var btn = S(36);
        var gap = S(3);
        var handle = S(14);
        var sep = S(10);

        var row1Height = S(50);
        var x = pad;
        var y = S(7);

        // Taşıma tutamacı
        _hits.Add(new Hit
        {
            Id = "drag",
            Bounds = new Rectangle(x, y, handle, btn),
            Icon = IconKind.DragHandle,
            Tip = "Araç çubuğunu taşı",
        });
        x += handle + gap;

        // --- Grup 1: Çizim Araçları ---
        _hits.Add(Button("tool:pointer", IconKind.Pointer, "Seç / Taşı (V)", x, y, btn, _session.Tool == ToolKind.Pointer));
        x += btn + gap;

        _hits.Add(Button("tool:rect", IconForShape(), _session.ShapeIsEllipse ? "Elips (O) — Tıkla / Sağ tıkla" : "Dikdörtgen (R) — Tıkla / Sağ tıkla", x, y, btn, _session.Tool is ToolKind.Rectangle or ToolKind.Ellipse));
        x += btn + gap;

        _hits.Add(Button("tool:pen", _session.PenIsHighlighter ? IconKind.Highlighter : IconKind.Pen, _session.PenIsHighlighter ? "Fosforlu kalem (H) — Tıkla / Sağ tıkla" : "Kalem (P) — Tıkla / Sağ tıkla", x, y, btn, _session.Tool is ToolKind.Pen or ToolKind.Highlighter));
        x += btn + gap;

        _hits.Add(Button("tool:arrow", _session.ArrowIsLine ? IconKind.Line : IconKind.Arrow, _session.ArrowIsLine ? "Çizgi (L) — Tıkla / Sağ tıkla" : "Ok (A) — Tıkla / Sağ tıkla", x, y, btn, _session.Tool is ToolKind.Arrow or ToolKind.Line));
        x += btn + gap;

        _hits.Add(Button("tool:text", IconKind.Text, "Metin (T)", x, y, btn, _session.Tool == ToolKind.Text));
        x += btn + gap;

        if (settings.ShowToolbarSerial)
        {
            _hits.Add(Button("tool:serial", IconKind.Serial, "Numaralandırma (N)", x, y, btn, _session.Tool == ToolKind.Serial));
            x += btn + gap;
        }

        _hits.Add(Button("tool:mosaic", _session.MosaicBlur ? IconKind.Blur : IconKind.Mosaic, _session.MosaicBlur ? "Bulanıklaştır (B) — Tıkla / Sağ tıkla" : "Mozaik (M) — Tıkla / Sağ tıkla", x, y, btn, _session.Tool is ToolKind.Mosaic or ToolKind.Blur));
        x += btn + gap;

        _hits.Add(Button("tool:eraser", IconKind.Eraser, "Silgi (X) — çizime tıkla", x, y, btn, _session.Tool == ToolKind.Eraser));
        x += btn + gap;

        // --- Grup 2: Geçmiş & Kırpma (Ayarlara bağlı) ---
        if (settings.ShowToolbarUndoRedo || settings.ShowToolbarCrop)
        {
            _hits.Add(Separator(x, y, sep, btn));
            x += sep + gap;

            if (settings.ShowToolbarUndoRedo)
            {
                _hits.Add(new Hit
                {
                    Id = "undo",
                    Bounds = new Rectangle(x, y, btn, btn),
                    Icon = IconKind.Undo,
                    Tip = "Geri al (Ctrl+Z)",
                    Enabled = _session.CanUndo,
                });
                x += btn + gap;

                _hits.Add(new Hit
                {
                    Id = "redo",
                    Bounds = new Rectangle(x, y, btn, btn),
                    Icon = IconKind.Redo,
                    Tip = "İleri al (Ctrl+Y)",
                    Enabled = _session.CanRedo,
                });
                x += btn + gap;
            }

            if (settings.ShowToolbarCrop)
            {
                _hits.Add(Button("crop", IconKind.Crop, "Seçime kırp", x, y, btn, false));
                x += btn + gap;
            }
        }

        // --- Grup 3: Genişletilmiş Araçlar (Ayarlara bağlı) ---
        if (settings.ShowToolbarLongCapture || settings.ShowToolbarRecord || settings.ShowToolbarOcr || settings.ShowToolbarPin)
        {
            _hits.Add(Separator(x, y, sep, btn));
            x += sep + gap;

            if (settings.ShowToolbarLongCapture)
            {
                _hits.Add(Button("long", IconKind.LongCapture, "Uzun yakalama (Ctrl+L)", x, y, btn, false));
                x += btn + gap;
            }

            if (settings.ShowToolbarRecord)
            {
                _hits.Add(Button("video", IconKind.Video, "Video / GIF kaydet (Ctrl+G)", x, y, btn, false));
                x += btn + gap;
            }

            if (settings.ShowToolbarOcr)
            {
                _hits.Add(Button("ocr", IconKind.Ocr, "Metni tanı (Shift+C)", x, y, btn, false));
                x += btn + gap;
            }

            if (settings.ShowToolbarPin)
            {
                _hits.Add(Button("pin", IconKind.Pin, "Ekrana sabitle (Ctrl+T)", x, y, btn, false));
                x += btn + gap;
            }
        }

        // --- Grup 4: Eylemler & Kopyalama ---
        _hits.Add(Separator(x, y, sep, btn));
        x += sep + gap;

        _hits.Add(Button("save", IconKind.Save, "Kaydet (Ctrl+S)", x, y, btn, false));
        x += btn + gap;

        _hits.Add(Button("close", IconKind.Close, "İptal (Esc)", x, y, btn, false));
        x += btn + gap;

        // Apple Primary Accent Capsule: Kopyala
        var copyWidth = S(84);
        _hits.Add(new Hit
        {
            Id = "copy",
            Bounds = new Rectangle(x, y, copyWidth, btn),
            Icon = IconKind.Copy,
            Text = "Kopyala",
            Tip = "Panoya kopyala ve kapat (Ctrl+C)",
        });
        x += copyWidth + gap;

        _hits.Add(Button("more", IconKind.More, "Daha fazla", x, y, btn, false));
        x += btn + pad;

        var width = x;

        // ---- ikinci satır: araca veya seçili çizime özel seçenekler
        var optionsTool = EffectiveOptionsTool();
        var row2Visible = optionsTool != ToolKind.Pointer && optionsTool != ToolKind.Eraser;
        var row2Height = S(42);

        if (row2Visible)
        {
            var ox = pad + S(2);
            var oy = row1Height + S(6);
            var oh = S(28);

            switch (optionsTool)
            {
                case ToolKind.Rectangle:
                case ToolKind.Ellipse:
                    ox = BuildShapeOptions(ox, oy, oh, gap);
                    break;

                case ToolKind.Pen:
                case ToolKind.Highlighter:
                    ox = BuildStrokeOptions(ox, oy, oh, gap, includeStyle: false);
                    break;

                case ToolKind.Arrow:
                case ToolKind.Line:
                    ox = BuildStrokeOptions(ox, oy, oh, gap, includeStyle: true);
                    break;

                case ToolKind.Text:
                    ox = BuildTextOptions(ox, oy, oh, gap);
                    break;

                case ToolKind.Mosaic:
                case ToolKind.Blur:
                    ox = BuildMosaicOptions(ox, oy, oh, gap);
                    break;

                case ToolKind.Serial:
                    ox = BuildSerialOptions(ox, oy, oh, gap);
                    break;
            }

            width = Math.Max(width, ox + pad);
        }

        var height = row1Height + (row2Visible ? row2Height : 0);

        if (ClientSize.Width != width || ClientSize.Height != height)
        {
            ClientSize = new Size(width, height);
        }

        UpdateFormRegion();
    }

    private ToolKind EffectiveOptionsTool()
    {
        if (_session.Tool != ToolKind.Pointer)
        {
            return _session.Tool;
        }

        return _session.SelectedAnnotation switch
        {
            RectangleAnnotation r => r.IsEllipse ? ToolKind.Ellipse : ToolKind.Rectangle,
            PenAnnotation p => p.IsHighlighter ? ToolKind.Highlighter : ToolKind.Pen,
            ArrowAnnotation a => a.ShowHead ? ToolKind.Arrow : ToolKind.Line,
            TextAnnotation => ToolKind.Text,
            SerialAnnotation => ToolKind.Serial,
            MosaicAnnotation m => m.IsBlur ? ToolKind.Blur : ToolKind.Mosaic,
            _ => ToolKind.Pointer,
        };
    }

    private IconKind IconForShape() => _session.ShapeIsEllipse ? IconKind.Ellipse : IconKind.Rectangle;

    private static Hit Button(string id, IconKind icon, string tip, int x, int y, int size, bool active)
        => new()
        {
            Id = id,
            Bounds = new Rectangle(x, y, size, size),
            Icon = icon,
            Tip = tip,
            Active = active,
        };

    private static Hit Separator(int x, int y, int width, int height)
        => new()
        {
            Id = "sep",
            Bounds = new Rectangle(x, y, width, height),
            Kind = HitKind.Separator,
        };

    private int BuildShapeOptions(int x, int y, int height, int gap)
    {
        var isEllipse = _session.SelectedAnnotation is RectangleAnnotation selRect
            ? selRect.IsEllipse
            : _session.ShapeIsEllipse;

        x = AddSegmented(x, y, height, "opt:shapeRect", "Kare", !isEllipse);
        x += gap;
        x = AddSegmented(x, y, height, "opt:shapeEllipse", "Elips", isEllipse);
        x += S(4);

        x = AddCheck(x, y, height, "opt:fill", "Dolgu", _session.Filled);
        x = AddRow2Separator(x, y, height);

        x = AddStyleButtons(x, y, height, gap);
        x = AddRow2Separator(x, y, height);

        x = AddLabel(x, y, height, "Kalınlık");
        x = AddSpinner(x, y, height, "opt:widthDec", _session.CurrentWidth.ToString("0"), "opt:widthInc");
        x += S(6);

        x = AddLabel(x, y, height, "Köşe");
        x = AddSpinner(x, y, height, "opt:radiusDec", _session.CornerRadius.ToString("0"), "opt:radiusInc");
        x = AddRow2Separator(x, y, height);

        x = AddPalette(x, y, height, gap);
        return x;
    }

    private int BuildStrokeOptions(int x, int y, int height, int gap, bool includeStyle)
    {
        if (!includeStyle)
        {
            var isHighlighter = _session.SelectedAnnotation is PenAnnotation selPen
                ? selPen.IsHighlighter
                : _session.Tool == ToolKind.Highlighter || _session.PenIsHighlighter;

            x = AddSegmented(x, y, height, "opt:modePen", "Kalem", !isHighlighter);
            x += gap;
            x = AddSegmented(x, y, height, "opt:modeHighlighter", "Fosforlu", isHighlighter);
            x = AddRow2Separator(x, y, height);
        }

        x = AddLabel(x, y, height, "Kalınlık");
        x = AddSpinner(x, y, height, "opt:widthDec", _session.CurrentWidth.ToString("0"), "opt:widthInc");

        var sliderWidth = S(104);
        _hits.Add(new Hit
        {
            Id = "opt:widthSlider",
            Bounds = new Rectangle(x + S(6), y + height / 2 - S(5), sliderWidth, S(10)),
            Kind = HitKind.Slider,
            Tip = "Kalınlık (fare tekerleği)",
            Value = (int)_session.CurrentWidth,
            MaxValue = 60,
        });
        x += sliderWidth + S(10);
        x = AddRow2Separator(x, y, height);

        if (includeStyle)
        {
            x = AddLabel(x, y, height, "Stil");
            x = AddStyleButtons(x, y, height, gap);
            x = AddRow2Separator(x, y, height);
        }

        x = AddPalette(x, y, height, gap);
        return x;
    }

    private int BuildTextOptions(int x, int y, int height, int gap)
    {
        x = AddToggle(x, y, height, "opt:bold", "B", _session.Bold, bold: true);
        x += gap;
        x = AddToggle(x, y, height, "opt:italic", "I", _session.Italic, italic: true);
        x += gap;
        x = AddToggle(x, y, height, "opt:outline", "Dış çizgi", _session.Outline);
        x = AddRow2Separator(x, y, height);

        var fontWidth = S(132);
        _hits.Add(new Hit
        {
            Id = "opt:font",
            Bounds = new Rectangle(x, y, fontWidth, height),
            Kind = HitKind.Text,
            Text = _session.FontFamily,
            Tip = "Yazı tipi",
            Chevron = true,
        });
        x += fontWidth;
        x = AddRow2Separator(x, y, height);

        x = AddLabel(x, y, height, "Boyut");
        x = AddSpinner(x, y, height, "opt:sizeDec", _session.CurrentFontSize.ToString("0"), "opt:sizeInc");
        x = AddRow2Separator(x, y, height);

        x = AddPalette(x, y, height, gap);
        return x;
    }

    private int BuildMosaicOptions(int x, int y, int height, int gap)
    {
        x = AddSegmented(x, y, height, "opt:modeMosaic", "Mozaik", !_session.MosaicBlur);
        x += gap;
        x = AddSegmented(x, y, height, "opt:modeBlur", "Bulanık", _session.MosaicBlur);
        x = AddRow2Separator(x, y, height);

        x = AddLabel(x, y, height, "Blok boyutu");
        x = AddSpinner(x, y, height, "opt:blockDec", _session.MosaicBlock.ToString(), "opt:blockInc");
        return x;
    }

    private int BuildSerialOptions(int x, int y, int height, int gap)
    {
        x = AddLabel(x, y, height, "Boyut");
        x = AddSpinner(x, y, height, "opt:widthDec", _session.CurrentWidth.ToString("0"), "opt:widthInc");
        x = AddRow2Separator(x, y, height);
        x = AddPalette(x, y, height, gap);
        return x;
    }

    private int AddRow2Separator(int x, int y, int height)
    {
        var sepW = S(12);
        _hits.Add(Separator(x, y, sepW, height));
        return x + sepW;
    }

    private int AddLabel(int x, int y, int height, string text)
    {
        using var graphics = CreateGraphics();
        var width = TextRenderer.MeasureText(graphics, text, Theme.SmallFont).Width + S(6);

        _hits.Add(new Hit
        {
            Id = "label",
            Bounds = new Rectangle(x, y, width, height),
            Kind = HitKind.Label,
            Text = text,
            Enabled = false,
        });

        return x + width;
    }

    private int AddSegmented(int x, int y, int height, string id, string text, bool active)
    {
        using var graphics = CreateGraphics();
        var width = TextRenderer.MeasureText(graphics, text, Theme.UiFont).Width + S(16);

        _hits.Add(new Hit
        {
            Id = id,
            Bounds = new Rectangle(x, y, width, height),
            Kind = HitKind.Text,
            Text = text,
            Active = active,
            Tip = text,
        });

        return x + width;
    }

    private int AddCheck(int x, int y, int height, string id, string text, bool active)
    {
        using var graphics = CreateGraphics();
        var width = TextRenderer.MeasureText(graphics, text, Theme.UiFont).Width + S(26);

        _hits.Add(new Hit
        {
            Id = id,
            Bounds = new Rectangle(x, y, width, height),
            Kind = HitKind.Check,
            Text = text,
            Active = active,
            Tip = text,
        });

        return x + width;
    }

    private int AddToggle(int x, int y, int height, string id, string text, bool active, bool bold = false, bool italic = false)
    {
        using var graphics = CreateGraphics();
        using var measureFont = new Font("Segoe UI", 9f, (bold ? FontStyle.Bold : FontStyle.Regular) | (italic ? FontStyle.Italic : FontStyle.Regular));
        var width = Math.Max(S(28), TextRenderer.MeasureText(graphics, text, measureFont).Width + S(14));

        _hits.Add(new Hit
        {
            Id = id,
            Bounds = new Rectangle(x, y, width, height),
            Kind = HitKind.Text,
            Text = text,
            Active = active,
            Tip = text,
        });

        return x + width;
    }

    private int AddStyleButtons(int x, int y, int height, int gap)
    {
        for (var i = 0; i < 3; i++)
        {
            var active = i switch
            {
                0 => _session.Dash == DashStyle.Solid,
                1 => _session.Dash == DashStyle.Dash,
                _ => _session.Dash == DashStyle.Dot,
            };

            _hits.Add(new Hit
            {
                Id = $"opt:style{i}",
                Bounds = new Rectangle(x, y, S(30), height),
                Kind = HitKind.Icon,
                Icon = IconKind.Line,
                Active = active,
                Tip = i switch { 0 => "Düz çizgi", 1 => "Kesikli çizgi", _ => "Noktalı çizgi" },
            });

            x += S(30) + gap;
        }

        return x;
    }

    private int AddSpinner(int x, int y, int height, string decId, string value, string incId)
    {
        var startX = x;
        var btnW = S(22);
        var valueWidth = S(32);

        _hits.Add(new Hit
        {
            Id = decId,
            Bounds = new Rectangle(x, y, btnW, height),
            Kind = HitKind.Text,
            Text = "−",
            Tip = "Azalt",
        });
        x += btnW;

        _hits.Add(new Hit
        {
            Id = "label",
            Bounds = new Rectangle(x, y, valueWidth, height),
            Kind = HitKind.Text,
            Text = value,
            Enabled = false,
        });
        x += valueWidth;

        _hits.Add(new Hit
        {
            Id = incId,
            Bounds = new Rectangle(x, y, btnW, height),
            Kind = HitKind.Text,
            Text = "+",
            Tip = "Artır",
        });
        x += btnW;

        _groupBoxes.Add(new Rectangle(startX, y, x - startX, height));
        return x;
    }

    private int AddPalette(int x, int y, int height, int gap)
    {
        var colors = new[]
        {
            Theme.Red, Theme.Yellow, Theme.Green, Theme.Blue, Theme.Black, Theme.White,
        };

        for (var i = 0; i < colors.Length; i++)
        {
            _hits.Add(new Hit
            {
                Id = $"opt:color{i}",
                Bounds = new Rectangle(x, y + (height - S(20)) / 2, S(20), S(20)),
                Kind = HitKind.Swatch,
                SwatchColor = colors[i],
                Active = _session.CurrentColor.ToArgb() == colors[i].ToArgb(),
                Tip = $"Renk #{colors[i].R:X2}{colors[i].G:X2}{colors[i].B:X2}",
            });

            x += S(20) + S(4);
        }

        _hits.Add(new Hit
        {
            Id = "opt:colorCustom",
            Bounds = new Rectangle(x + S(2), y + (height - S(20)) / 2, S(22), S(20)),
            Kind = HitKind.Swatch,
            SwatchColor = null,
            Tip = "Özel renk…",
        });

        return x + S(26);
    }

    // ----------------------------------------------------------------- çizim

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);

        var cornerRadius = S(18);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        // Apple Obsidian Glass arka plan
        using (var path = Theme.RoundedRect(rect, cornerRadius))
        {
            using var backBrush = new SolidBrush(Color.FromArgb(246, 22, 22, 26));
            g.FillPath(backBrush, path);

            // Dış ince kenar çizgisi (hairline)
            using var borderPen = new Pen(Color.FromArgb(48, 255, 255, 255), 1f);
            g.DrawPath(borderPen, path);
        }

        // Üst kenar speküler parlaklık çizgisi (Liquid Glass hissi)
        using (var specPen = new Pen(Color.FromArgb(32, 255, 255, 255), 1f))
        {
            g.DrawLine(specPen, cornerRadius, 1f, Width - cornerRadius, 1f);
        }

        // İkinci satırı ayıran ince saç teli çizgi
        if (Height > S(52))
        {
            using var line = new Pen(Color.FromArgb(26, 255, 255, 255), 1f);
            g.DrawLine(line, S(12), S(50), Width - S(12), S(50));
        }

        // Sayı artır/azalt kutularının kapsayıcı çerçeveleri
        foreach (var box in _groupBoxes)
        {
            Theme.FillRounded(g, new RectangleF(box.X, box.Y, box.Width, box.Height), S(6), Color.FromArgb(34, 34, 40));
            Theme.DrawRounded(g, new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), S(6), Color.FromArgb(56, 56, 64));
        }

        foreach (var hit in _hits)
        {
            var hover = _hoverId == hit.Id && hit.Kind != HitKind.Label && hit.Kind != HitKind.Separator;
            DrawHit(g, hit, hover);
        }
    }

    private void DrawHit(Graphics g, Hit hit, bool hover)
    {
        var rect = hit.Bounds;

        switch (hit.Kind)
        {
            case HitKind.Separator:
                using (var pen = new Pen(Color.FromArgb(28, 255, 255, 255), 1f))
                {
                    var x = rect.Left + rect.Width / 2;
                    var padY = rect.Height > S(28) ? S(8) : S(4);
                    g.DrawLine(pen, x, rect.Top + padY, x, rect.Bottom - padY);
                }
                return;

            case HitKind.Label:
                TextRenderer.DrawText(g, hit.Text, Theme.SmallFont, rect, Color.FromArgb(165, 165, 175),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                return;

            case HitKind.Swatch:
                DrawSwatch(g, hit, hover);
                return;

            case HitKind.Check:
                DrawCheck(g, hit, hover);
                return;

            case HitKind.Slider:
                DrawSlider(g, hit, hover);
                return;
        }

        // Buton zemini
        var isOpt = hit.Id.StartsWith("opt:", StringComparison.Ordinal);
        var radius = isOpt ? S(6) : S(8);

        // 1) Apple Primary Accent Capsule: Kopyala
        if (hit.Id == "copy")
        {
            var copyBg = hover ? Theme.AppleBlueHover : Theme.AppleBlue;
            Theme.FillRounded(g, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), S(8), copyBg);

            var iconBox = new RectangleF(rect.Left + S(8), rect.Top + (rect.Height - S(18)) / 2f, S(18), S(18));
            Icons.Draw(g, IconKind.Copy, iconBox, Color.White, 1.6f);

            var textRect = new Rectangle((int)iconBox.Right + S(4), rect.Top, rect.Width - (int)iconBox.Width - S(14), rect.Height);
            TextRenderer.DrawText(g, "Kopyala", Theme.UiFontBold, textRect, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            return;
        }

        // 2) Kapatma butonu üzerine gelindiğinde soft kırmızı
        if (hit.Id == "close" && hover)
        {
            Theme.FillRounded(g, new RectangleF(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), radius, Theme.AppleRedHover);
        }
        else if (hit.Active)
        {
            // Seçili araç: Apple System Blue dolgulu hap
            Theme.FillRounded(g, new RectangleF(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), radius, Theme.AppleBlue);
        }
        else if (hover)
        {
            // Üzerine gelindiğinde: yumuşak saydam beyaz vurgu
            Theme.FillRounded(g, new RectangleF(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), radius, Theme.Hover);
        }

        // Segment, yazı tipi ve biçim düğmeleri çerçeveli olsun.
        if (hit.Kind == HitKind.Text &&
            (hit.Id.StartsWith("opt:shape", StringComparison.Ordinal) ||
             hit.Id.StartsWith("opt:mode", StringComparison.Ordinal) ||
             hit.Id == "opt:font" ||
             hit.Id == "opt:bold" ||
             hit.Id == "opt:italic" ||
             hit.Id == "opt:outline"))
        {
            if (!hit.Active && !hover)
            {
                Theme.FillRounded(g, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), S(6), Color.FromArgb(36, 36, 42));
                Theme.DrawRounded(g, new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1, rect.Height - 1), S(6), Color.FromArgb(56, 56, 64));
            }
        }

        // Çizgi stil düğmeleri gerçek deseni göstersin.
        if (hit.Id.StartsWith("opt:style", StringComparison.Ordinal))
        {
            if (!hit.Active && !hover)
            {
                Theme.FillRounded(g, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), S(6), Color.FromArgb(34, 34, 40));
                Theme.DrawRounded(g, new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1, rect.Height - 1), S(6), Color.FromArgb(56, 56, 64));
            }

            var index = hit.Id[^1] - '0';
            var styleColor = (hit.Active || hover) ? Color.White : Theme.Text;
            using var stylePen = new Pen(styleColor, Math.Max(1.6f, 1.8f * _scale))
            {
                DashStyle = index switch
                {
                    1 => DashStyle.Dash,
                    2 => DashStyle.Dot,
                    _ => DashStyle.Solid,
                },
                DashCap = DashCap.Round,
            };

            var midY = rect.Top + rect.Height / 2f;
            g.DrawLine(stylePen, rect.Left + S(6), midY, rect.Right - S(6), midY);
            return;
        }

        if (hit.Chevron && hit.Icon == IconKind.More)
        {
            // Küçük aşağı ok
            using var pen = new Pen(hit.Active ? Theme.AppleBlue : hover ? Color.White : Theme.SubText, 1.4f);
            var cx = rect.Left + rect.Width / 2f;
            var cy = rect.Top + rect.Height / 2f + S(1);
            g.DrawLines(pen, new[]
            {
                new PointF(cx - S(3), cy - S(2)),
                new PointF(cx, cy + S(1)),
                new PointF(cx + S(3), cy - S(2)),
            });
            return;
        }

        var color = !hit.Enabled
            ? (hit.Id == "label" ? Theme.Text : Theme.Disabled)
            : (hit.Active || hover)
                ? Color.White
                : Theme.Text;

        if (hit.Icon is { } icon)
        {
            var iconBox = new RectangleF(
                rect.Left + (rect.Width - S(20)) / 2f,
                rect.Top + (rect.Height - S(20)) / 2f,
                S(20), S(20));
            Icons.Draw(g, icon, iconBox, color, Math.Max(1.3f, 1.5f * _scale));
        }

        if (hit.Kind == HitKind.Text && !string.IsNullOrEmpty(hit.Text))
        {
            var font = hit.Id == "opt:bold"
                ? new Font("Segoe UI", 9f * _scale, FontStyle.Bold)
                : hit.Id == "opt:italic"
                    ? new Font("Segoe UI", 9f * _scale, FontStyle.Italic)
                    : Theme.UiFont;

            if (hit.Id == "opt:font")
            {
                var textRect = new Rectangle(rect.Left + S(8), rect.Top, rect.Width - S(24), rect.Height);
                TextRenderer.DrawText(g, hit.Text, font, textRect, color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
            else
            {
                TextRenderer.DrawText(g, hit.Text, font, rect, color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }

            if (!ReferenceEquals(font, Theme.UiFont))
            {
                font.Dispose();
            }
        }

        if (hit.Chevron && hit.Icon != IconKind.More)
        {
            using var pen = new Pen(Theme.SubText, 1.4f);
            var cx = rect.Right - S(11);
            var cy = rect.Top + rect.Height / 2f;
            g.DrawLines(pen, new[]
            {
                new PointF(cx - S(3), cy - S(1)),
                new PointF(cx, cy + S(2)),
                new PointF(cx + S(3), cy - S(1)),
            });
        }
    }

    private void DrawSwatch(Graphics g, Hit hit, bool hover)
    {
        var rect = hit.Bounds;

        if (hover)
        {
            Theme.FillRounded(g, new RectangleF(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), S(4), Theme.Hover);
        }

        var circle = new RectangleF(rect.X + S(2), rect.Y + S(2), rect.Width - S(4), rect.Height - S(4));

        if (hit.SwatchColor is { } swatch)
        {
            using var brush = new SolidBrush(swatch);
            g.FillEllipse(brush, circle);
            using var border = new Pen(Color.FromArgb(120, 120, 130), 1f);
            g.DrawEllipse(border, circle);
        }
        else
        {
            // Gökkuşağı özel renk düğmesi
            using var path = new GraphicsPath();
            path.AddEllipse(circle);
            using var brush = new PathGradientBrush(path)
            {
                CenterColor = Color.White,
                SurroundColors = new[]
                {
                    Color.FromArgb(0xF4, 0x43, 0x36), Color.FromArgb(0xFF, 0xC1, 0x07),
                    Color.FromArgb(0x4C, 0xAF, 0x50), Color.FromArgb(0x21, 0x96, 0xF3),
                    Color.FromArgb(0x9C, 0x27, 0xB0), Color.FromArgb(0xE9, 0x1E, 0x63),
                },
            };
            g.FillEllipse(brush, circle);
            using var border = new Pen(Color.FromArgb(160, 160, 170), 1f);
            g.DrawEllipse(border, circle);
        }

        if (hit.Active)
        {
            using var ring = new Pen(Color.White, 1.6f);
            g.DrawEllipse(ring, RectangleF.Inflate(circle, 2f, 2f));
        }
    }

    private void DrawCheck(Graphics g, Hit hit, bool hover)
    {
        if (hover)
        {
            Theme.FillRounded(g, new RectangleF(hit.Bounds.X + 1, hit.Bounds.Y + 1, hit.Bounds.Width - 2, hit.Bounds.Height - 2), S(4), Theme.Hover);
        }

        var box = new RectangleF(hit.Bounds.Left + S(6), hit.Bounds.Top + (hit.Bounds.Height - S(14)) / 2f, S(14), S(14));

        if (hit.Active)
        {
            Theme.FillRounded(g, box, S(3), Theme.Accent);
            Icons.Draw(g, IconKind.Check, RectangleF.Inflate(box, -S(1), -S(1)), Color.White, 1.4f);
        }
        else
        {
            using var pen = new Pen(Color.FromArgb(150, 150, 160), 1f);
            Theme.DrawRounded(g, box, S(3), pen.Color, 1f);
        }

        TextRenderer.DrawText(g, hit.Text, Theme.UiFont,
            new Rectangle((int)box.Right + S(6), hit.Bounds.Top, hit.Bounds.Width, hit.Bounds.Height),
            Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void DrawSlider(Graphics g, Hit hit, bool hover)
    {
        var rect = hit.Bounds;
        var trackHeight = (int)Math.Max(3, S(4));
        var track = new RectangleF(rect.Left, rect.Top + (rect.Height - trackHeight) / 2f, rect.Width, trackHeight);

        Theme.FillRounded(g, track, trackHeight / 2f, Color.FromArgb(70, 70, 78));

        var ratio = Math.Clamp(hit.Value / (float)Math.Max(1, hit.MaxValue), 0f, 1f);
        var filled = new RectangleF(track.X, track.Y, Math.Max(trackHeight, track.Width * ratio), track.Height);
        Theme.FillRounded(g, filled, trackHeight / 2f, Theme.Accent);

        var knobX = track.X + track.Width * ratio;
        var knob = new RectangleF(knobX - S(6), rect.Top + (rect.Height - S(12)) / 2f, S(12), S(12));
        using var brush = new SolidBrush(hover ? Color.White : Color.FromArgb(235, 235, 240));
        g.FillEllipse(brush, knob);
    }

    // ----------------------------------------------------------------- fare

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_draggingForm)
        {
            var vs = SystemInformation.VirtualScreen;
            var screenPt = PointToScreen(e.Location);
            Location = new Point(
                Math.Clamp(screenPt.X - _dragOffset.X, vs.Left, Math.Max(vs.Left, vs.Right - Width)),
                Math.Clamp(screenPt.Y - _dragOffset.Y, vs.Top, Math.Max(vs.Top, vs.Bottom - Height)));
            _userPositioned = true;
            return;
        }

        if (_sliderId is not null)
        {
            SetSliderValue(_sliderId, e.X);
            return;
        }

        var hit = HitAt(e.Location);
        var newHover = hit?.Id;
        if (newHover != _hoverId)
        {
            _hoverId = newHover;
            _tipTimer.Stop();
            _tip.Hide(this);
            if (hit is not null && !string.IsNullOrEmpty(hit.Tip))
            {
                _pendingTip = hit.Tip;
                _tipTimer.Start();
            }
            else
            {
                _pendingTip = string.Empty;
            }
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        var hit = HitAt(e.Location);
        if (hit is null)
        {
            return;
        }

        _tipTimer.Stop();
        _tip.Hide(this);

        if (e.Button == MouseButtons.Right)
        {
            switch (hit.Id)
            {
                case "tool:rect":
                    ShowVariantMenu(hit.Id, new[] { ("Dikdörtgen (R)", "tool:rectOnly"), ("Elips (O)", "tool:ellipse") });
                    return;
                case "tool:pen":
                    ShowVariantMenu(hit.Id, new[] { ("Kalem (P)", "tool:penOnly"), ("Fosforlu kalem (H)", "tool:highlighter") });
                    return;
                case "tool:arrow":
                    ShowVariantMenu(hit.Id, new[] { ("Ok (A)", "tool:arrowOnly"), ("Çizgi (L)", "tool:line") });
                    return;
                case "tool:mosaic":
                    ShowVariantMenu(hit.Id, new[] { ("Mozaik (M)", "tool:mosaicOnly"), ("Bulanıklaştır (B)", "tool:blur") });
                    return;
            }
            return;
        }

        if (hit.Id == "drag")
        {
            _draggingForm = true;
            _dragOffset = e.Location;
            Capture = true;
            return;
        }

        if (hit.Id == "opt:widthSlider")
        {
            if (_session.SelectedAnnotation is not null)
            {
                _session.PushUndo();
            }

            _sliderId = hit.Id;
            SetSliderValue(hit.Id, e.X);
            Capture = true;
            return;
        }

        Dispatch(hit.Id);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _draggingForm = false;
        _sliderId = null;
        Capture = false;

        // Tıklama araç çubuğunu ön plana geçirmiş olabilir; kaplamaya klavye
        // odağını geri ver (menü açıksa dokunma).
        if (!_menuOpen)
        {
            RestoreOverlayFocus();
        }
    }

    private void RestoreOverlayFocus()
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated)
        {
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed)
                {
                    _session.FocusOverlay();
                }
            }));
        }
        else
        {
            _session.FocusOverlay();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Araç çubuğu ön planda kalırsa kısayollar kaybolmasın.
        if (_session.OnKeyDown(e))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (_session.OnKeyPress(e))
        {
            e.Handled = true;
        }

        base.OnKeyPress(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverId = null;
        _tipTimer.Stop();
        _tip.Hide(this);
        Invalidate();
    }

    private Hit? HitAt(Point point)
    {
        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].Kind == HitKind.Separator || _hits[i].Kind == HitKind.Label || !_hits[i].Enabled)
            {
                continue;
            }

            if (_hits[i].Bounds.Contains(point))
            {
                return _hits[i];
            }
        }

        return null;
    }

    private void SetSliderValue(string id, int mouseX)
    {
        var hit = _hits.FirstOrDefault(h => h.Id == id);
        if (hit is null) return;

        var ratio = Math.Clamp((mouseX - hit.Bounds.Left) / (float)hit.Bounds.Width, 0f, 1f);
        var value = Math.Max(1, (int)Math.Round(ratio * hit.MaxValue));

        switch (id)
        {
            case "opt:widthSlider":
                _session.CurrentWidth = value;
                if (_session.SelectedAnnotation is not null)
                {
                    _session.SelectedAnnotation.Width = value;
                }
                break;
        }

        OnSessionChanged();
        _session.RefreshOverlays();
    }

    // ----------------------------------------------------------------- eylemler

    private void ToggleOrSelectTool(ToolKind target, params ToolKind[] group)
    {
        if (_session.Tool == target || group.Contains(_session.Tool))
        {
            _session.SelectTool(ToolKind.Pointer);
        }
        else
        {
            _session.SelectTool(target);
        }
    }

    private void Dispatch(string id)
    {
        switch (id)
        {
            case "tool:pointer": _session.SelectTool(ToolKind.Pointer); break;
            case "tool:rect":
                if (_session.Tool is ToolKind.Rectangle or ToolKind.Ellipse)
                {
                    _session.ShapeIsEllipse = !_session.ShapeIsEllipse;
                    _session.SelectTool(_session.ShapeIsEllipse ? ToolKind.Ellipse : ToolKind.Rectangle);
                }
                else
                {
                    _session.SelectTool(_session.ShapeIsEllipse ? ToolKind.Ellipse : ToolKind.Rectangle);
                }
                break;
            case "tool:pen":
                if (_session.Tool is ToolKind.Pen or ToolKind.Highlighter)
                {
                    _session.PenIsHighlighter = !_session.PenIsHighlighter;
                    _session.SelectTool(_session.PenIsHighlighter ? ToolKind.Highlighter : ToolKind.Pen);
                }
                else
                {
                    _session.SelectTool(_session.PenIsHighlighter ? ToolKind.Highlighter : ToolKind.Pen);
                }
                break;
            case "tool:arrow":
                if (_session.Tool is ToolKind.Arrow or ToolKind.Line)
                {
                    _session.ArrowIsLine = !_session.ArrowIsLine;
                    _session.SelectTool(_session.ArrowIsLine ? ToolKind.Line : ToolKind.Arrow);
                }
                else
                {
                    _session.SelectTool(_session.ArrowIsLine ? ToolKind.Line : ToolKind.Arrow);
                }
                break;
            case "tool:text":
                _session.SelectTool(ToolKind.Text);
                break;
            case "tool:serial":
                _session.SelectTool(ToolKind.Serial);
                break;
            case "tool:mosaic":
                if (_session.Tool is ToolKind.Mosaic or ToolKind.Blur)
                {
                    _session.MosaicBlur = !_session.MosaicBlur;
                    _session.SelectTool(_session.MosaicBlur ? ToolKind.Blur : ToolKind.Mosaic);
                }
                else
                {
                    _session.SelectTool(_session.MosaicBlur ? ToolKind.Blur : ToolKind.Mosaic);
                }
                break;
            case "tool:eraser":
                _session.SelectTool(ToolKind.Eraser);
                break;

            case "menu:rect": ShowVariantMenu(id, new[] { ("Dikdörtgen", "tool:rectOnly"), ("Elips", "tool:ellipse") }); return;
            case "menu:pen": ShowVariantMenu(id, new[] { ("Kalem", "tool:penOnly"), ("Fosforlu kalem", "tool:highlighter") }); return;
            case "menu:arrow": ShowVariantMenu(id, new[] { ("Ok", "tool:arrowOnly"), ("Çizgi", "tool:line") }); return;
            case "menu:mosaic": ShowVariantMenu(id, new[] { ("Mozaik", "tool:mosaicOnly"), ("Bulanıklaştır", "tool:blur") }); return;

            case "tool:rectOnly": _session.ShapeIsEllipse = false; _session.SelectTool(ToolKind.Rectangle); break;
            case "tool:ellipse": _session.ShapeIsEllipse = true; _session.SelectTool(ToolKind.Ellipse); break;
            case "tool:penOnly": _session.PenIsHighlighter = false; _session.SelectTool(ToolKind.Pen); break;
            case "tool:highlighter": _session.PenIsHighlighter = true; _session.SelectTool(ToolKind.Highlighter); break;
            case "tool:arrowOnly": _session.ArrowIsLine = false; _session.SelectTool(ToolKind.Arrow); break;
            case "tool:line": _session.ArrowIsLine = true; _session.SelectTool(ToolKind.Line); break;
            case "tool:mosaicOnly": _session.MosaicBlur = false; _session.SelectTool(ToolKind.Mosaic); break;
            case "tool:blur": _session.MosaicBlur = true; _session.SelectTool(ToolKind.Blur); break;

            case "undo": _session.Undo(); break;
            case "redo": _session.Redo(); break;
            case "crop": _session.CropToSelection(); break;
            case "long": _session.StartLongCapture(); break;
            case "video": _session.StartRecording(); break;
            case "ocr": _session.RunOcr(); break;
            case "pin": _session.Pin(); break;
            case "save": _session.Save(); break;
            case "close": _session.RequestCancel(); break;
            case "copy": _session.CopyAndClose(); break;
            case "more": ShowMoreMenu(); return;

            case "opt:shapeRect":
                _session.ShapeIsEllipse = false;
                if (_session.SelectedAnnotation is RectangleAnnotation)
                {
                    _session.UpdateSelectedAnnotation(a => ((RectangleAnnotation)a).IsEllipse = false);
                }
                else
                {
                    _session.SelectTool(ToolKind.Rectangle);
                }
                break;

            case "opt:shapeEllipse":
                _session.ShapeIsEllipse = true;
                if (_session.SelectedAnnotation is RectangleAnnotation)
                {
                    _session.UpdateSelectedAnnotation(a => ((RectangleAnnotation)a).IsEllipse = true);
                }
                else
                {
                    _session.SelectTool(ToolKind.Ellipse);
                }
                break;

            case "opt:fill":
                _session.Filled = !_session.Filled;
                _session.UpdateSelectedAnnotation(a => a.Filled = _session.Filled);
                break;

            case "opt:style0":
                _session.Dash = DashStyle.Solid;
                _session.UpdateSelectedAnnotation(a => a.Dash = DashStyle.Solid);
                break;
            case "opt:style1":
                _session.Dash = DashStyle.Dash;
                _session.UpdateSelectedAnnotation(a => a.Dash = DashStyle.Dash);
                break;
            case "opt:style2":
                _session.Dash = DashStyle.Dot;
                _session.UpdateSelectedAnnotation(a => a.Dash = DashStyle.Dot);
                break;

            case "opt:widthDec":
                _session.CurrentWidth = Math.Max(1, _session.CurrentWidth - 1);
                _session.UpdateSelectedAnnotation(a =>
                {
                    a.Width = _session.CurrentWidth;
                    if (a is SerialAnnotation s) s.Radius = Math.Max(12f, _session.CurrentWidth * 3.2f);
                });
                break;
            case "opt:widthInc":
                _session.CurrentWidth = Math.Min(60, _session.CurrentWidth + 1);
                _session.UpdateSelectedAnnotation(a =>
                {
                    a.Width = _session.CurrentWidth;
                    if (a is SerialAnnotation s) s.Radius = Math.Max(12f, _session.CurrentWidth * 3.2f);
                });
                break;
            case "opt:radiusDec":
                _session.CornerRadius = Math.Max(0, _session.CornerRadius - 2);
                _session.UpdateSelectedAnnotation(a => { if (a is RectangleAnnotation r) r.CornerRadius = _session.CornerRadius; });
                break;
            case "opt:radiusInc":
                _session.CornerRadius = Math.Min(60, _session.CornerRadius + 2);
                _session.UpdateSelectedAnnotation(a => { if (a is RectangleAnnotation r) r.CornerRadius = _session.CornerRadius; });
                break;
            case "opt:sizeDec":
                _session.CurrentFontSize = Math.Max(8, _session.CurrentFontSize - 2);
                _session.UpdateSelectedAnnotation(a => { if (a is TextAnnotation t) t.FontSize = _session.CurrentFontSize; });
                break;
            case "opt:sizeInc":
                _session.CurrentFontSize = Math.Min(200, _session.CurrentFontSize + 2);
                _session.UpdateSelectedAnnotation(a => { if (a is TextAnnotation t) t.FontSize = _session.CurrentFontSize; });
                break;
            case "opt:blockDec":
                _session.MosaicBlock = Math.Max(2, _session.MosaicBlock - 2);
                _session.UpdateSelectedAnnotation(a => { if (a is MosaicAnnotation m) m.BlockSize = _session.MosaicBlock; });
                break;
            case "opt:blockInc":
                _session.MosaicBlock = Math.Min(80, _session.MosaicBlock + 2);
                _session.UpdateSelectedAnnotation(a => { if (a is MosaicAnnotation m) m.BlockSize = _session.MosaicBlock; });
                break;

            case "opt:bold":
                _session.Bold = !_session.Bold;
                _session.UpdateSelectedAnnotation(a => { if (a is TextAnnotation t) t.Bold = _session.Bold; });
                break;
            case "opt:italic":
                _session.Italic = !_session.Italic;
                _session.UpdateSelectedAnnotation(a => { if (a is TextAnnotation t) t.Italic = _session.Italic; });
                break;
            case "opt:outline":
                _session.Outline = !_session.Outline;
                _session.UpdateSelectedAnnotation(a => { if (a is TextAnnotation t) t.Outline = _session.Outline; });
                break;
            case "opt:font": ShowFontMenu(); return;

            case "opt:modePen":
                _session.PenIsHighlighter = false;
                if (_session.SelectedAnnotation is PenAnnotation)
                {
                    _session.UpdateSelectedAnnotation(a => ((PenAnnotation)a).IsHighlighter = false);
                }
                else
                {
                    _session.SelectTool(ToolKind.Pen);
                }
                break;
            case "opt:modeHighlighter":
                _session.PenIsHighlighter = true;
                if (_session.SelectedAnnotation is PenAnnotation)
                {
                    _session.UpdateSelectedAnnotation(a => ((PenAnnotation)a).IsHighlighter = true);
                }
                else
                {
                    _session.SelectTool(ToolKind.Highlighter);
                }
                break;

            case "opt:modeMosaic":
                _session.MosaicBlur = false;
                if (_session.SelectedAnnotation is MosaicAnnotation)
                {
                    _session.UpdateSelectedAnnotation(a => ((MosaicAnnotation)a).IsBlur = false);
                }
                else
                {
                    _session.SelectTool(ToolKind.Mosaic);
                }
                break;
            case "opt:modeBlur":
                _session.MosaicBlur = true;
                if (_session.SelectedAnnotation is MosaicAnnotation)
                {
                    _session.UpdateSelectedAnnotation(a => ((MosaicAnnotation)a).IsBlur = true);
                }
                else
                {
                    _session.SelectTool(ToolKind.Blur);
                }
                break;

            case "opt:colorCustom": PickCustomColor(); return;

            default:
                if (id.StartsWith("opt:color"))
                {
                    var index = int.Parse(id["opt:color".Length..]);
                    var colors = new[] { Theme.Red, Theme.Yellow, Theme.Green, Theme.Blue, Theme.Black, Theme.White };
                    if (index >= 0 && index < colors.Length)
                    {
                        _session.CurrentColor = colors[index];
                        _session.UpdateSelectedAnnotation(a => a.Color = colors[index]);
                    }
                }
                break;
        }

        OnSessionChanged();
        _session.RefreshOverlays();
        _session.FocusOverlay();
    }

    private void ShowVariantMenu(string id, (string Text, string Action)[] items)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.ToolbarBack,
            ForeColor = Theme.Text,
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = Theme.UiFont,
        };

        foreach (var (text, action) in items)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (_, _) =>
            {
                Dispatch(action);
                _session.FocusOverlay();
            };
            menu.Items.Add(item);
        }

        _menuOpen = true;
        var hit = _hits.FirstOrDefault(h => h.Id == id);
        if (hit is not null)
        {
            menu.Show(this, new Point(hit.Bounds.Left, Bounds.Height));
        }
        else
        {
            _menuOpen = false;
            UiFactory.DisposeLater(menu);
        }

        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            UiFactory.DisposeLater(menu);
            RestoreOverlayFocus();
        };
    }

    private void ShowFontMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.ToolbarBack,
            ForeColor = Theme.Text,
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = Theme.UiFont,
        };

        var families = new[]
        {
            "Segoe UI", "Arial", "Calibri", "Tahoma", "Verdana",
            "Times New Roman", "Georgia", "Consolas", "Courier New", "Impact",
        };

        foreach (var family in families)
        {
            var item = new ToolStripMenuItem(family) { Checked = _session.FontFamily == family };
            item.Click += (_, _) =>
            {
                _session.FontFamily = family;
                _session.UpdateSelectedAnnotation(a => { if (a is TextAnnotation t) t.FontFamily = family; });
                OnSessionChanged();
                _session.RefreshOverlays();
                RestoreOverlayFocus();
            };
            menu.Items.Add(item);
        }

        _menuOpen = true;
        var hit = _hits.FirstOrDefault(h => h.Id == "opt:font");
        if (hit is not null)
        {
            menu.Show(this, new Point(hit.Bounds.Left, Bounds.Height));
        }
        else
        {
            _menuOpen = false;
            UiFactory.DisposeLater(menu);
        }

        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            UiFactory.DisposeLater(menu);
            RestoreOverlayFocus();
        };
    }

    private void PickCustomColor()
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = _session.CurrentColor,
            AnyColor = true,
        };

        if (dialog.ShowDialog(_session.OwnerWindow) == DialogResult.OK)
        {
            _session.CurrentColor = dialog.Color;
            _session.UpdateSelectedAnnotation(a => a.Color = dialog.Color);
            OnSessionChanged();
            _session.RefreshOverlays();
        }

        _session.FocusOverlay();
    }

    private void ShowMoreMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.ToolbarBack,
            ForeColor = Theme.Text,
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = Theme.UiFont,
        };

        menu.Items.Add(MenuItem("Panoya kopyala (Ctrl+C)", () => _session.CopyAllToClipboard()));
        menu.Items.Add(MenuItem("Görüntüyü kaydet (Ctrl+S)", _session.Save));
        menu.Items.Add(MenuItem("Seçime kırp", _session.CropToSelection));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("Metni tanı (OCR) (Shift+C)", _session.RunOcr));
        menu.Items.Add(MenuItem("Ekrana sabitle (Pin) (Ctrl+T)", _session.Pin));
        menu.Items.Add(MenuItem("Uzun yakalama (Ctrl+L)", _session.StartLongCapture));
        menu.Items.Add(MenuItem("Video / GIF kaydet (Ctrl+G)", _session.StartRecording));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("Tüm çizimleri temizle (Ctrl+Shift+Z)", _session.ClearAnnotations));
        menu.Items.Add(MenuItem("Ayarlar…", () => SnapDeskApplication.Current?.ShowSettings(_session.OwnerWindow)));

        _menuOpen = true;
        var hit = _hits.FirstOrDefault(h => h.Id == "more");
        if (hit is not null)
        {
            menu.Show(this, new Point(hit.Bounds.Left - S(130), Bounds.Height + S(2)));
        }
        else
        {
            _menuOpen = false;
            UiFactory.DisposeLater(menu);
        }

        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            UiFactory.DisposeLater(menu);
            RestoreOverlayFocus();
        };
    }

    private static ToolStripMenuItem MenuItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tipTimer.Dispose();
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
