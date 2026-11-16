using System.Runtime.InteropServices;
using StickyDesk.Core;

namespace StickyDesk.UIHarness;

internal static class WindowLifecycleChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        CheckNewStickers(Path.Combine(root, "new-stickers"), check);
        CheckLayeredPreview(check);
        var paths = new AppPaths(root);
        var file = Path.Combine(paths.Notes, "Закрываемая заметка.md");
        File.WriteAllText(file, "Первый текст");
        var record = new NoteRecord { FilePath = file, Title = "Проверка закрытия", Color = "#DDF2C2", Width = 300, Height = 300 };
        var store = new ConfigurationService(paths);
        store.Save(new AppConfiguration { Notes = [record] });
        static bool WaitUntil(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 4000;
            while (!condition() && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            return condition();
        }
        StickyNoteView FindView() => Application.OpenForms.OfType<StickyNoteView>().Single(view => view.TitleText == record.Title);
        using (var manager = new NoteManager(paths))
        {
            check(WaitUntil(() => manager.Count == 1), "A configured sticker opens before any user resize");
            var view = FindView();
            check(HasRoundedRegion(view), "The initial desktop window clips all four corners before resizing");
            SetWindowRgn(view.Handle, 0, false);
            manager.ScheduleReattach();
            check(WaitUntil(() => HasRoundedRegion(view)), "Desktop reattachment restores rounded clipping after a native region reset");
            view.BeginEditing();
            view.EditorControl.Text = "Текст перед закрытием";
            var closeItem = view.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(item => item.Text == "Закрыть заметку");
            closeItem.PerformClick();
            check(manager.Count == 0 && view.IsDisposed && File.ReadAllText(file) == "Текст перед закрытием",
                "The close menu saves pending text and removes only the sticker");
            var closed = store.Load().Notes.Single();
            check(closed.IsClosed && closed.Id == record.Id && closed.Color == record.Color && closed.Title == record.Title,
                "Closing preserves the note identity and appearance in configuration");
        }
        using (var manager = new NoteManager(paths))
        {
            Application.DoEvents();
            check(manager.Count == 0 && File.Exists(file), "A closed note stays closed after application restart");
            manager.ToggleVisibility();
            manager.OpenFile(file);
            var view = FindView();
            var reopened = store.Load().Notes.Single();
            check(manager.Count == 1 && view.Visible && !manager.Hidden && !reopened.IsClosed && reopened.Id == record.Id && view.Markdown == "Текст перед закрытием",
                "Opening a closed file restores the original note without duplicate records");
            view.Close();
            check(WaitUntil(() => manager.Count == 0) && File.Exists(file), "A window close request closes the sticker without deleting its file");
            manager.OpenFile(file);
            view = FindView();
            File.Delete(file);
            check(manager.CloseNote(record.Id, false) && manager.Count == 0 && !File.Exists(file)
                && File.ReadAllText(paths.RecoveryFile(record.Id)) == "Текст перед закрытием",
                "Closing immediately after external deletion preserves text without recreating the file");
            File.WriteAllText(file, "Текст перед закрытием");
            manager.OpenFile(file);
            view = FindView();
            File.WriteAllText(paths.RecoveryFile(record.Id), "Previous recovery");
            view.BeginEditing();
            view.EditorControl.Text = "Последнее изменение";
            using (File.Open(paths.RecoveryFile(record.Id), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                check(!manager.CloseNote(record.Id, false) && manager.Count == 1 && !view.IsDisposed && !store.Load().Notes.Single().IsClosed,
                    "A note remains open if its pending text cannot be preserved");
            }
            check(manager.CloseNote(record.Id, false) && File.ReadAllText(file) == "Последнее изменение",
                "Closing succeeds once recovery storage becomes writable again");
        }
    }

    private static void CheckLayeredPreview(Action<bool, string> check)
    {
        using var host = new Form { ClientSize = new Size(340, 390), BackColor = Color.FromArgb(30, 36, 45) };
        using var view = new StickyNoteView(new NoteRecord { Title = "Проверка прозрачности" }) { TopLevel = false, Dock = DockStyle.Fill };
        host.Controls.Add(view);
        host.Show();
        view.Show();
        view.UseLayeredDesktopRendering = true;
        var extended = (long)NativeMethods.GetWindowLongPtr(view.Handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLongPtr(view.Handle, NativeMethods.GwlExStyle, (nint)(extended | NativeMethods.WsExLayered));
        view.RefreshShape();
        Application.DoEvents();
        check(HasTransparentCornerPixels(view), "The standalone visual harness enables child-window transparency without a runtime exception");
        NativeMethods.SetWindowLongPtr(view.Handle, NativeMethods.GwlExStyle, (nint)(extended & ~NativeMethods.WsExLayered));
        view.RefreshShape();
        check(view.BackColor != StickyNoteView.CornerTransparencyColor && view.Region is not null,
            "A temporary layering failure keeps an opaque paper background without a key-colored frame or exception");
        NativeMethods.SetWindowLongPtr(view.Handle, NativeMethods.GwlExStyle, (nint)(extended | NativeMethods.WsExLayered));
        view.RefreshShape();
        Application.DoEvents();
        check(HasTransparentCornerPixels(view), "Transparent corners recover when native layering becomes available again");
    }

    private static void CheckNewStickers(string root, Action<bool, string> check)
    {
        var paths = new AppPaths(root);
        new ConfigurationService(paths).Save(new AppConfiguration());
        using var manager = new NoteManager(paths);
        Application.DoEvents();
        for (var index = 0; index < 3; index++)
        {
            if (index == 0) manager.CreateNote();
            else
            {
                var menu = index == 1
                    ? Application.OpenForms.OfType<StickyNoteView>().Single(view => view.TitleText == "Заметка").ContextMenuStrip!
                    : manager.TrayMenu;
                menu.Show(new Point(30, 30));
                Application.DoEvents();
                menu.Items.Cast<ToolStripItem>().Single(item => item.Text == "Создать стикер").PerformClick();
                menu.Close();
            }
            Application.DoEvents();
            var views = Application.OpenForms.OfType<StickyNoteView>()
                .Where(view => view.ContextMenuStrip is not null && view.Text == "StickyDesk — Заметка").ToArray();
            check(manager.Count == index + 1 && views.Count(HasRoundedRegion) == index + 1,
                $"New sticker {index + 1} and its predecessors retain rounded clipping after editor focus");
            check(views.All(view => !view.UseLayeredDesktopRendering || HasTransparentCornerPixels(view)),
                $"New sticker {index + 1} has actual transparent corner pixels and opaque paper in the layered compositor");
        }
        var stickers = Application.OpenForms.OfType<StickyNoteView>().Where(view => view.TitleText == "Заметка").ToArray();
        var colorsPreserveCorners = true;
        foreach (var color in new[] { "#FFF2B3", "#DDF2C2", "#D9EEFA", "#F8DCE8", "#F5F5F5" })
        {
            foreach (var sticker in stickers) sticker.ApplyColor(color);
            colorsPreserveCorners &= stickers.All(HasRoundedRegion);
        }
        check(colorsPreserveCorners, "All five paper colors preserve real transparent corners");
        foreach (var sticker in stickers) sticker.Size = new Size(180, 120);
        Application.DoEvents();
        check(stickers.All(HasRoundedRegion), "Transparent corners survive resizing down to the minimum sticker size");
        foreach (var sticker in stickers) SetWindowRgn(sticker.Handle, 0, false);
        Application.DoEvents();
        check(stickers.All(HasRoundedRegion), "Every sticker repairs native clipping reset without resizing or manual desktop reattachment");
        manager.ToggleVisibility();
        manager.ToggleVisibility();
        Application.DoEvents();
        check(stickers.All(view => view.Visible && HasRoundedRegion(view)), "All subsequently created stickers stay rounded after hide and show");
    }

    private static bool HasRoundedRegion(StickyNoteView view)
    {
        if (view.IsDisposed || !view.IsHandleCreated) return false;
        if (view.UseLayeredDesktopRendering) return HasTransparentCornerPixels(view);
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            if (GetWindowRgn(view.Handle, region) != 3 || !NativeMethods.GetWindowRect(view.Handle, out var bounds)) return false;
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            return !PtInRegion(region, 0, 0) && !PtInRegion(region, width - 1, 0)
                && !PtInRegion(region, 0, height - 1) && !PtInRegion(region, width - 1, height - 1)
                && PtInRegion(region, width / 2, height / 2);
        }
        finally { DeleteObject(region); }
    }

    private static bool HasTransparentCornerPixels(StickyNoteView view)
    {
        if (!NativeMethods.GetLayeredWindowAttributes(view.Handle, out var key, out var alpha, out var flags)
            || key != (uint)ColorTranslator.ToWin32(StickyNoteView.CornerTransparencyColor) || alpha != 255 || flags != 3) return false;
        using var bitmap = new Bitmap(view.ClientSize.Width, view.ClientSize.Height);
        view.DrawToBitmap(bitmap, view.ClientRectangle);
        var transparent = StickyNoteView.CornerTransparencyColor.ToArgb();
        var width = bitmap.Width;
        var height = bitmap.Height;
        return bitmap.GetPixel(1, 1).ToArgb() == transparent && bitmap.GetPixel(width - 2, 1).ToArgb() == transparent
            && bitmap.GetPixel(1, height - 2).ToArgb() == transparent && bitmap.GetPixel(width - 2, height - 2).ToArgb() == transparent
            && bitmap.GetPixel(width / 2, 1).ToArgb() != transparent && bitmap.GetPixel(width / 2, height - 2).ToArgb() != transparent
            && bitmap.GetPixel(1, height / 2).ToArgb() != transparent && bitmap.GetPixel(width - 2, height / 2).ToArgb() != transparent
            && bitmap.GetPixel(width / 2, height / 2).ToArgb() != transparent;
    }

    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
}
