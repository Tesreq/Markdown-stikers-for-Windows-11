using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace StickyDesk;

internal enum NoteGlyph { Menu, Edit, Check, Resize }

internal sealed class NoteActionButton : Control
{
    private bool _hovered;
    private bool _pressed;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal NoteGlyph Glyph { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Filled { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color Accent { get; set; }

    internal NoteActionButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.PushButton;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9);
        ForeColor = NoteTheme.Ink;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        using var outline = NoteTheme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8 * scale);
        if (Glyph != NoteGlyph.Resize && (Filled || _hovered || _pressed))
        {
            using var fill = new SolidBrush(NoteTheme.Blend(BackColor, Accent, _pressed ? .26f : _hovered ? .19f : .11f));
            g.FillPath(fill, outline);
        }
        if (Focused && ShowFocusCues && Glyph != NoteGlyph.Resize)
        {
            using var border = new Pen(Accent, scale);
            g.DrawPath(border, outline);
        }
        var hasText = Text.Length > 0;
        var left = hasText ? 10 * scale : (Width - 16 * scale) / 2;
        var top = (Height - 16 * scale) / 2;
        using var pen = new Pen(Glyph == NoteGlyph.Resize ? NoteTheme.Blend(BackColor, NoteTheme.Ink, .35f) : ForeColor, 1.45f * scale)
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        PointF P(float x, float y) => new(left + x * scale, top + y * scale);
        switch (Glyph)
        {
            case NoteGlyph.Menu:
                using (var dot = new SolidBrush(ForeColor))
                    foreach (var x in new[] { 3f, 8f, 13f }) g.FillEllipse(dot, left + (x - 1) * scale, top + 7 * scale, 2 * scale, 2 * scale);
                break;
            case NoteGlyph.Edit:
                g.DrawPolygon(pen, [P(3, 10), P(10.5f, 2.5f), P(13.5f, 5.5f), P(6, 13), P(2, 14)]);
                g.DrawLine(pen, P(9, 4), P(12, 7));
                break;
            case NoteGlyph.Check:
                g.DrawLines(pen, [P(3, 8), P(6.5f, 11.5f), P(13, 4.5f)]);
                break;
            case NoteGlyph.Resize:
                g.DrawLine(pen, P(8, 13), P(13, 8));
                g.DrawLine(pen, P(12, 13), P(13, 12));
                break;
        }
        if (hasText)
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(left + 23 * scale), 0, Width - (int)(left + 27 * scale), Height),
                ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}
