using StickyDesk;
using StickyDesk.Core;

namespace StickyDesk.UIHarness;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var root = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine(Path.GetTempPath(), "StickyDesk-UIHarness");
        if (args.Contains("--self-test")) return RunSelfTests(root);
        var paths = new AppPaths(root);
        var file = Path.Combine(paths.Notes, "проверка.md");
        if (!File.Exists(file)) File.WriteAllText(file, "Здесь начинается **основной текст** заметки.\n\n- [ ] Проверить автосохранение\n- [x] Собрать StickyDesk\n\n[Документация](https://example.com)\n\n```csharp\nConsole.WriteLine(\"Привет!\");\n```");
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        using var session = new NoteSession(new NoteRecord { Id = "aabbccddaabbccddaabbccddaabbccdd", FilePath = file, Title = "Рабочие заметки" },
            paths, new MarkdownFileService(), 700, action => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); });
        var view = session.View;
        using var window = new Form { Text = "StickyDesk — проверка редактора", StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(340, 390), MinimumSize = new Size(220, 180), AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.None, BackColor = Color.FromArgb(30, 36, 45), Padding = new Padding(12) };
        view.TopLevel = false;
        view.Dock = DockStyle.Fill;
        window.Controls.Add(view);
        window.Shown += (_, _) => view.Show();
        view.ContextMenuStrip = ModernMenu.Create();
        view.ContextMenuStrip.Items.Add("Сохранить / разрешить конфликт…", null, (_, _) => session.ResolveOrRetry());
        view.ContextMenuStrip.Items.Add("Изменить заголовок…", null, (_, _) => view.BeginTitleEditing());
        view.ContextMenuStrip.Items.Add("Редактировать Markdown", null, (_, _) => view.BeginEditing());
        view.ContextMenuStrip.Items.Add("Показать оформление Markdown", null, (_, _) => view.ShowPreview());
        foreach (var (name, hex) in new[] { ("Жёлтый", "#FFF2B3"), ("Зелёный", "#DDF2C2"), ("Голубой", "#D9EEFA"), ("Розовый", "#F8DCE8"), ("Белый", "#F5F5F5") })
            view.ContextMenuStrip.Items.Add(name, null, (_, _) => view.ApplyColor(hex));
        view.ContextMenuStrip.Items.Add("Выход", null, (_, _) => window.Close());
        window.FormClosing += (_, _) => session.Flush(false);
        Application.Run(window);
        return 0;
    }

    private static int RunSelfTests(string root)
    {
        var results = new List<string>();
        var paths = new AppPaths(root);
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); results.Add("PASS " + name); }
        static bool WaitUntil(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 4000;
            while (!condition() && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            return condition();
        }
        try
        {
            const string source = "# Заголовок\r\n\r\n**важно**, *курсив* и `код {\\rtf1}` 🙂\r\n\r\n- [ ] Дело\r\n- [x] Готово\r\n\r\n[ссылка](https://example.com)\r\n";
            var file = Path.Combine(paths.Notes, "проверка-разметки.md");
            File.WriteAllText(file, source, new System.Text.UTF8Encoding(true));
            var original = File.ReadAllBytes(file);
            using var dispatcher = new Control();
            _ = dispatcher.Handle;
            using var session = new NoteSession(new NoteRecord { FilePath = file }, paths, new MarkdownFileService(), 700,
                action => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); });
            var view = session.View;
            var initialTitle = view.TitleText;
            using var host = new Form { ClientSize = new Size(380, 450) };
            view.TopLevel = false;
            view.Dock = DockStyle.Fill;
            host.Controls.Add(view);
            host.Show();
            view.Show();
            Application.DoEvents();
            _ = view.Handle;
            var titleTop = view.TitleLabelControl.PointToScreen(Point.Empty).Y;
            var titleBottom = view.TitleLabelControl.PointToScreen(new Point(0, view.TitleLabelControl.Height)).Y;
            var lineTop = view.TitleSeparatorControl.PointToScreen(Point.Empty).Y;
            var contentTop = view.PreviewControl.PointToScreen(Point.Empty).Y;
            var dragBottom = view.DragAreaControl.PointToScreen(new Point(0, view.DragAreaControl.Height)).Y;
            Check(titleTop >= dragBottom && !view.DragAreaControl.Contains(view.TitleLabelControl) && view.TitleLabelControl.Font.SizeInPoints >= 16,
                "Large editable title belongs to note content below the drag strip");
            Check(lineTop >= titleBottom && contentTop > lineTop + view.TitleSeparatorControl.Height,
                "Divider is below the inline title and above the body text");
            var preview = view.PreviewControl;
            _ = preview.Handle;
            Check(preview.Text.Contains("Заголовок", StringComparison.Ordinal) && !preview.Text.Contains("**важно**", StringComparison.Ordinal), "Native RichEdit displays rendered text");
            preview.Select(preview.Text.IndexOf("Заголовок", StringComparison.Ordinal), 5);
            Check(preview.SelectionFont is { Bold: true, SizeInPoints: > 14 }, "Heading uses a larger bold font");
            preview.Select(preview.Text.IndexOf("важно", StringComparison.Ordinal), 5);
            Check(preview.SelectionFont?.Bold == true, "Bold text has native bold formatting");
            preview.Select(preview.Text.IndexOf("курсив", StringComparison.Ordinal), 6);
            Check(preview.SelectionFont?.Italic == true, "Italic text has native italic formatting");
            preview.Select(preview.Text.IndexOf("код", StringComparison.Ordinal), 3);
            Check(preview.SelectionFont?.FontFamily.Name == "Consolas", "Code uses a monospace font");
            Check(preview.Text.Contains("{\\rtf1}", StringComparison.Ordinal) && preview.Text.Contains("🙂", StringComparison.Ordinal), "RTF escaping and surrogate pairs survive native rendering");
            Check(preview.Text.Contains("☐", StringComparison.Ordinal) && preview.Text.Contains("☑", StringComparison.Ordinal), "Checklist glyphs are visible");
            preview.Select(preview.Text.IndexOf("ссылка", StringComparison.Ordinal), 6);
            Check(preview.SelectionColor.B > preview.SelectionColor.R && preview.SelectionFont?.Underline == true, "Named links have native link styling");
            var edits = 0;
            view.TextEdited += () => edits++;
            for (var i = 0; i < 3; i++) { view.BeginEditing(); view.ShowPreview(); }
            Check(edits == 0 && !session.IsDirty, "Mode switching does not mark the source as edited");
            Check(File.ReadAllBytes(file).SequenceEqual(original), "Mode switching preserves file bytes including BOM and CRLF");
            Check(view.Markdown == MarkdownSnapshot.Normalize(source), "Formatted text never replaces the Markdown source");
            view.BeginEditing();
            view.EditorControl.SelectionStart = view.EditorControl.TextLength;
            view.EditorControl.SelectedText = "\n**Добавлено**";
            Check(session.IsDirty, "Editing source marks the note as dirty");
            view.ShowPreview();
            var saved = File.ReadAllText(file);
            Check(saved.Contains("**Добавлено**", StringComparison.Ordinal) && !saved.StartsWith("{\\rtf", StringComparison.Ordinal), "Save writes Markdown, not RTF");
            Check(view.PreviewControl.Text.Contains("Добавлено", StringComparison.Ordinal) && !view.PreviewControl.Text.Contains("**Добавлено**", StringComparison.Ordinal), "Preview refreshes after editing");
            view.BeginEditing();
            view.EditorControl.Text = "Абобу написать цапко";
            view.ShowPreview();
            Check(view.TitleText == initialTitle, "Changing the first body line never changes the header");
            Check(view.Markdown == "Абобу написать цапко", "Body text remains in the body after leaving edit mode");
            var bodyBeforeRename = File.ReadAllBytes(file);
            var metadataChanges = 0;
            session.MetadataChanged += () => metadataChanges++;
            view.BeginTitleEditing();
            view.TitleEditorControl.Text = "Моя шапка";
            view.CommitTitleEditing();
            Check(view.TitleText == "Моя шапка" && session.Record.Title == "Моя шапка" && metadataChanges == 1, "Header edits update independent note metadata");
            var previousFile = file;
            file = session.FilePath;
            Check(Path.GetFileName(file) == "Моя шапка.md" && !File.Exists(previousFile) && File.ReadAllBytes(file).SequenceEqual(bodyBeforeRename),
                "Editing the header renames the Markdown file without changing its bytes");
            view.BeginTitleEditing();
            view.TitleEditorControl.Text = "Отменённый заголовок";
            view.CancelTitleEditing();
            Check(view.TitleText == "Моя шапка" && session.Record.Title == "Моя шапка" && session.FilePath == file && File.Exists(file) && metadataChanges == 1,
                "Cancelling a header edit preserves its title and file name");
            var configurationService = new ConfigurationService(paths);
            configurationService.Save(new AppConfiguration { Notes = [session.Record] });
            Check(configurationService.Load().Notes.Single().Title == "Моя шапка", "Independent header survives configuration save and reload");
            var retained = view.Markdown;
            File.Delete(file);
            Check(WaitUntil(() => session.IsFileMissing), "FileSystemWatcher detects deletion and keeps the sticker");
            Check(!view.IsDisposed && view.Markdown == retained && File.ReadAllText(paths.RecoveryFile(session.Record.Id)) == retained,
                "Deleted file text is retained in the sticker and recovery storage");
            Check(!session.Flush(false) && !File.Exists(file), "Autosave never recreates a deleted file");
            Check(session.RestoreMissingFile(false) && !session.IsFileUnavailable && !session.IsDirty && new MarkdownFileService().Read(file).EditorText == retained,
                "Explicit restoration recreates the file and clears the error state");
            Check(!File.Exists(paths.RecoveryFile(session.Record.Id)), "Successful restoration clears recovery text");
            File.Delete(file);
            Check(WaitUntil(() => session.IsFileMissing), "Repeated deletion is detected after restoration");
            session.Dispose();
            using var restarted = new NoteSession(session.Record, paths, new MarkdownFileService(), 700,
                action => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); });
            Check(restarted.IsFileMissing && restarted.View.Markdown == retained && !File.Exists(file),
                "Restart with a missing file restores the sticker text without recreating its file");
            Check(restarted.RestoreMissingFile(false) && new MarkdownFileService().Read(file).EditorText == retained,
                "Preserved text can be restored to a file after restart");
            restarted.View.BeginEditing();
            restarted.View.EditorControl.Clear();
            restarted.View.ShowPreview();
            File.Delete(file);
            Check(WaitUntil(() => restarted.IsFileMissing), "An empty note also exposes file recovery");
            restarted.View.BeginEditing();
            restarted.View.EditorControl.Text = "Временный текст";
            restarted.View.EditorControl.Clear();
            Check(File.ReadAllText(paths.RecoveryFile(restarted.Record.Id)).Length == 0,
                "Erasing a missing note also updates its recovery copy");
            RecoverySessionChecks.Run(Path.Combine(root, "recovery-scenarios"), Check);
            DeletionSessionChecks.Run(Path.Combine(root, "deletion-scenarios"), Check);
            RenamingSessionChecks.Run(Path.Combine(root, "renaming-scenarios"), Check);
            StorageSafetySessionChecks.Run(Path.Combine(root, "storage-safety-scenarios"), Check);
            WindowLifecycleChecks.Run(Path.Combine(root, "window-lifecycle"), Check);
            TrayWorkflowChecks.Run(Path.Combine(root, "tray-workflows"), Check);
            File.WriteAllLines(Path.Combine(root, "ui-self-test-results.txt"), results);
            return 0;
        }
        catch (Exception ex)
        {
            results.Add("FAIL " + ex);
            File.WriteAllLines(Path.Combine(root, "ui-self-test-results.txt"), results);
            return 1;
        }
    }
}
