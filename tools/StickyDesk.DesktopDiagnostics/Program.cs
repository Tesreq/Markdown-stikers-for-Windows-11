using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// Read-only native window diagnostics. Does not move, activate, or modify windows.
var roots = new List<object>();
var notes = new List<object>();
var invalidNotes = 0;
var editLengths = new List<int>();
var sourceHashes = new List<object>();
Native.EnumWindows((window, _) =>
{
    var name = Native.ClassName(window);
    Native.GetWindowThreadProcessId(window, out var process);
    if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" || (args.Length > 0 && process.ToString() == args[0]))
    {
        roots.Add(new { Window = Describe(window), Children = DirectChildren(window, 0) });
    }
    return true;
}, 0);
var verification = args.Contains("--verify");
Console.WriteLine(JsonSerializer.Serialize(args.Contains("--source-hashes") ? sourceHashes : args.Contains("--editor-lengths") ? editLengths.Cast<object>().ToList() : verification ? notes : roots,
    new JsonSerializerOptions { WriteIndented = true }));
return verification && (invalidNotes > 0 || notes.Count == 0) ? 1 : 0;

object Describe(nint window)
{
    Native.GetWindowThreadProcessId(window, out var process);
    Native.GetWindowRect(window, out var bounds);
    Native.GetClientRect(window, out var client);
    Native.DwmGetWindowAttribute(window, 14, out var cloaked, 4);
    var title = new StringBuilder(256);
    Native.GetWindowText(window, title, 256);
    if (args.Contains("--source-hashes") && process.ToString() == args.FirstOrDefault()
        && Native.ClassName(window).Contains(".EDIT.", StringComparison.OrdinalIgnoreCase) && Native.IsWindowVisible(window))
        sourceHashes.Add(new { Handle = $"0x{window:X}", Success = false, Sha256 = (string?)null, Reason = "A title or dialog field is being edited" });
    if (Native.ClassName(window).Contains("RICHEDIT", StringComparison.OrdinalIgnoreCase) && process.ToString() == args.FirstOrDefault())
    {
        var success = Native.SendMessageTimeout(window, 14, 0, 0, 2, 1000, out var length);
        editLengths.Add(success != 0 ? (int)length : -1);
        if (args.Contains("--source-hashes") && ((long)Native.GetWindowLongPtr(window, -16) & 0x800) == 0)
        {
            // Only the editable source, never the read-only formatted preview.
            var readable = success != 0 && length < 65535;
            var buffer = new StringBuilder(readable ? (int)length + 1 : 1);
            var read = readable ? Native.ReadText(window, 13, (nuint)buffer.Capacity, buffer, 2, 1000, out _) : 0;
            var source = buffer.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
            var hash = read != 0 ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source))) : null;
            sourceHashes.Add(new { Handle = $"0x{window:X}", Success = read != 0, Length = source.Length, Sha256 = hash });
        }
    }
    if (title.ToString().StartsWith("StickyDesk — ", StringComparison.Ordinal) && (args.Length == 0 || process.ToString() == args[0]))
    {
        var parent = Native.GetParent(window);
        var parentStyle = (long)Native.GetWindowLongPtr(parent, -20);
        var extended = (long)Native.GetWindowLongPtr(window, -20);
        var layeredRequired = (parentStyle & 0x200000) != 0;
        var hasAttributes = Native.GetLayeredWindowAttributes(window, out _, out var alpha, out var flags);
        var renderable = !layeredRequired || ((extended & 0x80000) != 0 && hasAttributes && alpha == 255 && (flags & 2) != 0);
        var rounded = HasRoundedCorners(window, bounds);
        if (!renderable || !rounded) invalidNotes++;
        notes.Add(new { Handle = $"0x{window:X}", ParentClass = Native.ClassName(parent), LayeredRequired = layeredRequired,
            HasLayeredStyle = (extended & 0x80000) != 0, Alpha = alpha, RenderContractSatisfied = renderable, RoundedCorners = rounded,
            ChildWindow = ((long)Native.GetWindowLongPtr(window, -16) & 0x40000000) != 0,
            ToolWindow = (extended & 0x80) != 0, TopMost = (extended & 8) != 0, Visible = Native.IsWindowVisible(window) });
    }
    return new { Handle = $"0x{window:X}", Class = Native.ClassName(window), Title = title.ToString(), Process = process,
        Parent = $"0x{Native.GetParent(window):X}", Visible = Native.IsWindowVisible(window), Cloaked = cloaked,
        Style = $"0x{(long)Native.GetWindowLongPtr(window, -16):X}", ExStyle = $"0x{(long)Native.GetWindowLongPtr(window, -20):X}",
        Bounds = $"{bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}", Client = $"{client.Left},{client.Top},{client.Right},{client.Bottom}",
        Dpi = Native.GetDpiForWindow(window) };
}

bool HasRoundedCorners(nint window, Native.Rect bounds)
{
    var region = Native.CreateRectRgn(0, 0, 0, 0);
    try
    {
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        return Native.GetWindowRgn(window, region) == 3
            && !Native.PtInRegion(region, 0, 0) && !Native.PtInRegion(region, width - 1, 0)
            && !Native.PtInRegion(region, 0, height - 1) && !Native.PtInRegion(region, width - 1, height - 1)
            && Native.PtInRegion(region, width / 2, height / 2);
    }
    finally { Native.DeleteObject(region); }
}

List<object> DirectChildren(nint window, int depth)
{
    var children = new List<object>();
    if (depth > 4) return children;
    for (var child = Native.GetWindow(window, 5); child != 0; child = Native.GetWindow(child, 2))
        children.Add(new { Window = Describe(child), Children = DirectChildren(child, depth + 1) });
    return children;
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll")] internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(nint handle);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    internal delegate bool EnumProc(nint window, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumProc proc, nint parameter);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetLayeredWindowAttributes(nint window, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint SendMessageTimeout(nint window, uint message, nuint wparam, nint lparam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] internal static extern nint ReadText(nint window, uint message, nuint wparam, StringBuilder text, uint flags, uint timeout, out nuint result);
    internal static string ClassName(nint window) { var text = new StringBuilder(256); GetClassName(window, text, 256); return text.ToString(); }
}
