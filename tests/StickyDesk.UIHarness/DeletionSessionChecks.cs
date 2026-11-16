using StickyDesk.Core;

namespace StickyDesk.UIHarness;

internal static class DeletionSessionChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var paths = new AppPaths(root);
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        NoteSession Open(string name, string text)
        {
            var record = new NoteRecord { FilePath = Path.Combine(paths.Notes, name + ".md") };
            File.WriteAllText(record.FilePath, text);
            return new NoteSession(record, paths, new MarkdownFileService(), 700,
                action => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); });
        }
        static bool WaitUntil(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 4000;
            while (!condition() && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            return condition();
        }
        using (var session = Open("pending", "Версия на диске"))
        {
            session.View.BeginEditing();
            session.View.EditorControl.Text = "Последний текст стикера";
            var recycled = Path.Combine(root, "recycled-test.md");
            check(session.RecycleFile(path => File.Move(path, recycled)) && !File.Exists(session.FilePath)
                && File.ReadAllText(recycled) == "Версия на диске",
                "Deletion moves the disk version without overwriting pending text");
            check(File.ReadAllText(paths.RecoveryFile(session.Record.Id)) == "Последний текст стикера",
                "Deletion preserves the current unsaved text in recovery storage");
        }
        using (var session = Open("failed", "Исходный текст"))
        {
            session.View.BeginEditing();
            session.View.EditorControl.Text = "Несохранённый текст";
            try { session.RecycleFile(_ => throw new IOException("Test failure")); throw new InvalidOperationException("Failure ignored"); }
            catch (IOException) { }
            check(!session.View.IsDisposed && File.ReadAllText(session.FilePath) == "Исходный текст"
                && File.ReadAllText(paths.RecoveryFile(session.Record.Id)) == "Несохранённый текст",
                "Failed deletion keeps the file, sticker and recovery text");
            check(WaitUntil(() => !session.IsDirty && File.ReadAllText(session.FilePath) == "Несохранённый текст"),
                "Autosave resumes after a failed deletion");
            try { session.RecycleFile(_ => throw new OperationCanceledException()); throw new InvalidOperationException("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            check(!session.View.IsDisposed && File.Exists(session.FilePath), "Cancelled recycling keeps the sticker and file");
        }
        using (var session = Open("already-missing", "Текст без файла"))
        {
            File.Delete(session.FilePath);
            check(session.RecycleFile(_ => throw new InvalidOperationException("No file to recycle"))
                && !File.Exists(session.FilePath) && File.ReadAllText(paths.RecoveryFile(session.Record.Id)) == "Текст без файла",
                "An already deleted file can be removed without recreating it or losing the text");
        }
        var neighbor = Path.Combine(paths.Notes, "neighbor.md");
        File.WriteAllText(neighbor, "Не трогать");
        using (var session = Open("real-recycle", "Тест Корзины Windows"))
        {
            check(session.RecycleFile() && !File.Exists(session.FilePath)
                && File.ReadAllText(paths.RecoveryFile(session.Record.Id)) == "Тест Корзины Windows" && File.ReadAllText(neighbor) == "Не трогать",
                "Windows recycling removes only the test note and preserves its recovery copy");
        }
        var emptyPaths = new AppPaths(Path.Combine(root, "empty-session"));
        new ConfigurationService(emptyPaths).Save(new AppConfiguration());
        using (var manager = new NoteManager(emptyPaths))
        {
            Application.DoEvents();
            check(manager.Count == 0 && new ConfigurationService(emptyPaths).Load().Notes.Count == 0,
                "An intentionally empty session stays empty after restart");
        }
    }
}
