using System.ComponentModel;
using System.Runtime.InteropServices;
using StickyDesk.Core;

namespace StickyDesk;

internal sealed class DesktopIntegration : IDisposable
{
    private readonly AppPaths _paths;
    private readonly Action _shellChanged;
    private readonly NativeMethods.WinEventProc _callback;
    private nint _hook;
    public nint Host { get; private set; }
    public bool RequiresLayeredRendering { get; private set; }
    public bool Available => Host != 0 && NativeMethods.IsWindow(Host);

    public DesktopIntegration(AppPaths paths, Action shellChanged)
    {
        _paths = paths;
        _shellChanged = shellChanged;
        _callback = OnShellEvent;
    }

    public bool Discover()
    {
        if (_hook != 0) { NativeMethods.UnhookWinEvent(_hook); _hook = 0; }
        Host = 0;
        NativeMethods.EnumWindows((top, _) =>
        {
            if (NativeMethods.ClassName(top) is not ("Progman" or "WorkerW")) return true;
            NativeMethods.EnumChildWindows(top, (child, _) =>
            {
                if (NativeMethods.ClassName(child) != "SHELLDLL_DefView") return true;
                Host = NativeMethods.GetParent(child);
                return false;
            }, 0);
            return Host == 0;
        }, 0);
        if (!Available) return false;
        // Windows 11's raised desktop has no GDI redirection surface on Progman.
        // A normal child can be WS_VISIBLE and correctly placed but still never composed.
        RequiresLayeredRendering = ((long)NativeMethods.GetWindowLongPtr(Host, NativeMethods.GwlExStyle)
            & NativeMethods.WsExNoRedirectionBitmap) != 0;
        NativeMethods.GetWindowThreadProcessId(Host, out var process);
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EventObjectDestroy, NativeMethods.EventObjectDestroy,
            0, _callback, process, 0, 2); // OUTOFCONTEXT, SKIPOWNPROCESS
        _paths.Log($"Desktop host: 0x{Host:X}, {NativeMethods.ClassName(Host)}, Explorer PID {process}, layered children required={RequiresLayeredRendering}.");
        return true;
    }

    public void Attach(StickyNoteView view, Rectangle screenBounds)
    {
        if (!Available) throw new InvalidOperationException("Рабочий стол Windows пока недоступен.");
        view.UseLayeredDesktopRendering = RequiresLayeredRendering;
        var handle = view.Handle;
        var style = (long)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlStyle);
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlStyle, (nint)((style & ~NativeMethods.WsPopup) | NativeMethods.WsChild));
        var extended = (long)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
        extended = (extended & ~NativeMethods.WsExAppWindow) | NativeMethods.WsExToolWindow;
        if (RequiresLayeredRendering) extended |= NativeMethods.WsExLayered;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, (nint)extended);
        Marshal.SetLastPInvokeError(0);
        var previous = NativeMethods.SetParent(handle, Host);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (NativeMethods.GetParent(handle) != Host) throw new InvalidOperationException("Windows не удалось прикрепить стикер к рабочему столу.");
        if (RequiresLayeredRendering && !view.EnsureDesktopTransparency())
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Не удалось включить отрисовку стикера на рабочем столе Windows 11.");
        Position(view, screenBounds);
    }

    public void Position(StickyNoteView view, Rectangle screenBounds)
    {
        var position = new NativeMethods.Point { X = screenBounds.X, Y = screenBounds.Y };
        NativeMethods.MapWindowPoints(0, Host, ref position, 1);
        if (!NativeMethods.SetWindowPos(view.Handle, 0, position.X, position.Y, screenBounds.Width, screenBounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        // Applying native child-window styles can reset clipping even when WM_SIZE never fires.
        view.RefreshShape();
    }

    private void OnShellEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (window == Host && objectId == 0) _shellChanged();
    }

    public void Dispose()
    {
        if (_hook != 0) NativeMethods.UnhookWinEvent(_hook);
        GC.KeepAlive(_callback);
    }
}
