namespace SnapDesk.UI;

/// <summary>
/// Apple Obsidian Glass & Linear tasarım dilinde onay iletişim kutusu.
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
            ClientSize = new Size(440, 198),
            ShowInTaskbar = false,
            TopMost = true,
            KeyPreview = true,
        };

        UiFactory.ApplyRoundedRegion(form, 16);
        UiFactory.AttachSmoothEntrance(form);

        form.Resize += (_, _) => UiFactory.ApplyRoundedRegion(form, 16);
        form.Paint += (_, e) => Theme.PaintObsidianSurface(e.Graphics, form.Width, form.Height, 16f);

        var titleLabel = new Label
        {
            Text = title,
            Font = Theme.TitleFont,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            AutoSize = false,
            Bounds = new Rectangle(24, 22, 392, 28),
        };

        var messageLabel = new Label
        {
            Text = message,
            Font = Theme.UiFont,
            ForeColor = Theme.SubText,
            BackColor = Color.Transparent,
            AutoSize = false,
            Bounds = new Rectangle(24, 54, 392, 26),
        };

        var check = UiFactory.Check(checkText);
        check.BackColor = Theme.ToolbarBack;
        check.Location = new Point(24, 94);

        var no = UiFactory.Button("Vazgeç", 100, 34);
        no.BackColor = Theme.ToolbarBack;
        no.DialogResult = DialogResult.Cancel;
        no.Location = new Point(208, 142);

        var yes = UiFactory.Button("Evet", 100, 34, primary: true);
        yes.BackColor = Theme.ToolbarBack;
        yes.DialogResult = DialogResult.OK;
        yes.Location = new Point(316, 142);

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
