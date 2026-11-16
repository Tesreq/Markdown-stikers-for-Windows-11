using StickyDesk.Core;

namespace StickyDesk.UIHarness;

internal static class RecoverySessionChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var paths = new AppPaths(root);
        var files = new MarkdownFileService();
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        NoteSession Open(NoteRecord record) => new(record, paths, files, 700,
            action => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); });
        static bool WaitUntil(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 4000;
            while (!condition() && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            return condition();
        }
        static void Edit(NoteSession session, string text)
        {
            session.View.BeginEditing();
            session.View.EditorControl.Text = text;
        }

        var emptyRecord = new NoteRecord { FilePath = Path.Combine(paths.Notes, "missing-empty.md") };
        File.WriteAllText(paths.RecoveryFile(emptyRecord.Id), "");
        using (var session = Open(emptyRecord))
        {
            File.WriteAllText(session.FilePath, "Вернувшийся файл");
            check(WaitUntil(() => !session.IsFileUnavailable), "A returned file is detected after restart");
            check(session.View.Markdown.Length == 0 && !session.Flush(false) && File.ReadAllText(session.FilePath) == "Вернувшийся файл",
                "An intentionally empty recovery is protected from a different returned file");
            Edit(session, "Вернувшийся файл");
            check(WaitUntil(() => !session.IsDirty && !File.Exists(paths.RecoveryFile(emptyRecord.Id))),
                "Matching the returned file clears the conflict and recovery copy");
        }

        var returnedRecord = new NoteRecord { FilePath = Path.Combine(paths.Notes, "returned.md") };
        File.WriteAllText(returnedRecord.FilePath, "Исходная версия");
        using (var session = Open(returnedRecord))
        {
            File.Delete(session.FilePath);
            check(WaitUntil(() => session.IsFileMissing), "Deletion is detected before local changes");
            Edit(session, "Изменения без файла");
            check(File.ReadAllText(paths.RecoveryFile(returnedRecord.Id)) == "Изменения без файла",
                "Editing a missing file immediately preserves the current text");
            File.WriteAllText(session.FilePath, "Внешняя версия");
            check(WaitUntil(() => !session.IsFileUnavailable) && !session.Flush(false)
                && session.View.Markdown == "Изменения без файла" && File.ReadAllText(session.FilePath) == "Внешняя версия",
                "A different returned file never overwrites or is overwritten by local text");
            Edit(session, "Внешняя версия");
            check(WaitUntil(() => !session.IsDirty && !File.Exists(paths.RecoveryFile(returnedRecord.Id))),
                "Editing to the external version resolves a conflict without a dialog");
        }

        var relinkRecord = new NoteRecord { FilePath = Path.Combine(paths.Notes, "relink-source.md") };
        File.WriteAllText(relinkRecord.FilePath, "Оригинал");
        var destination = Path.Combine(paths.Notes, "relink-destination.md");
        File.WriteAllText(destination, "Совпадающий текст");
        using (var session = Open(relinkRecord))
        {
            Edit(session, "Совпадающий текст");
            check(session.Relink(destination) && !session.IsDirty && !File.Exists(paths.RecoveryFile(relinkRecord.Id)),
                "Relinking an identical file completes without a false conflict");
            Edit(session, "Свой вариант");
            var other = Path.Combine(paths.Notes, "relink-other.md");
            File.WriteAllText(other, "Другой вариант");
            check(session.Relink(other) && File.Exists(paths.RecoveryFile(relinkRecord.Id))
                && File.ReadAllText(paths.RecoveryFile(relinkRecord.Id)) == "Свой вариант" && !session.Flush(false),
                "Relinking a different file preserves pending text before any autosave");
            Edit(session, "Другой вариант");
            check(WaitUntil(() => !session.IsDirty && !File.Exists(paths.RecoveryFile(relinkRecord.Id))),
                "Matching a relinked file clears its conflict");
            Edit(session, "Следующее изменение");
            check(WaitUntil(() => !session.IsDirty && File.ReadAllText(other) == "Следующее изменение"),
                "Autosave resumes after a relink conflict is resolved");
        }
    }
}
