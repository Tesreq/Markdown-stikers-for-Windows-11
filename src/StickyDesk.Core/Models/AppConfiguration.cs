namespace StickyDesk.Core;

public sealed class AppConfiguration
{
    public int Version { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<NoteRecord> Notes { get; set; } = [];
}

public sealed class AppSettings
{
    public int AutosaveDelayMs { get; set; } = 700;
}
