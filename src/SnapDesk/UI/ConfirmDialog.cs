namespace SnapDesk.UI;

/// <summary>
/// <summary>Kalıcı onay iletişim kutusu ("Bir daha sorma" seçeneğiyle).</summary>
/// (Enter = onayla, Esc = vazgeç).
/// </summary>
public static class ConfirmDialog
{
    public sealed record Result(bool Confirmed, bool DontAskAgain);

    public static Result Ask(string title, string message, string checkText)
    {
        using var form = new Form
        {
            Text = "SnapDesk",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = Theme.ToolbarBack,
            ForeColor = Theme.Text,
            ClientSize = new Size(430, 190),
            ShowInTaskbar = false,
            TopMost = true,
            KeyPreview = true,
        };

        form.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.ToolbarBorder, 1f);
            e.Graphics.DrawRectangle(pen, 0, 0, form.ClientSize.Width - 1, form.ClientSize.Height - 1);
        };

        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            AutoSize = false,
            Bounds = new Rectangle(22, 20, 386, 28),
        };

        var messageLabel = new Label
        {
            Text = message,
            Font = Theme.UiFont,
            ForeColor = Theme.SubText,
            BackColor = Color.Transparent,
            AutoSize = false,
            Bounds = new Rectangle(22, 52, 386, 24),
        };

        var check = UiFactory.Check(checkText);
        check.ForeColor = Theme.SubText;
        check.Location = new Point(22, 92);

        var no = UiFactory.Button("Vazgeç", 96, 34);
        no.DialogResult = DialogResult.Cancel;
        no.Location = new Point(214, 136);

        var yes = UiFactory.Button("Evet", 96, 34, primary: true);
        yes.DialogResult = DialogResult.OK;
        yes.Location = new Point(318, 136);

        form.Controls.AddRange(new Control[] { titleLabel, messageLabel, check, no, yes });
        form.AcceptButton = yes;
        form.CancelButton = no;

        form.Shown += (_, _) =>
        {
            var cursor = Cursor.Position;
            var screen = Screen.FromPoint(cursor).WorkingArea;
            form.Location = new Point(
                Math.Clamp(cursor.X - form.Width / 2, screen.Left, screen.Right - form.Width),
                Math.Clamp(cursor.Y - form.Height / 2, screen.Top, screen.Bottom - form.Height));
            form.Activate();
            yes.Focus();
        };

        var result = form.ShowDialog();
        return new Result(result == DialogResult.OK, check.Checked);
    }
}
