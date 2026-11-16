using System.Drawing.Drawing2D;

namespace StickyDesk;

internal static class ModernMenu
{
    internal static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Segoe UI", 9.5f), BackColor = Color.White, ForeColor = NoteTheme.Ink,
            ShowImageMargin = false, Padding = new Padding(6), Renderer = new Renderer()
        };
        menu.ItemAdded += (_, e) =>
        {
            if (e.Item is not null && e.Item is not ToolStripSeparator) e.Item.Padding = new Padding(8, 6, 10, 6);
        };
        menu.Disposed += (_, _) => menu.Font.Dispose();
        return menu;
    }

    internal static void StyleDropDown(ToolStripMenuItem item)
    {
        item.DropDown.Renderer = new Renderer();
        item.DropDown.Padding = new Padding(6);
        item.DropDown.BackColor = Color.White;
        item.DropDown.ForeColor = NoteTheme.Ink;
        foreach (ToolStripItem child in item.DropDownItems) child.Padding = new Padding(8, 6, 10, 6);
    }

    private sealed class Renderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Color.White);
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var border = new Pen(Color.FromArgb(226, 229, 234));
            e.Graphics.DrawRectangle(border, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = NoteTheme.Round(new RectangleF(1, 0, e.Item.Width - 2, e.Item.Height - 1), 5);
            using var fill = new SolidBrush(Color.FromArgb(241, 243, 246));
            e.Graphics.FillPath(fill, path);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(235, 237, 240));
            e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = NoteTheme.Muted;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = e.ImageRectangle;
            using var pen = new Pen(NoteTheme.Ink, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLines(pen, [new(bounds.Left + 2, bounds.Top + bounds.Height / 2), new(bounds.Left + 5, bounds.Bottom - 4), new(bounds.Right - 2, bounds.Top + 3)]);
        }
    }
}
