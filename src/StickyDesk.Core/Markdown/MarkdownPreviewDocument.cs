namespace StickyDesk.Core;

public sealed record PreviewLink(int Start, int Length, string Url);
public sealed record PreviewSourceRange(int Start, int Length, int SourceStart, int SourceLength);
public sealed record MarkdownPreviewDocument(string Text, string Rtf, IReadOnlyList<PreviewLink> Links, IReadOnlyList<PreviewSourceRange> SourceRanges)
{
    public int SourceOffset(int displayOffset, int sourceLength)
    {
        var previous = 0;
        foreach (var range in SourceRanges)
        {
            if (displayOffset < range.Start) return Math.Clamp(previous, 0, sourceLength);
            if (displayOffset >= range.Start + range.Length) { previous = range.SourceStart + range.SourceLength; continue; }
            var offset = Math.Min(displayOffset - range.Start, Math.Max(0, range.SourceLength - 1));
            return Math.Clamp(range.SourceStart + offset, 0, sourceLength);
        }
        return sourceLength;
    }
}
