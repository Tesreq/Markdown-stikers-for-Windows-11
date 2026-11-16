using System.Text;
using StickyDesk.Core;

if (args.FirstOrDefault() == "--storage-child") return StorageSafetyChecks.Child(args);
var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "StickyDesk-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var failures = 0;
var total = 0;
void Run(string name, Action test)
{
    total++;
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex}"); }
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var service = new MarkdownFileService();
Run("UTF-8 Cyrillic, BOM and CRLF survive editing", () =>
{
    var path = Path.Combine(root, "Заметка с пробелами.md");
    File.WriteAllText(path, "# Привет\r\n- [ ] Дело\r\n", new UTF8Encoding(true));
    var original = service.Read(path);
    service.Save(path, "# Привет\n- [x] Дело\n", original);
    var result = service.Read(path);
    Check(result.HasBom && result.NewLine == "\r\n", "Encoding or line ending changed.");
    Check(result.Text == "# Привет\r\n- [x] Дело\r\n", "Markdown changed unexpectedly.");
});
Run("External changes cannot be silently overwritten", () =>
{
    var path = Path.Combine(root, "conflict.md");
    File.WriteAllText(path, "initial");
    var original = service.Read(path);
    File.WriteAllText(path, "external");
    try { service.Save(path, "local", original); throw new Exception("Conflict not detected."); }
    catch (FileConflictException) { }
    Check(File.ReadAllText(path) == "external", "External text was overwritten.");
});
Run("A deleted linked file is not recreated by autosave", () =>
{
    var path = Path.Combine(root, "deleted.md");
    File.WriteAllText(path, "original");
    var original = service.Read(path);
    File.Delete(path);
    try { service.Save(path, "local", original); throw new Exception("Deletion not detected."); }
    catch (FileConflictException) { }
    Check(!File.Exists(path), "Deleted file was recreated.");
});
Run("Explicit restoration preserves BOM, line endings and current text", () =>
{
    var path = Path.Combine(root, "restore-bom.md");
    File.WriteAllText(path, "Прежний текст\r\n", new UTF8Encoding(true));
    var previous = service.Read(path);
    File.Delete(path);
    var restored = service.RestoreMissing(path, "Изменённый **текст** 🙂\n", previous);
    var actual = service.Read(path);
    Check(actual.HasBom && actual.NewLine == "\r\n", "Original encoding or line ending lost.");
    Check(actual.Text == "Изменённый **текст** 🙂\r\n" && actual == restored, "Restored text differs.");
});
Run("Restoration cannot overwrite a file that reappeared", () =>
{
    var path = Path.Combine(root, "restore-existing.md");
    File.WriteAllText(path, "Внешняя версия\r\n", new UTF8Encoding(true));
    var expected = File.ReadAllBytes(path);
    try { service.RestoreMissing(path, "local"); throw new Exception("Existing file was accepted."); }
    catch (IOException) { }
    Check(File.ReadAllBytes(path).SequenceEqual(expected), "Reappeared file was overwritten.");
    Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "Failed restore left a temporary file.");
});
Run("Restoration after restart defaults to UTF-8 without losing Unicode", () =>
{
    var path = Path.Combine(root, "restore-new.md");
    var restored = service.RestoreMissing(path, "Копия после перезапуска 🙂\r\n");
    Check(service.Read(path) == restored && restored.EditorText == "Копия после перезапуска 🙂\n", "Unicode text changed.");
    Check(!restored.HasBom && restored.NewLine == "\n", "Unexpected default format.");
});
Run("Backup keeps the last successfully saved version", () =>
{
    var path = Path.Combine(root, "backup.md");
    File.WriteAllText(path, "previous");
    service.Save(path, "next", service.Read(path));
    Check(File.ReadAllText(path + ".bak") == "previous", "Backup missing or incorrect.");
    Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "Temporary files leaked.");
});
Run("Renaming preserves Markdown bytes, BOM and line endings", () =>
{
    var path = Path.Combine(root, "rename-original.md");
    File.WriteAllText(path, "**Текст** 🙂\r\n", new UTF8Encoding(true));
    var bytes = File.ReadAllBytes(path);
    var renamed = service.Rename(path, "Мои планы 🙂");
    Check(!renamed.IsMissing && Path.GetFileName(renamed.FilePath) == "Мои планы 🙂.md" && !File.Exists(path), "File name was not changed.");
    Check(File.ReadAllBytes(renamed.FilePath).SequenceEqual(bytes), "Rename changed file contents.");
});
Run("Renaming never overwrites an existing file or directory", () =>
{
    var path = Path.Combine(root, "rename-collision-source.md");
    File.WriteAllText(path, "source");
    File.WriteAllText(Path.Combine(root, "occupied.md"), "other");
    Directory.CreateDirectory(Path.Combine(root, "occupied-folder.md"));
    foreach (var name in new[] { "occupied", "occupied-folder" })
    {
        try { service.Rename(path, name); throw new Exception("Collision accepted."); }
        catch (IOException) { }
    }
    Check(File.ReadAllText(path) == "source" && File.ReadAllText(Path.Combine(root, "occupied.md")) == "other", "A collision changed data.");
});
Run("Invalid and reserved Windows file names are rejected before moving", () =>
{
    var path = Path.Combine(root, "rename-validation.md");
    File.WriteAllText(path, "source");
    foreach (var name in new[] { "../outside", "a\\b", "a:b", "a?b", "a*b", "a|b", "a<b", "a>b", "a\"b", "a\0b", "last.", " ", "trailing ", "CON", "nul.txt", "COM1", "LPT9", "COM¹", "LPT³.txt" })
    {
        try { service.Rename(path, name); throw new Exception("Invalid name accepted: " + name); }
        catch (ArgumentException) { }
    }
    Check(File.ReadAllText(path) == "source", "Validation changed the source file.");
});
Run("A title can change only the case of its file name", () =>
{
    var path = Path.Combine(root, "lower-case.md");
    File.WriteAllText(path, "case");
    var renamed = service.Rename(path, "LOWER-CASE");
    Check(Directory.EnumerateFiles(root).Any(file => Path.GetFileName(file) == "LOWER-CASE.md") && File.ReadAllText(renamed.FilePath) == "case", "Case-only rename failed.");
});
Run("Renaming a missing note changes its intended path without creating a file", () =>
{
    var renamed = service.Rename(Path.Combine(root, "missing-to-rename.md"), "После удаления");
    Check(renamed.IsMissing && !File.Exists(renamed.FilePath) && Path.GetFileName(renamed.FilePath) == "После удаления.md", "Missing file was recreated.");
});
Run("A path reserved by another sticker cannot be used for renaming", () =>
{
    var path = Path.Combine(root, "rename-reserved-source.md");
    File.WriteAllText(path, "source");
    try { service.Rename(path, "reserved-note", candidate => Path.GetFileName(candidate) == "reserved-note.md"); throw new Exception("Reserved path accepted."); }
    catch (IOException) { }
    Check(File.ReadAllText(path) == "source", "Reserved path changed the source file.");
});
Run("Corrupt config recovers from backup without losing paths", () =>
{
    var paths = new AppPaths(Path.Combine(root, "configuration"));
    var configuration = new AppConfiguration();
    configuration.Notes.Add(new NoteRecord { FilePath = @"notes\заметка.md", X = -1400, Color = "#F8DCE8" });
    var store = new ConfigurationService(paths);
    store.Save(configuration);
    store.Save(configuration);
    File.WriteAllText(paths.Config, "{broken");
    var recovered = store.Load();
    Check(recovered.Notes.Count == 1 && recovered.Notes[0].X == -1400, "Configuration was not recovered.");
    Check(store.Warnings.Count > 0, "Recovery was silent.");
    Check(Directory.EnumerateFiles(paths.Root, "*.corrupt-*").Any(), "Damaged configuration was not preserved.");
});
Run("Invalid UTF-8 is rejected instead of replacing characters", () =>
{
    var path = Path.Combine(root, "encoding.md");
    File.WriteAllBytes(path, [0xFF, 0xFE, 0x61, 0x00]);
    try { service.Read(path); throw new Exception("Invalid UTF-8 accepted."); }
    catch (DecoderFallbackException) { }
});
Run("One note save does not modify adjacent Markdown files", () =>
{
    var first = Path.Combine(root, "first.md");
    var other = Path.Combine(root, "other.md");
    File.WriteAllText(first, "one");
    File.WriteAllText(other, "**do not change**\n");
    var expected = File.ReadAllBytes(other);
    service.Save(first, "changed", service.Read(first));
    Check(File.ReadAllBytes(other).SequenceEqual(expected), "Unrelated file changed.");
});
Run("Preview renders nested emphasis and headers without Markdown delimiters", () =>
{
    var rendered = MarkdownPreviewRenderer.Render("## Заголовок\n\n**Жирный и *курсив***, `код`.");
    Check(rendered.Text == "Заголовок\nЖирный и курсив, код.\n", rendered.Text);
});
Run("Fenced code preserves Markdown-looking text and RTF metacharacters", () =>
{
    var rendered = MarkdownPreviewRenderer.Render("```csharp\n**not bold** {\\rtf1} 🙂\n```");
    Check(rendered.Text.Contains("**not bold** {\\rtf1} 🙂", StringComparison.Ordinal), "Code was interpreted or lost.");
    Check(rendered.Rtf.Contains(@"\{\\rtf1\}", StringComparison.Ordinal), "Code was not escaped for RTF.");
});
Run("Checklists are displayed without duplicate list markers", () =>
{
    var rendered = MarkdownPreviewRenderer.Render("- [ ] Купить молоко\n- [x] Завершено\n\n3. Третий\n4. Четвёртый");
    Check(rendered.Text.Contains("☐ Купить молоко", StringComparison.Ordinal), rendered.Text);
    Check(rendered.Text.Contains("☑ Завершено", StringComparison.Ordinal), rendered.Text);
    Check(!rendered.Text.Contains("• ☐", StringComparison.Ordinal), "Duplicate bullet.");
    Check(rendered.Text.Contains("3. Третий", StringComparison.Ordinal) && rendered.Text.Contains("4. Четвёртый", StringComparison.Ordinal), "Ordered list start was lost.");
});
Run("Named links keep the destination separate from the visible label", () =>
{
    var rendered = MarkdownPreviewRenderer.Render("Открыть [документацию](https://example.com/?x=1&y=2).");
    var link = rendered.Links.Single();
    Check(rendered.Text.Substring(link.Start, link.Length) == "документацию", "Link range is wrong.");
    Check(link.Url == "https://example.com/?x=1&y=2", "Link URL changed.");
    Check(!rendered.Text.Contains("https://", StringComparison.Ordinal), "Raw destination leaks into the label.");
});
Run("Click positions map back into the original Markdown", () =>
{
    const string source = "# Заголовок\n\nТекст **жирно** и *курсив*.";
    var rendered = MarkdownPreviewRenderer.Render(source);
    var offset = rendered.Text.IndexOf("жирно", StringComparison.Ordinal) + 2;
    Check(rendered.SourceOffset(offset, source.Length) == source.IndexOf("жирно", StringComparison.Ordinal) + 2, "Source caret offset is wrong.");
});
Run("Escapes, entities, and raw HTML remain readable", () =>
{
    var rendered = MarkdownPreviewRenderer.Render(@"\*буквально\* &amp; <script>ничего не исполняется</script>");
    Check(rendered.Text.Contains("*буквально* &", StringComparison.Ordinal), "Escapes or entities were lost.");
    Check(rendered.Text.Contains("<script>ничего не исполняется</script>", StringComparison.Ordinal), "HTML text disappeared.");
});
Run("Malformed Markdown and incomplete delimiters remain readable", () =>
{
    const string source = "####### Не заголовок **незакончено [ссылка](";
    Check(MarkdownPreviewRenderer.Render(source).Text.TrimEnd('\n') == source, "Incomplete syntax lost text.");
});
StorageSafetyChecks.Run(root, Run, Check);
Console.WriteLine($"{total - failures}/{total} passed. Test files: {root}");
return failures == 0 ? 0 : 1;
