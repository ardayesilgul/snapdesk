using SnapDesk.Core;

namespace SnapDesk.Editor;

/// <summary>
/// Monitör başına tam ekran, çerçevesiz kaplama. Çizimi CaptureSession yapar;
/// bu pencere yalnızca olayları iletir ve odağı korur.
/// </summary>
public sealed class OverlayForm : Form
{
    private readonly CaptureSession _session;

    public MonitorInfo Monitor { get; }

    public OverlayForm(CaptureSession session, MonitorInfo monitor)
    {
        _session = session;
        Monitor = monitor;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = monitor.Bounds;
        TopMost = true;
        BackColor = Color.Black;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        Cursor = Cursors.Cross;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BringToFront();
        Activate();
        Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        _session.Render(e.Graphics, this);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Activate();
        Focus();
        base.OnMouseDown(e);
        _session.OnMouseDown(this, ToScreen(e.Location), e.Button, ModifierKeys);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _session.OnMouseMove(this, ToScreen(e.Location), e.Button);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _session.OnMouseUp(this, ToScreen(e.Location), e.Button);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        _session.OnDoubleClick(this, ToScreen(e.Location), e.Button);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _session.OnMouseWheel(this, ToScreen(e.Location), e.Delta);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var handled = _session.OnKeyDown(e);
        if (handled)
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

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // Araç çubuğu tıklamaları odağı çalmasın diye pencereyi öne almıyoruz;
        // yalnızca kaplama kendiliğinden kapanmışsa oturum bunu bilir.
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: alt+tab listesinde görünme
            return cp;
        }
    }

    private Point ToScreen(Point client)
        => new(Monitor.Bounds.Left + client.X, Monitor.Bounds.Top + client.Y);
}
