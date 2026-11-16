using System.Text;
using System.Text.Json;
using StickyDesk.Core;

namespace StickyDesk.UIHarness;

internal static class StorageSafetySessionChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var paths = new AppPaths(root);
        var files = new MarkdownFileService();
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        NoteSession Open(NoteRecord record) => new(record, paths, files, 700,
            action => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); });
        static void LeaveJournal(string path, string original, string pending) =>
            File.WriteAllBytes(ProtectedFileWrite.JournalPath(path), JsonSerializer.SerializeToUtf8Bytes(new
            { Version = 1, Original = Encoding.UTF8.GetBytes(original), Pending = Encoding.UTF8.GetBytes(pending) }));
        static void Edit(NoteSession session, string text)
        {
            session.View.BeginEditing();
            session.View.EditorControl.Text = text;
        }

        var partial = new NoteRecord { FilePath = Path.Combine(paths.Notes, "partial.md") };
        File.WriteAllBytes(partial.FilePath, [0xD0]);
        LeaveJournal(partial.FilePath, "Исходный текст", "Полный новый текст 🙂");
        using (var session = Open(partial))
        {
            check(session.View.Markdown == "Полный новый текст 🙂" && session.IsDirty && session.IsFileUnavailable
                && !session.Flush(false) && File.ReadAllBytes(partial.FilePath).SequenceEqual(new byte[] { 0xD0 })
                && File.ReadAllText(paths.RecoveryFile(partial.Id)) == "Полный новый текст 🙂",
                "An interrupted UTF-8 write restores complete text in the sticker without overwriting the partial file");
        }

        var newer = new NoteRecord { FilePath = Path.Combine(paths.Notes, "newer-recovery.md") };
        File.WriteAllText(newer.FilePath, "Версия на диске");
        LeaveJournal(newer.FilePath, "Версия на диске", "Текст из журнала");
        File.WriteAllText(paths.RecoveryFile(newer.Id), "Более свежая копия редактора");
        using (var session = Open(newer))
        {
            check(session.View.Markdown == "Более свежая копия редактора" && session.IsDirty && !session.IsFileUnavailable
                && !session.Flush(false) && File.ReadAllText(newer.FilePath) == "Версия на диске",
                "A newer editor recovery takes precedence over the interrupted-write journal and keeps a conflict");
        }

        var dirty = new NoteRecord { FilePath = Path.Combine(paths.Notes, "dirty.md") };
        File.WriteAllText(dirty.FilePath, "Исходник");
        using (var session = Open(dirty))
        {
            Edit(session, "Текущий ввод пользователя");
            LeaveJournal(dirty.FilePath, "Исходник", "Старая незавершённая запись");
            File.WriteAllText(dirty.FilePath, "Внешний вариант");
            check(!session.Flush(false) && session.View.Markdown == "Текущий ввод пользователя"
                && File.ReadAllText(dirty.FilePath) == "Внешний вариант"
                && File.ReadAllText(paths.RecoveryFile(dirty.Id)) == "Текущий ввод пользователя"
                && Directory.GetFiles(paths.Notes, "dirty.md.stickydesk-write.recovered-*.json").Length == 1,
                "Discovering an interrupted write during save preserves current input and the external version");
        }

        var rollback = new NoteRecord { FilePath = Path.Combine(paths.Notes, "rename-original.md"), Title = "Прежнее название" };
        File.WriteAllText(rollback.FilePath, "Исходник переименования");
        using (var session = Open(rollback))
        {
            Edit(session, "Несохранённый ввод");
            session.MetadataChanged += () => throw new IOException("Simulated configuration failure");
            var renamed = session.RenameTitle("rename-rolled-back", false);
            var config = new AppConfiguration { Notes = [rollback] };
            var recovery = new NoteRenameService(paths, files);
            recovery.Recover(config, () => new ConfigurationService(paths).SaveCheckpoint(config));
            check(!renamed && rollback.Title == "Прежнее название" && session.View.TitleText == rollback.Title
                && Path.GetFileName(session.FilePath) == "rename-original.md" && File.ReadAllText(session.FilePath) == "Исходник переименования"
                && File.ReadAllText(paths.RecoveryFile(rollback.Id)) == "Несохранённый ввод"
                && recovery.Warnings.Count == 0 && !File.Exists(Path.Combine(paths.Operations, rollback.Id + ".rename.json")),
                "A failed rename restores the visible title and file path while retaining unsaved editor text");
        }

        var unsafeRollback = new NoteRecord { FilePath = Path.Combine(paths.Notes, "unsafe-original.md"), Title = "До сбоя" };
        var originalPath = unsafeRollback.FilePath;
        File.WriteAllText(originalPath, "Прежняя версия");
        using (var session = Open(unsafeRollback))
        {
            Edit(session, "Мой новый текст");
            session.MetadataChanged += () =>
            {
                File.WriteAllText(session.FilePath, "Чужая правка после перемещения");
                throw new IOException("Simulated configuration failure after external edit");
            };
            var renamed = session.RenameTitle("unsafe-moved", false);
            var oldConfig = new AppConfiguration { Notes = [new NoteRecord { Id = unsafeRollback.Id, FilePath = originalPath, Title = "До сбоя" }] };
            var recovery = new NoteRenameService(paths, files);
            recovery.Recover(oldConfig, () => throw new InvalidOperationException("An ambiguous operation must not be committed"));
            check(!renamed && !session.Flush(false) && session.View.Markdown == "Мой новый текст"
                && File.ReadAllText(session.FilePath) == "Чужая правка после перемещения" && !File.Exists(originalPath)
                && recovery.Warnings.Count == 1 && File.Exists(Path.Combine(paths.Operations, unsafeRollback.Id + ".rename.json"))
                && File.ReadAllText(paths.RecoveryFile(unsafeRollback.Id)) == "Мой новый текст",
                "An unsafe rename rollback preserves both local input and the externally changed destination for recovery");
        }
    }
}
