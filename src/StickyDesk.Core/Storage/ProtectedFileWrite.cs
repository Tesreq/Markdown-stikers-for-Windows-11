using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StickyDesk.Core;

/// <summary>Holds one Windows file handle from validation through commit, with durable undo data.</summary>
public sealed class ProtectedFileWrite : IDisposable
{
    private readonly string _path;
    private readonly FileStream _stream;
    private readonly byte[] _original;
    private readonly byte[] _pending;
    private bool _leaveJournal;
    private bool _completed;
    private bool _disposed;

    private ProtectedFileWrite(string path, FileStream stream, byte[] original, byte[] pending)
        => (_path, _stream, _original, _pending) = (path, stream, original, pending);

    public static string JournalPath(string path) => path + ".stickydesk-write.json";

    public static ProtectedFileWrite Prepare(string path, byte[] pending, string expectedFingerprint)
    {
        path = Path.GetFullPath(path);
        FileStream stream;
        try { stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new FileConflictException(path); }
        try
        {
            InspectInterruptedWrite(path, stream);
            var original = ReadBytes(stream);
            if (Hash(original) != expectedFingerprint) throw new FileConflictException(path);
            // FileMode.Open never recreates a deleted note. This handle denies writes and deletion until commit ends.
            var pendingCopy = (byte[])pending.Clone();
            AtomicFile.Create(JournalPath(path), JsonSerializer.SerializeToUtf8Bytes(new WriteJournal(1, original, pendingCopy)));
            return new(path, stream, original, pendingCopy);
        }
        catch { stream.Dispose(); throw; }
    }

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Запись уже завершена.");
        AtomicFile.Write(_path + ".bak", _original);
        _leaveJournal = true;
        try
        {
            AssertPathIdentity(_original);
            WriteBytes(_pending);
            AssertPathIdentity(_pending);
            _completed = true;
            _leaveJournal = false;
            TryDeleteJournal(_path);
        }
        catch (FileConflictException) { throw; }
        catch (IOException failure)
        {
            try { WriteBytes(_original); }
            catch (IOException rollbackFailure)
            {
                throw new IOException("Запись прервана. Исходная и новая версии сохранены в журнале восстановления.",
                    new AggregateException(failure, rollbackFailure));
            }
            throw;
        }
    }

    private void AssertPathIdentity(byte[] expectedBytes)
    {
        FileStream mapped;
        try { mapped = new(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new FileConflictException(_path); }
        using (mapped)
        {
            if (OperatingSystem.IsWindows())
            {
                if (!GetFileInformationByHandle(_stream.SafeFileHandle, out var original)
                    || !GetFileInformationByHandle(mapped.SafeFileHandle, out var current))
                    throw new IOException("Не удалось проверить идентификатор файла.");
                if (original.VolumeSerialNumber != current.VolumeSerialNumber || original.FileIndexHigh != current.FileIndexHigh
                    || original.FileIndexLow != current.FileIndexLow) throw new FileConflictException(_path);
            }
            else if (Hash(ReadBytes(mapped)) != Hash(expectedBytes)) throw new FileConflictException(_path);
        }
    }

    internal static byte[] ReadRecovering(string path)
    {
        if (!File.Exists(JournalPath(path))) return File.ReadAllBytes(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        InspectInterruptedWrite(path, stream);
        return ReadBytes(stream);
    }

    private static void InspectInterruptedWrite(string path, FileStream stream)
    {
        var journalPath = JournalPath(path);
        if (!File.Exists(journalPath)) return;
        WriteJournal journal;
        try
        {
            journal = JsonSerializer.Deserialize<WriteJournal>(File.ReadAllBytes(journalPath))
                ?? throw new JsonException("Пустой журнал.");
            if (journal.Version != 1 || journal.Original is null || journal.Pending is null) throw new JsonException("Неизвестный журнал.");
        }
        catch (JsonException ex) { throw new IOException("Не удалось прочитать журнал незавершённой записи.", ex); }
        var current = ReadBytes(stream);
        if (Hash(current) == Hash(journal.Pending))
        {
            TryDeleteJournal(path);
            return;
        }
        // A partial write and an external edit cannot safely be distinguished after a crash.
        // Preserve all versions and let the user choose; never replace the current disk version.
        var archive = path + $".stickydesk-write.recovered-{Guid.NewGuid():N}.json";
        File.Move(journalPath, archive, false);
        throw new InterruptedFileWriteException(current, journal.Pending, archive);
    }

    private static byte[] ReadBytes(FileStream stream)
    {
        stream.Position = 0;
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void WriteBytes(byte[] bytes)
    {
        _stream.Position = 0;
        _stream.Write(bytes);
        _stream.SetLength(bytes.Length);
        _stream.Flush(true);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void TryDeleteJournal(string path)
    {
        try { File.Delete(JournalPath(path)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
        if (!_leaveJournal) TryDeleteJournal(_path);
    }

    private sealed record WriteJournal(int Version, byte[] Original, byte[] Pending);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        internal uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
}

internal sealed class InterruptedFileWriteException(byte[] current, byte[] pending, string archivePath)
    : IOException("Обнаружена незавершённая запись.")
{
    internal byte[] Current { get; } = current;
    internal byte[] Pending { get; } = pending;
    internal string ArchivePath { get; } = archivePath;
}
