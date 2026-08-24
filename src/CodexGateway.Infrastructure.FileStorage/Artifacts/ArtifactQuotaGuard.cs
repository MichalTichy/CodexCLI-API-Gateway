using System.ComponentModel;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;

namespace CodexGateway.Infrastructure.FileStorage.Artifacts;

internal static class ArtifactQuotaGuard
{
    public static ArtifactUsage Measure(
        string root,
        ArtifactOptions limits,
        CancellationToken cancellationToken)
    {
        var fileCount = 0;
        long totalBytes = 0;
        long largestFileBytes = 0;
        var directories = new Stack<string>();
        directories.Push(Path.GetFullPath(root));

        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryAttributes = TryGetAttributes(directory);
            if (directoryAttributes is null ||
                (directoryAttributes.Value & FileAttributes.ReparsePoint) != 0 ||
                (directoryAttributes.Value & FileAttributes.Directory) == 0)
            {
                continue;
            }

            string[] entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception exception) when (IsAccessDenied(exception))
            {
                throw ArtifactErrors.LimitExceeded();
            }
            catch (Exception exception) when (IsTransientFileSystemException(exception))
            {
                // Codex may rename or remove a directory while a snapshot is being taken.
                // The next monitor pass will inspect the stable replacement.
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = TryGetAttributes(entry);
                if (attributes is null)
                {
                    continue;
                }

                if ((attributes.Value & FileAttributes.ReparsePoint) != 0)
                {
                    // Count the link as an entry so links cannot bypass MaxFiles, but never
                    // traverse it or inspect the target. Commit validation rejects it later.
                    fileCount++;
                    EnsureWithinLimits(fileCount, totalBytes, largestFileBytes, limits);
                    continue;
                }

                if ((attributes.Value & FileAttributes.Directory) != 0)
                {
                    directories.Push(entry);
                    continue;
                }

                fileCount++;
                var length = TryGetRegularFileLength(entry);
                if (length is { } regularFileLength)
                {
                    totalBytes = AddSaturating(totalBytes, regularFileLength);
                    largestFileBytes = Math.Max(largestFileBytes, regularFileLength);
                }

                EnsureWithinLimits(fileCount, totalBytes, largestFileBytes, limits);
            }
        }

        return new ArtifactUsage(fileCount, totalBytes, largestFileBytes);
    }

    public static void EnsureAdditionalFileAllowed(
        ArtifactUsage current,
        long additionalBytes,
        ArtifactOptions limits)
    {
        var knownAdditionalBytes = Math.Max(0, additionalBytes);

        EnsureWithinLimits(
            current.FileCount + 1,
            AddSaturating(current.TotalBytes, knownAdditionalBytes),
            Math.Max(current.LargestFileBytes, knownAdditionalBytes),
            limits);
    }

    private static long? TryGetRegularFileLength(string path)
    {
        try
        {
            return SafeArtifactFile.GetLength(path);
        }
        catch (Exception exception) when (IsAccessDenied(exception))
        {
            throw ArtifactErrors.LimitExceeded();
        }
        catch (Exception exception) when (IsTransientFileSystemException(exception))
        {
            // SafeArtifactFile uses O_PATH/O_NOFOLLOW on Linux and OPEN_REPARSE_POINT
            // on Windows. A special file, racing rename, or actively replaced path is
            // deliberately not opened or followed and will be reconsidered next pass.
            return null;
        }
    }

    private static FileAttributes? TryGetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception exception) when (IsAccessDenied(exception))
        {
            throw ArtifactErrors.LimitExceeded();
        }
        catch (Exception exception) when (IsTransientFileSystemException(exception))
        {
            return null;
        }
    }

    private static void EnsureWithinLimits(
        int fileCount,
        long totalBytes,
        long largestFileBytes,
        ArtifactOptions limits)
    {
        var maximumFileBytes = limits.MaxFileMegabytes * 1024L * 1024L;
        var maximumTotalBytes = limits.MaxTotalMegabytes * 1024L * 1024L;
        if (fileCount > limits.MaxFiles ||
            largestFileBytes > maximumFileBytes ||
            totalBytes > maximumTotalBytes)
        {
            throw ArtifactErrors.LimitExceeded();
        }
    }

    private static long AddSaturating(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;

    private static bool IsTransientFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException;

    private static bool IsAccessDenied(Exception exception)
    {
        var nativeCode = exception.InnerException is Win32Exception native
            ? native.NativeErrorCode
            : exception.HResult & 0xFFFF;
        return exception is UnauthorizedAccessException || nativeCode is 1 or 5 or 13;
    }
}
