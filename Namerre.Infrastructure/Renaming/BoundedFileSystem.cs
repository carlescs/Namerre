using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Namerre.Application.Renaming;
using Namerre.Domain.Renaming;
using Namerre.Infrastructure.Diagnostics;

namespace Namerre.Infrastructure.Renaming;

public sealed class BoundedFileSystem(string? configuredRoot, string basePath) : IRenameFileSystem
{
    public string Root
    {
        get
        {
            if (string.IsNullOrWhiteSpace(configuredRoot))
                throw new RenameException("RootUnavailable", "Namerre:RootPath must be configured.", 503);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot, basePath));
            CheckAncestors(root);
            if (!Directory.Exists(root)) throw new RenameException("RootUnavailable", "The configured root is unavailable.", 503);
            return root;
        }
    }

    public string RootBinding
    {
        get
        {
            var root = Root;
            if (OperatingSystem.IsWindows())
            {
                using var handle = OpenDirectory(root);
                if (!GetFileInformationByHandle(handle, out var information))
                    throw new RenameException("RootUnavailable", "The root identity is unavailable.", 503);
                return root + "|" + information.VolumeSerialNumber + "|" + information.FileIndexHigh + ":" + information.FileIndexLow;
            }
            if (OperatingSystem.IsLinux())
            {
                var buffer = new byte[256];
                if (statx(-100, root, 0, 0x7ff, buffer) != 0)
                    throw new RenameException("RootUnavailable", "The root identity is unavailable.", 503);
                return root + "|" + Device(root) + "|" + BitConverter.ToUInt64(buffer, 32);
            }
            throw new RenameException("UnsupportedPlatform", "Only Windows and Linux mutations are supported.", 503);
        }
    }

    public void Validate(string path) => _ = Resolve(path);
    public bool Exists(string path)
    {
        var full = Resolve(path, IsInternal(path));
        return File.Exists(full) || Directory.Exists(full);
    }

    public string ActualPath(string path)
    {
        _ = Resolve(path, IsInternal(path));
        var current = Root;
        var parts = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            var comparison = IsCaseSensitive(current) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var match = new DirectoryInfo(current).EnumerateFileSystemInfos().FirstOrDefault(entry =>
                string.Equals(entry.Name, segment, comparison));
            if (match is null) throw new RenameException("SourceMissing", "A required path is no longer present.");
            if ((match.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new RenameException("LinkForbidden", "Symbolic links and junctions are not allowed.", 403);
            parts.Add(match.Name);
            current = match.FullName;
        }
        return string.Join('/', parts);
    }

    public bool SamePath(string first, string second)
    {
        if (first == second) return true;
        var parent = ExistingParent(Resolve(first, IsInternal(first)));
        return string.Equals(first, second, IsCaseSensitive(parent) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    public async Task<(string Hash, long Size)> Hash(string path, CancellationToken cancellationToken)
    {
        var full = Resolve(path, IsInternal(path));
        if (!File.Exists(full) || Directory.Exists(full))
            throw new RenameException("SourceMissing", "A required file is missing.");
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var size = stream.Length;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        NamerreTelemetry.HashedBytes.Add(size);
        return (Convert.ToHexStringLower(hash), size);
    }

    public const string StampPrefix = "stat:";

    public (string Hash, long Size) Stamp(string path)
    {
        var info = new FileInfo(Resolve(path, IsInternal(path)));
        if (!info.Exists)
            throw new RenameException("SourceMissing", "A required file is missing.");
        return ($"{StampPrefix}{info.Length}:{info.LastWriteTimeUtc.Ticks}", info.Length);
    }

    public FileFacts Facts(string path)
    {
        var info = new FileInfo(Resolve(path, IsInternal(path)));
        if (!info.Exists)
            throw new RenameException("SourceMissing", "A required file is missing.");
        var created = info.CreationTimeUtc;
        return new(info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            created > DateTime.UnixEpoch ? new DateTimeOffset(created, TimeSpan.Zero) : null);
    }

    public void CheckSameDevice(string source, string target)
    {
        var sourcePath = Resolve(source, IsInternal(source));
        var targetPath = Resolve(target, IsInternal(target));
        var rootDevice = Device(Root);
        if (Device(File.Exists(sourcePath) ? sourcePath : ExistingParent(sourcePath)) != rootDevice
            || Device(File.Exists(targetPath) ? targetPath : ExistingParent(targetPath)) != rootDevice)
            throw new RenameException("CrossDevice", "Sources and destinations must be on the configured root's filesystem.");
        _ = IsCaseSensitive(ExistingParent(sourcePath));
        _ = IsCaseSensitive(ExistingParent(targetPath));
    }

    public void Move(string source, string destination)
    {
        CheckSameDevice(source, destination);
        var from = Resolve(source, IsInternal(source));
        var to = Resolve(destination, IsInternal(destination));
        if (!File.Exists(from) || Directory.Exists(from))
            throw new RenameException("SourceMissing", "The file is no longer at its expected location.");
        if (File.Exists(to) || Directory.Exists(to))
            throw new RenameException("TargetExists", "The destination already exists; nothing was overwritten.");
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(from, to, 8)) ThrowMoveError(Marshal.GetLastPInvokeError());
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                if (renameat2(-100, from, -100, to, 1) != 0) ThrowMoveError(Marshal.GetLastPInvokeError());
            }
            catch (EntryPointNotFoundException)
            {
                throw new RenameException("UnsupportedFileSystem", "Atomic no-overwrite renameat2 support is required. No copy-and-delete fallback is used.");
            }
            FlushDirectory(Path.GetDirectoryName(from)!);
            if (Path.GetDirectoryName(from) != Path.GetDirectoryName(to)) FlushDirectory(Path.GetDirectoryName(to)!);
        }
        else throw new RenameException("UnsupportedPlatform", "This platform does not support bounded no-overwrite moves.");
    }

    public IReadOnlyList<string> MissingDirectories(string path)
    {
        var parent = Path.GetDirectoryName(Resolve(path, IsInternal(path)))!;
        var result = new List<string>();
        while (!Directory.Exists(parent))
        {
            result.Add(Path.GetRelativePath(Root, parent).Replace('\\', '/'));
            parent = Path.GetDirectoryName(parent)!;
        }
        result.Reverse();
        return result;
    }

    public void CreateDirectory(string path)
    {
        var full = Resolve(path);
        var parent = Path.GetDirectoryName(full)!;
        CheckAncestors(parent);
        if (!Directory.Exists(parent)) throw new RenameException("ParentMissing", "A destination parent is unavailable.");
        if (File.Exists(full) || Directory.Exists(full))
            throw new RenameException("DirectoryChanged", "A destination directory appeared during execution.");
        Directory.CreateDirectory(full);
        CheckAncestors(full);
        if (Device(full) != Device(Root))
            throw new RenameException("CrossDevice", "The destination directory is on another filesystem.");
        FlushDirectory(full);
        FlushDirectory(parent);
    }

    public bool IsEmptyDirectory(string path)
    {
        var full = Resolve(path);
        return Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any();
    }

    public bool DeleteEmptyDirectory(string path)
    {
        if (!IsEmptyDirectory(path)) return false;
        var full = Resolve(path);
        // Non-recursive: an entry that appeared since the check makes the delete fail instead of removing it.
        Directory.Delete(full, false);
        FlushDirectory(Path.GetDirectoryName(full)!);
        return true;
    }

    public string Resolve(string path, bool allowInternal = false)
    {
        try
        {
            if (allowInternal)
            {
                var name = path[(path.LastIndexOf('/') + 1)..];
                if (!name.StartsWith(".namerre-stage-", StringComparison.Ordinal)
                    || !Guid.TryParseExact(name[15..], "N", out _))
                    throw new ArgumentException("Invalid staging name.");
                var folder = path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
                if (folder.Length > 0) NamingRules.ValidateRelativePath(folder);
            }
            else NamingRules.ValidateRelativePath(path);
            var full = Path.GetFullPath(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = Path.EndsInDirectorySeparator(Root) ? Root : Root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Path is outside the root.");
            CheckAncestors(full);
            return full;
        }
        catch (ArgumentException)
        {
            throw new RenameException("InvalidPath", "The path is invalid or outside the configured root.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new RenameException("AccessDenied", "The server cannot access the selected path.", 403);
        }
    }

    private static bool IsInternal(string path) => path[(path.LastIndexOf('/') + 1)..].StartsWith(".namerre-stage-", StringComparison.Ordinal);

    public static void CheckAncestors(string full)
    {
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new RenameException("LinkForbidden", "Symbolic links and junctions are not allowed.", 403);
                if (current != full && !Directory.Exists(current))
                    throw new RenameException("InvalidParent", "A parent component is not a directory.");
            }
        }
    }

    private static string ExistingParent(string full)
    {
        var current = Directory.Exists(full) ? full : Path.GetDirectoryName(full)!;
        while (!Directory.Exists(current)) current = Path.GetDirectoryName(current)
            ?? throw new RenameException("RootUnavailable", "The configured root is unavailable.", 503);
        CheckAncestors(current);
        return current;
    }

    private static string Device(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            using var handle = OpenDirectory(directory);
            if (!GetFileInformationByHandle(handle, out var info))
                throw new RenameException("DeviceUnavailable", "The filesystem identity cannot be established.", 503);
            return info.VolumeSerialNumber.ToString("X8");
        }
        if (OperatingSystem.IsLinux())
        {
            var buffer = new byte[256];
            if (statx(-100, directory, 0, 0x7ff, buffer) != 0)
                throw new RenameException("DeviceUnavailable", "The filesystem identity cannot be established.", 503);
            return BitConverter.ToUInt32(buffer, 136) + ":" + BitConverter.ToUInt32(buffer, 140);
        }
        throw new RenameException("UnsupportedPlatform", "Safe mutation is supported on Windows and Linux only.", 503);
    }

    private static bool IsCaseSensitive(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            using var handle = OpenDirectory(directory);
            if (GetFileInformationByHandleEx(handle, 23, out var flags, 4)) return (flags & 1) != 0;
            // Older Windows volumes do not expose per-directory case sensitivity.
            return false;
        }
        if (OperatingSystem.IsLinux())
        {
            var buffer = new byte[256];
            if (statfs(directory, buffer) != 0)
                throw new RenameException("FileSystemUnavailable", "Filesystem semantics cannot be established.", 503);
            var type = BitConverter.ToInt64(buffer);
            if (type is not (0xEF53 or 0x9123683E or 0x01021994 or 0x794c7630))
                throw new RenameException("UnsupportedFileSystem", "This filesystem's case semantics are not supported for safe mutation.");
            var fd = open(directory, 0x10000);
            if (fd < 0) throw new RenameException("AccessDenied", "The directory cannot be inspected.", 403);
            try
            {
                long flags = 0;
                var inspected = ioctl(fd, 0x80086601, ref flags);
                if (inspected != 0 && type is 0xEF53 or 0x9123683E)
                    throw new RenameException("UnsupportedFileSystem", "Directory case flags could not be established.");
                if (inspected == 0 && (flags & 0x40000000) != 0)
                    throw new RenameException("UnsupportedFileSystem", "Case-folded Linux directories are not supported.");
            }
            finally { close(fd); }
            return true;
        }
        throw new RenameException("UnsupportedPlatform", "This platform is unsupported.", 503);
    }

    private static SafeFileHandle OpenDirectory(string path)
    {
        var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new RenameException("AccessDenied", "The directory cannot be inspected.", 403);
        }
        return handle;
    }

    private static void ThrowMoveError(int error)
    {
        if ((OperatingSystem.IsWindows() && error == 17) || (OperatingSystem.IsLinux() && error == 18))
            throw new RenameException("CrossDevice", "The filesystem rejected a cross-device move. No copy-and-delete fallback is used.");
        if ((OperatingSystem.IsWindows() && error is 80 or 183) || (OperatingSystem.IsLinux() && error == 17))
            throw new RenameException("TargetExists", "The destination already exists; it was not overwritten.");
        throw new RenameException("MoveFailed", "The no-overwrite filesystem move failed. Inspect the journal and recovery evidence.");
    }

    private static void FlushDirectory(string directory)
    {
        if (!OperatingSystem.IsLinux()) return;
        var fd = open(directory, 0x10000);
        if (fd < 0) throw new RenameException("DirectorySyncFailed", "The directory cannot be synchronized. Inspect recovery evidence.");
        try
        {
            if (fsync(fd) != 0)
                throw new RenameException("DirectorySyncFailed", "Directory synchronization failed. Inspect recovery evidence.");
        }
        finally { close(fd); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out uint flags, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existing, string destination, uint flags);
    [DllImport("libc", SetLastError = true)]
    private static extern int statx(int fd, string path, int flags, uint mask, byte[] buffer);
    [DllImport("libc", SetLastError = true)]
    private static extern int statfs(string path, byte[] buffer);
    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref long flags);
    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
    [DllImport("libc", SetLastError = true)]
    private static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)]
    private static extern int renameat2(int sourceDirectory, string source, int targetDirectory, string target, uint flags);
}
