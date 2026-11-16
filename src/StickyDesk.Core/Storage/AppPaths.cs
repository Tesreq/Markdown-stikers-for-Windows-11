namespace StickyDesk.Core;

public sealed class AppPaths
{
    public string Root { get; }
    public string Notes => Path.Combine(Root, "notes");
    public string Recovery => Path.Combine(Root, "recovery");
    public string Logs => Path.Combine(Root, "logs");
    public string Config => Path.Combine(Root, "config.json");

    public AppPaths(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Notes);
        Directory.CreateDirectory(Recovery);
        Directory.CreateDirectory(Logs);
    }

    public string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
    public string RecoveryFile(string id) => Path.Combine(Recovery, $"{id}.md");
    public void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(Logs, "errors.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
