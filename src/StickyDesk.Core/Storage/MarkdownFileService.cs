using System.Security.Cryptography;
using System.Text;

namespace StickyDesk.Core;

public sealed class MarkdownFileService
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public MarkdownSnapshot Read(string path)
    {
        try { return Decode(ProtectedFileWrite.ReadRecovering(path)); }
        catch (InterruptedFileWriteException ex) { throw DecodeInterruption(ex); }
    }

    private static MarkdownSnapshot Decode(byte[] bytes)
    {
        var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var text = Utf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        var line = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
        return new(text, Fingerprint(bytes), line, bom);
    }

    public MarkdownSnapshot Save(string path, string editorText, MarkdownSnapshot expected)
    {
        var text = MarkdownSnapshot.Normalize(editorText).Replace("\n", expected.NewLine, StringComparison.Ordinal);
        var content = Utf8.GetBytes(text);
        var bytes = expected.HasBom ? Encoding.UTF8.Preamble.ToArray().Concat(content).ToArray() : content;
        try
        {
            using var write = ProtectedFileWrite.Prepare(path, bytes, expected.Fingerprint);
            write.Commit();
        }
        catch (InterruptedFileWriteException ex) { throw DecodeInterruption(ex); }
        return new(text, Fingerprint(bytes), expected.NewLine, expected.HasBom);
    }

    public MarkdownSnapshot RestoreMissing(string path, string editorText, MarkdownSnapshot? previous = null)
    {
        var line = previous?.NewLine ?? "\n";
        var bom = previous?.HasBom ?? false;
        var text = MarkdownSnapshot.Normalize(editorText).Replace("\n", line, StringComparison.Ordinal);
        var content = Utf8.GetBytes(text);
        var bytes = bom ? Encoding.UTF8.Preamble.ToArray().Concat(content).ToArray() : content;
        // Create-only commit: a file that reappeared must never be overwritten.
        AtomicFile.Create(path, bytes);
        return new(text, Fingerprint(bytes), line, bom);
    }

    public (string FilePath, bool IsMissing) Rename(string path, string title, Func<string, bool>? pathInUse = null)
    {
        var source = Path.GetFullPath(path);
        var destination = RenameDestination(source, title, pathInUse);
        try { _ = File.GetAttributes(source); }
        catch (FileNotFoundException) { return (destination, true); }
        catch (DirectoryNotFoundException) { return (destination, true); }
        if (!string.Equals(source, destination, StringComparison.Ordinal)) File.Move(source, destination, false);
        return (destination, false);
    }

    public string RenameDestination(string path, string title, Func<string, bool>? pathInUse = null)
    {
        // Windows file-name rules also apply when Core is tested on another platform.
        if (string.IsNullOrWhiteSpace(title) || title != title.Trim() || title.EndsWith('.')
            || title.Any(character => character < 32 || "<>:\"/\\|?*".Contains(character)))
            throw new ArgumentException("В названии нельзя использовать < > : \" / \\ | ? *, управляющие символы и точку в конце.", nameof(title));
        var device = title.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL"
            || (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789¹²³".Contains(device[3])))
            throw new ArgumentException("Это имя зарезервировано Windows. Выберите другое название.", nameof(title));
        var source = Path.GetFullPath(path);
        var extension = Path.GetExtension(source);
        var destination = Path.Combine(Path.GetDirectoryName(source)!, title + (extension.Length == 0 ? ".md" : extension));
        var sameName = string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
        if (!sameName && (pathInUse?.Invoke(destination) == true || File.Exists(destination) || Directory.Exists(destination)))
            throw new IOException("Заметка с таким именем уже существует. Выберите другое название.");
        return destination;
    }

    private static InterruptedSaveException DecodeInterruption(InterruptedFileWriteException error)
    {
        MarkdownSnapshot? current;
        try { current = Decode(error.Current); }
        catch (DecoderFallbackException) { current = null; }
        return new(Decode(error.Pending), current, error.ArchivePath);
    }

    private static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
