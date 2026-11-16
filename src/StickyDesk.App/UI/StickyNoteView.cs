using StickyDesk.Core;

namespace StickyDesk;

internal sealed class StickyNoteView : Form
{
    private readonly Label _title;
    private readonly TextBox _titleEditor;
    private readonly Panel _titleSeparator;
    private readonly NoteStatus _status;
    private readonly Panel _header;
    private readonly Panel _footer;
    private readonly PlainMarkdownEditor _editor;
    private readonly RichTextBox _preview;
    private readonly NoteActionButton _modeButton;
    private readonly NoteActionButton _menuButton;
    private readonly NoteActionButton _grip;
    private NoteTheme _theme;
    private MarkdownPreviewDocument? _previewDocument;
    private string? _previewSource;
    private bool _isEditing;
    private Point _dragOrigin;
    private Rectangle _originalBounds;
    private bool _moving;
    private bool _resizing;
    private bool _loading;
    private bool _isEditingTitle;
    private bool _refreshingShape;
    private bool _shapeRefreshQueued;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool UseLayeredDesktopRendering { get; set; }
    public event Action? TextEdited;
    public event Action<Rectangle>? BoundsEdited;
    public event Action<Rectangle>? BoundsCommitted;
    public event Action? SaveRequested;
    public event Action? StatusClicked;
    public event Action? CloseRequested;
    public event Action? ShellHandleLost;
    public event Action? EditingChanged;
    public event Action<string>? TitleEdited;
    public string Markdown => MarkdownSnapshot.Normalize(_editor.Text);
    public bool IsEditing => _isEditing;
    internal RichTextBox PreviewControl => _preview;
    internal RichTextBox EditorControl => _editor;
    internal TextBox TitleEditorControl => _titleEditor;
    internal Control TitleLabelControl => _title;
    internal Control TitleSeparatorControl => _titleSeparator;
    internal Control DragAreaControl => _header;
    internal string TitleText => _title.Text;
    internal void ShowFileRecoveryMenu(ContextMenuStrip menu) => menu.Show(_status, new Point(0, _status.Height));

    public StickyNoteView(NoteRecord note)
    {
        Text = "StickyDesk — " + Path.GetFileName(note.FilePath);
        Name = "StickyNote";
        AccessibleName = Text;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        MinimumSize = new Size(180, 120);
        Size = new Size(note.Width, note.Height);
        Font = new Font("Segoe UI", 10);
        Padding = new Padding(1);
        DoubleBuffered = true;

        _header = new Panel { Dock = DockStyle.Top, Height = 34, Cursor = Cursors.SizeAll, Padding = new Padding(16, 3, 10, 3) };
        _header.Paint += (_, e) =>
        {
            var scale = DeviceDpi / 96f;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var accent = new Pen(_theme.Accent, 3 * scale)
            { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            e.Graphics.DrawLine(accent, 16 * scale, 17 * scale, 39 * scale, 17 * scale);
        };
        var titleSection = new Panel { Dock = DockStyle.Top, Height = 44 };
        var titleHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 9) };
        _title = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            Text = note.Title ?? (note.IsOwned ? "Заметка" : Path.GetFileNameWithoutExtension(note.FilePath)), Font = new Font("Segoe UI Semibold", 16), ForeColor = NoteTheme.Ink,
            Cursor = Cursors.IBeam, AccessibleName = "Название заметки" };
        _titleEditor = new TextBox { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, AutoSize = false,
            Font = new Font("Segoe UI Semibold", 16),
            ForeColor = NoteTheme.Ink, MaxLength = 80, Visible = false, AccessibleName = "Заголовок заметки" };
        titleHost.Controls.Add(_title);
        titleHost.Controls.Add(_titleEditor);
        _menuButton = new NoteActionButton { Dock = DockStyle.Right, Width = 32, Glyph = NoteGlyph.Menu, AccessibleName = "Меню стикера" };
        _titleSeparator = new Panel { Dock = DockStyle.Bottom, Height = 1, AccessibleName = "Линия под названием заметки" };
        titleSection.Controls.Add(titleHost);
        titleSection.Controls.Add(_titleSeparator);
        _header.Controls.Add(_menuButton);
        _footer = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(16, 4, 8, 10) };
        _status = new NoteStatus { Dock = DockStyle.Fill, ForeColor = NoteTheme.Muted, AutoEllipsis = true, Text = "Сохранено", Cursor = Cursors.Default };
        _modeButton = new NoteActionButton { Dock = DockStyle.Right, Width = 88, Text = "Править", Glyph = NoteGlyph.Edit,
            Filled = true, AccessibleName = "Редактировать Markdown" };
        _grip = new NoteActionButton { Dock = DockStyle.Right, Width = 20, Glyph = NoteGlyph.Resize,
            Cursor = Cursors.SizeNWSE, AccessibleName = "Изменить размер", TabStop = false };
        _footer.Controls.Add(_status);
        _footer.Controls.Add(_modeButton);
        _footer.Controls.Add(_grip);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 2) };
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
        _editor = new PlainMarkdownEditor { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 11),
            ForeColor = NoteTheme.Ink, DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
            AcceptsTab = true, HideSelection = false, AutoWordSelection = false, AccessibleName = "Текст заметки Markdown", Visible = false };
        _preview = new RichTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 11),
            ForeColor = NoteTheme.Ink, ReadOnly = true, DetectUrls = false, WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical, HideSelection = false, Cursor = Cursors.IBeam, AccessibleName = "Оформленная заметка Markdown" };
        content.Controls.Add(_preview);
        content.Controls.Add(_editor);
        body.Controls.Add(content);
        body.Controls.Add(titleSection);
        Controls.Add(body);
        Controls.Add(_footer);
        Controls.Add(_header);
        ApplyColor(note.Color);

        _editor.TextChanged += (_, _) => { if (!_loading) TextEdited?.Invoke(); };
        _editor.LostFocus += (_, _) =>
        {
            SaveRequested?.Invoke();
            if (IsHandleCreated && !Disposing) BeginInvoke(() =>
            {
                if (!IsDisposed && !_editor.Focused && ContextMenuStrip?.Visible != true) ShowPreview();
            });
        };
        _preview.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var offset = _preview.GetCharIndexFromPosition(e.Location);
            var link = _previewDocument?.Links.FirstOrDefault(item => offset >= item.Start && offset < item.Start + item.Length);
            if (ModifierKeys.HasFlag(Keys.Control) && link is not null) { OpenLink(link.Url); return; }
            BeginEditing(_previewDocument?.SourceOffset(offset, _editor.TextLength) ?? 0);
        };
        _modeButton.Click += (_, _) => { if (_isEditing) ShowPreview(); else BeginEditing(); };
        _title.Click += (_, _) => BeginTitleEditing();
        _titleEditor.LostFocus += (_, _) => { if (!Disposing && !IsDisposed) CommitTitleEditing(); };
        _titleEditor.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { CommitTitleEditing(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { CancelTitleEditing(); e.SuppressKeyPress = true; }
        };
        _status.Click += (_, _) => StatusClicked?.Invoke();
        foreach (var control in new Control[] { _header })
        {
            control.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartDrag(control, false); };
            control.MouseMove += (_, _) => UpdateDrag();
            control.MouseUp += (_, _) => EndDrag(control);
            control.MouseCaptureChanged += (_, _) => { if (!control.Capture) EndDrag(control); };
        }
        _grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartDrag(_grip, true); };
        _grip.MouseMove += (_, _) => UpdateDrag();
        _grip.MouseUp += (_, _) => EndDrag(_grip);
        _grip.MouseCaptureChanged += (_, _) => { if (!_grip.Capture) EndDrag(_grip); };
        _menuButton.Click += (_, _) => ContextMenuStrip?.Show(_menuButton, new Point(0, _menuButton.Height));
        _header.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ContextMenuStrip?.Show(Cursor.Position); };
        _title.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ContextMenuStrip?.Show(Cursor.Position); };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S) { CommitTitleEditing(); SaveRequested?.Invoke(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape && _isEditing) { ShowPreview(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F2 && !_isEditingTitle) { BeginEditing(); e.SuppressKeyPress = true; }
        };
        Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = NoteTheme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 12 * DeviceDpi / 96f);
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawPath(pen, path);
        };
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var value = base.CreateParams;
            value.ExStyle |= (int)NativeMethods.WsExToolWindow;
            if (UseLayeredDesktopRendering) value.ExStyle |= (int)NativeMethods.WsExLayered;
            return value;
        }
    }

    public void SetMarkdown(string text)
    {
        _loading = true;
        var selection = _editor.SelectionStart;
        try { _editor.Text = text; _editor.SelectionStart = Math.Min(selection, _editor.TextLength); _editor.ClearUndo(); }
        finally { _loading = false; }
        if (!_isEditing) RenderPreview();
    }

    public void FocusEditor() => BeginEditing();

    public void BeginEditing(int? caret = null)
    {
        CommitTitleEditing();
        _isEditing = true;
        _preview.Visible = false;
        _editor.Visible = true;
        _editor.BringToFront();
        if (caret.HasValue) { _editor.SelectionStart = Math.Clamp(caret.Value, 0, _editor.TextLength); _editor.SelectionLength = 0; }
        _modeButton.Text = "Готово";
        _modeButton.Glyph = NoteGlyph.Check;
        _modeButton.AccessibleName = "Показать оформленную заметку";
        _editor.Focus();
        EditingChanged?.Invoke();
    }

    public void ShowPreview()
    {
        if (!_isEditing) return;
        _isEditing = false;
        RenderPreview();
        _editor.Visible = false;
        _preview.Visible = true;
        _preview.BringToFront();
        _modeButton.Text = "Править";
        _modeButton.Glyph = NoteGlyph.Edit;
        _modeButton.AccessibleName = "Редактировать Markdown";
        SaveRequested?.Invoke();
        EditingChanged?.Invoke();
    }

    private void RenderPreview()
    {
        var source = Markdown;
        if (_previewDocument is not null && source == _previewSource) return;
        _previewDocument = MarkdownPreviewRenderer.Render(source);
        _previewSource = source;
        _preview.ForeColor = source.Length == 0 ? NoteTheme.Muted : NoteTheme.Ink;
        _preview.Rtf = _previewDocument.Rtf;
        if (source.Length == 0)
        {
            _preview.Text = "Напишите что-нибудь…";
        }
        _preview.SelectionStart = 0;
        _preview.SelectionLength = 0;
    }

    private static void OpenLink(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto")) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { MessageBox.Show(ex.Message, "StickyDesk — ссылка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.Error = error;
        _status.ForeColor = error ? Color.FromArgb(174, 70, 64) : NoteTheme.Muted;
        _status.Cursor = error ? Cursors.Hand : Cursors.Default;
        _status.AccessibleRole = error ? AccessibleRole.Link : AccessibleRole.StaticText;
        _status.Invalidate();
    }

    public void SetTitle(string title) { _title.Text = title; Text = "StickyDesk — " + title; AccessibleName = Text; }

    public void BeginTitleEditing()
    {
        if (_isEditingTitle) return;
        ShowPreview();
        _isEditingTitle = true;
        _titleEditor.Text = _title.Text;
        _title.Visible = false;
        _titleEditor.Visible = true;
        _titleEditor.BringToFront();
        _titleEditor.Focus();
        _titleEditor.SelectAll();
    }

    internal void CommitTitleEditing()
    {
        if (!_isEditingTitle) return;
        var title = _titleEditor.Text;
        _isEditingTitle = false;
        _titleEditor.Visible = false;
        _title.Visible = true;
        if (title != _title.Text) TitleEdited?.Invoke(title);
    }

    internal void CancelTitleEditing()
    {
        if (!_isEditingTitle) return;
        _isEditingTitle = false;
        _titleEditor.Visible = false;
        _title.Visible = true;
    }

    public void ApplyColor(string hex)
    {
        _theme = NoteTheme.From(hex);
        BackColor = _theme.Surface;
        foreach (Control control in Controls) SetBackground(control, _theme.Surface);
        _titleSeparator.BackColor = _theme.Divider;
        _menuButton.Accent = _modeButton.Accent = _grip.Accent = _theme.Accent;
        Invalidate(true);
    }

    private static void SetBackground(Control control, Color color)
    {
        control.BackColor = color;
        foreach (Control child in control.Controls) SetBackground(child, color);
    }

    public Rectangle ScreenBounds
    {
        get
        {
            if (IsHandleCreated && NativeMethods.GetWindowRect(Handle, out var rect))
                return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return Bounds;
        }
    }

    public void RestoreHandle() { if (!IsHandleCreated) _ = Handle; }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _shapeRefreshQueued = false;
        base.OnHandleDestroyed(e);
        if (!Disposing && !IsDisposed && !RecreatingHandle) ShellHandleLost?.Invoke();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        MinimumSize = new Size(180 * DeviceDpi / 96, 120 * DeviceDpi / 96);
        UpdateShape();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateShape();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshShape();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshShape();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && CloseRequested is not null)
        {
            e.Cancel = true;
            CloseRequested.Invoke();
        }
        base.OnFormClosing(e);
    }

    private void UpdateShape() => RefreshShape();

    internal void RefreshShape()
    {
        if (_refreshingShape || Disposing || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        _refreshingShape = true;
        try
        {
            using var path = NoteTheme.Round(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 12 * DeviceDpi / 96f);
            var previous = Region;
            Region = new Region(path);
            previous?.Dispose();
        }
        finally { _refreshingShape = false; }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        // Native style, focus and menu transitions can reset clipping after Show/Position.
        // Reapply after the whole transition, including on every subsequently created sticker.
        if (message.Msg is 0x0047 or 0x007D or 0x0086 or 0x0018) QueueShapeRefresh();
    }

    private void QueueShapeRefresh()
    {
        if (_refreshingShape || _shapeRefreshQueued || !IsHandleCreated || Disposing || IsDisposed) return;
        _shapeRefreshQueued = true;
        BeginInvoke(() =>
        {
            _shapeRefreshQueued = false;
            if (IsHandleCreated && !Disposing && !IsDisposed) RefreshShape();
        });
    }

    private void StartDrag(Control sender, bool resize)
    {
        _dragOrigin = Cursor.Position;
        _originalBounds = ScreenBounds;
        _resizing = resize;
        _moving = !resize;
        sender.Capture = true;
    }

    private void UpdateDrag()
    {
        if (!_moving && !_resizing) return;
        var delta = new Size(Cursor.Position.X - _dragOrigin.X, Cursor.Position.Y - _dragOrigin.Y);
        var bounds = _originalBounds;
        if (_moving) bounds.Location += delta;
        else bounds.Size = new Size(Math.Max(MinimumSize.Width, bounds.Width + delta.Width), Math.Max(MinimumSize.Height, bounds.Height + delta.Height));
        BoundsEdited?.Invoke(Constrain(bounds));
    }

    private void EndDrag(Control control)
    {
        if (!_moving && !_resizing) return;
        _moving = _resizing = false;
        control.Capture = false;
        BoundsCommitted?.Invoke(ScreenBounds);
    }

    internal static Rectangle Constrain(Rectangle bounds)
    {
        var screen = Screen.FromRectangle(bounds).WorkingArea;
        bounds.Width = Math.Min(bounds.Width, screen.Width);
        bounds.Height = Math.Min(bounds.Height, screen.Height);
        bounds.X = Math.Clamp(bounds.X, screen.Left, screen.Right - bounds.Width);
        bounds.Y = Math.Clamp(bounds.Y, screen.Top, screen.Bottom - bounds.Height);
        return bounds;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ContextMenuStrip?.Dispose(); _editor.Font.Dispose(); _preview.Font.Dispose(); _modeButton.Font.Dispose(); _menuButton.Font.Dispose(); _grip.Font.Dispose(); _title.Font.Dispose(); _titleEditor.Font.Dispose(); _status.Font.Dispose(); }
        base.Dispose(disposing);
    }
}
