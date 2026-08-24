using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexGateway.Infrastructure.FileStorage.Artifacts;

internal static class SafeArtifactFile
{
    private const int LinuxOpenReadOnly = 0;
    private const int LinuxOpenNonBlocking = 0x800;
    private const int LinuxOpenNoFollow = 0x20000;
    private const int LinuxOpenCloseOnExec = 0x80000;
    private const int LinuxOpenPath = 0x200000;
    private const int LinuxAtEmptyPath = 0x1000;
    private const uint LinuxStatxType = 0x0001;
    private const uint LinuxStatxSize = 0x0200;
    private const ushort LinuxFileTypeMask = 0xF000;
    private const ushort LinuxRegularFile = 0x8000;
    private const int LinuxInvalidArgument = 22;
    private const int LinuxFunctionNotImplemented = 38;
    private const uint WindowsGenericRead = 0x80000000;
    private const uint WindowsShareRead = 0x00000001;
    private const uint WindowsShareWrite = 0x00000002;
    private const uint WindowsShareDelete = 0x00000004;
    private const uint WindowsOpenExisting = 3;
    private const uint WindowsFlagOpenReparsePoint = 0x00200000;
    private const uint WindowsFlagSequentialScan = 0x08000000;
    private const uint WindowsFlagBackupSemantics = 0x02000000;
    private const int FileStandardInfoClass = 1;
    private const int FileAttributeTagInfoClass = 9;

    public static long GetLength(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            return GetLinuxLength(path);
        }

        if (OperatingSystem.IsWindows())
        {
            return GetWindowsLength(path);
        }

        return GetPortableLength(path);
    }

    public static FileStream OpenRead(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            return OpenLinux(path);
        }

        if (OperatingSystem.IsWindows())
        {
            return OpenWindows(path);
        }

        return OpenPortable(path);
    }

    private static FileStream OpenLinux(string path)
    {
        var descriptor = Open(
            path,
            LinuxOpenReadOnly | LinuxOpenNonBlocking | LinuxOpenNoFollow | LinuxOpenCloseOnExec);
        if (descriptor < 0)
        {
            throw new IOException("The artifact file could not be opened safely.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }

        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try
        {
            var stream = new FileStream(handle, FileAccess.Read, bufferSize: 81920, isAsync: false);
            EnsureRegularSeekableFile(stream);
            return stream;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static long GetLinuxLength(string path)
    {
        // O_PATH obtains inode metadata without read permission, O_NOFOLLOW pins a link
        // itself rather than its target, and it never blocks while opening a FIFO/device.
        var descriptor = Open(path, LinuxOpenPath | LinuxOpenNoFollow | LinuxOpenCloseOnExec);
        if (descriptor < 0)
        {
            throw new IOException("The artifact file metadata could not be opened safely.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }

        using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        LinuxStatx metadata;
        int result;
        try
        {
            result = Statx(
                descriptor,
                string.Empty,
                LinuxAtEmptyPath,
                LinuxStatxType | LinuxStatxSize,
                out metadata);
        }
        catch (EntryPointNotFoundException)
        {
            return GetPortableLength(path);
        }

        if (result != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is LinuxFunctionNotImplemented or LinuxInvalidArgument)
            {
                return GetPortableLength(path);
            }

            throw new IOException("The artifact file metadata could not be read safely.", new Win32Exception(error));
        }

        if ((metadata.Mode & LinuxFileTypeMask) != LinuxRegularFile || metadata.Size > long.MaxValue)
        {
            throw new IOException("The artifact entry is not a regular file.");
        }

        return (long)metadata.Size;
    }

    private static FileStream OpenWindows(string path)
    {
        var handle = CreateFile(
            path,
            WindowsGenericRead,
            WindowsShareRead | WindowsShareWrite | WindowsShareDelete,
            IntPtr.Zero,
            WindowsOpenExisting,
            WindowsFlagOpenReparsePoint | WindowsFlagSequentialScan | WindowsFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException("The artifact file could not be opened safely.", new Win32Exception(error));
        }

        try
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileAttributeTagInfoClass,
                    out var tagInfo,
                    (uint)Marshal.SizeOf<FileAttributeTagInfo>()) ||
                (tagInfo.FileAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new IOException("The artifact entry is not a regular file.");
            }

            var stream = new FileStream(handle, FileAccess.Read, bufferSize: 81920, isAsync: false);
            EnsureRegularSeekableFile(stream);
            return stream;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static long GetWindowsLength(string path)
    {
        // Metadata-only access can inspect a file that is still being written even when
        // the writer did not share read access. OPEN_REPARSE_POINT keeps the handle on
        // the link itself, which is rejected below, rather than following its target.
        var handle = CreateFile(
            path,
            0,
            WindowsShareRead | WindowsShareWrite | WindowsShareDelete,
            IntPtr.Zero,
            WindowsOpenExisting,
            WindowsFlagOpenReparsePoint | WindowsFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException("The artifact file metadata could not be opened safely.", new Win32Exception(error));
        }

        using (handle)
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileAttributeTagInfoClass,
                    out var tagInfo,
                    (uint)Marshal.SizeOf<FileAttributeTagInfo>()) ||
                (tagInfo.FileAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                !GetFileStandardInformationByHandleEx(
                    handle,
                    FileStandardInfoClass,
                    out var standardInfo,
                    (uint)Marshal.SizeOf<FileStandardInfo>()) ||
                standardInfo.Directory != 0 ||
                standardInfo.EndOfFile < 0)
            {
                throw new IOException("The artifact entry is not a regular file.");
            }

            return standardInfo.EndOfFile;
        }
    }

    private static FileStream OpenPortable(string path)
    {
        var before = File.GetAttributes(path);
        if ((before & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException("The artifact entry is not a regular file.");
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            var after = File.GetAttributes(path);
            if ((after & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new IOException("The artifact entry is not a regular file.");
            }

            EnsureRegularSeekableFile(stream);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static long GetPortableLength(string path)
    {
        var before = File.GetAttributes(path);
        if ((before & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException("The artifact entry is not a regular file.");
        }

        // Metadata queries do not open/read special-file content and therefore cannot
        // wait for a FIFO peer. Re-checking narrows the portable fallback's rename race.
        var length = new FileInfo(path).Length;
        var after = File.GetAttributes(path);
        if ((after & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException("The artifact entry is not a regular file.");
        }

        return length;
    }

    private static void EnsureRegularSeekableFile(FileStream stream)
    {
        if (!stream.CanSeek)
        {
            throw new IOException("The artifact entry is not a regular file.");
        }

        _ = stream.Length;
    }

#pragma warning disable SYSLIB1054
    [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int Statx(int directoryFileDescriptor, string path, int flags, uint mask, out LinuxStatx metadata);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileAttributeTagInfo fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileStandardInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileStandardInfo fileInformation,
        uint bufferSize);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public FileAttributes FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
    }

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct LinuxStatx
    {
        public uint Mask;
        public uint BlockSize;
        public ulong Attributes;
        public uint HardLinkCount;
        public uint UserId;
        public uint GroupId;
        public ushort Mode;
        public ushort Reserved;
        public ulong Inode;
        public ulong Size;
    }
}
