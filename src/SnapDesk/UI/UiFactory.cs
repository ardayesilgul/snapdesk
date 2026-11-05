namespace SnapDesk.UI;

/// <summary>Koyu temalı ortak denetim üreticileri.</summary>
public static class UiFactory
{
    public static Button Button(string text, int width = 96, int height = 32, bool primary = false)
    {
        var button = new ThemedButton(primary)
        {
            Text = text,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Theme.Accent : Theme.ToolbarBackAlt,
            ForeColor = primary ? Color.White : Theme.Text,
            Font = primary ? Theme.UiFontBold : Theme.UiFont,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
        };

        button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.ToolbarBorder;
        button.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(94, 168, 255)
            : Theme.Hover;
        button.FlatAppearance.MouseDownBackColor = primary
            ? Color.FromArgb(56, 132, 220)
            : Theme.ToolbarBorder;

        return button;
    }

    private sealed class ThemedButton : Button
    {
        private readonly bool _primary;

        public ThemedButton(bool primary)
        {
            _primary = primary;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            if (Enabled)
            {
                base.OnPaint(pevent);
                return;
            }

            var g = pevent.Graphics;
            var rect = ClientRectangle;
            using var back = new SolidBrush(Color.FromArgb(44, 44, 50));
            using var border = new Pen(Color.FromArgb(62, 62, 70));
            g.FillRectangle(back, rect);
            g.DrawRectangle(border, 0, 0, rect.Width - 1, rect.Height - 1);
            TextRenderer.DrawText(
                g,
                Text,
                Font,
                rect,
                Color.FromArgb(120, 120, 130),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    public static TextBox TextBox(bool multiline = false)
        => new()
        {
            Multiline = multiline,
            BackColor = Theme.ToolbarBackAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.UiFont,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
        };

    public static CheckBox Check(string text, bool @checked = false)
        => new()
        {
            Text = text,
            Checked = @checked,
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = Theme.UiFont,
            // Flat işaret kutusu beyaz kutuya beyaz onay çiziyordu; işaretli
            // durum görünmüyordu. Sistem çizimi koyu zeminde okunaklı.
            FlatStyle = FlatStyle.Standard,
        };

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
        => new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.ToolbarBackAlt,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.UiFont,
        };

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

    public static void StyleForm(Form form)
    {
        form.BackColor = Theme.ToolbarBack;
        form.ForeColor = Theme.Text;
        form.Font = Theme.UiFont;
        form.AutoScaleMode = AutoScaleMode.Dpi;

        if (form.IsHandleCreated)
        {
            ApplyDarkTitleBar(form);
        }
        else
        {
            form.HandleCreated += (_, _) => ApplyDarkTitleBar(form);
        }
    }

    /// <summary>Windows 10/11 başlık çubuğunu koyu temaya çevirir.</summary>
    private static void ApplyDarkTitleBar(Form form)
    {
        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref enabled, sizeof(int));
        }
        catch
        {
            // eski Windows sürümlerinde yoksay
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
