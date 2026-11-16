using System.Diagnostics;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace StickyDesk;

internal sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem[] _recentItems;
    private readonly ToolStripMenuItem _recentHeading;
    private readonly ToolStripSeparator _recentSeparator;
    internal ContextMenuStrip Menu => _menu;
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public TrayManager(NoteManager manager)
    {
        _image = CreateIcon();
        _menu = ModernMenu.Create();
        _menu.Items.Add("Создать стикер", null, (_, _) => manager.CreateNote());
        _menu.Items.Add("Открыть Markdown-файл…", null, (_, _) => manager.OpenMarkdown());
        _menu.Items.Add("Открыть папку заметок", null, (_, _) => manager.OpenNotesDirectory());
        _menu.Items.Add(new ToolStripSeparator());
        _recentHeading = new ToolStripMenuItem("Последние заметки") { Enabled = false, Visible = false };
        _menu.Items.Add(_recentHeading);
        _recentItems = Enumerable.Range(0, 3).Select(_ => new ToolStripMenuItem { Visible = false }).ToArray();
        foreach (var item in _recentItems)
        {
            item.Click += (_, _) => { if (item.Tag is string id) manager.OpenRecentNote(id); };
            _menu.Items.Add(item);
        }
        _recentSeparator = new ToolStripSeparator { Visible = false };
        _menu.Items.Add(_recentSeparator);
        var visibility = new ToolStripMenuItem("Скрыть стикеры", null, (_, _) => manager.ToggleVisibility());
        _menu.Items.Add(visibility);
        _menu.Items.Add("Восстановить стикеры", null, (_, _) => manager.ScheduleReattach());
        _menu.Items.Add(new ToolStripSeparator());
        var settings = new ToolStripMenuItem("Настройки");
        var startup = new ToolStripMenuItem("Запускать вместе с Windows") { CheckOnClick = false };
        startup.Click += (_, _) =>
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
                if (key.GetValue("StickyDesk") is not null) key.DeleteValue("StickyDesk", false);
                else
                {
                    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь приложения.");
                    key.SetValue("StickyDesk", $"\"{executable}\" --data-dir \"{manager.DataDirectory}\"");
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { MessageBox.Show(ex.Message, "StickyDesk — автозапуск", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        settings.DropDownItems.Add(startup);
        ModernMenu.StyleDropDown(settings);
        _menu.Items.Add(settings);
        _menu.Items.Add("О StickyDesk", null, (_, _) => MessageBox.Show($"StickyDesk {typeof(TrayManager).Assembly.GetName().Version?.ToString(3)}\nMarkdown-стикеры для рабочего стола Windows 11.\n\nНазвание крупным текстом в начале заметки меняется одним кликом.\n\nКликните по основному тексту или нажмите «Править» для редактирования Markdown. «Готово» и Esc возвращают просмотр. Ctrl+клик открывает ссылку.\n\nЕсли файл удалён, нажмите красный статус для восстановления, выбора расположения или сохранения копии.", "StickyDesk", MessageBoxButtons.OK, MessageBoxIcon.Information));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Выход", null, (_, _) => manager.RequestExit());
        _menu.Opening += (_, _) =>
        {
            RefreshRecentNotes(manager);
            visibility.Text = manager.Hidden ? "Показать стикеры" : "Скрыть стикеры";
            try { using var key = Registry.CurrentUser.OpenSubKey(StartupKey); startup.Checked = key?.GetValue("StickyDesk") is not null; }
            catch (System.Security.SecurityException) { startup.Enabled = false; }
        };
        _icon = new NotifyIcon { Icon = _image, Text = "StickyDesk — Markdown на рабочем столе", ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => manager.CreateNote();
    }

    private void RefreshRecentNotes(NoteManager manager)
    {
        var notes = manager.RecentNotes;
        _recentHeading.Visible = _recentSeparator.Visible = notes.Count > 0;
        for (var index = 0; index < _recentItems.Length; index++)
        {
            var item = _recentItems[index];
            item.Visible = index < notes.Count;
            item.Tag = null;
            if (index >= notes.Count) continue;
            var note = notes[index];
            var title = string.IsNullOrWhiteSpace(note.Title)
                ? note.IsOwned ? "Заметка" : Path.GetFileNameWithoutExtension(note.FilePath) : note.Title;
            item.Text = (title.Length > 48 ? title[..47] + "…" : title).Replace("&", "&&", StringComparison.Ordinal);
            item.ToolTipText = Path.IsPathRooted(note.FilePath) ? note.FilePath : Path.Combine(manager.DataDirectory, note.FilePath);
            item.Tag = note.Id;
        }
    }

    public void Notify(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message.Length > 240 ? message[..240] : message;
        _icon.BalloonTipIcon = ToolTipIcon.Warning;
        _icon.ShowBalloonTip(6000);
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var paper = new SolidBrush(Color.FromArgb(255, 242, 179));
            using var ink = new Pen(Color.FromArgb(74, 99, 77), 2.2f);
            graphics.FillRectangle(paper, 5, 3, 23, 26);
            graphics.DrawRectangle(ink, 5, 3, 23, 26);
            graphics.DrawLine(ink, 10, 11, 23, 11);
            graphics.DrawLine(ink, 10, 16, 23, 16);
            graphics.DrawLine(ink, 10, 21, 18, 21);
        }
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { NativeMethods.DestroyIcon(handle); }
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); _menu.Dispose(); _image.Dispose(); }
}
