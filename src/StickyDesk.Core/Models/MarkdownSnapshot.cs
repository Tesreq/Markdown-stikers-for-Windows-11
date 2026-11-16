namespace StickyDesk.Core;

public sealed record MarkdownSnapshot(string Text, string Fingerprint, string NewLine, bool HasBom)
{
    public string EditorText => Normalize(Text);
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
