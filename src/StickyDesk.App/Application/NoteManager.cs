using System.Diagnostics;
using Microsoft.Win32;
using StickyDesk.Core;

namespace StickyDesk;

internal sealed class NoteManager : ApplicationContext
{
    private readonly AppPaths _paths;
    private readonly ConfigurationService _configurationService;
    private readonly AppConfiguration _configuration;
    private readonly bool _firstRun;
    private readonly MarkdownFileService _files = new();
    private readonly List<NoteSession> _notes = [];
    private readonly Control _dispatcher = new();
    private readonly DesktopIntegration _desktop;
    private readonly ShellMessageWindow _messages;
    private readonly TrayManager _tray;
    private readonly System.Windows.Forms.Timer _configurationTimer = new() { Interval = 350 };
    private readonly System.Windows.Forms.Timer _shellRetry = new() { Interval = 300 };
    private int _retryAttempt;
    private bool _hidden;
    private bool _exiting;
    private bool _disposed;
    private bool _configurationDirty;
    public int Count => _notes.Count;
    public bool Hidden => _hidden;
    public bool DesktopAvailable => _desktop.Available;
    public string DataDirectory => _paths.Root;

    public NoteManager(AppPaths paths)
    {
        _paths = paths;
        _firstRun = !File.Exists(paths.Config) && !File.Exists(paths.Config + ".bak");
        _configurationService = new(paths);
        _configuration = _configurationService.Load();
        var renameRecovery = new NoteRenameService(paths, _files);
        renameRecovery.Recover(_configuration, () => _configurationService.SaveCheckpoint(_configuration));
        _configurationService.Warnings.AddRange(renameRecovery.Warnings);
        _ = _dispatcher.Handle;
        _desktop = new(paths, () => Post(ScheduleReattach));
        _messages = new();
        _messages.ShellReady += () => Post(ScheduleReattach);
        _tray = new(this);
        _configurationTimer.Tick += (_, _) => { _configurationTimer.Stop(); SaveConfiguration(); };
        _shellRetry.Tick += (_, _) => RetryAttach();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        Post(Start);
    }

    private void Start()
    {
        _desktop.Discover();
        foreach (var record in _configuration.Notes.Where(note => !note.IsClosed).ToArray())
        {
            try { AddSession(record); }
            catch (Exception ex) { _paths.Log(ex.ToString()); _tray.Notify("Не удалось открыть одну из заметок", ex.Message); }
        }
        if (_firstRun && _configuration.Notes.Count == 0) CreateNote();
        if (!_desktop.Available) ScheduleReattach();
        if (_configurationService.Warnings.Count > 0) _tray.Notify("Восстановление настроек", string.Join("\n", _configurationService.Warnings));
    }

    public void CreateNote()
    {
        try
        {
            if (_hidden) ToggleVisibility();
            var record = new NoteRecord { IsOwned = true };
            record.FilePath = Path.Combine("notes", $"note-{DateTime.Now:yyyyMMdd-HHmmss}-{record.Id[..6]}.md");
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            record.X = screen.Left + 72 + _notes.Count % 6 * 28;
            record.Y = screen.Top + 72 + _notes.Count % 6 * 28;
            using (File.Create(_paths.Resolve(record.FilePath))) { }
            _configuration.Notes.Add(record);
            var session = AddSession(record);
            SaveConfiguration();
            if (_desktop.Available) session.View.FocusEditor();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _paths.Log(ex.ToString());
            MessageBox.Show(ex.Message, "StickyDesk — создание заметки", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void OpenMarkdown()
    {
        using var dialog = new OpenFileDialog { Title = "Открыть Markdown-файл", Filter = "Markdown (*.md)|*.md", CheckFileExists = true, RestoreDirectory = true };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        OpenFile(dialog.FileName);
    }

    internal void OpenFile(string path)
    {
        var existing = FindFile(path);
        if (existing is not null)
        {
            MessageBox.Show("Этот файл уже открыт в стикере. Вторую редактируемую копию создавать не будем.", "StickyDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (_hidden) ToggleVisibility();
            existing.View.FocusEditor();
            return;
        }
        try
        {
            _files.Read(path);
            if (_hidden) ToggleVisibility();
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            var record = _configuration.Notes.FirstOrDefault(note => note.IsClosed
                && string.Equals(_paths.Resolve(note.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                record = new NoteRecord { FilePath = Path.GetFullPath(path), X = area.Left + 100, Y = area.Top + 100 };
                _configuration.Notes.Add(record);
            }
            record.IsClosed = false;
            AddSession(record);
            SaveConfiguration();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            MessageBox.Show($"Не удалось открыть файл как UTF-8 Markdown.\n\n{ex.Message}", "StickyDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private NoteSession AddSession(NoteRecord record)
    {
        var session = new NoteSession(record, _paths, _files, _configuration.Settings.AutosaveDelayMs, Post,
            path => _configuration.Notes.Any(other => other.Id != record.Id && string.Equals(_paths.Resolve(other.FilePath), path, StringComparison.OrdinalIgnoreCase)));
        _notes.Add(session);
        session.View.ContextMenuStrip = CreateNoteMenu(session);
        session.View.BoundsEdited += bounds => { if (_desktop.Available) _desktop.Position(session.View, bounds); };
        session.View.BoundsCommitted += bounds =>
        {
            record.X = bounds.X;
            record.Y = bounds.Y;
            record.Width = Math.Max(180, (int)Math.Round(bounds.Width * 96d / session.View.DeviceDpi));
            record.Height = Math.Max(120, (int)Math.Round(bounds.Height * 96d / session.View.DeviceDpi));
            ScheduleConfigurationSave();
        };
        session.View.ShellHandleLost += () => Post(ScheduleReattach);
        session.View.CloseRequested += () => Post(() => CloseNote(record.Id));
        session.RelinkRequested += () => Relink(session);
        session.MetadataChanged += () =>
        {
            try { _configurationService.SaveCheckpoint(_configuration); _configurationDirty = false; }
            catch { _configurationDirty = true; throw; }
        };
        if (_desktop.Available) AttachAndShow(session);
        return session;
    }

    private ContextMenuStrip CreateNoteMenu(NoteSession session)
    {
        var menu = ModernMenu.Create();
        menu.Items.Add("Создать стикер", null, (_, _) => CreateNote());
        var colors = new ToolStripMenuItem("Цвет");
        foreach (var (label, hex) in new[] { ("Жёлтый", "#FFF2B3"), ("Зелёный", "#DDF2C2"), ("Голубой", "#D9EEFA"), ("Розовый", "#F8DCE8"), ("Белый", "#F5F5F5") })
        {
            var item = new ToolStripMenuItem(label) { Tag = hex, Checked = session.Record.Color == hex };
            item.Click += (_, _) =>
            {
                session.Record.Color = hex;
                session.View.ApplyColor(hex);
                foreach (ToolStripMenuItem color in colors.DropDownItems) color.Checked = Equals(color.Tag, hex);
                ScheduleConfigurationSave();
            };
            colors.DropDownItems.Add(item);
        }
        ModernMenu.StyleDropDown(colors);
        menu.Items.Add(colors);
        menu.Items.Add("Открыть папку", null, (_, _) => OpenNoteFolder(session.FilePath));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Закрыть заметку", null, (_, _) => CloseNote(session.Record.Id));
        menu.Items.Add("Удалить заметку…", null, (_, _) => Delete(session)).ForeColor = Color.Firebrick;
        return menu;
    }

    private void Relink(NoteSession session)
    {
        using var dialog = new OpenFileDialog { Title = "Указать расположение Markdown-файла", Filter = "Markdown (*.md)|*.md", RestoreDirectory = true };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var existing = FindFile(dialog.FileName);
        if (existing is not null && existing != session) { MessageBox.Show("Этот файл уже подключён к другому стикеру.", "StickyDesk"); return; }
        if (session.Relink(dialog.FileName)) SaveConfiguration();
    }

    private NoteSession? FindFile(string path) => _notes.FirstOrDefault(note => string.Equals(note.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

    internal bool CloseNote(string id, bool interactive = true)
    {
        var session = _notes.FirstOrDefault(note => note.Record.Id == id);
        if (session is null || !session.PrepareToClose(interactive)) return false;
        session.Record.IsClosed = true;
        if (!SaveConfiguration())
        {
            session.Record.IsClosed = false;
            return false;
        }
        _notes.Remove(session);
        session.Dispose();
        return true;
    }

    private void Delete(NoteSession session)
    {
        session.View.CommitTitleEditing();
        var description = session.IsFileMissing
            ? "Файл уже отсутствует. Стикер будет убран с рабочего стола."
            : "Стикер будет убран с рабочего стола, а Markdown-файл — перемещён в Корзину.";
        var answer = MessageBox.Show($"Удалить заметку «{session.View.TitleText}»?\n\n{description}\nКопия текущего текста сохранится для восстановления.\n\n{session.FilePath}",
            "StickyDesk — удаление заметки", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        try
        {
            if (session.RecycleFile()) Detach(session);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _paths.Log(ex.ToString());
            MessageBox.Show($"Не удалось удалить заметку. Стикер остаётся на рабочем столе.\n\n{ex.Message}",
                "StickyDesk — удаление заметки", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Detach(NoteSession session)
    {
        var index = _configuration.Notes.IndexOf(session.Record);
        _configuration.Notes.RemoveAt(index);
        if (!SaveConfiguration())
        {
            _configuration.Notes.Insert(index, session.Record);
            return;
        }
        _notes.Remove(session);
        session.Dispose();
    }

    private void AttachAndShow(NoteSession session)
    {
        try
        {
            session.View.RestoreHandle();
            var record = session.Record;
            var scale = session.View.DeviceDpi / 96d;
            var bounds = StickyNoteView.Constrain(new Rectangle(record.X, record.Y, (int)(record.Width * scale), (int)(record.Height * scale)));
            _desktop.Attach(session.View, bounds);
            if (!_hidden) session.View.Show();
            _desktop.Position(session.View, bounds);
            _paths.Log($"Attached note {record.Id}: parent=0x{NativeMethods.GetParent(session.View.Handle):X}, visible={NativeMethods.IsWindowVisible(session.View.Handle)}, style=0x{(long)NativeMethods.GetWindowLongPtr(session.View.Handle, NativeMethods.GwlStyle):X}, exStyle=0x{(long)NativeMethods.GetWindowLongPtr(session.View.Handle, NativeMethods.GwlExStyle):X}, bounds={session.View.ScreenBounds}.");
            record.X = bounds.X;
            record.Y = bounds.Y;
        }
        catch (Exception ex)
        {
            session.View.Hide();
            _paths.Log(ex.ToString());
            _tray.Notify("Не удалось прикрепить стикер", ex.Message + " Повторите через меню трея.");
        }
    }

    public void ToggleVisibility()
    {
        _hidden = !_hidden;
        foreach (var session in _notes)
        {
            if (_hidden) { session.Flush(false); session.View.Hide(); }
            else if (_desktop.Available) AttachAndShow(session);
        }
    }

    public void ScheduleReattach()
    {
        if (_exiting || _disposed) return;
        _retryAttempt = 0;
        _shellRetry.Stop();
        _shellRetry.Interval = 300;
        _shellRetry.Start();
    }

    private void RetryAttach()
    {
        _shellRetry.Stop();
        if (_desktop.Discover())
        {
            foreach (var note in _notes) AttachAndShow(note);
            ScheduleConfigurationSave();
            return;
        }
        if (++_retryAttempt < 6)
        {
            _shellRetry.Interval = Math.Min(4000, 300 * (1 << _retryAttempt));
            _shellRetry.Start();
        }
        else _tray.Notify("Рабочий стол недоступен", "Заметки сохранены. После запуска Проводника выберите «Восстановить стикеры» в трее.");
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Post(ScheduleReattach);
    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        void SaveSession() { foreach (var session in _notes) { session.View.CommitTitleEditing(); session.Flush(false); } SaveConfiguration(); }
        if (_dispatcher.InvokeRequired) _dispatcher.Invoke(SaveSession);
        else SaveSession();
    }

    private void ScheduleConfigurationSave()
    {
        _configurationDirty = true;
        _configurationTimer.Stop();
        _configurationTimer.Start();
    }

    private bool SaveConfiguration()
    {
        try { _configurationService.Save(_configuration); _configurationDirty = false; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _configurationDirty = true;
            _paths.Log(ex.ToString());
            _tray.Notify("Не удалось сохранить настройки", ex.Message);
            return false;
        }
    }

    public void RequestExit()
    {
        if (_exiting) return;
        foreach (var note in _notes) note.View.CommitTitleEditing();
        var failed = _notes.Where(note => !note.Flush(true)).ToList();
        if (failed.Count > 0)
        {
            var response = MessageBox.Show($"Не сохранены заметки: {failed.Count}.\n\nТекст остаётся в памяти. Вы можете отменить выход и разрешить конфликт или сохранить копию.\n\nВсё равно выйти? Копии восстановления, если их удалось записать, будут предложены при следующем запуске.",
                "StickyDesk — несохранённые изменения", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (response != DialogResult.Yes) return;
        }
        if (!SaveConfiguration() && MessageBox.Show("Настройки не сохранены. Всё равно выйти?", "StickyDesk", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _exiting = true;
        ExitThread();
    }

    public void OpenDataDirectory() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.Root}\"") { UseShellExecute = true });
    private static void OpenNoteFolder(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            MessageBox.Show("Папка этой заметки недоступна. Возможно, она была перемещена или удалена.",
                "StickyDesk — папка заметки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Не удалось открыть папку заметки.\n\n{ex.Message}",
                "StickyDesk — папка заметки", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Post(Action action)
    {
        if (_disposed || _exiting || _dispatcher.IsDisposed) return;
        try { _dispatcher.BeginInvoke(() => { if (!_disposed && !_exiting) action(); }); }
        catch (InvalidOperationException) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            _shellRetry.Dispose();
            _configurationTimer.Dispose();
            foreach (var session in _notes) { session.Flush(false); session.Dispose(); }
            if (_configurationDirty) SaveConfiguration();
            _desktop.Dispose();
            _messages.Dispose();
            _tray.Dispose();
            _dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
