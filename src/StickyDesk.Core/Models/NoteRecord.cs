namespace StickyDesk.Core;

public sealed class NoteRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? Title { get; set; }
    public string FilePath { get; set; } = "";
    public bool IsOwned { get; set; }
    public bool IsClosed { get; set; }
    public DateTimeOffset? LastOpenedUtc { get; set; }
    // Screen coordinates are physical pixels; size is in 96-DPI logical pixels.
    public int X { get; set; } = 120;
    public int Y { get; set; } = 120;
    public int Width { get; set; } = 280;
    public int Height { get; set; } = 300;
    public string Color { get; set; } = "#FFF2B3";
}
