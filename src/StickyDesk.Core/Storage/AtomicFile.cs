namespace StickyDesk.Core;

public static class AtomicFile
{
    public static void Write(string path, byte[] bytes)
        => WriteCore(path, bytes, replaceExisting: true);

    public static void Create(string path, byte[] bytes)
        => WriteCore(path, bytes, replaceExisting: false);

    private static void WriteCore(string path, byte[] bytes, bool replaceExisting)
    {
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (replaceExisting && File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
            else File.Move(temp, path, false);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
