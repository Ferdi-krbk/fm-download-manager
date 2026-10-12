using System.Drawing;
using System.Windows.Forms;

namespace McDm.App;

public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var rc = new Rectangle(Point.Empty, e.Item.Size);
        var brush = e.Item.Selected ? new SolidBrush(Color.FromArgb(0x1A, 0x1A, 0x1A)) : new SolidBrush(Color.FromArgb(0x0A, 0x0A, 0x0A));
        e.Graphics.FillRectangle(brush, rc);
        brush.Dispose();
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.FromArgb(0xF2, 0xF2, 0xF2);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        var pen = new Pen(Color.FromArgb(0x2A, 0x2A, 0x2A));
        e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        pen.Dispose();
    }
}

public sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(0x3A, 0x3A, 0x3A);
    public override Color ToolStripDropDownBackground => Color.FromArgb(0x0A, 0x0A, 0x0A);
    public override Color ImageMarginGradientBegin => Color.FromArgb(0x0A, 0x0A, 0x0A);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(0x0A, 0x0A, 0x0A);
    public override Color ImageMarginGradientEnd => Color.FromArgb(0x0A, 0x0A, 0x0A);
    public override Color MenuItemSelected => Color.FromArgb(0x1A, 0x1A, 0x1A);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(0x1A, 0x1A, 0x1A);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(0x1A, 0x1A, 0x1A);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color CheckBackground => Color.FromArgb(0x3A, 0x1A, 0x00);
    public override Color CheckSelectedBackground => Color.FromArgb(0xFF, 0x6A, 0x00);
}
