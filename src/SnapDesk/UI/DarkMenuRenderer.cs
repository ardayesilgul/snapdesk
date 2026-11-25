using System.Drawing.Drawing2D;

namespace SnapDesk.UI;

/// <summary>Apple Obsidian koyu tema menü çizimi (yuvarlatılmış seçim hapları ve ince kenarlık).</summary>
public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.ToolbarBack);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new RectangleF(4f, 1.5f, Math.Max(1f, e.Item.Width - 8f), Math.Max(1f, e.Item.Height - 3f));
        if (e.Item.Selected && e.Item.Enabled)
        {
            Theme.FillRounded(g, rect, 6f, Color.FromArgb(42, 255, 255, 255));
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item?.Enabled == false
            ? Theme.Disabled
            : e.Item?.Selected == true
                ? Color.White
                : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Selected == true ? Color.White : Theme.SubText;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.DividerLine, 1f);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.ToolbarBorder, 1f);
        g.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.ToolbarBack;
        public override Color ImageMarginGradientBegin => Theme.ToolbarBack;
        public override Color ImageMarginGradientMiddle => Theme.ToolbarBack;
        public override Color ImageMarginGradientEnd => Theme.ToolbarBack;
        public override Color MenuBorder => Theme.ToolbarBorder;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Color.Transparent;
        public override Color MenuItemSelectedGradientBegin => Color.Transparent;
        public override Color MenuItemSelectedGradientEnd => Color.Transparent;
        public override Color SeparatorDark => Theme.DividerLine;
        public override Color SeparatorLight => Theme.DividerLine;
    }
}
