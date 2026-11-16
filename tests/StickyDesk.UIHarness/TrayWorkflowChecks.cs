using StickyDesk.Core;

namespace StickyDesk.UIHarness;

internal static class TrayWorkflowChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var paths = new AppPaths(root);
        var store = new ConfigurationService(paths);
        var records = Enumerable.Range(0, 4).Select(index => new NoteRecord
        {
            FilePath = Path.Combine(paths.Notes, $"recent-{index}.md"), Title = $"Заметка {index}",
            IsClosed = true, Color = "#D9EEFA", LastOpenedUtc = DateTimeOffset.UtcNow.AddDays(index - 5)
        }).ToArray();
        foreach (var record in records) File.WriteAllText(record.FilePath, "Текст " + record.Title);
        store.Save(new AppConfiguration { Notes = records.ToList() });
        static ToolStripItem[] RecentItems(NoteManager manager)
        {
            manager.TrayMenu.Show(new Point(30, 30));
            Application.DoEvents();
            var items = manager.TrayMenu.Items.Cast<ToolStripItem>().Where(item => item.Available && item.Tag is string).ToArray();
            manager.TrayMenu.Close();
            return items;
        }
        static StickyNoteView Find(NoteRecord record) => Application.OpenForms.OfType<StickyNoteView>().Single(view => view.TitleText == record.Title);
        string? openedDirectory = null;
        using (var manager = new NoteManager(paths, directory => openedDirectory = directory))
        {
            Application.DoEvents();
            var recent = RecentItems(manager);
            check(recent.Select(item => item.Tag).SequenceEqual(new[] { records[3].Id, records[2].Id, records[1].Id }),
                "The tray offers exactly the three most recently opened notes in descending order");
            manager.TrayMenu.Items.Cast<ToolStripItem>().Single(item => item.Text == "Открыть папку заметок").PerformClick();
            check(openedDirectory == paths.Notes && openedDirectory != paths.Root,
                "The tray folder action opens the notes directory instead of the data or project root");
            recent[2].PerformClick();
            Application.DoEvents();
            var reopened = store.Load().Notes.Single(note => note.Id == records[1].Id);
            check(manager.Count == 1 && !reopened.IsClosed && reopened.Color == records[1].Color
                && Find(records[1]).Markdown == "Текст Заметка 1" && Equals(RecentItems(manager)[0].Tag, records[1].Id),
                "Selecting a recent note reopens its original sticker and promotes it in the persisted history");
            manager.ToggleVisibility();
            RecentItems(manager)[0].PerformClick();
            Application.DoEvents();
            check(manager.Count == 1 && !manager.Hidden && Find(records[1]).Visible && store.Load().Notes.Count == 4,
                "Selecting an already open recent note reveals it without a duplicate or confirmation dialog");
            var view = Find(records[1]);
            view.BeginTitleEditing();
            view.TitleEditorControl.Text = "Новое & название";
            view.CommitTitleEditing();
            check(RecentItems(manager)[0].Text == "Новое && название" && Equals(RecentItems(manager)[0].Tag, records[1].Id),
                "Recent-note labels follow renaming and escape menu access-key characters");
            check(manager.CloseNote(records[1].Id, false), "A recent note can be closed while remaining in the history");
        }
        using (var manager = new NoteManager(paths))
        {
            Application.DoEvents();
            var recent = RecentItems(manager);
            check(manager.Count == 0 && Equals(recent[0].Tag, records[1].Id) && recent[0].Text == "Новое && название",
                "Recent order and renamed labels survive an application restart");
            File.Delete(records[0].FilePath);
            File.WriteAllText(paths.RecoveryFile(records[0].Id), "Текст отсутствующего файла");
            manager.OpenRecentNote(records[0].Id);
            Application.DoEvents();
            check(manager.Count == 1 && Find(records[0]).Markdown == "Текст отсутствующего файла"
                && !File.Exists(records[0].FilePath) && Equals(RecentItems(manager)[0].Tag, records[0].Id),
                "A recent missing file reopens its recoverable sticker without recreating the file");
            manager.CloseNote(records[0].Id, false);
            manager.OpenRecentNote(records[1].Id);
            var renamedRecord = store.Load().Notes.Single(note => note.Id == records[1].Id);
            var deleting = Application.OpenForms.OfType<StickyNoteView>().Single(view => view.TitleText == renamedRecord.Title);
            deleting.BeginEditing();
            deleting.EditorControl.Text = "Последний текст перед удалением";
            deleting.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(item => item.Text == "Удалить заметку").PerformClick();
            check(manager.Count == 0 && deleting.IsDisposed && !File.Exists(renamedRecord.FilePath)
                && store.Load().Notes.All(note => note.Id != records[1].Id)
                && File.ReadAllText(paths.RecoveryFile(records[1].Id)) == "Последний текст перед удалением"
                && File.Exists(records[2].FilePath),
                "One click deletes a note to Windows recycling without confirmation and retains its unsaved text");
            check(RecentItems(manager).All(item => !Equals(item.Tag, records[1].Id)),
                "Deleted notes disappear from the tray history");
        }
        foreach (var record in records) record.LastOpenedUtc = null;
        store.Save(new AppConfiguration { Notes = records.ToList() });
        using (var manager = new NoteManager(paths))
        {
            Application.DoEvents();
            check(RecentItems(manager).Select(item => item.Tag).SequenceEqual(new[] { records[3].Id, records[2].Id, records[1].Id }),
                "Existing configurations without timestamps offer the last three created notes");
        }
    }
}
