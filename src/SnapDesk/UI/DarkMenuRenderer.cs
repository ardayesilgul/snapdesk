namespace SnapDesk.UI;

/// <summary>Koyu tema menü çizimi (sağ tık ve açılır menüler).</summary>
public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item?.Enabled == false ? Theme.Disabled : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.SubText;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.ToolbarBorder);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.ToolbarBorder);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.ToolbarBack;
        public override Color ImageMarginGradientBegin => Theme.ToolbarBack;
        public override Color ImageMarginGradientMiddle => Theme.ToolbarBack;
        public override Color ImageMarginGradientEnd => Theme.ToolbarBack;
        public override Color MenuBorder => Theme.ToolbarBorder;
        public override Color MenuItemBorder => Theme.Accent;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color SeparatorDark => Theme.ToolbarBorder;
        public override Color SeparatorLight => Theme.ToolbarBorder;
    }
}
