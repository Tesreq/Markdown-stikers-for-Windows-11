using System.Drawing.Drawing2D;

namespace StickyDesk;

internal readonly record struct NoteTheme(Color Surface, Color Accent)
{
    internal static readonly Color Ink = Color.FromArgb(42, 48, 57);
    internal static readonly Color Muted = Color.FromArgb(116, 123, 132);
    internal Color Border => Blend(Surface, Ink, .13f);
    internal Color Divider => Blend(Surface, Ink, .12f);

    internal static NoteTheme From(string hex) => hex.ToUpperInvariant() switch
    {
        "#FFF2B3" => new(Color.FromArgb(255, 248, 222), Color.FromArgb(190, 151, 57)),
        "#DDF2C2" => new(Color.FromArgb(237, 246, 233), Color.FromArgb(116, 151, 100)),
        "#D9EEFA" => new(Color.FromArgb(235, 243, 252), Color.FromArgb(108, 145, 184)),
        "#F8DCE8" => new(Color.FromArgb(251, 237, 242), Color.FromArgb(190, 125, 153)),
        "#F5F5F5" => new(Color.FromArgb(248, 249, 251), Color.FromArgb(139, 151, 169)),
        _ => new(Color.FromArgb(248, 249, 251), Color.FromArgb(139, 151, 169))
    };

    internal static Color Blend(Color first, Color second, float amount) => Color.FromArgb(
        (int)(first.R + (second.R - first.R) * amount),
        (int)(first.G + (second.G - first.G) * amount),
        (int)(first.B + (second.B - first.B) * amount));

    internal static GraphicsPath Round(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) return path;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
