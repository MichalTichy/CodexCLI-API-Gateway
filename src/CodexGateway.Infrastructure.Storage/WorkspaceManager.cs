using System.Text.Json;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Storage;

public sealed class WorkspaceManager(StoragePaths paths, FileStore files, IOptions<GatewayOptions> options) : IWorkspaceManager
{
    internal const string ContainerOutputSchemaPath = "/workspace/.gateway/output-schema.json";
    private const string GatewayDirectoryName = ".gateway";
    private const string OutputSchemaFileName = "output-schema.json";
    private readonly ArtifactOptions _limits = options.Value.Artifacts;

    public Task<RunWorkspace> CreateEmptyAsync(CancellationToken cancellationToken) =>
        CreateCoreAsync(project: null, apiKeyId: null, [], cancellationToken);

    public async Task<RunWorkspace> CreateAsync(
        ProjectDefinition? project,
        string apiKeyId,
        IReadOnlyCollection<string> temporaryFileIds,
        CancellationToken cancellationToken) =>
        await CreateCoreAsync(project, apiKeyId, temporaryFileIds, cancellationToken);

    public async Task StageOutputSchemaAsync(
        RunWorkspace workspace,
        JsonElement outputSchema,
        CancellationToken cancellationToken)
    {
        if (outputSchema.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("A validated output schema must be a JSON object.");
        }

        EnsureSafeDirectory(workspace.RootPath, createIfMissing: false);
        var gatewayDirectory = GetContainedChildPath(workspace.RootPath, GatewayDirectoryName);
        EnsureSafeDirectory(gatewayDirectory, createIfMissing: true);
        var schemaPath = GetContainedChildPath(gatewayDirectory, OutputSchemaFileName);
        await using var stream = new FileStream(
            schemaPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            options: System.IO.FileOptions.Asynchronous | System.IO.FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, outputSchema, cancellationToken: cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task<RunWorkspace> CreateCoreAsync(
        ProjectDefinition? project,
        string? apiKeyId,
        IReadOnlyCollection<string> temporaryFileIds,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(paths.Runs, "run_" + Guid.NewGuid().ToString("N"));
        var artifacts = Path.Combine(root, "artifacts");
        try
        {
            EnsureSafeDirectory(paths.Runs, createIfMissing: true);
            EnsureSafeDirectory(artifacts, createIfMissing: true);

            if (project is not null)
            {
                await CopyDirectoryAsync(
                    paths.ProjectArtifacts(project.Id),
                    artifacts,
                    new ArtifactCopyBudget(_limits),
                    cancellationToken);
            }
            else
            {
                var budget = new ArtifactCopyBudget(_limits);
                foreach (var fileId in temporaryFileIds.Distinct(StringComparer.Ordinal))
                {
                    var record = await files.GetRequiredAsync(
                        null,
                        apiKeyId ?? throw new InvalidOperationException("An API key identity is required for temporary files."),
                        fileId,
                        cancellationToken);
                    var sourceRoot = files.TemporaryFileDirectory(record);
                    var source = GetContainedChildPath(sourceRoot, record.StoredName);
                    var destination = GetContainedChildPath(artifacts, record.Id + "_" + record.FileName);
                    await CopyFileAsync(source, sourceRoot, destination, artifacts, budget, cancellationToken);
                }
            }

            return new RunWorkspace(root, artifacts, project?.Id);
        }
        catch
        {
            DeleteDirectoryIfExists(root);
            throw;
        }
    }

    public async Task CommitAsync(RunWorkspace workspace, CancellationToken cancellationToken)
    {
        if (workspace.ProjectId is null)
        {
            return;
        }

        var projectRoot = paths.ProjectRoot(workspace.ProjectId);
        EnsureSafeDirectory(projectRoot, createIfMissing: true);
        var target = paths.ProjectArtifacts(workspace.ProjectId);
        var staging = Path.Combine(projectRoot, "artifacts-staging-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(projectRoot, "artifacts-backup-" + Guid.NewGuid().ToString("N"));

        try
        {
            await CopyDirectoryAsync(
                workspace.ArtifactsPath,
                staging,
                new ArtifactCopyBudget(_limits),
                cancellationToken);

            if (TryGetAttributes(target) is not null)
            {
                ValidateDirectoryTree(target, cancellationToken);
                Directory.Move(target, backup);
            }

            Directory.Move(staging, target);
            if (TryGetAttributes(backup) is not null)
            {
                Directory.Delete(backup, true);
            }
        }
        catch
        {
            if (TryGetAttributes(target) is null && TryGetAttributes(backup) is not null)
            {
                Directory.Move(backup, target);
            }

            throw;
        }
        finally
        {
            DeleteDirectoryIfExists(staging);
        }
    }

    public void Delete(RunWorkspace workspace)
    {
        DeleteDirectoryIfExists(workspace.RootPath);
    }

    internal static void DeleteWorkspace(RunWorkspace workspace)
    {
        DeleteDirectoryIfExists(workspace.RootPath);
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        ArtifactCopyBudget budget,
        CancellationToken cancellationToken)
    {
        var sourceRoot = GetFullPath(source);
        var destinationRoot = GetFullPath(destination);
        if (PathsOverlap(sourceRoot, destinationRoot))
        {
            throw UnsafeArtifactPath();
        }

        EnsurePathComponentsAreSafe(sourceRoot);
        var sourceAttributes = TryGetAttributes(sourceRoot);
        if (sourceAttributes is null)
        {
            return;
        }

        EnsureDirectoryAttributesAreSafe(sourceAttributes.Value);
        EnsureSafeDirectory(destinationRoot, createIfMissing: true);
        await CopyDirectoryEntriesAsync(sourceRoot, sourceRoot, destinationRoot, destinationRoot, budget, cancellationToken);
    }

    private static async Task CopyDirectoryEntriesAsync(
        string sourceRoot,
        string sourceDirectory,
        string destinationRoot,
        string destinationDirectory,
        ArtifactCopyBudget budget,
        CancellationToken cancellationToken)
    {
        EnsureSafeDirectory(sourceDirectory, createIfMissing: false);
        EnsureSafeDirectory(destinationDirectory, createIfMissing: false);

        string[] entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(sourceDirectory, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (IsUnsafeFileSystemException(exception))
        {
            throw UnsafeArtifactPath();
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceEntry = EnsureContainedPath(sourceRoot, entry);
            var relativePath = Path.GetRelativePath(sourceRoot, sourceEntry);
            var destinationEntry = GetContainedChildPath(destinationRoot, relativePath);
            var attributes = GetRequiredAttributes(sourceEntry);
            EnsureNotReparsePoint(attributes);

            if ((attributes & FileAttributes.Directory) != 0)
            {
                EnsureSafeDirectory(destinationEntry, createIfMissing: true);
                await CopyDirectoryEntriesAsync(
                    sourceRoot,
                    sourceEntry,
                    destinationRoot,
                    destinationEntry,
                    budget,
                    cancellationToken);
            }
            else
            {
                await CopyFileAsync(sourceEntry, sourceRoot, destinationEntry, destinationRoot, budget, cancellationToken);
            }
        }
    }

    private static async Task CopyFileAsync(
        string source,
        string sourceRoot,
        string destination,
        string destinationRoot,
        ArtifactCopyBudget budget,
        CancellationToken cancellationToken)
    {
        source = EnsureContainedPath(sourceRoot, source);
        destination = EnsureContainedPath(destinationRoot, destination);
        var destinationDirectory = Path.GetDirectoryName(destination) ?? throw UnsafeArtifactPath();
        EnsureSafeDirectory(destinationDirectory, createIfMissing: false);

        var sourceAttributes = GetRequiredAttributes(source);
        EnsureNotReparsePoint(sourceAttributes);
        if ((sourceAttributes & FileAttributes.Directory) != 0 || TryGetAttributes(destination) is not null)
        {
            throw UnsafeArtifactPath();
        }

        try
        {
            await using var input = SafeArtifactFile.OpenRead(source);
            budget.BeginFile(input.Length);
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            var buffer = new byte[81920];
            long fileBytes = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                fileBytes += read;
                budget.AddBytes(read, fileBytes);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch (Exception exception) when (IsUnsafeFileSystemException(exception))
        {
            throw UnsafeArtifactPath();
        }
    }

    private static void ValidateDirectoryTree(string root, CancellationToken cancellationToken)
    {
        root = GetFullPath(root);
        EnsureSafeDirectory(root, createIfMissing: false);
        ValidateDirectoryEntries(root, root, cancellationToken);
    }

    private static void ValidateDirectoryEntries(string root, string directory, CancellationToken cancellationToken)
    {
        EnsureSafeDirectory(directory, createIfMissing: false);
        string[] entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (IsUnsafeFileSystemException(exception))
        {
            throw UnsafeArtifactPath();
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var containedEntry = EnsureContainedPath(root, entry);
            var attributes = GetRequiredAttributes(containedEntry);
            EnsureNotReparsePoint(attributes);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                ValidateDirectoryEntries(root, containedEntry, cancellationToken);
            }
        }
    }

    private static void EnsureSafeDirectory(string path, bool createIfMissing)
    {
        path = GetFullPath(path);
        EnsurePathComponentsAreSafe(path);
        var attributes = TryGetAttributes(path);
        if (attributes is null && createIfMissing)
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception exception) when (IsUnsafeFileSystemException(exception))
            {
                throw UnsafeArtifactPath();
            }

            EnsurePathComponentsAreSafe(path);
            attributes = GetRequiredAttributes(path);
        }

        if (attributes is null)
        {
            throw UnsafeArtifactPath();
        }

        EnsureDirectoryAttributesAreSafe(attributes.Value);
    }

    private static void EnsurePathComponentsAreSafe(string path)
    {
        var fullPath = GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw UnsafeArtifactPath();
        var relative = Path.GetRelativePath(root, fullPath);
        var current = root;
        foreach (var component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            var attributes = TryGetAttributes(current);
            if (attributes is null)
            {
                break;
            }

            EnsureNotReparsePoint(attributes.Value);
        }
    }

    private static FileAttributes? TryGetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (IsUnsafeFileSystemException(exception))
        {
            throw UnsafeArtifactPath();
        }
    }

    private static FileAttributes GetRequiredAttributes(string path) =>
        TryGetAttributes(path) ?? throw UnsafeArtifactPath();

    private static void EnsureDirectoryAttributesAreSafe(FileAttributes attributes)
    {
        EnsureNotReparsePoint(attributes);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw UnsafeArtifactPath();
        }
    }

    private static void EnsureNotReparsePoint(FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw UnsafeArtifactPath();
        }
    }

    private static string GetContainedChildPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw UnsafeArtifactPath();
        }

        return EnsureContainedPath(root, Path.Combine(root, relativePath));
    }

    private static string EnsureContainedPath(string root, string candidate)
    {
        root = Path.TrimEndingDirectorySeparator(GetFullPath(root));
        candidate = GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, comparison))
        {
            throw UnsafeArtifactPath();
        }

        return candidate;
    }

    private static bool PathsOverlap(string first, string second)
    {
        first = Path.TrimEndingDirectorySeparator(GetFullPath(first));
        second = Path.TrimEndingDirectorySeparator(GetFullPath(second));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(first, second, comparison) ||
               first.StartsWith(second + Path.DirectorySeparatorChar, comparison) ||
               second.StartsWith(first + Path.DirectorySeparatorChar, comparison);
    }

    private static string GetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw UnsafeArtifactPath();
        }
    }

    private static bool IsUnsafeFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException;

    private static GatewayException UnsafeArtifactPath() => GatewayException.InvalidRequest(
        "Artifact files must remain inside the workspace; symbolic links and reparse points are not allowed.",
        "unsafe_artifact_path");

    private sealed class ArtifactCopyBudget(ArtifactOptions limits)
    {
        private readonly long _maxFileBytes = limits.MaxFileMegabytes * 1024L * 1024L;
        private readonly long _maxTotalBytes = limits.MaxTotalMegabytes * 1024L * 1024L;
        private readonly int _maxFiles = limits.MaxFiles;
        private int _files;
        private long _bytes;

        public void BeginFile(long length)
        {
            _files++;
            if (_files > _maxFiles ||
                length < 0 ||
                length > _maxFileBytes ||
                length > _maxTotalBytes - _bytes)
            {
                throw ArtifactLimitExceeded();
            }
        }

        public void AddBytes(int bytes, long fileBytes)
        {
            _bytes += bytes;
            if (fileBytes > _maxFileBytes || _bytes > _maxTotalBytes)
            {
                throw ArtifactLimitExceeded();
            }
        }

        private static GatewayException ArtifactLimitExceeded() => ArtifactErrors.LimitExceeded();
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        var root = GetFullPath(path);
        var attributes = TryGetAttributes(root);
        if (attributes is null)
        {
            return;
        }

        try
        {
            if ((attributes.Value & FileAttributes.ReparsePoint) != 0)
            {
                DeleteReparsePoint(root, attributes.Value);
                return;
            }

            EnsureDirectoryAttributesAreSafe(attributes.Value);
            DeleteDirectoryEntries(root, root);
            Directory.Delete(root, false);
        }
        catch (GatewayException)
        {
            throw;
        }
        catch (Exception exception) when (IsUnsafeFileSystemException(exception))
        {
            throw UnsafeArtifactPath();
        }
    }

    private static void DeleteDirectoryEntries(string root, string directory)
    {
        var entries = Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
        foreach (var entry in entries)
        {
            var containedEntry = EnsureContainedPath(root, entry);
            var attributes = GetRequiredAttributes(containedEntry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                DeleteReparsePoint(containedEntry, attributes);
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteDirectoryEntries(root, containedEntry);
                Directory.Delete(containedEntry, false);
            }
            else
            {
                File.Delete(containedEntry);
            }
        }
    }

    private static void DeleteReparsePoint(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) != 0)
        {
            Directory.Delete(path, false);
        }
        else
        {
            File.Delete(path);
        }
    }
}
