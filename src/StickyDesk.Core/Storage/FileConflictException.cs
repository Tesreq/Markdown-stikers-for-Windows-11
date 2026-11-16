namespace StickyDesk.Core;

public sealed class FileConflictException(string path) : IOException($"Файл изменён другой программой: {path}");
