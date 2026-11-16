namespace StickyDesk;

internal sealed class ShellMessageWindow : NativeWindow, IDisposable
{
    private readonly uint _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    public event Action? ShellReady;

    public ShellMessageWindow() => CreateHandle(new CreateParams { Caption = "StickyDesk messages", ExStyle = 0x80 });

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == _taskbarCreated) ShellReady?.Invoke();
        base.WndProc(ref message);
    }

    public void Dispose() => DestroyHandle();
}
