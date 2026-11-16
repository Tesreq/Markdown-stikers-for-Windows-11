using StickyDesk.Core;

namespace StickyDesk.UIHarness;

internal static class RenamingSessionChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var paths = new AppPaths(root);
        var files = new MarkdownFileService();
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        void Post(Action action) { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); }
        static bool WaitUntil(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 4000;
            while (!condition() && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            return condition();
        }
        var record = new NoteRecord { IsOwned = true, FilePath = Path.Combine("notes", "original.md") };
        File.WriteAllText(paths.Resolve(record.FilePath), "Исходный текст\r\n", new System.Text.UTF8Encoding(true));
        using (var session = new NoteSession(record, paths, files, 700, Post))
        {
            var oldPath = session.FilePath;
            session.View.BeginEditing();
            session.View.EditorControl.Text = "Новый **текст**";
            check(session.RenameTitle("Рабочие планы", false) && !Path.IsPathRooted(record.FilePath)
                && Path.GetFileName(session.FilePath) == "Рабочие планы.md" && !File.Exists(oldPath),
                "Renaming an owned note updates its relative storage path");
            check(WaitUntil(() => !session.IsDirty && files.Read(session.FilePath).EditorText == "Новый **текст**")
                && !File.Exists(oldPath) && files.Read(session.FilePath).HasBom,
                "Pending text autosaves to the renamed file with its original encoding");
            var configurationService = new ConfigurationService(paths);
            configurationService.Save(new AppConfiguration { Notes = [record] });
            var reloaded = configurationService.Load().Notes.Single();
            check(reloaded.Title == "Рабочие планы" && reloaded.FilePath == record.FilePath && File.Exists(paths.Resolve(reloaded.FilePath)),
                "The new title and file path survive a configuration reload");
            File.WriteAllText(session.FilePath, "Внешняя правка");
            check(WaitUntil(() => session.View.Markdown == "Внешняя правка") && !session.IsFileUnavailable,
                "FileSystemWatcher follows the new file name after renaming");
            var currentPath = session.FilePath;
            var occupied = Path.Combine(paths.Notes, "Занято.md");
            File.WriteAllText(occupied, "Другая заметка");
            check(!session.RenameTitle("Занято", false) && session.FilePath == currentPath && record.Title == "Рабочие планы"
                && session.View.TitleText == "Рабочие планы" && File.ReadAllText(occupied) == "Другая заметка",
                "A duplicate name leaves both note files and the current title intact");
            check(!session.RenameTitle("Недопустимое:имя", false) && session.FilePath == currentPath && File.Exists(currentPath),
                "An invalid title leaves the file and metadata unchanged");
            File.Delete(currentPath);
            check(session.RenameTitle("После удаления", false) && session.IsFileMissing && !File.Exists(session.FilePath)
                && session.RestoreMissingFile(false) && files.Read(session.FilePath).EditorText == "Внешняя правка"
                && Path.GetFileName(session.FilePath) == "После удаления.md",
                "A missing note can be renamed and explicitly restored under its new name");
        }
        var external = Path.Combine(root, "external.md");
        File.WriteAllText(external, "Внешний файл");
        using (var session = new NoteSession(new NoteRecord { FilePath = external }, paths, files, 700, Post))
        {
            check(session.RenameTitle("Внешняя заметка", false) && Path.GetDirectoryName(session.FilePath) == root
                && Path.GetFileName(session.FilePath) == "Внешняя заметка.md" && !session.Record.IsOwned && !File.Exists(external),
                "An external note is renamed in its own folder");
        }
    }
}
