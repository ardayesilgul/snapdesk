using System.Drawing.Drawing2D;

namespace SnapDesk.UI;

/// <summary>
/// Apple Obsidian & Linear tasarım diline sahip, mikro-animasyonlu ve
/// keskin kenar hatasız (glitch-free) ortak denetim üreticileri.
/// </summary>
public static class UiFactory
{
    public static Button Button(string text, int width = 96, int height = 32, bool primary = false)
        => new AppleButton(text, width, height, primary);

    public static TextBox TextBox(bool multiline = false)
        => new ThemedTextBox(multiline);

    public static CheckBox Check(string text, bool @checked = false)
        => new AppleCheckBox(text, @checked);

    public static AppleToggleSwitch Toggle(bool isChecked = false)
        => new(isChecked);

    public static AppleSegmentedControl Segmented(params string[] options)
        => new(options);

    public static Label Label(string text, bool small = false)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = small ? Theme.SubText : Theme.Text,
            BackColor = Color.Transparent,
            Font = small ? Theme.SmallFont : Theme.UiFont,
        };

    public static ComboBox Combo()
        => new AppleComboBox();

    public static Panel CardPanel(Color? fill = null, float radius = 10f)
        => new RoundedCardPanel(fill ?? Theme.SurfaceCard, radius);

    public static void ApplyRoundedRegion(Form form, int radius = 16)
    {
        if (form.Width <= 0 || form.Height <= 0) return;
        using var path = Theme.RoundedRect(new RectangleF(0, 0, form.Width, form.Height), radius);
        var oldRegion = form.Region;
        form.Region = new Region(path);
        oldRegion?.Dispose();
    }

    public static void AttachSmoothEntrance(Form form)
    {
        form.Opacity = 0.0;
        var timer = new System.Windows.Forms.Timer { Interval = 15 };
        timer.Tick += (_, _) =>
        {
            if (form.IsDisposed)
            {
                timer.Stop();
                timer.Dispose();
                return;
            }

            var next = form.Opacity + (1.0 - form.Opacity) * 0.38;
            if (next >= 0.98)
            {
                form.Opacity = 1.0;
                timer.Stop();
                timer.Dispose();
            }
            else
            {
                form.Opacity = next;
            }
        };
        form.Shown += (_, _) => timer.Start();
        form.FormClosed += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
        };
    }

    /// <summary>
    /// Bağlam menülerini kendi Closed olayı içinde dispose etmek, WinForms'un
    /// menüyü kapatma ve tıklanan öğenin Click olayını tetikleme adımıyla çakışır.
    /// Menüyü mevcut mesaj döngüsü adımından sonra zamanlayıcı ile serbest bırak.
    /// </summary>
    public static void DisposeLater(Control control)
    {
        if (control.IsDisposed)
        {
            return;
        }

        var timer = new System.Windows.Forms.Timer { Interval = 60 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (!control.IsDisposed)
            {
                control.Dispose();
            }
        };
        timer.Start();
    }

    private static readonly Lazy<Icon?> CachedAppIcon = new(() =>
    {
        try
        {
            using var stream = typeof(UiFactory).Assembly.GetManifestResourceStream("SnapDesk.app.ico");
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
            // yoksay
        }

        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                return Icon.ExtractAssociatedIcon(exePath);
            }
        }
        catch
        {
            // yoksay
        }

        return null;
    });

    public static void StyleForm(Form form)
    {
        form.BackColor = Theme.WindowBack;
        form.ForeColor = Theme.Text;
        form.Font = Theme.UiFont;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        if (CachedAppIcon.Value is { } appIcon)
        {
            form.Icon = appIcon;
        }

        if (form.IsHandleCreated)
        {
            ApplyDarkTitleBar(form);
        }
        else
        {
            form.HandleCreated += (_, _) => ApplyDarkTitleBar(form);
        }
    }

    /// <summary>Windows 10/11 başlık çubuğunu koyu OLED temasına uyarlar ve köşe yuvarlatmasını etkinleştirir.</summary>
    private static void ApplyDarkTitleBar(Form form)
    {
        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref enabled, sizeof(int));
            var cornerPreference = 2; /* DWMWCP_ROUND */
            DwmSetWindowAttribute(form.Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref cornerPreference, sizeof(int));
            var c = form.BackColor;
            var captionColor = c.R | (c.G << 8) | (c.B << 16);
            DwmSetWindowAttribute(form.Handle, 35 /* DWMWA_CAPTION_COLOR */, ref captionColor, sizeof(int));
            var borderColor = 0x00262121;
            DwmSetWindowAttribute(form.Handle, 34 /* DWMWA_BORDER_COLOR */, ref borderColor, sizeof(int));
        }
        catch
        {
            // eski Windows sürümlerinde yoksay
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ----------------------------------------------------------------- AppleButton

    private sealed class AppleButton : Button
    {
        private readonly bool _primary;
        private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 16 };
        private float _hover;
        private float _press;
        private bool _isHovered;
        private bool _isPressed;

        public AppleButton(string text, int width, int height, bool primary)
        {
            _primary = primary;
            _animTimer.Tick += (_, _) => StepAnimation();

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);

            Text = text;
            Width = width;
            Height = height;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.WindowBack;
            ForeColor = primary ? Color.White : Theme.Text;
            Font = primary ? Theme.UiFontBold : Theme.UiFont;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
        }

        private void StepAnimation()
        {
            var targetHover = _isHovered && Enabled ? 1f : 0f;
            var targetPress = _isPressed && Enabled ? 1f : 0f;

            _hover += (targetHover - _hover) * 0.34f;
            _press += (targetPress - _press) * 0.42f;

            if (Math.Abs(targetHover - _hover) < 0.01f && Math.Abs(targetPress - _press) < 0.01f)
            {
                _hover = targetHover;
                _press = targetPress;
                _animTimer.Stop();
            }

            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            _animTimer.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            _isPressed = false;
            _animTimer.Start();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                _isPressed = true;
                _animTimer.Start();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _isPressed = false;
            _animTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            Theme.Prepare(g);

            var bgClear = Parent?.BackColor is { A: 255 } parentBg ? parentBg : Theme.WindowBack;
            g.Clear(bgClear);

            var inset = 0.5f + _press * 0.6f;
            var rect = new RectangleF(inset, inset, Math.Max(2f, Width - 1f - inset * 2f), Math.Max(2f, Height - 1f - inset * 2f));
            var radius = Math.Min(8f, rect.Height / 2f);

            if (!Enabled)
            {
                Theme.FillRounded(g, rect, radius, Color.FromArgb(28, 28, 34));
                Theme.DrawRounded(g, rect, radius, Color.FromArgb(24, 255, 255, 255), 1f);
                Theme.DrawText(g, Text, Font, ClientRectangle, Theme.Disabled, StringAlignment.Center, StringAlignment.Center);
                return;
            }

            Color fillColor;
            Color borderColor;
            Color textColor;

            if (_primary)
            {
                var hoverColor = Theme.Lerp(Theme.AppleBlue, Theme.AppleBlueHover, _hover);
                fillColor = Theme.Lerp(hoverColor, Theme.AppleBluePress, _press);
                borderColor = Color.FromArgb(65, 255, 255, 255);
                textColor = Color.White;
            }
            else
            {
                var baseFill = Color.FromArgb(36, 36, 44);
                var hoverFill = Color.FromArgb(52, 52, 62);
                var pressFill = Color.FromArgb(28, 28, 35);
                var currentFill = Theme.Lerp(baseFill, hoverFill, _hover);
                fillColor = Theme.Lerp(currentFill, pressFill, _press);

                borderColor = Theme.Lerp(
                    Color.FromArgb(40, 255, 255, 255),
                    Color.FromArgb(72, 255, 255, 255),
                    _hover);
                textColor = Theme.Lerp(Theme.Text, Color.White, _hover);
            }

            using (var path = Theme.RoundedRect(rect, radius))
            {
                using (var fillBrush = new SolidBrush(fillColor))
                {
                    g.FillPath(fillBrush, path);
                }

                // Üst speküler ışıltı çizgisi
                using (var specPen = new Pen(Color.FromArgb(_primary ? 54 : 26, 255, 255, 255), 1f))
                {
                    g.DrawLine(specPen, rect.Left + radius, rect.Top + 0.5f, Math.Max(rect.Left + radius, rect.Right - radius), rect.Top + 0.5f);
                }

                using (var borderPen = new Pen(borderColor, 1f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            Theme.DrawText(g, Text, Font, ClientRectangle, textColor, StringAlignment.Center, StringAlignment.Center);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // ----------------------------------------------------------------- AppleCheckBox

    private sealed class AppleCheckBox : CheckBox
    {
        private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 16 };
        private float _checkAnim;
        private float _hoverAnim;
        private bool _hovered;

        public AppleCheckBox(string text, bool isChecked)
        {
            _animTimer.Tick += (_, _) => StepAnimation();

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);

            _checkAnim = isChecked ? 1f : 0f;
            Text = text;
            Checked = isChecked;
            AutoSize = true;
            ForeColor = Theme.Text;
            BackColor = Theme.WindowBack;
            Font = Theme.UiFont;
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var textSize = TextRenderer.MeasureText(Text, Font);
            return new Size(textSize.Width + 28, Math.Max(24, textSize.Height + 6));
        }

        private void StepAnimation()
        {
            var targetCheck = Checked ? 1f : 0f;
            var targetHover = _hovered && Enabled ? 1f : 0f;

            _checkAnim += (targetCheck - _checkAnim) * 0.36f;
            _hoverAnim += (targetHover - _hoverAnim) * 0.32f;

            if (Math.Abs(targetCheck - _checkAnim) < 0.01f && Math.Abs(targetHover - _hoverAnim) < 0.01f)
            {
                _checkAnim = targetCheck;
                _hoverAnim = targetHover;
                _animTimer.Stop();
            }

            Invalidate();
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            _animTimer.Start();
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

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            Theme.Prepare(g);

            var bgClear = Parent?.BackColor is { A: 255 } parentBg ? parentBg : Theme.WindowBack;
            g.Clear(bgClear);

            var boxSize = 16f;
            var boxY = (Height - boxSize) / 2f;
            var boxRect = new RectangleF(1f, boxY, boxSize, boxSize);

            var offFill = Theme.Lerp(Color.FromArgb(34, 34, 42), Color.FromArgb(46, 46, 56), _hoverAnim);
            var onFill = Theme.Lerp(Theme.AppleBlue, Theme.AppleBlueHover, _hoverAnim);
            var fill = Theme.Lerp(offFill, onFill, _checkAnim);

            var offBorder = Theme.Lerp(Color.FromArgb(65, 255, 255, 255), Color.FromArgb(110, 255, 255, 255), _hoverAnim);
            var border = Theme.Lerp(offBorder, Theme.AppleBlueHover, _checkAnim);

            Theme.FillRounded(g, boxRect, 4.5f, fill);
            Theme.DrawRounded(g, boxRect, 4.5f, border, 1.1f);

            if (_checkAnim > 0.08f)
            {
                var alpha = (int)Math.Clamp(_checkAnim * 255f, 0f, 255f);
                var iconRect = RectangleF.Inflate(boxRect, -1.5f, -1.5f);
                Icons.Draw(g, IconKind.Check, iconRect, Color.FromArgb(alpha, Color.White), 1.6f);
            }

            var textRect = new RectangleF(24, 0, Math.Max(10, Width - 24), Height);
            var textColor = !Enabled ? Theme.Disabled : Theme.Lerp(Theme.Text, Color.White, _hoverAnim * 0.5f);
            Theme.DrawText(g, Text, Font, textRect, textColor, StringAlignment.Near, StringAlignment.Center);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // ----------------------------------------------------------------- AppleComboBox

    private sealed class AppleComboBox : ComboBox
    {
        private const int WmPaint = 0x000F;
        private bool _hovered;

        public AppleComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 22;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.SurfaceInput;
            ForeColor = Theme.Text;
            Font = Theme.UiFont;
            Cursor = Cursors.Hand;
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

        protected override void OnDropDown(EventArgs e)
        {
            base.OnDropDown(e);
            Invalidate();
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            base.OnDropDownClosed(e);
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                return;
            }

            var g = e.Graphics;
            Theme.Prepare(g);

            var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var bgBrush = new SolidBrush(Theme.ToolbarBack))
            {
                g.FillRectangle(bgBrush, e.Bounds);
            }

            if (selected)
            {
                var pill = new RectangleF(e.Bounds.X + 3f, e.Bounds.Y + 1.5f, Math.Max(4f, e.Bounds.Width - 6f), Math.Max(4f, e.Bounds.Height - 3f));
                Theme.FillRounded(g, pill, 5f, Theme.AppleBlue);
            }

            var text = Items[e.Index]?.ToString() ?? string.Empty;
            var textRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(10, e.Bounds.Width - 16), e.Bounds.Height);
            TextRenderer.DrawText(
                g, text, Font, textRect,
                selected ? Color.White : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WmPaint)
            {
                using var g = CreateGraphics();
                PaintSurface(g);
            }
            else if (m.Msg == 0x0318 /* WM_PRINTCLIENT */ && m.WParam != IntPtr.Zero)
            {
                using var g = Graphics.FromHdc(m.WParam);
                PaintSurface(g);
            }
        }

        private void PaintSurface(Graphics g)
        {
            Theme.Prepare(g);

            var bgClear = Parent?.BackColor is { A: 255 } parentBg ? parentBg : Theme.WindowBack;
            g.Clear(bgClear);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            var radius = 7f;
            var fill = DroppedDown || _hovered ? Theme.SurfaceHover : Theme.SurfaceInput;
            var border = DroppedDown || Focused
                ? Theme.AppleBlue
                : _hovered
                    ? Color.FromArgb(80, 255, 255, 255)
                    : Theme.InputBorder;

            Theme.FillRounded(g, rect, radius, fill);
            Theme.DrawRounded(g, rect, radius, border, DroppedDown || Focused ? 1.3f : 1f);

            var displayText = SelectedItem?.ToString() ?? Text ?? string.Empty;
            var textRect = new RectangleF(10, 0, Math.Max(10, Width - 32), Height);
            Theme.DrawText(
                g, displayText, Font, textRect,
                Enabled ? Theme.Text : Theme.Disabled,
                StringAlignment.Near, StringAlignment.Center, ellipsis: true);

            var cx = Width - 15f;
            var cy = Height / 2f;
            using var chevronPen = new Pen(_hovered || DroppedDown ? Color.White : Theme.SubText, 1.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(chevronPen, new[]
            {
                new PointF(cx - 4f, cy - 1.5f),
                new PointF(cx, cy + 2.5f),
                new PointF(cx + 4f, cy - 1.5f),
            });
        }
    }

    // ----------------------------------------------------------------- ThemedTextBox

    private sealed class ThemedTextBox : TextBox
    {
        private Control? _hookedParent;
        private bool _hovered;

        public ThemedTextBox(bool multiline)
        {
            Multiline = multiline;
            BackColor = Theme.SurfaceInput;
            ForeColor = Theme.Text;
            BorderStyle = BorderStyle.None;
            Font = Theme.UiFont;
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            if (_hookedParent is not null)
            {
                _hookedParent.Paint -= PaintParentBorder;
            }

            base.OnParentChanged(e);
            _hookedParent = Parent;

            if (_hookedParent is not null)
            {
                _hookedParent.Paint += PaintParentBorder;
                _hookedParent.Invalidate();
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            InvalidateParentWell();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            InvalidateParentWell();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            InvalidateParentWell();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            InvalidateParentWell();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            InvalidateParentWell();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            InvalidateParentWell();
        }

        private Rectangle GetWellBounds()
            => Multiline
                ? Rectangle.Inflate(Bounds, 8, 8)
                : Rectangle.Inflate(Bounds, 7, 5);

        private void InvalidateParentWell()
        {
            if (_hookedParent is not null && !_hookedParent.IsDisposed)
            {
                _hookedParent.Invalidate(Rectangle.Inflate(GetWellBounds(), 4, 4));
            }
        }

        private void PaintParentBorder(object? sender, PaintEventArgs e)
        {
            if (!Visible || IsDisposed) return;

            var g = e.Graphics;
            Theme.Prepare(g);

            var well = GetWellBounds();
            var rect = new RectangleF(well.X + 0.5f, well.Y + 0.5f, well.Width - 1f, well.Height - 1f);
            var radius = Multiline ? 10f : 7f;

            var border = Focused
                ? Theme.AppleBlue
                : _hovered
                    ? Color.FromArgb(75, 255, 255, 255)
                    : Theme.InputBorder;

            Theme.FillRounded(g, rect, radius, BackColor);
            Theme.DrawRounded(g, rect, radius, border, Focused ? 1.4f : 1f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _hookedParent is not null)
            {
                _hookedParent.Paint -= PaintParentBorder;
                _hookedParent = null;
            }

            base.Dispose(disposing);
        }
    }

    // ----------------------------------------------------------------- RoundedCardPanel

    private sealed class RoundedCardPanel : Panel
    {
        private readonly Color _fill;
        private readonly float _radius;

        public RoundedCardPanel(Color fill, float radius)
        {
            _fill = fill;
            _radius = radius;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = fill;
            BorderStyle = BorderStyle.None;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);

            var bgClear = Parent?.BackColor is { A: 255 } parentBg ? parentBg : Theme.WindowBack;
            g.Clear(bgClear);

            var rect = new RectangleF(0.5f, 0.5f, Math.Max(2f, Width - 1f), Math.Max(2f, Height - 1f));
            using var path = Theme.RoundedRect(rect, _radius);
            using (var brush = new SolidBrush(_fill))
            {
                g.FillPath(brush, path);
            }

            using (var specPen = new Pen(Color.FromArgb(20, 255, 255, 255), 1f))
            {
                g.DrawLine(specPen, rect.Left + _radius, rect.Top + 0.5f, Math.Max(rect.Left + _radius, rect.Right - _radius), rect.Top + 0.5f);
            }

            using (var borderPen = new Pen(Theme.CardBorder, 1f))
            {
                g.DrawPath(borderPen, path);
            }
        }
    }

    // ----------------------------------------------------------------- AppleToggleSwitch

    public sealed class AppleToggleSwitch : Control
    {
        private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 16 };
        private float _checkAnim;
        private float _hoverAnim;
        private bool _checked;
        private bool _hovered;

        public event EventHandler? CheckedChanged;

        public AppleToggleSwitch(bool isChecked)
        {
            _checked = isChecked;
            _checkAnim = isChecked ? 1f : 0f;
            _animTimer.Tick += (_, _) => StepAnimation();

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
            Size = new Size(40, 22);
            BackColor = Theme.SurfaceCard;
            Cursor = Cursors.Hand;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                _animTimer.Start();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void StepAnimation()
        {
            var targetCheck = _checked ? 1f : 0f;
            var targetHover = _hovered && Enabled ? 1f : 0f;

            _checkAnim += (targetCheck - _checkAnim) * 0.34f;
            _hoverAnim += (targetHover - _hoverAnim) * 0.32f;

            if (Math.Abs(targetCheck - _checkAnim) < 0.01f && Math.Abs(targetHover - _hoverAnim) < 0.01f)
            {
                _checkAnim = targetCheck;
                _hoverAnim = targetHover;
                _animTimer.Stop();
            }

            Invalidate();
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
            {
                Focus();
                Checked = !Checked;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Space or Keys.Enter && Enabled)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            var bgClear = Parent?.BackColor is { A: 255 } parentBg ? parentBg : Theme.SurfaceCard;
            g.Clear(bgClear);

            var trackRect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
            var radius = trackRect.Height / 2f;

            var offTrack = Theme.Lerp(Color.FromArgb(40, 40, 48), Color.FromArgb(52, 52, 62), _hoverAnim);
            var onTrack = Theme.Lerp(Theme.AppleBlue, Theme.AppleBlueHover, _hoverAnim);
            var trackFill = Theme.Lerp(offTrack, onTrack, _checkAnim);

            var borderColor = Theme.Lerp(Color.FromArgb(42, 255, 255, 255), Color.FromArgb(80, 255, 255, 255), Math.Max(_checkAnim, _hoverAnim));
            Theme.FillRounded(g, trackRect, radius, trackFill);
            Theme.DrawRounded(g, trackRect, radius, borderColor, 1f);

            var knobDiameter = trackRect.Height - 4f;
            var knobX = trackRect.Left + 2f + (trackRect.Width - 4f - knobDiameter) * _checkAnim;
            var knobRect = new RectangleF(knobX, trackRect.Top + 2f, knobDiameter, knobDiameter);
            using var knobBrush = new SolidBrush(Enabled ? Color.White : Color.FromArgb(160, 160, 168));
            g.FillEllipse(knobBrush, knobRect);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // ----------------------------------------------------------------- AppleSegmentedControl

    public sealed class AppleSegmentedControl : Control
    {
        private readonly string[] _options;
        private int _selectedIndex;
        private int _hoverIndex = -1;

        public event EventHandler? SelectedIndexChanged;

        public AppleSegmentedControl(string[] options)
        {
            _options = options.Length > 0 ? options : new[] { "Seçenek" };
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            Size = new Size(152, 30);
            BackColor = Theme.SurfaceCard;
            Font = Theme.UiFontBold;
            Cursor = Cursors.Hand;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var next = Math.Clamp(value, 0, _options.Length - 1);
                if (_selectedIndex == next) return;
                _selectedIndex = next;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string SelectedValue => _options[_selectedIndex];

        public void SelectValue(string value)
        {
            var idx = Array.FindIndex(_options, o => o.Equals(value, StringComparison.OrdinalIgnoreCase));
            SelectedIndex = idx >= 0 ? idx : 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var idx = Math.Clamp(e.X * _options.Length / Math.Max(1, Width), 0, _options.Length - 1);
            if (idx != _hoverIndex)
            {
                _hoverIndex = idx;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            SelectedIndex = Math.Clamp(e.X * _options.Length / Math.Max(1, Width), 0, _options.Length - 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            var bgClear = Parent?.BackColor is { A: 255 } parentBg ? parentBg : Theme.SurfaceCard;
            g.Clear(bgClear);

            var outer = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(g, outer, 8f, Theme.WindowBack);
            Theme.DrawRounded(g, outer, 8f, Theme.InputBorder, 1f);

            var segWidth = (Width - 6f) / _options.Length;
            for (var i = 0; i < _options.Length; i++)
            {
                var segRect = new RectangleF(3f + i * segWidth, 3f, segWidth, Height - 6f);
                if (i == _selectedIndex)
                {
                    Theme.FillRounded(g, segRect, 6f, Color.FromArgb(44, 45, 54));
                    Theme.DrawRounded(g, segRect, 6f, Color.FromArgb(40, 255, 255, 255), 1f);
                }
                else if (i == _hoverIndex)
                {
                    Theme.FillRounded(g, segRect, 6f, Color.FromArgb(20, 255, 255, 255));
                }

                var color = i == _selectedIndex ? Theme.Text : Theme.SubText;
                Theme.DrawText(
                    g, _options[i], i == _selectedIndex ? Theme.UiFontBold : Theme.UiFont,
                    segRect, color, StringAlignment.Center, StringAlignment.Center);
            }
        }
    }
}
