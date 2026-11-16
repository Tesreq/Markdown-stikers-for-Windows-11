using System.Text;
using StickyDesk.Core;

namespace StickyDesk;

internal sealed class NoteSession : IDisposable
{
    private readonly AppPaths _paths;
    private readonly MarkdownFileService _files;
    private readonly Action<Action> _post;
    private readonly Func<string, bool>? _pathInUse;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private readonly System.Windows.Forms.Timer _watchTimer;
    private FileSystemWatcher? _watcher;
    private MarkdownSnapshot? _snapshot;
    private bool _busy;
    private bool _disposed;
    private bool _conflict;
    private bool _unavailable;
    private bool _missing;
    private readonly ContextMenuStrip _fileRecoveryMenu;
    private readonly ToolStripMenuItem _restoreFileItem;
    public NoteRecord Record { get; }
    public StickyNoteView View { get; }
    public bool IsDirty { get; private set; }
    public bool IsFileUnavailable => _unavailable;
    public bool IsFileMissing => _missing;
    public string FilePath => _paths.Resolve(Record.FilePath);
    public event Action? RelinkRequested;
    public event Action? MetadataChanged;

    public NoteSession(NoteRecord record, AppPaths paths, MarkdownFileService files, int delay, Action<Action> post,
        Func<string, bool>? pathInUse = null)
    {
        Record = record;
        _paths = paths;
        _files = files;
        _post = post;
        _pathInUse = pathInUse;
        View = new(record);
        _restoreFileItem = new ToolStripMenuItem("Восстановить файл", null, (_, _) => RestoreMissingFile());
        _fileRecoveryMenu = ModernMenu.Create();
        _fileRecoveryMenu.Items.Add(_restoreFileItem);
        _fileRecoveryMenu.Items.Add("Выбрать другое расположение…", null, (_, _) => RelinkRequested?.Invoke());
        _fileRecoveryMenu.Items.Add("Сохранить копию…", null, (_, _) =>
        {
            try { ExportCopy(); }
            catch (Exception ex) when (IsFileError(ex)) { MessageBox.Show(ex.Message, "StickyDesk — сохранение копии", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        });
        _fileRecoveryMenu.Items.Add(new ToolStripSeparator());
        _fileRecoveryMenu.Items.Add("Проверить доступность снова", null, (_, _) =>
        {
            CheckExternal();
            if (!_unavailable) { if (_conflict) ResolveOrRetry(); else Flush(true); }
        });
        _fileRecoveryMenu.Opening += (_, _) => _restoreFileItem.Enabled = _missing;
        _saveTimer = new() { Interval = delay };
        _watchTimer = new() { Interval = 250 };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Flush(false); };
        _watchTimer.Tick += (_, _) => { _watchTimer.Stop(); CheckExternal(); };
        View.TextEdited += OnTextEdited;
        View.SaveRequested += () => { if (!_busy && !_conflict) Flush(false); };
        View.StatusClicked += ResolveOrRetry;
        View.TitleEdited += value => RenameTitle(value);
        LoadInitial();
        UpdateTitle();
        StartWatching();
    }

    private void LoadInitial()
    {
        try
        {
            _snapshot = _files.Read(FilePath);
            View.SetMarkdown(_snapshot.EditorText);
        }
        catch (Exception ex) when (IsFileError(ex)) { MarkUnavailable(ex); }
        var recovery = _paths.RecoveryFile(Record.Id);
        if (!File.Exists(recovery)) return;
        try
        {
            var text = File.ReadAllText(recovery);
            if (MarkdownSnapshot.Normalize(text) == _snapshot?.EditorText) return;
            if (_missing)
            {
                // The linked file is absent: show the preserved text without recreating the file.
                View.SetMarkdown(text);
                // Empty text can be an intentional local edit, too.
                IsDirty = true;
                SetUnavailableStatus();
                return;
            }
            var answer = MessageBox.Show($"Для заметки «{Path.GetFileName(FilePath)}» найдены несохранённые изменения.\n\nВосстановить их? Файл на диске пока не изменится.",
                "StickyDesk — восстановление", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                View.SetMarkdown(text);
                IsDirty = true;
                _conflict = _snapshot is not null;
                View.SetStatus("Восстановлено · нажмите для сохранения", true);
            }
            else File.Move(recovery, recovery + $".archived-{DateTime.Now:yyyyMMdd-HHmmssfff}");
        }
        catch (Exception ex) when (IsFileError(ex)) { _paths.Log(ex.ToString()); View.SetStatus("Ошибка чтения копии восстановления", true); }
    }

    private void OnTextEdited()
    {
        IsDirty = _snapshot is null || View.Markdown != _snapshot.EditorText || (_missing && View.Markdown.Length > 0);
        _saveTimer.Stop();
        if (_unavailable) { PreserveRecovery(); SetUnavailableStatus(); return; }
        if (_conflict)
        {
            PreserveRecovery();
            View.SetStatus("Конфликт версий · нажмите здесь", true);
            // Reconcile after typing stops, including when the local text now matches the disk.
            _watchTimer.Stop();
            _watchTimer.Start();
            return;
        }
        if (!IsDirty)
        {
            ClearRecovery();
            View.SetStatus("Сохранено");
            return;
        }
        View.SetStatus("Сохранение…");
        _saveTimer.Start();
    }

    public bool Flush(bool interactive)
    {
        if (_disposed) return !IsDirty;
        _saveTimer.Stop();
        if (!IsDirty) return true;
        if (_busy) return false;
        _busy = true;
        try
        {
            AtomicFile.Write(_paths.RecoveryFile(Record.Id), Encoding.UTF8.GetBytes(View.Markdown));
            if (_snapshot is null || _unavailable)
            {
                SetUnavailableStatus();
                return false;
            }
            if (_conflict) return false;
            _snapshot = _files.Save(FilePath, View.Markdown, _snapshot);
            IsDirty = false;
            View.SetStatus("Сохранено");
            ClearRecovery();
            return true;
        }
        catch (FileConflictException)
        {
            _conflict = true;
            View.SetStatus("Конфликт версий · нажмите здесь", true);
            return false;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _paths.Log(ex.ToString());
            View.SetStatus("Не сохранено · нажмите для повтора", true);
            if (interactive) MessageBox.Show($"Не удалось сохранить заметку. Текст остаётся в стикере.\n\n{ex.Message}", "StickyDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally { _busy = false; }
    }

    public bool PrepareToClose(bool interactive = true)
    {
        if (_disposed || _busy) return false;
        View.CommitTitleEditing();
        CheckExternal();
        if (Flush(false)) return true;
        try
        {
            // A closed note keeps its identity, so this text can be recovered when reopened.
            AtomicFile.Write(_paths.RecoveryFile(Record.Id), Encoding.UTF8.GetBytes(View.Markdown));
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _paths.Log(ex.ToString());
            if (interactive) MessageBox.Show($"Не удалось сохранить текст перед закрытием. Заметка остаётся открытой.\n\n{ex.Message}",
                "StickyDesk — закрытие заметки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    public void ResolveOrRetry()
    {
        if (_busy || _disposed) return;
        CheckExternal();
        if (_unavailable) { View.ShowFileRecoveryMenu(_fileRecoveryMenu); return; }
        if (!_conflict) { Flush(true); return; }
        _busy = true;
        try
        {
            var latest = _files.Read(FilePath);
            using var dialog = new ConflictDialog(Path.GetFileName(FilePath));
            var choice = dialog.ShowDialog();
            if (choice == DialogResult.Yes)
            {
                _snapshot = latest;
                View.SetMarkdown(latest.EditorText);
                IsDirty = _conflict = false;
                ClearRecovery();
                View.SetStatus("Загружена внешняя версия");
            }
            else if (choice == DialogResult.No)
            {
                // Explicit permission to replace this specific version; still recheck before writing.
                _snapshot = _files.Save(FilePath, View.Markdown, latest);
                IsDirty = _conflict = false;
                ClearRecovery();
                View.SetStatus("Сохранена версия стикера");
            }
            else if (choice == DialogResult.Retry && ExportCopy())
            {
                // Keep the original association and load the current disk version after exporting.
                _snapshot = _files.Read(FilePath);
                View.SetMarkdown(_snapshot.EditorText);
                IsDirty = _conflict = false;
                ClearRecovery();
                View.SetStatus("Копия сохранена · загружен исходный файл");
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _paths.Log(ex.ToString());
            View.SetStatus("Не сохранено · нажмите для повтора", true);
            MessageBox.Show(ex.Message, "StickyDesk — сохранение", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _busy = false; }
    }

    public bool ExportCopy()
    {
        using var dialog = new SaveFileDialog { Title = "Сохранить текст стикера в отдельный файл", Filter = "Markdown (*.md)|*.md", DefaultExt = "md",
            AddExtension = true, FileName = Path.GetFileNameWithoutExtension(FilePath) + "-копия.md", RestoreDirectory = true, OverwritePrompt = true };
        if (dialog.ShowDialog() != DialogResult.OK) return false;
        if (string.Equals(Path.GetFullPath(dialog.FileName), FilePath, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Для копии выберите другое имя файла.", "StickyDesk");
            return false;
        }
        AtomicFile.Write(dialog.FileName, Encoding.UTF8.GetBytes(View.Markdown));
        return true;
    }

    public bool RestoreMissingFile(bool interactive = true)
    {
        if (_disposed || _busy) return false;
        _saveTimer.Stop();
        _busy = true;
        try
        {
            _snapshot = _files.RestoreMissing(FilePath, View.Markdown, _snapshot);
            _unavailable = _missing = _conflict = IsDirty = false;
            ClearRecovery();
            View.SetStatus("Файл восстановлен");
            StartWatching();
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _paths.Log(ex.ToString());
            PreserveRecovery();
            if (interactive) MessageBox.Show($"Не удалось восстановить файл. Текст остаётся в стикере.\n\n{ex.Message}\n\nЕсли файл уже появился, используйте «Проверить доступность снова», чтобы выбрать версию.",
                "StickyDesk — восстановление файла", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        finally { _busy = false; }
    }

    public bool RecycleFile(Action<string>? recycle = null)
    {
        if (_disposed || _busy) return false;
        _saveTimer.Stop();
        _watchTimer.Stop();
        _busy = true;
        try
        {
            // Keep local text separately: the disk may contain a different version or be absent.
            AtomicFile.Write(_paths.RecoveryFile(Record.Id), Encoding.UTF8.GetBytes(View.Markdown));
            try
            {
                _ = File.GetAttributes(FilePath);
                if (recycle is not null) recycle(FilePath);
                else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(FilePath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            return true;
        }
        finally
        {
            _busy = false;
            QueueExternalCheck();
            if (IsDirty && !_unavailable && !_conflict) _saveTimer.Start();
        }
    }

    public bool Relink(string newPath)
    {
        try
        {
            var snapshot = _files.Read(newPath);
            var preserveLocal = IsDirty || _conflict;
            Record.FilePath = Path.GetFullPath(newPath);
            Record.IsOwned = false;
            _snapshot = snapshot;
            _unavailable = _missing = false;
            if (preserveLocal && View.Markdown != snapshot.EditorText)
            {
                IsDirty = _conflict = true;
                PreserveRecovery();
                View.SetStatus("Файл подключён · выберите версию", true);
            }
            else
            {
                View.SetMarkdown(snapshot.EditorText);
                IsDirty = _conflict = false;
                ClearRecovery();
                View.SetStatus("Сохранено");
            }
            UpdateTitle();
            StartWatching();
            return true;
        }
        catch (Exception ex) when (IsFileError(ex)) { MessageBox.Show(ex.Message, "StickyDesk", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }

    private void StartWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(FilePath)!, Path.GetFileName(FilePath))
            { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, IncludeSubdirectories = false };
            _watcher.Changed += FileEvent;
            _watcher.Created += FileEvent;
            _watcher.Deleted += FileEvent;
            _watcher.Renamed += FileEvent;
            _watcher.Error += (_, e) => { _paths.Log(e.GetException().ToString()); QueueExternalCheck(); };
            _watcher.EnableRaisingEvents = true;
            QueueExternalCheck();
        }
        catch (Exception ex) when (IsFileError(ex) || ex is ArgumentException) { MarkUnavailable(ex); }
    }

    private void FileEvent(object sender, FileSystemEventArgs e) => QueueExternalCheck();
    private void QueueExternalCheck() => _post(() => { if (_disposed) return; _watchTimer.Stop(); _watchTimer.Start(); });

    private void CheckExternal()
    {
        if (_disposed || _busy) return;
        try
        {
            var wasUnavailable = _unavailable;
            var current = _files.Read(FilePath);
            _unavailable = _missing = false;
            if (current.Fingerprint == _snapshot?.Fingerprint)
            {
                if (wasUnavailable || _conflict)
                {
                    IsDirty = View.Markdown != current.EditorText;
                    if (!IsDirty)
                    {
                        _conflict = false;
                        ClearRecovery();
                        View.SetStatus("Сохранено");
                    }
                    else if (_conflict) View.SetStatus("Конфликт версий · нажмите здесь", true);
                    else { _saveTimer.Start(); View.SetStatus("Сохранение…"); }
                }
                return;
            }
            if (IsDirty && current.EditorText != View.Markdown)
            {
                _conflict = true;
                _saveTimer.Stop();
                // Capture local text even if the external edit arrived before the autosave delay.
                AtomicFile.Write(_paths.RecoveryFile(Record.Id), Encoding.UTF8.GetBytes(View.Markdown));
                View.SetStatus("Конфликт версий · нажмите здесь", true);
                return;
            }
            _snapshot = current;
            _conflict = false;
            IsDirty = false;
            if (View.Markdown != current.EditorText) View.SetMarkdown(current.EditorText);
            ClearRecovery();
            View.SetStatus("Сохранено");
        }
        catch (Exception ex) when (IsFileError(ex)) { MarkUnavailable(ex); }
    }

    private void MarkUnavailable(Exception error)
    {
        var firstError = !_unavailable;
        _unavailable = true;
        _missing = error is FileNotFoundException or DirectoryNotFoundException;
        _saveTimer.Stop();
        if (_missing && (_snapshot is not null || IsDirty))
        {
            IsDirty |= View.Markdown.Length > 0;
            _conflict = false;
            PreserveRecovery();
        }
        SetUnavailableStatus();
        if (firstError) _paths.Log($"{FilePath}: {error.Message}");
    }

    private void SetUnavailableStatus() => View.SetStatus(_missing ? "Файл удалён · восстановить…" : "Файл недоступен · действия…", true);

    private void PreserveRecovery()
    {
        try { AtomicFile.Write(_paths.RecoveryFile(Record.Id), Encoding.UTF8.GetBytes(View.Markdown)); }
        catch (Exception ex) when (IsFileError(ex)) { _paths.Log(ex.ToString()); }
    }

    private void UpdateTitle()
    {
        var title = string.IsNullOrWhiteSpace(Record.Title)
            ? Record.IsOwned ? "Заметка" : Path.GetFileNameWithoutExtension(FilePath)
            : Record.Title;
        View.SetTitle(title.Length > 80 ? title[..80] : title);
    }

    public bool RenameTitle(string value, bool interactive = true)
    {
        if (_disposed || _busy) return false;
        var title = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (title.Length > 80) title = title[..80];
        if (title.Length == 0) title = Record.IsOwned ? "Заметка" : Path.GetFileNameWithoutExtension(FilePath);
        _saveTimer.Stop();
        _watchTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _busy = true;
        try
        {
            var renamed = _files.Rename(FilePath, title, _pathInUse);
            Record.FilePath = Path.IsPathRooted(Record.FilePath) ? renamed.FilePath : Path.GetRelativePath(_paths.Root, renamed.FilePath);
            Record.Title = title;
            if (renamed.IsMissing)
            {
                _unavailable = _missing = IsDirty = true;
                PreserveRecovery();
                SetUnavailableStatus();
            }
            UpdateTitle();
            MetadataChanged?.Invoke();
            return true;
        }
        catch (Exception ex) when (IsFileError(ex) || ex is ArgumentException or NotSupportedException)
        {
            _paths.Log(ex.ToString());
            UpdateTitle();
            if (interactive) MessageBox.Show($"Не удалось переименовать заметку. Прежнее название сохранено.\n\n{ex.Message}",
                "StickyDesk — название заметки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        finally
        {
            _busy = false;
            StartWatching();
            if (IsDirty && !_unavailable && !_conflict) _saveTimer.Start();
        }
    }

    private void ClearRecovery()
    {
        try
        {
            var path = _paths.RecoveryFile(Record.Id);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
        catch (Exception ex) when (IsFileError(ex)) { _paths.Log(ex.ToString()); }
    }

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or DecoderFallbackException;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _saveTimer.Dispose();
        _watchTimer.Dispose();
        _fileRecoveryMenu.Dispose();
        View.Dispose();
    }
}
