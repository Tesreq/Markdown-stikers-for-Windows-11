using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using StickyDesk.Core;

internal static class StorageSafetyChecks
{
    internal static int Child(string[] args)
    {
        var operation = args[1];
        var path = args[2];
        try
        {
            if (operation == "write") File.WriteAllText(path, "foreign");
            else if (operation == "delete") File.Delete(path);
            else if (operation == "replace")
            {
                var temp = path + ".foreign.tmp";
                try { File.WriteAllText(temp, "foreign"); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            else if (operation == "write-crash")
            {
                var files = new MarkdownFileService();
                using var write = ProtectedFileWrite.Prepare(path, Encoding.UTF8.GetBytes(args[3]), files.Read(path).Fingerprint);
                Environment.Exit(77); // Intentionally skip Dispose, as an abruptly killed application would.
            }
            else if (operation is "rename-crash" or "rename-committed-crash")
            {
                var paths = new AppPaths(path);
                var store = new ConfigurationService(paths);
                var config = store.Load();
                new NoteRenameService(paths, new MarkdownFileService()).Rename(config.Notes.Single(), args[3], () =>
                {
                    if (operation == "rename-committed-crash") store.SaveCheckpoint(config);
                    Environment.Exit(77);
                });
            }
            else return 2;
            return 0;
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && operation is "write" or "delete" or "replace") { return 23; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    internal static void Run(string root, Action<string, Action> run, Action<bool, string> check)
    {
        root = Path.Combine(root, "storage-safety");
        Directory.CreateDirectory(root);
        var files = new MarkdownFileService();
        void CrashWrite(string path, string pending)
        {
            File.WriteAllText(path, "original");
            check(StartChild("write-crash", path, pending) == 77 && File.Exists(ProtectedFileWrite.JournalPath(path)), "Crash fixture did not leave a journal.");
        }
        (AppPaths Paths, ConfigurationService Store, AppConfiguration Config) RenameFixture(string name, bool missing = false)
        {
            var paths = new AppPaths(Path.Combine(root, name));
            var config = new AppConfiguration { Notes = [new NoteRecord { IsOwned = true, FilePath = Path.Combine("notes", "before.md"), Title = "before" }] };
            if (!missing) File.WriteAllText(paths.Resolve(config.Notes[0].FilePath), "note content");
            var store = new ConfigurationService(paths);
            store.SaveCheckpoint(config);
            return (paths, store, config);
        }

        run("A protected save blocks direct writes and deletion from another process", () =>
        {
            var path = Path.Combine(root, "concurrent.md");
            File.WriteAllText(path, "original");
            using (var write = ProtectedFileWrite.Prepare(path, Encoding.UTF8.GetBytes("saved"), files.Read(path).Fingerprint))
            {
                foreach (var operation in new[] { "write", "delete" })
                    check(StartChild(operation, path) == 23, "Concurrent operation was not blocked: " + operation);
                write.Commit();
            }
            check(File.ReadAllText(path) == "saved" && File.ReadAllText(path + ".bak") == "original", "Protected save lost a version.");
        });
        run("A protected save rejects an atomic replacement from another process", () =>
        {
            var path = Path.Combine(root, "replaced-handle.md");
            File.WriteAllText(path, "original");
            using (var write = ProtectedFileWrite.Prepare(path, Encoding.UTF8.GetBytes("local pending"), files.Read(path).Fingerprint))
            {
                var exit = StartChild("replace", path);
                check(exit == 23, "Atomic replacement was not blocked: " + exit);
                write.Commit();
            }
            check(File.ReadAllText(path) == "local pending", "Protected file was not saved.");
        });
        run("A process crash before commit preserves pending text without modifying the disk version", () =>
        {
            var path = Path.Combine(root, "crash-before-write.md");
            CrashWrite(path, "pending");
            try { files.Read(path); throw new Exception("Interruption was ignored."); }
            catch (InterruptedSaveException ex)
            {
                check(ex.Current?.Text == "original" && ex.Pending.Text == "pending" && File.ReadAllText(path) == "original", "A recovered version was lost.");
                using var archive = JsonDocument.Parse(File.ReadAllBytes(ex.ArchivePath));
                check(Encoding.UTF8.GetString(archive.RootElement.GetProperty("Original").GetBytesFromBase64()) == "original", "Original journal version was lost.");
            }
        });
        run("Recovery preserves a partial UTF-8 file and exposes the complete pending text", () =>
        {
            var path = Path.Combine(root, "crash-partial.md");
            CrashWrite(path, "Полный текст 🙂");
            File.WriteAllBytes(path, [0xD0]);
            try { files.Read(path); throw new Exception("Partial write was ignored."); }
            catch (InterruptedSaveException ex)
            {
                check(ex.Current is null && ex.Pending.Text == "Полный текст 🙂" && File.ReadAllBytes(path).SequenceEqual(new byte[] { 0xD0 }),
                    "Recovery silently overwrote ambiguous disk data.");
            }
        });
        run("Recovery never overwrites a file edited externally after a crash", () =>
        {
            var path = Path.Combine(root, "crash-external.md");
            CrashWrite(path, "local");
            File.WriteAllText(path, "external after crash");
            try { files.Read(path); throw new Exception("Recovery conflict was ignored."); }
            catch (InterruptedSaveException ex)
            {
                check(ex.Current?.Text == "external after crash" && ex.Pending.Text == "local" && File.ReadAllText(path) == "external after crash", "External recovery text was overwritten.");
            }
        });
        run("A completed write with a leftover journal is recognized without a false conflict", () =>
        {
            var path = Path.Combine(root, "crash-completed.md");
            CrashWrite(path, "completed");
            File.WriteAllText(path, "completed");
            check(files.Read(path).Text == "completed" && !File.Exists(ProtectedFileWrite.JournalPath(path)), "Completed write was not recognized.");
        });
        run("A failed metadata commit rolls a renamed file and its title back", () =>
        {
            var fixture = RenameFixture("rename-failure");
            var note = fixture.Config.Notes.Single();
            try { new NoteRenameService(fixture.Paths, files).Rename(note, "after", () => throw new IOException("Config failure")); throw new Exception("Persistence failure ignored."); }
            catch (IOException) { }
            check(note.Title == "before" && Path.GetFileName(note.FilePath) == "before.md"
                && File.ReadAllText(fixture.Paths.Resolve(note.FilePath)) == "note content" && !File.Exists(Path.Combine(fixture.Paths.Notes, "after.md")), "Rename rollback failed.");
            var recovery = new NoteRenameService(fixture.Paths, files);
            recovery.Recover(fixture.Config, () => fixture.Store.SaveCheckpoint(fixture.Config));
            check(recovery.Warnings.Count == 0 && !Directory.EnumerateFiles(fixture.Paths.Operations, "*.rename.json").Any(), "Rolled back metadata was not confirmed.");
        });
        run("A crash between file rename and metadata commit repairs the saved path on restart", () =>
        {
            var fixture = RenameFixture("rename-crash");
            check(StartChild("rename-crash", fixture.Paths.Root, "after") == 77, "Rename child did not crash.");
            var config = fixture.Store.Load();
            var recovery = new NoteRenameService(fixture.Paths, files);
            recovery.Recover(config, () => fixture.Store.SaveCheckpoint(config));
            var note = fixture.Store.Load().Notes.Single();
            check(note.Title == "after" && Path.GetFileName(note.FilePath) == "after.md" && File.ReadAllText(fixture.Paths.Resolve(note.FilePath)) == "note content"
                && recovery.Warnings.Count == 0, "Crash recovery left the old path.");
        });
        run("A failed case-only rename remains rolled back after restart", () =>
        {
            var fixture = RenameFixture("rename-case-rollback");
            var note = fixture.Config.Notes.Single();
            try { new NoteRenameService(fixture.Paths, files).Rename(note, "BEFORE", () => throw new IOException("Config failure")); throw new Exception("Failure ignored."); }
            catch (IOException) { }
            var config = fixture.Store.Load();
            new NoteRenameService(fixture.Paths, files).Recover(config, () => fixture.Store.SaveCheckpoint(config));
            check(config.Notes.Single().Title == "before" && Directory.EnumerateFiles(fixture.Paths.Notes, "*.md").Select(Path.GetFileName).Single() == "before.md",
                "Recovery undid the case-only rollback.");
        });
        run("A failed missing-file rename preserves its original restoration path", () =>
        {
            var fixture = RenameFixture("rename-missing-rollback", missing: true);
            try { new NoteRenameService(fixture.Paths, files).Rename(fixture.Config.Notes.Single(), "after", () => throw new IOException("Config failure")); throw new Exception("Failure ignored."); }
            catch (IOException) { }
            var config = fixture.Store.Load();
            new NoteRenameService(fixture.Paths, files).Recover(config, () => fixture.Store.SaveCheckpoint(config));
            check(config.Notes.Single().Title == "before" && Path.GetFileName(config.Notes.Single().FilePath) == "before.md"
                && !Directory.EnumerateFiles(fixture.Paths.Notes, "*.md").Any(), "Recovery changed the original restoration path.");
        });
        run("A committed rename survives both a leftover journal and corrupt primary configuration", () =>
        {
            var fixture = RenameFixture("rename-committed");
            check(StartChild("rename-committed-crash", fixture.Paths.Root, "after") == 77, "Committed child did not crash.");
            var config = fixture.Store.Load();
            new NoteRenameService(fixture.Paths, files).Recover(config, () => fixture.Store.SaveCheckpoint(config));
            File.WriteAllText(fixture.Paths.Config, "{broken");
            var note = fixture.Store.Load().Notes.Single();
            check(note.Title == "after" && File.Exists(fixture.Paths.Resolve(note.FilePath)), "Configuration backup points at the old file name.");
        });
        run("A missing file rename is recovered without recreating its Markdown file", () =>
        {
            var fixture = RenameFixture("rename-missing", missing: true);
            check(StartChild("rename-crash", fixture.Paths.Root, "after") == 77, "Missing-file child did not crash.");
            var config = fixture.Store.Load();
            new NoteRenameService(fixture.Paths, files).Recover(config, () => fixture.Store.SaveCheckpoint(config));
            check(config.Notes.Single().Title == "after" && !Directory.EnumerateFiles(fixture.Paths.Notes, "*.md").Any(), "Recovery recreated a deleted file.");
        });
        run("Ambiguous rename recovery leaves both externally changed files intact", () =>
        {
            var fixture = RenameFixture("rename-ambiguous");
            check(StartChild("rename-crash", fixture.Paths.Root, "after") == 77, "Ambiguous child did not crash.");
            var original = Path.Combine(fixture.Paths.Notes, "before.md");
            var renamed = Path.Combine(fixture.Paths.Notes, "after.md");
            File.WriteAllText(original, "foreign original path");
            File.WriteAllText(renamed, "foreign renamed path");
            var config = fixture.Store.Load();
            var recovery = new NoteRenameService(fixture.Paths, files);
            recovery.Recover(config, () => fixture.Store.SaveCheckpoint(config));
            check(recovery.Warnings.Count == 1 && File.ReadAllText(original) == "foreign original path"
                && File.ReadAllText(renamed) == "foreign renamed path" && Directory.EnumerateFiles(fixture.Paths.Operations, "*.rename.json").Any(),
                "Ambiguous recovery guessed and changed a file.");
        });
    }

    private static int StartChild(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--storage-child");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Test process did not start.");
        if (!process.WaitForExit(10000)) { process.Kill(); process.WaitForExit(); throw new IOException("Test child timed out."); }
        var error = process.StandardError.ReadToEnd();
        if (error.Length > 0) Console.WriteLine("CHILD " + error);
        return process.ExitCode;
    }
}
