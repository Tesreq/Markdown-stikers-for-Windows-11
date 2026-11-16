using System.Text.Json;

namespace StickyDesk.Core;

public sealed class NoteRenameService(AppPaths paths, MarkdownFileService files)
{
    public List<string> Warnings { get; } = [];
    private string JournalPath(string id) => Path.Combine(paths.Operations, id + ".rename.json");

    public (string FilePath, bool IsMissing) Rename(NoteRecord record, string title, Action persist, Func<string, bool>? pathInUse = null)
    {
        if (!Guid.TryParseExact(record.Id, "N", out _)) throw new ArgumentException("Некорректный идентификатор заметки.");
        var journalPath = JournalPath(record.Id);
        if (File.Exists(journalPath)) RecoverOne(record, ReadJournal(journalPath), persist, journalPath);
        var source = paths.Resolve(record.FilePath);
        var destination = files.RenameDestination(source, title, pathInUse);
        MarkdownSnapshot? snapshot;
        try { snapshot = files.Read(source); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { snapshot = null; }
        var newStoredPath = Path.IsPathRooted(record.FilePath) ? destination : Path.GetRelativePath(paths.Root, destination);
        var journal = new RenameJournal(1, record.Id, record.FilePath, record.Title, newStoredPath, title, snapshot?.Fingerprint);
        Directory.CreateDirectory(paths.Operations);
        AtomicFile.Create(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal));
        var moved = false;
        try
        {
            var result = files.Rename(source, title, pathInUse);
            moved = !result.IsMissing && !string.Equals(source, destination, StringComparison.Ordinal);
            record.FilePath = newStoredPath;
            record.Title = title;
            persist();
            Finish(journalPath);
            return result;
        }
        catch (Exception failure)
        {
            if (moved)
            {
                try
                {
                    var sameLocation = string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
                    if ((!sameLocation && File.Exists(source)) || !File.Exists(destination)
                        || files.Read(destination).Fingerprint != journal.Fingerprint)
                        throw new IOException("Файлы изменились после переименования.");
                    File.Move(destination, source, false);
                }
                catch (Exception rollbackFailure) when (IsStorageError(rollbackFailure))
                {
                    paths.Log($"Rename rollback: {rollbackFailure}");
                    throw new RenameRecoveryException("Файл переименован, но сохранить новое расположение не удалось. "
                        + "Текст сохранён. Операция будет проверена при следующем запуске.", failure);
                }
            }
            record.FilePath = journal.OldPath;
            record.Title = journal.OldTitle;
            try { AtomicFile.Write(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal with { RolledBack = true })); }
            catch (Exception stateFailure) when (IsStorageError(stateFailure)) { paths.Log($"Rename rollback journal: {stateFailure}"); }
            // A persistence failure can occur after the replacement already reached disk.
            // Keep the intent until the original metadata is durably confirmed, too.
            try { persist(); Finish(journalPath); }
            catch (Exception rollbackFailure) when (IsStorageError(rollbackFailure)) { paths.Log($"Rename metadata rollback: {rollbackFailure}"); }
            throw;
        }
    }

    public void Recover(AppConfiguration configuration, Action persist)
    {
        if (!Directory.Exists(paths.Operations)) return;
        foreach (var journalPath in Directory.EnumerateFiles(paths.Operations, "*.rename.json"))
        {
            try
            {
                var journal = ReadJournal(journalPath);
                var record = configuration.Notes.FirstOrDefault(note => note.Id == journal.NoteId)
                    ?? throw new IOException("Запись заметки для операции не найдена.");
                RecoverOne(record, journal, persist, journalPath);
            }
            catch (Exception ex) when (IsStorageError(ex) || ex is JsonException)
            {
                paths.Log($"Rename recovery: {ex}");
                Warnings.Add("Не удалось завершить восстановление названия одной из заметок. Данные операции сохранены.");
            }
        }
    }

    private void RecoverOne(NoteRecord record, RenameJournal journal, Action persist, string journalPath)
    {
        var source = paths.Resolve(journal.OldPath);
        var destination = paths.Resolve(journal.NewPath);
        if (record.Id != journal.NoteId || !string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Некорректный журнал переименования.");
        var sourceExists = File.Exists(source);
        var destinationExists = File.Exists(destination);
        var sameLocation = string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
        var committed = record.FilePath == journal.NewPath && record.Title == journal.NewTitle;
        var useNew = journal.Fingerprint is null || (!sourceExists && !destinationExists)
            || (destinationExists && (committed || files.Read(destination).Fingerprint == journal.Fingerprint));
        if (journal.RolledBack)
        {
            if (sourceExists)
            {
                if (journal.Fingerprint is not null && files.Read(source).Fingerprint != journal.Fingerprint)
                    throw new IOException("Исходное расположение изменено после отката.");
                if (sameLocation && !string.Equals(source, destination, StringComparison.Ordinal)) File.Move(destination, source, false);
            }
            else if (destinationExists)
            {
                if (files.Read(destination).Fingerprint != journal.Fingerprint) throw new IOException("Файл изменился после отката.");
                File.Move(destination, source, false);
            }
            useNew = false;
        }
        else if (sameLocation && sourceExists)
        {
            files.Rename(source, journal.NewTitle);
            useNew = true;
        }
        else if (!destinationExists && sourceExists) useNew = false;
        else if (!useNew && (!sourceExists || files.Read(source).Fingerprint != journal.Fingerprint))
            throw new IOException("Оба расположения изменены. Автоматический выбор версии небезопасен.");
        var previousPath = record.FilePath;
        var previousTitle = record.Title;
        record.FilePath = useNew ? journal.NewPath : journal.OldPath;
        record.Title = useNew ? journal.NewTitle : journal.OldTitle;
        try { persist(); Finish(journalPath); }
        catch { record.FilePath = previousPath; record.Title = previousTitle; throw; }
    }

    private static RenameJournal ReadJournal(string path)
    {
        var journal = JsonSerializer.Deserialize<RenameJournal>(File.ReadAllBytes(path)) ?? throw new JsonException("Пустой журнал.");
        if (journal.Version != 1 || !Guid.TryParseExact(journal.NoteId, "N", out _)
            || string.IsNullOrWhiteSpace(journal.OldPath) || string.IsNullOrWhiteSpace(journal.NewPath) || string.IsNullOrWhiteSpace(journal.NewTitle))
            throw new JsonException("Некорректный журнал переименования.");
        return journal;
    }

    private void Finish(string journalPath)
    {
        try { File.Delete(journalPath); File.Delete(journalPath + ".bak"); }
        catch (Exception ex) when (IsStorageError(ex)) { paths.Log($"Rename journal cleanup: {ex}"); }
    }

    private static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException;
    private sealed record RenameJournal(int Version, string NoteId, string OldPath, string? OldTitle, string NewPath, string NewTitle, string? Fingerprint, bool RolledBack = false);
}

public sealed class RenameRecoveryException(string message, Exception inner) : IOException(message, inner);
