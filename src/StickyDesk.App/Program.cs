using StickyDesk.Core;

namespace StickyDesk;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var dataIndex = Array.IndexOf(args, "--data-dir");
        var dataRoot = dataIndex >= 0 && dataIndex + 1 < args.Length ? args[dataIndex + 1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyDesk");
        AppPaths? paths = null;
        try
        {
            paths = new(dataRoot);
            // Shared lifetime marker lets Setup refuse an upgrade while any instance is running.
            using var installationMutex = new Mutex(false, @"Local\StickyDesk-Running");
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paths.Root.ToUpperInvariant())))[..20];
            using var mutex = new Mutex(true, $"Local\\StickyDesk-{key}", out var first);
            if (!first)
            {
                MessageBox.Show("StickyDesk уже запущен. Управление доступно через значок в системном трее.", "StickyDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Application.ThreadException += (_, e) => { paths.Log(e.Exception.ToString()); MessageBox.Show(e.Exception.Message, "StickyDesk — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            using var context = new NoteManager(paths);
            Application.Run(context);
        }
        catch (Exception ex)
        {
            paths?.Log(ex.ToString());
            MessageBox.Show($"Не удалось запустить StickyDesk.\n\n{ex.Message}", "StickyDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
