using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace StickyDesk;

internal sealed class NoteStatus : Label
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Error { get; set; }

    internal NoteStatus()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        Font = new Font("Segoe UI", 8.5f);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var dot = new SolidBrush(Error ? Color.FromArgb(185, 78, 71) : Color.FromArgb(119, 147, 115));
        e.Graphics.FillEllipse(dot, 0, (Height - 5 * scale) / 2, 5 * scale, 5 * scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(12 * scale), 0, Width - (int)(12 * scale), Height),
            ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}
