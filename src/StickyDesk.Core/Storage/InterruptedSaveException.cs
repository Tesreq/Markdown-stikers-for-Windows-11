namespace StickyDesk.Core;

public sealed class InterruptedSaveException(MarkdownSnapshot pending, MarkdownSnapshot? current, string archivePath)
    : IOException("Обнаружена незавершённая запись. Сохранены версии для восстановления.")
{
    public MarkdownSnapshot Pending { get; } = pending;
    public MarkdownSnapshot? Current { get; } = current;
    public string ArchivePath { get; } = archivePath;
}
